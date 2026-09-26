using SimpleWeather.Interop;

namespace SimpleWeather.Core;

/// <summary>
/// Weather state machine for the native build. There is no Dispatcher here: a background timer
/// drives refreshes, the HTTP work completes on a threadpool thread, and completion is signalled
/// back to the UI thread with a posted window message.
/// </summary>
internal sealed class WeatherController : IDisposable
{
    /// <summary>Posted to the UI window whenever state changed and the widget should repaint.</summary>
    public const uint WmWeatherUpdated = 0x8000 + 10;   // WM_APP + 10

    /// <summary>
    /// How long after a failed fetch the widget waits before trying again on its own. The regular
    /// <see cref="Settings.RefreshMinutes"/> schedule is measured in minutes, so without this a
    /// dropped connection left the strip on "could not load" until the next slot — or until the
    /// user hit Refresh. 30 s matches how long a Wi-Fi handover or a router reboot usually takes.
    /// </summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly WeatherService _service = new();
    private readonly Timer _refreshTimer;
    private readonly Timer _retryTimer;
    private readonly object _gate = new();

    private IntPtr _notifyHwnd;
    private CancellationTokenSource? _cts;
    private int _busy;
    private bool _disposed;

    public WeatherController(AppSettings settings)
    {
        Settings = settings;
        _refreshTimer = new Timer(_ => _ = RefreshAsync(forceLocate: false), null, Timeout.Infinite, Timeout.Infinite);
        _retryTimer = new Timer(_ => _ = ProbeAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public AppSettings Settings { get; }

    public WeatherSnapshot? Snapshot { get; private set; }

    public GeoLocation? Location { get; private set; }

    /// <summary>Localisation key describing what the widget is doing; empty when idle.</summary>
    public string StatusKey { get; private set; } = "Status.Locating";

    public string? ErrorDetail { get; private set; }

    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    /// <summary>The window that receives <see cref="WmWeatherUpdated"/>.</summary>
    public void Attach(IntPtr hwnd) => _notifyHwnd = hwnd;

    public void Start()
    {
        int minutes = Math.Max(1, Settings.RefreshMinutes);
        _refreshTimer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(minutes));
    }

    public void Stop()
    {
        _refreshTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _retryTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void ApplyRefreshInterval()
    {
        int minutes = Math.Max(1, Settings.RefreshMinutes);
        _refreshTimer.Change(TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes));
    }

    /// <summary>
    /// Fetches weather. <paramref name="forceLocate"/> re-runs IP geolocation as well.
    ///
    /// A refresh already in flight is cancelled so the caller's request wins immediately instead of
    /// being dropped by the re-entrancy guard: the 30 s offline probe can be mid-request (a failed
    /// round takes up to ~26 s) exactly when the user picks Refresh from the menu, and a silent
    /// no-op there reads as a broken menu item.
    /// </summary>
    public async Task RefreshAsync(bool forceLocate)
    {
        // Returns false only on the rare case of a click landing inside the microseconds between
        // the previous refresh clearing its busy flag and this call claiming it.
        if (!await TryEnterAsync().ConfigureAwait(false)) return;

        CancellationTokenSource cts;
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            cts = _cts;
        }

        var token = cts.Token;
        ErrorDetail = null;
        StatusKey = Location is null ? "Status.Locating" : "Status.Loading";
        Notify();

        const int MaxAttempts = 3;
        Exception? lastError = null;

        // Set only by a completed fetch. A cancelled attempt leaves it false, so a probe that was
        // interrupted by a manual refresh cannot cancel the retry schedule that refresh set up.
        bool snapshotUpdated = false;

        try
        {
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    GeoLocation location;

                    if (Settings.UseManualLocation && Settings.CoordinatesValid)
                    {
                        location = new GeoLocation(Settings.ManualLocationName, string.Empty, string.Empty,
                            Settings.ManualLatitude, Settings.ManualLongitude, "auto");
                    }
                    else if (Location is not null && !forceLocate)
                    {
                        location = Location;
                    }
                    else
                    {
                        location = await _service.LocateAsync(token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                    }

                    Location = location;
                    StatusKey = "Status.Loading";
                    Notify();

                    Snapshot = await _service.GetAsync(location, token).ConfigureAwait(false);
                    StatusKey = string.Empty;
                    snapshotUpdated = true;
                    Log.Write($"weather ok: {location.DisplayName} {Snapshot.TemperatureC:0.#}C code={Snapshot.CurrentCode}");
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = ex;

                    if (attempt == MaxAttempts) break;

                    // Geolocation providers fail intermittently (rate limits, transient network
                    // faults). Waiting for the next scheduled refresh — up to 60 minutes — is what
                    // made a single failure look like a permanently stuck "Locating…" widget.
                    TimeSpan backoff = TimeSpan.FromSeconds(attempt * 3);
                    Log.Write($"weather fetch attempt {attempt}/{MaxAttempts} failed: {ex.Message}; retrying in {backoff.TotalSeconds:0}s");
                    await Task.Delay(backoff, token).ConfigureAwait(false);
                }
            }

            StatusKey = "Status.Error";
            ErrorDetail = lastError?.Message;
            Log.Write("weather fetch failed after retries", lastError!);
        }
        catch (OperationCanceledException)
        {
            // Either the controller is shutting down or a newer request took over. Both leave the
            // state to the caller that cancelled this one.
            return;
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);

