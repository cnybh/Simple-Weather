using System.Runtime.InteropServices;
using SimpleWeather.Core;
using SimpleWeather.Interop;

namespace SimpleWeather.Ui;

/// <summary>
/// The configuration window. Unlike the strip and the card this is an ordinary, opaque window
/// built from stock Win32 controls — they already look exactly like Windows, cost almost nothing,
/// and need no custom drawing.
/// </summary>
internal sealed class SettingsWindow : IDisposable
{
    private const string ClassName = "SimpleWeatherSettings";

    // Control ids
    private const int IdStartup = 1001;
    private const int IdUnit = 1002;
    private const int IdRefresh = 1003;
    private const int IdAuto = 1004;
    private const int IdManual = 1005;
    private const int IdSearchEdit = 1006;
    private const int IdSearchButton = 1007;
    private const int IdResults = 1008;
    private const int IdAbout = 1009;
    private const int IdRelease = 1010;

    private const int BnClicked = 0;
    private const int CbnSelChange = 1;

    /// <summary>
    /// Design-space window sizes (96 DPI). Everything the window lays out — control rectangles,
    /// button rows, window height — is written in these units and scaled by <see cref="Metrics"/>
    /// inside <see cref="Add"/> and the few explicit moves below, so the dialog follows the DPI of
    /// the taskbar monitor like the rest of the widget.
    ///
    /// Both heights leave roughly one button height (40 design px) of blank space below the
    /// bottom button row, which sits at y=388 and is 30 tall: the row ends at 418 and the window
    /// at 496, so the gap is 78 versus the 38 it was before. The expanded height grows by the same
    /// 40 so the layout keeps its proportions when the search-results list is on screen.
    /// </summary>
    private const int Width_ = 500;
    private const int HeightCollapsed_ = 496;
    private const int HeightExpanded_ = 592;

    public static int WindowWidth => Metrics.Round(Width_);
    public static int WindowHeightCollapsed => Metrics.Round(HeightCollapsed_);
    public static int WindowHeightExpanded => Metrics.Round(HeightExpanded_);

    /// <summary>Kept for callers that just want a default size.</summary>
    public static int WindowHeight => WindowHeightCollapsed;

    /// <summary>Project home page opened by the "Software Release Page" button.</summary>
    private const string ReleasePageUrl = "https://github.com/cnybh/Simple-Weather";

    private static readonly int[] RefreshChoices = [5, 10, 15, 30, 60, 120];

    private readonly Win32.WndProc _wndProc;
    private readonly WeatherController _controller;

    private IntPtr _hwnd;
    private IntPtr _font;
    private IntPtr _titleFont;

    private IntPtr _startupCheck, _unitCombo, _refreshCombo;
    private IntPtr _autoRadio, _manualRadio, _searchEdit, _searchButton, _results, _status;
    private IntPtr _aboutButton, _releaseButton;
    private IntPtr _generalLabel, _unitsLabel, _locationLabel;
    private IntPtr _startupDesc, _unitLabel, _refreshLabel, _refreshDesc, _autoDesc;

    private bool _loading = true;
    private bool _disposed;
    private AboutWindow? _about;
    private IReadOnlyList<GeoLocation> _searchResults = [];

    public SettingsWindow(WeatherController controller)
    {
        _controller = controller;
        _wndProc = WindowProc;
    }

    public IntPtr Handle => _hwnd;

    /// <summary>Creates the window on first use, then shows and focuses it.</summary>
    public void Show()
    {
        if (_hwnd == IntPtr.Zero && !Create()) return;

        Win32.ShowWindow(_hwnd, Win32.SW_SHOW);
        _ = Win32.SetForegroundWindow(_hwnd);
    }

    public void Hide()
    {
        if (_hwnd != IntPtr.Zero) Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
    }

    // ------------------------------------------------------------- creation

