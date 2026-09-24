using System.Drawing;
using System.Threading;
using SimpleWeather.Core;
using SimpleWeather.Interop;
using SimpleWeather.Ui;

namespace SimpleWeather;

internal static class Program
{
    private const string MutexName = @"Local\SimpleWeather.SingleInstance";
    private const int WatchIntervalMs = 1000;

    // Menu command ids.
    private const int CmdSettings = 100;
    private const int CmdRefresh = 101;
    private const int CmdExit = 102;

    private static TaskbarBand? _band;
    private static StripWindow? _strip;
    private static FlyoutWindow? _flyout;
    private static MenuWindow? _menu;
    private static SettingsWindow? _settingsWindow;
    private static WeatherController? _controller;
    private static AppSettings? _settings;
    private static string? _lastTaskbarSignature;

    /// <summary>UI thread id, so a worker can post it a quit message.</summary>
    private static uint _uiThreadId;

    [STAThread]
    private static int Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance) return 0;

        Log.Write("=== start ===");
        Log.VerboseEnabled = Environment.GetEnvironmentVariable("SIMPLEWEATHER_VERBOSE") == "1";
        _uiThreadId = Win32.GetCurrentThreadId();

        // Undo a band left behind by a previous hard kill before reserving anything.
        Log.Write($"RestoreTaskbarBand -> {TaskbarBand.RestoreTaskbarBand()}");

        Loc.Initialize();
        Theme.Refresh();

        // The app is per-monitor DPI aware, so nothing is stretched for us: everything below is
        // sized from the taskbar monitor's DPI, scaled again for the card. See Metrics.
        ApplyDpi(TaskbarBand.Probe()?.Dpi ?? Metrics.BaselineDpi);

        // Async work started from the UI thread must come back to it; without this the settings
        // window's continuations resume on a pool thread and poke UI-thread windows from there.
        UiSynchronizationContext.Install();

        _settings = AppSettings.Load();
        _settings.StartWithWindows = StartupRegistration.IsEnabled();
        _settings.Save();
        Log.Write($"settings: unit={_settings.Unit} band={_settings.BandThicknessPx}->{BandThicknessPx}px "
                  + $"gap={_settings.BandGapPx}->{BandGapPx}px "
                  + $"lang={Loc.LanguageTag} shellLight={Theme.SystemUsesLightTheme} dpi={Metrics.Dpi} "
                  + $"scale={Metrics.DpiScale:0.##} cardScale={Metrics.CardScale:0.##}");

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();

        // Log the horizontal (bottom-docked) layout maths even though this machine docks the
        // taskbar vertically; the shell offers no way to reposition Shell_TrayWnd for testing.
        TaskbarBand.SelfTestHorizontal(BandThicknessPx, HorizontalBandWidthPx, BandGapPx);

        if (!CreateStrip()) return 1;

        // The strip is the sink for queued UI continuations: its message loop is the UI thread.
        UiSynchronizationContext.Installed?.AttachSink(_strip!.Handle);

        _controller = new WeatherController(_settings);
        _controller.Attach(_strip!.Handle);
        _controller.Start();

        UpdateStripContent();
        StartAutoCloseTimer();
        RunMessageLoop();
        Cleanup();
        return 0;
    }

    // ------------------------------------------------------------- scaling

    /// <summary>
    /// Band length on a vertical taskbar, in device pixels. The stored setting is a 96-DPI value,
    /// so a high-DPI taskbar reserves proportionally more room for the (also scaled) content.
    /// </summary>
    private static int BandThicknessPx => Math.Max(16, Metrics.Round(_settings!.BandThicknessPx));

    private static int HorizontalBandWidthPx => Math.Max(32, Metrics.Round(_settings!.HorizontalBandWidthPx));

    private static int BandGapPx => Math.Max(0, Metrics.Round(_settings!.BandGapPx));

    /// <summary>
    /// Adopts a monitor DPI. <c>SIMPLEWEATHER_DPI</c> overrides it so the scaled layout can be
    /// exercised on a 96-DPI machine (see README §4). Returns true when the scale changed.
    /// </summary>
    private static bool ApplyDpi(int dpi)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("SIMPLEWEATHER_DPI"), out int forced) && forced > 0)
            dpi = forced;

        if (dpi == Metrics.Dpi) return false;

        Metrics.SetDpi(dpi);
        return true;
    }

    /// <summary>Reserves taskbar space with the DPI-scaled band sizes.</summary>
    private static bool ReserveBand()
        => _band is not null && _band.Reserve(BandThicknessPx, HorizontalBandWidthPx, BandGapPx);

    private static bool CreateStrip()
    {
        _band = new TaskbarBand();
        if (!ReserveBand())
        {
            Log.Write("FATAL: could not reserve taskbar band");
            return false;
        }

        var rect = _band.StripScreenRect;
        _strip = new StripWindow(rect.Width, rect.Height);
        if (!_strip.Create())
        {
            Log.Write("FATAL: could not create strip window");
            return false;
        }

        _strip.SetBounds(rect.Left, rect.Top, rect.Width, rect.Height);
        _strip.Activated += (_, _) => ToggleFlyout();
        _strip.ContextRequested += (_, _) => ShowContextMenu();
        _strip.WeatherUpdated += (_, _) => UpdateStripContent();
        _strip.WatchTick += (_, _) => FollowTaskbar();
        _strip.GuardTick += (_, _) => GuardTaskbar();
        _strip.SetVisible(true);

        // Two timers on the same window: the follow tick once a second, and a cheap guard that
        // reclaims z-order and the button band as soon as the shell takes them away.
        Win32.SetTimer(_strip.Handle, StripWindow.WatchTimerId, WatchIntervalMs, IntPtr.Zero);
        Win32.SetTimer(_strip.Handle, StripWindow.GuardTimerId, StripWindow.GuardIntervalMs, IntPtr.Zero);

        _lastTaskbarSignature = TaskbarBand.Probe()?.Signature;
        Log.Write($"strip placed at ({rect.Left},{rect.Top}) size {rect.Width}x{rect.Height}");
        return true;
    }

    private static void RunMessageLoop()
    {
        while (true)
        {
            int result = Win32.GetMessage(out var msg, IntPtr.Zero, 0, 0);
            if (result is 0 or -1) break;   // WM_QUIT or error
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
    }

    // ------------------------------------------------------------ content

    /// <summary>Strip glyph size in device pixels; matches the strip's own drawing.</summary>
    private static int StripGlyphPx => Math.Max(8, Metrics.Round(21f));

    private static void UpdateStripContent()
    {
        if (_strip is null || _controller is null || _settings is null) return;

        var snapshot = _controller.Snapshot;
        if (snapshot is null)
        {
            string status = Loc.T(string.IsNullOrEmpty(_controller.StatusKey) ? "Status.Loading" : _controller.StatusKey);
            _strip.SetContent(status, WeatherIcons.Render(WeatherKind.Unknown, true, Theme.SystemUsesLightTheme, StripGlyphPx));
        }
        else
        {
            _strip.SetContent(
                TemperatureUnits.FormatRange(snapshot.TodayMinC, snapshot.TodayMaxC, _settings.Unit),
                WeatherIcons.Render(snapshot.CurrentCode, snapshot.IsDay, Theme.SystemUsesLightTheme, StripGlyphPx));
        }

        _strip.Render();

        if (_flyout is { IsVisible: true }) _flyout.Apply(_controller);
    }

    // ---------------------------------------------------------------- flyout

    private static void ToggleFlyout()
    {
        if (_controller is null || _band is null) return;

        if (_flyout is null)
        {
            _flyout = new FlyoutWindow();
            if (!_flyout.Create())
            {
                Log.Write("could not create flyout window");
                _flyout = null;
                return;
            }
        }

        if (_flyout.IsVisible)
        {
            _flyout.HideCard();
            return;
        }

        // A click that lands within a second of the card closing is almost certainly the second
        // half of a double-click through it, not a request to re-open. Swallow it.
        if (_flyout.IsReopenSuppressed)
        {
            Log.Verbose("strip click ignored: card closed less than a second ago");
            return;
        }

        // Apply anchors the card to the work-area corner itself, so no probe or placement is
        // needed here; if the taskbar is unreachable it simply repaints where it already is.
        _flyout.Apply(_controller);
        _flyout.ShowCard();
    }

    private static void OpenSettings()
    {
        if (_controller is null) return;

        _settingsWindow ??= new SettingsWindow(_controller);
        _settingsWindow.Show();
    }

    // --------------------------------------------------------- taskbar glue

    /// <summary>
    /// Keeps the popup glued to the taskbar. Unlike a child window it does not move by itself,
    /// so position, reservation, auto-hide and z-order are re-checked once a second.
    /// </summary>
    private static void FollowTaskbar()
    {
        if (_strip is null || _band is null || _settings is null) return;

        var info = TaskbarBand.Probe();
        if (info is null)
        {
            // Explorer is restarting; hide until it is back.
            _strip.SetVisible(false);
            _lastTaskbarSignature = null;
            return;
        }

        bool shellMoved = info.Signature != _lastTaskbarSignature;
        bool bandLost = !_band.IsBandIntact();

        // A monitor change (or a DPI change on the same monitor) rescales every surface.
        if (ApplyDpi(info.Dpi)) UpdateStripContent();

        if (shellMoved || bandLost)
        {
            Log.Write($"taskbar changed (shellMoved={shellMoved} bandLost={bandLost}); re-reserving");
            if (!RestoreBand())
            {
                _strip.SetVisible(false);
                return;
            }

            _lastTaskbarSignature = TaskbarBand.Probe()?.Signature;
        }

        // Auto-hide slides the bar off-screen; mirror that so the strip is not left floating.
        bool offScreen = info.IsAutoHidden && (info.TaskbarRect.Left < info.MonitorRect.Left
                                               || info.TaskbarRect.Top < info.MonitorRect.Top
                                               || info.TaskbarRect.Right > info.MonitorRect.Right
                                               || info.TaskbarRect.Bottom > info.MonitorRect.Bottom);

        // A fullscreen game or presentation covers the taskbar entirely; the strip must not
        // float on top of it.
        if (Win32.IsShellBusy())
        {
            _flyout?.HideCard();
            _strip.SetVisible(false);
            return;
        }

        _strip.SetVisible(!offScreen);
        if (!offScreen) _strip.ReassertTopMost();
    }

    /// <summary>
    /// Re-applies the reservation and puts the strip back on top. Used by both the follow tick and
    /// the fast guard; each step is a no-op when nothing changed.
    /// </summary>
    private static bool RestoreBand()
    {
        if (_band is null || _settings is null || _strip is null) return false;

        if (!ReserveBand()) return false;

        var rect = _band.StripScreenRect;
        _strip.SetBounds(rect.Left, rect.Top, rect.Width, rect.Height);
        _strip.ReassertTopMost();
        return true;
    }

    /// <summary>
    /// Undoes the shell's own taskbar re-layout several times a second.
    ///
    /// Opening or closing the notification area's hidden-icons flyout makes explorer raise
    /// Shell_TrayWnd above the strip *and* expand ReBarWindow32 back to its full height. Measured
    /// timeline for one chevron click: covered at 0.20 s, band back at 865, and still covered at
    /// 0.68 s because the follow tick only runs once a second — the widget vanishes and pops back,
    /// which is the "refresh" users complain about.
    ///
    /// Only two cheap window queries run per tick, and only the taskbar itself is fought: a
    /// fullscreen app covering the strip is legitimate and stays the follow tick's business.
    /// </summary>
    private static void GuardTaskbar()
    {
        if (_strip is null || _band is null || _settings is null) return;
        if (!_strip.IsVisible) return;

        bool bandLost = !_band.IsBandIntact();
        if (bandLost)
        {
            // Do NOT hide the strip when the reservation cannot be re-taken. Hiding is what turned
            // a momentary shell re-layout into a visible 0.6 s disappearance: the log showed
            // "Reserve: degenerate rebar geometry (0,38)-(1920,38) 1920x0" — for one instant the
            // shell reports ReBarWindow32 with zero height, Reserve fails, and the old code took
            // that as a reason to vanish. The band is retried on the next tick anyway, and if the
            // taskbar really did cover the strip the z-order check below puts it back.
            RestoreBand();
        }

        if (bandLost || _strip.IsCoveredByTaskbar()) _strip.ReassertTopMost();
    }

    // --------------------------------------------------------------- menu

    private static void ShowContextMenu()
    {
        if (_strip is null) return;

        IReadOnlyList<MenuEntry> entries =
        [
            new MenuEntry(Loc.T("Menu.Settings"), CmdSettings),
            new MenuEntry(Loc.T("Menu.Refresh"), CmdRefresh),
            new MenuEntry(string.Empty, null),
            new MenuEntry(Loc.T("Menu.Exit"), CmdExit),
        ];

        Win32.GetCursorPos(out var pt);
        var pos = MenuWindow.ClampToMonitor(pt.X, pt.Y, MenuWindow.WidthPx, MenuWindow.MeasureHeight(entries));

        _menu ??= CreateMenu();
        _menu.ShowMenu(pos.X, pos.Y, entries);
    }

    private static MenuWindow CreateMenu()
    {
        var menu = new MenuWindow();
        if (!menu.Create()) Log.Write("could not create menu window");

        menu.ItemChosen += (_, command) =>
        {
            switch (command)
            {
                case CmdSettings:
                    OpenSettings();
                    break;

                case CmdRefresh:
                    _ = _controller?.RefreshAsync(forceLocate: true);
                    break;

                case CmdExit:
                    Log.Write("exit requested from menu");
                    Win32.PostQuitMessage(0);
                    break;
            }
        };

        return menu;
    }

    // ------------------------------------------------------------ teardown

    /// <summary>
    /// Test hook so the spike can be driven from a script. The quit has to be posted to the UI
    /// thread's queue: <see cref="Win32.PostQuitMessage"/> only affects the calling thread, so
    /// calling it from this worker left the widget running forever.
    /// </summary>
    private static void StartAutoCloseTimer()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("SIMPLEWEATHER_AUTOCLOSE"), out int seconds) || seconds <= 0)
            return;

        uint uiThread = _uiThreadId;
        var thread = new Thread(() =>
        {
            Thread.Sleep(seconds * 1000);
            Log.Write($"autoclose after {seconds}s");
            if (!Win32.PostThreadMessage(uiThread, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero))
                Log.Write($"autoclose: PostThreadMessage failed err={System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}");
        })
        { IsBackground = true };

        thread.Start();
    }

    private static void Cleanup()
    {
        _controller?.Dispose();
        _controller = null;

        _settingsWindow?.Dispose();
        _settingsWindow = null;

        _menu?.Dispose();
        _menu = null;

        _flyout?.Dispose();
        _flyout = null;

        _strip?.Dispose();
        _strip = null;

        _band?.Dispose();
        _band = null;

        Log.Write("=== exit ===");
    }
}