            if (snapshotUpdated) StopRetrySchedule();
            else ScheduleRetryIn(RetryDelay);

            Notify();
        }
    }

    /// <summary>
    /// Claims the busy flag, cancelling whatever is in flight and waiting for it to unwind. Waits
    /// are bounded so a hung request can never wedge the widget; in practice a cancelled fetch
    /// notices within one HTTP call (≤ 6 s) or one inter-attempt delay (≤ 6 s).
    /// </summary>
    private async Task<bool> TryEnterAsync()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) == 0) return true;

        CancellationTokenSource? inFlight;
        lock (_gate) inFlight = _cts;

        if (inFlight is { IsCancellationRequested: false })
        {
            try { inFlight.Cancel(); } catch (ObjectDisposedException) { }
        }

        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(100).ConfigureAwait(false);
            if (Interlocked.CompareExchange(ref _busy, 1, 0) == 0) return true;
        }

        Log.Write("could not start a new refresh: the previous one is still shutting down");
        return false;
    }

    /// <summary>
    /// One 30 s offline check. Runs through the normal fetch path, which re-uses the last known
    /// coordinates unless location detection is set to automatic; no separate connectivity probe is
    /// needed, because a refresh that reaches the network succeeds and stops the schedule by
    /// itself.
    /// </summary>
    private Task ProbeAsync()
    {
        if (_disposed) return Task.CompletedTask;

        // Re-uses the last known coordinates, so a network that is back but whose geolocation
        // providers are still flaky only has to answer the weather request to recover.
        return RefreshAsync(forceLocate: false);
    }

    private void ScheduleRetryIn(TimeSpan delay)
    {
        if (_disposed) return;

        // One-shot. The next interval is armed after the attempt it scheduled has finished, so a
        // slow failing round (up to ~26 s) can never pile attempts on top of each other.
        try { _retryTimer.Change(delay, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { }   // Dispose() won the race; the widget is shutting down.
    }

    private void StopRetrySchedule()
    {
        if (_disposed) return;

        try { _retryTimer.Change(Timeout.Infinite, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>City lookup for the settings window.</summary>
    public Task<IReadOnlyList<GeoLocation>> SearchAsync(string query)
        => _service.SearchAsync(query, CancellationToken.None);

    /// <summary>Switches to a location picked in the settings window.</summary>
    public async Task UseFixedLocationAsync(GeoLocation location)
    {
        Settings.UseManualLocation = true;
        Settings.ManualLocationName = location.DisplayName;
        Settings.ManualLatitude = location.Latitude;
        Settings.ManualLongitude = location.Longitude;
        Settings.Save();

        Location = location;
        await RefreshAsync(forceLocate: false).ConfigureAwait(false);
    }

    /// <summary>Switches back to network-based location detection.</summary>
    public async Task UseAutomaticLocationAsync()
    {
        Settings.UseManualLocation = false;
        Settings.Save();

        Location = null;
        await RefreshAsync(forceLocate: true).ConfigureAwait(false);
    }

    /// <summary>Forces a repaint without re-fetching, e.g. after the unit setting changed.</summary>
    public void Notify()
    {
        IntPtr hwnd = _notifyHwnd;
        if (hwnd != IntPtr.Zero)
            Win32.PostMessage(hwnd, WmWeatherUpdated, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _refreshTimer.Dispose();
        _retryTimer.Dispose();

        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