    private bool Create()
    {
        var wc = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = Win32.GetModuleHandle(null),
            lpszClassName = ClassName,
            hbrBackground = new IntPtr(16),   // COLOR_BTNFACE + 1, the standard dialog背景
            // Same reason as LayeredWindow: a class without a cursor leaves the shell's busy ring
            // in place over the window instead of the arrow.
            hCursor = Win32.LoadCursor(IntPtr.Zero, Win32.IDC_ARROW),
            // The application icon on the title bar. Windows would fall back to the module icon on
            // its own, but setting it here makes the dialog's identity explicit and independent of
            // that fallback.
            hIcon = AboutWindow.AppIcon,
            hIconSm = AboutWindow.AppIcon,
        };
        _ = Win32.RegisterClassEx(ref wc);

        _hwnd = Win32.CreateWindowEx(
            0, ClassName, Loc.T("Settings.Title"),
            Win32.WS_OVERLAPPEDWINDOW & ~Win32.WS_MAXIMIZEBOX & ~Win32.WS_THICKFRAME,
            0, 0, WindowWidth, WindowHeightCollapsed,
            IntPtr.Zero, IntPtr.Zero, Win32.GetModuleHandle(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            Log.Write($"SettingsWindow: CreateWindowEx failed err={Marshal.GetLastPInvokeError()}");
            return false;
        }

        Win32.LOGFONT messageFont = default;
        _font = Win32.CreateMessageFont(out messageFont, FontScale);

        Win32.LOGFONT titleFont = MakeTitleLogFont();
        _titleFont = Win32.CreateFontIndirect(ref titleFont);

        BuildControls();
        LoadValues();
        CentreOnScreen();
        _loading = false;
        return true;
    }

    /// <summary>
    /// How much to scale the shell's dialog font. <c>SPI_GETNONCLIENTMETRICS</c> reports sizes for
    /// the *system* DPI, so the factor is the widget's DPI relative to that — scaling by the raw
    /// DPI would double-scale on a machine whose system DPI is already high.
    /// </summary>
    private static float FontScale
    {
        get
        {
            float system = Math.Max(1f, Win32.GetDpiForSystem() / (float)Metrics.BaselineDpi);
            return Metrics.DpiScale / system;
        }
    }

    private static Win32.LOGFONT MakeTitleLogFont()
    {
        _ = Win32.CreateMessageFont(out var baseFont);
        baseFont.lfHeight = (int)(baseFont.lfHeight * 1.35);
        baseFont.lfWeight = 600;   // semibold
        return baseFont;
    }

    private void CentreOnScreen()
    {
        IntPtr monitor = Win32.MonitorFromWindow(_hwnd, Win32.MONITOR_DEFAULTTONEAREST);
        var mi = new Win32.MONITORINFOEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFOEX>(),
            szDevice = string.Empty,
        };
        if (!Win32.GetMonitorInfo(monitor, ref mi)) return;

