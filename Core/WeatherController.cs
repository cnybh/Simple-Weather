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

    private readonly WeatherService _service = new();
    private readonly Timer _timer;
    private readonly object _gate = new();

    private IntPtr _notifyHwnd;
    private CancellationTokenSource? _cts;
    private int _busy;
    private bool _disposed;

    public WeatherController(AppSettings settings)
    {
        Settings = settings;
        _timer = new Timer(_ => _ = RefreshAsync(forceLocate: false), null, Timeout.Infinite, Timeout.Infinite);
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
        _timer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(minutes));
    }

    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    public void ApplyRefreshInterval()
    {
        int minutes = Math.Max(1, Settings.RefreshMinutes);
        _timer.Change(TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes));
    }

    /// <summary>Fetches weather. <paramref name="forceLocate"/> re-runs IP geolocation as well.</summary>
    public async Task RefreshAsync(bool forceLocate)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;

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
            Log.Write($"weather ok: {location.DisplayName} {Snapshot.TemperatureC:0.#}C code={Snapshot.CurrentCode}");
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            StatusKey = "Status.Error";
            ErrorDetail = ex.Message;
            Log.Write("weather fetch failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            Notify();
        }
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

        _timer.Dispose();
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