        var work = mi.rcWork;
        int x = work.Left + ((work.Width - WindowWidth) / 2);
        int y = work.Top + ((work.Height - WindowHeightCollapsed) / 2);
        _ = Win32.SetWindowPos(_hwnd, IntPtr.Zero, x, y, WindowWidth, WindowHeightCollapsed,
            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Creates a child control. The rectangle is given in design units and scaled here, so the
    /// whole dialog follows the DPI without touching each layout constant.
    /// </summary>
    private IntPtr Add(string className, string text, int style, int id, int x, int y, int w, int h, IntPtr font = default)
    {
        IntPtr control = Win32.CreateWindowEx(
            0, className, text, Win32.WS_CHILD | Win32.WS_VISIBLE | style,
            Metrics.Round(x), Metrics.Round(y), Metrics.Round(w), Metrics.Round(h),
            _hwnd, new IntPtr(id), Win32.GetModuleHandle(null), IntPtr.Zero);

        IntPtr use = font == IntPtr.Zero ? _font : font;
        if (control != IntPtr.Zero && use != IntPtr.Zero) Win32.SetFont(control, use);
        return control;
    }

    private void BuildControls()
    {
        const int left = 20;
        const int fieldX = 190;
        const int fieldW = 285;
        const int indented = left + 22;
        const int contentW = 430;

        _generalLabel = Add("STATIC", Loc.T("Settings.General"), Win32.SS_LEFT, 0, left, 14, 400, 26, _titleFont);
        _startupCheck = Add("BUTTON", Loc.T("Settings.StartWithWindows"), Win32.BS_AUTOCHECKBOX | Win32.WS_TABSTOP, IdStartup, left, 44, 400, 22);
        _startupDesc = Add("STATIC", Loc.T("Settings.StartWithWindows.Desc"), Win32.SS_LEFT, 0, indented, 68, contentW, 18);

        _unitsLabel = Add("STATIC", Loc.T("Settings.Units"), Win32.SS_LEFT, 0, left, 100, 400, 26, _titleFont);
        _unitLabel = Add("STATIC", Loc.T("Settings.TemperatureUnit"), Win32.SS_LEFT, 0, left, 132, 170, 20);
        _unitCombo = Add("COMBOBOX", "", Win32.CBS_DROPDOWNLIST | Win32.WS_TABSTOP | Win32.WS_VSCROLL, IdUnit, fieldX, 128, fieldW, 200);
        _refreshLabel = Add("STATIC", Loc.T("Settings.RefreshInterval"), Win32.SS_LEFT, 0, left, 168, 170, 20);
        _refreshCombo = Add("COMBOBOX", "", Win32.CBS_DROPDOWNLIST | Win32.WS_TABSTOP | Win32.WS_VSCROLL, IdRefresh, fieldX, 164, fieldW, 200);
        _refreshDesc = Add("STATIC", Loc.T("Settings.RefreshInterval.Desc"), Win32.SS_LEFT, 0, fieldX, 188, fieldW, 18);

        _locationLabel = Add("STATIC", Loc.T("Settings.Location"), Win32.SS_LEFT, 0, left, 216, 400, 26, _titleFont);
        _autoRadio = Add("BUTTON", Loc.T("Settings.AutoLocation"), Win32.BS_AUTORADIOBUTTON | Win32.WS_TABSTOP | Win32.WS_GROUP, IdAuto, left, 246, contentW, 22);
        _autoDesc = Add("STATIC", Loc.T("Settings.AutoLocation.Desc"), Win32.SS_LEFT, 0, indented, 268, contentW, 18);
        _manualRadio = Add("BUTTON", Loc.T("Settings.ManualLocation"), Win32.BS_AUTORADIOBUTTON | Win32.WS_TABSTOP, IdManual, left, 292, contentW, 22);

        _searchEdit = Add("EDIT", "", Win32.ES_AUTOHSCROLL | Win32.WS_TABSTOP | Win32.WS_EX_CLIENTEDGE, IdSearchEdit, indented, 318, 290, 24);
        _searchButton = Add("BUTTON", Loc.T("Settings.Search"), Win32.BS_PUSHBUTTON | Win32.WS_TABSTOP, IdSearchButton, indented + 300, 318, 90, 24);

        // "Current Location Setting: ..." sits directly under the search row, as in the reference.
        _status = Add("STATIC", "", Win32.SS_LEFT, 0, left, 356, contentW, 18);

        // The result list only occupies space while a search is pending; the buttons and the
        // window height move down to make room (see SetResultsVisible).
        _results = Add("LISTBOX", "", Win32.LBS_NOTIFY | Win32.WS_TABSTOP | Win32.WS_VSCROLL | Win32.WS_EX_CLIENTEDGE, IdResults, indented, 382, contentW - 22, 96);

        _releaseButton = Add("BUTTON", Loc.T("Settings.ReleasePage"), Win32.BS_PUSHBUTTON | Win32.WS_TABSTOP, IdRelease, 284, 388, 180, 30);
        _aboutButton = Add("BUTTON", Loc.T("Settings.AboutButton"), Win32.BS_PUSHBUTTON | Win32.WS_TABSTOP, IdAbout, 194, 388, 82, 30);

        Win32.ShowWindow(_results, Win32.SW_HIDE);
    }

    /// <summary>
    /// Shows or hides the search-results list, shifting the bottom button row and resizing the
    /// window so the layout stays tight when no search is running.
    /// </summary>
    private void SetResultsVisible(bool visible)
    {
        Win32.ShowWindow(_results, visible ? Win32.SW_SHOW : Win32.SW_HIDE);

        int buttonY = visible ? 484 : 388;   // design units, scaled by MoveWindow's wrapper below
        MoveControl(_aboutButton, 194, buttonY, 82, 30);
        MoveControl(_releaseButton, 284, buttonY, 180, 30);

        int height = visible ? WindowHeightExpanded : WindowHeightCollapsed;
        if (!Win32.TryGetRect(_hwnd, out var current)) return;
        if (current.Height != height)
        {
            _ = Win32.SetWindowPos(_hwnd, IntPtr.Zero, current.Left, current.Top,
                WindowWidth, height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        }
    }

    /// <summary>Moves a child control given a design-unit rectangle.</summary>
    private static void MoveControl(IntPtr control, int x, int y, int w, int h)
        => Win32.MoveWindow(control, Metrics.Round(x), Metrics.Round(y), Metrics.Round(w), Metrics.Round(h), true);

    /// <summary>Renders the "Current Location Setting: ..." line.</summary>
    private void UpdateLocationStatus(string? overrideText = null)
    {
        string name = overrideText ?? _controller.Location?.DisplayName ?? "\u2014";
        Win32.SetWindowText(_status, $"{Loc.T("Settings.CurrentLocation")}: {name}");
    }

    private void LoadValues()
    {
        var settings = _controller.Settings;

        Win32.SetChecked(_startupCheck, StartupRegistration.IsEnabled());

        foreach (var unit in TemperatureUnits.All) Win32.ComboAdd(_unitCombo, DescribeUnit(unit));
        Win32.ComboSelect(_unitCombo, Array.IndexOf(TemperatureUnits.All, settings.Unit));

        foreach (int minutes in RefreshChoices) Win32.ComboAdd(_refreshCombo, $"{minutes} {Loc.T("Settings.Minutes")}");
        int refreshIndex = Array.IndexOf(RefreshChoices, settings.RefreshMinutes);
        Win32.ComboSelect(_refreshCombo, refreshIndex >= 0 ? refreshIndex : 1);

        Win32.SetChecked(settings.UseManualLocation ? _manualRadio : _autoRadio, true);
        UpdateLocationStatus();
    }

    private static string DescribeUnit(TemperatureUnit unit) => unit switch
    {
        TemperatureUnit.Fahrenheit => Loc.IsChinese ? "\u2109  \u534e\u6c0f\u5ea6" : "\u2109  Fahrenheit",
        TemperatureUnit.Kelvin => Loc.IsChinese ? "K  \u5f00\u5c14\u6587" : "K  Kelvin",
        _ => Loc.IsChinese ? "\u2103  \u6444\u6c0f\u5ea6" : "\u2103  Celsius",
    };

    // --------------------------------------------------------------- events

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (_hwnd == IntPtr.Zero) _hwnd = hWnd;

        switch (msg)
        {
            case Win32.WM_COMMAND:
                OnCommand((int)(wParam.ToInt64() & 0xFFFF), (int)((wParam.ToInt64() >> 16) & 0xFFFF));
                return IntPtr.Zero;

            case Win32.WM_CLOSE:
                Hide();
                return IntPtr.Zero;

            case Win32.WM_SETCURSOR:
                // A child control forwards its WM_SETCURSOR here (wParam = the child) so the parent
                // can override the child's class cursor. Only the dialog body itself is ours:
                // handling the forwarded message would replace the edit control's I-beam with an
                // arrow (measured).
                if (wParam == hWnd && (lParam.ToInt64() & 0xFFFF) == Win32.HTCLIENT)
                {
                    Win32.SetCursor(Win32.LoadCursor(IntPtr.Zero, Win32.IDC_ARROW));
                    return new IntPtr(1);
                }
                break;
        }

        return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void OnCommand(int id, int notification)
    {
        if (_loading) return;

        switch (id)
        {
            case IdStartup when notification == BnClicked:
                OnStartupToggled();
                break;

            case IdAbout when notification == BnClicked:
                ShowAbout();
                break;

            case IdRelease when notification == BnClicked:
                OpenReleasePage();
                break;

            case IdUnit when notification == CbnSelChange:
                OnUnitChanged();
                break;

            case IdRefresh when notification == CbnSelChange:
                OnRefreshChanged();
                break;

            case IdAuto when notification == BnClicked:
                _ = OnAutoLocationAsync();
                break;

            case IdManual when notification == BnClicked:
                UpdateLocationStatus();
                break;

            case IdSearchButton when notification == BnClicked:
                _ = OnSearchAsync();
                break;

            case IdResults when notification == 1:   // LBN_SELCHANGE
                _ = OnResultChosenAsync();
                break;
        }
    }

    private void OnStartupToggled()
    {
        bool enabled = Win32.IsChecked(_startupCheck);
        bool ok = StartupRegistration.SetEnabled(enabled);

        _controller.Settings.StartWithWindows = ok && enabled;
        _controller.Settings.Save();

        if (!ok)
        {
            _loading = true;
            Win32.SetChecked(_startupCheck, StartupRegistration.IsEnabled());
            _loading = false;
        }
    }

    private void OnUnitChanged()
    {
        int index = Win32.ComboSelection(_unitCombo);
        if (index < 0 || index >= TemperatureUnits.All.Length) return;

        _controller.Settings.Unit = TemperatureUnits.All[index];
        _controller.Settings.Save();
        _controller.Notify();
    }

    private void OnRefreshChanged()
    {
        int index = Win32.ComboSelection(_refreshCombo);
        if (index < 0 || index >= RefreshChoices.Length) return;

        _controller.Settings.RefreshMinutes = RefreshChoices[index];
        _controller.Settings.Save();
        _controller.ApplyRefreshInterval();
    }

    private async Task OnAutoLocationAsync()
    {
        UpdateLocationStatus(Loc.T("Status.Locating"));
        await _controller.UseAutomaticLocationAsync();
        UpdateLocationStatus();
    }

    /// <summary>Opens the project's GitHub page in the default browser.</summary>
    private void OpenReleasePage()
    {
        if (!Win32.OpenWithShell(ReleasePageUrl))
            Log.Write($"failed to open {ReleasePageUrl}");
    }

    /// <summary>
    /// Shows the About dialog, owned by this window: it then stays above the settings window
    /// without being topmost over the whole desktop, and dies with its owner.
    /// </summary>
    private void ShowAbout()
    {
        // Created once at its measured size, so it never has to be resized into place.
        _about ??= new AboutWindow(_hwnd);
        _about.Show();
    }

    private async Task OnSearchAsync()
    {
        string query = Win32.TextOf(_searchEdit).Trim();
        if (query.Length == 0) return;

        Win32.EnableWindow(_searchButton, false);
        Win32.ListClear(_results);
        Win32.SetWindowText(_status, Loc.T("Settings.Searching"));

        try
        {
            _searchResults = await _controller.SearchAsync(query);
            if (_searchResults.Count == 0)
            {
                SetResultsVisible(false);
                Win32.SetWindowText(_status, Loc.T("Settings.NoResults"));
                return;
            }

            foreach (var place in _searchResults) Win32.ListAdd(_results, place.DisplayName);
            SetResultsVisible(true);
            Win32.SetWindowText(_status, string.Empty);
        }
        catch (Exception ex)
        {
            SetResultsVisible(false);
            Win32.SetWindowText(_status, ex.Message);
        }
        finally
        {
            Win32.EnableWindow(_searchButton, true);
        }
    }

    private async Task OnResultChosenAsync()
    {
        int index = Win32.ListSelection(_results);
        if (index < 0 || index >= _searchResults.Count) return;

        var location = _searchResults[index];

        _loading = true;
        Win32.SetChecked(_manualRadio, true);
        _loading = false;

        UpdateLocationStatus(location.DisplayName);
        await _controller.UseFixedLocationAsync(location);

        Win32.ShowWindow(_results, Win32.SW_HIDE);
        Win32.ListClear(_results);
        Win32.SetWindowText(_searchEdit, string.Empty);
        _searchResults = [];
        SetResultsVisible(false);
        UpdateLocationStatus();
    }

    // ------------------------------------------------------------- teardown

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Before the settings window itself: the About dialog is owned by it, so destroying the
        // owner first would take the dialog (and its GDI objects) down with it.
        _about?.Dispose();
        _about = null;

        if (_hwnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_font != IntPtr.Zero) { Win32.DeleteObject(_font); _font = IntPtr.Zero; }
        if (_titleFont != IntPtr.Zero) { Win32.DeleteObject(_titleFont); _titleFont = IntPtr.Zero; }
    }
}
