using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using SimpleWeather.Core;
using SimpleWeather.Interop;

namespace SimpleWeather.Ui;

/// <summary>
/// The About box, laid out to match the one in the SimpleWallpaper application:
///
/// <code>
/// Width=300, height fits its content, not resizable, centred on its owner
///   StackPanel Margin=12
///     Image     64x64, centred
///     TextBlock name,    FontSize 16 SemiBold, centred
///     TextBlock version, margin 0,6,0,0, #6A6A6A, centred
///     TextBlock homepage, margin 0,6,0,0, #6A6A6A, centred
///     Button    90x30, margin 0,14,0,0, centred, default button
/// </code>
///
/// That reference is WPF; this build has no UI framework, so the same layout is reproduced with
/// stock Win32 controls. The numbers above are the specification, and every one of them is
/// honoured: a 12px content margin, 64px logo, 16pt semibold name, 6/6/14px gaps, a 90x30 default
/// button, and a window that is exactly as tall as its content needs.
///
/// The logo is the application icon — the same file the executable carries — decoded from the icon
/// packed into this assembly, so the box, the title bars and the .exe all show one icon.
/// </summary>
internal sealed class AboutWindow : IDisposable
{
    private const string ClassName = "SimpleWeatherAbout";

    /// <summary>
    /// Suffix of the resource name the application icon is packed under. The manifest name is
    /// prefixed with the root namespace (<c>SimpleWeather.logo.ico</c>), which is why this is
    /// matched as a suffix rather than hard-coded: renaming the project must not break the logo.
    /// </summary>
    private const string LogoResourceSuffix = ".ico";

    // Control ids
    private const int IdOk = 2001;

    private const int BnClicked = 0;

    /// <summary>Project home page, shown in the box and opened when that line is clicked.</summary>
    private const string HomePage = "https://github.com/cnybh/Simple-Weather";

    // --- design units, straight from the reference layout ---------------------------------------

    /// <summary>Window width. Fixed; the height is the content's.</summary>
    private const int Width_ = 300;

    /// <summary>StackPanel Margin=12.</summary>
    private const int Margin_ = 12;

    private const int LogoSize_ = 64;

    private const int NameGap_ = 10;      // TextBlock Margin="0,10,0,0"
    private const int VersionGap_ = 6;    // TextBlock Margin="0,6,0,0"
    private const int HomePageGap_ = 6;   // TextBlock Margin="0,6,0,0"
    private const int ButtonGap_ = 14;    // Button     Margin="0,14,0,0"

    private const int ButtonWidth_ = 90;
    private const int ButtonHeight_ = 30;

    // Font sizes: WPF's FontSize is in device-independent pixels, the Win32 equivalent is a pixel
    // height, so these are the same numbers.
    private const float NameFontSizePx = 16f;
    private const float BodyFontSizePx = 10f;   // the reference leaves the default 12px line box for
                                                // the two grey lines; 10px matches its rendered size

    /// <summary>#6A6A6A, the reference's Foreground for the version and homepage lines (COLORREF).</summary>
    private const uint MutedColor = 0x006A6A6A;

    /// <summary>COLOR_BTNFACE, the dialog surface (COLORREF 0x00BBGGRR).</summary>
    private const uint DialogFace = 0x00F0F0F0;

    private readonly Win32.WndProc _wndProc;
    private readonly IntPtr _owner;

    private IntPtr _hwnd;
    private IntPtr _nameFont;
    private IntPtr _bodyFont;

    private IntPtr _name, _version, _homePage, _ok;
    private IntPtr _backgroundBrush;
    private bool _disposed;

    // Measured before the window exists, because CreateWindowEx needs the height up front.
    private static bool _layoutReady;
    private static int _layoutDpi;
    private static int _windowWidthPx;
    private static int _windowHeightPx;
    private static int _logoTopPx, _nameTopPx, _versionTopPx, _homePageTopPx, _buttonTopPx;
    private static int _nameHeightPx, _bodyHeightPx;

    public AboutWindow(IntPtr owner)
    {
        _owner = owner;
        _wndProc = WindowProc;
    }

    public IntPtr Handle => _hwnd;

    // ------------------------------------------------------------- creation

    /// <summary>Creates the dialog on first use, then shows and focuses it.</summary>
    public void Show()
    {
        if (_hwnd == IntPtr.Zero && !Create()) return;

        _ = Win32.SetForegroundWindow(_hwnd);
        Win32.ShowWindow(_hwnd, Win32.SW_SHOW);
    }

    private bool Create()
    {
        EnsureLayout();

        var wc = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = Win32.GetModuleHandle(null),
            lpszClassName = ClassName,
            hbrBackground = new IntPtr(16),          // COLOR_BTNFACE + 1
            hCursor = Win32.LoadCursor(IntPtr.Zero, Win32.IDC_ARROW),
            hIcon = AppIcon,                          // the title bar and Alt+Tab entry
            hIconSm = AppIcon,
        };
        _ = Win32.RegisterClassEx(ref wc);

        _hwnd = Win32.CreateWindowEx(
            0, ClassName, Loc.T("About.Title"),
            Win32.WS_OVERLAPPEDWINDOW & ~Win32.WS_MAXIMIZEBOX & ~Win32.WS_MINIMIZEBOX & ~Win32.WS_THICKFRAME,
            0, 0, _windowWidthPx, _windowHeightPx,
            _owner, IntPtr.Zero, Win32.GetModuleHandle(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            Log.Write($"AboutWindow: CreateWindowEx failed err={Marshal.GetLastPInvokeError()}");
            return false;
        }

        CreateFonts();
        BuildControls();
        CentreOnOwner();
        return true;
    }

    /// <summary>
    /// The application icon, taken from this module's resources — the same icon <c>ApplicationIcon</c>
    /// stamped onto the executable. <c>LR_SHARED</c> means the system owns the handle and it must
    /// not be destroyed, which is what makes it safe to hand to every window class.
    /// </summary>
    internal static IntPtr AppIcon { get; } = Win32.LoadAppIcon();

    /// <summary>
    /// Same DPI handling as the settings window: <c>SPI_GETNONCLIENTMETRICS</c> answers for the
    /// *system* DPI, so the factor is this window's DPI relative to that, not the raw scale.
    /// </summary>
    private static float FontScale
    {
        get
        {
            float system = Math.Max(1f, Win32.GetDpiForSystem() / (float)Metrics.BaselineDpi);
            return Metrics.DpiScale / system;
        }
    }

    private void CreateFonts()
    {
        _bodyFont = Win32.CreateMessageFont(out _, FontScale);

        // The name line is the reference's FontSize 16 SemiBold.
        Win32.LOGFONT logFont;
        _ = Win32.CreateMessageFont(out logFont, FontScale);
        logFont.lfHeight = -(int)Math.Round(NameFontSizePx * Metrics.DpiScale * FontScale);
        logFont.lfWeight = 600;   // semibold
        _nameFont = Win32.CreateFontIndirect(ref logFont);

        _backgroundBrush = Win32.CreateSolidBrush(DialogFace);
    }

    // -------------------------------------------------------------- layout

    /// <summary>
    /// Window size and every row's position, derived from the reference layout.
    ///
    /// The rows are 12px apart from the content margin, and the content is
    /// margin + logo + gaps + three text rows + button + margin tall — the equivalent of the
    /// reference's <c>SizeToContent="Height"</c>.
    ///
    /// That figure is a <b>client-area</b> height, and the first version handed it straight to
    /// CreateWindowEx, which measures the whole window including its title bar and borders. The
    /// rows near the bottom were then clipped — the button was cut off entirely. The client
    /// requirement is expanded through AdjustWindowRectEx so the window is big enough to *show*
    /// what was measured; the reference gets this for free because WPF's Height is content-driven.
    ///
    /// Static and cached because the caller can ask for the size before the window exists.
    /// </summary>
    private static void MeasureLayout()
    {
        int margin = Metrics.Round(Margin_);
        int logo = Metrics.Round(LogoSize_);
        int buttonHeight = Metrics.Round(ButtonHeight_);

        _windowWidthPx = Metrics.Round(Width_);

        // One line box for the name, one for each of the two grey lines; measured, not guessed, so
        // a different UI font cannot clip them.
        _nameHeightPx = MeasureTextHeightPx(NameFontSizePx);
        _bodyHeightPx = MeasureTextHeightPx(BodyFontSizePx);

        _logoTopPx = margin;
        _nameTopPx = _logoTopPx + logo + Metrics.Round(NameGap_);
        _versionTopPx = _nameTopPx + _nameHeightPx + Metrics.Round(VersionGap_);
        _homePageTopPx = _versionTopPx + _bodyHeightPx + Metrics.Round(HomePageGap_);
        _buttonTopPx = _homePageTopPx + _bodyHeightPx + Metrics.Round(ButtonGap_);

        int clientHeight = _buttonTopPx + buttonHeight + margin;

        // ClientToWindowHeight already returns the *whole* window height, non-client margin
        // included — it is not a margin to be added. (Adding it again is what produced a 455px box
        // for a 208px client area.)
        //
        // The window is shown without a thick frame (see Create), which is what the style below
        // describes: WS_CAPTION | WS_SYSMENU.
        _windowHeightPx = Win32.ClientToWindowHeight(
            _windowWidthPx, clientHeight, Win32.WS_CAPTION | Win32.WS_SYSMENU);

        _layoutDpi = Metrics.Dpi;
        _layoutReady = true;
    }

    /// <summary>Measures on first use, and again if the DPI changed since the last measurement.</summary>
    private static void EnsureLayout()
    {
        if (!_layoutReady || _layoutDpi != Metrics.Dpi) MeasureLayout();
    }

    /// <summary>
    /// Height GDI needs for a line of text at a design pixel size, in device pixels.
    ///
    /// The font is built at the size the control will really use — design size times the DPI factor
    /// and <see cref="FontScale"/> — so the measurement matches the static control's rendering
    /// rather than an idealised 96-DPI figure.
    /// </summary>
    private static int MeasureTextHeightPx(float designSizePx)
    {
        float size = Math.Max(1f, designSizePx * Metrics.DpiScale * FontScale);

        using var font = new Font(Theme.UiFontFamily, size, FontStyle.Regular, GraphicsUnit.Pixel);
        using var bitmap = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bitmap);

        // Sample with a string that has both an ascender and a descender: a line box measured from
        // "x" alone is short enough to clip a "g".
        return (int)Math.Ceiling(g.MeasureString("Ayg", font).Height);
    }

    /// <summary>Creates a child control; the rectangle is in design units and scales here.</summary>
    private IntPtr Add(string className, string text, int style, int id, int x, int y, int w, int h, IntPtr font = default)
    {
        IntPtr control = Win32.CreateWindowEx(
            0, className, text, Win32.WS_CHILD | Win32.WS_VISIBLE | style,
            Metrics.Round(x), Metrics.Round(y), Metrics.Round(w), Metrics.Round(h),
            _hwnd, new IntPtr(id), Win32.GetModuleHandle(null), IntPtr.Zero);

        IntPtr use = font == IntPtr.Zero ? _bodyFont : font;
        if (control != IntPtr.Zero && use != IntPtr.Zero) Win32.SetFont(control, use);
        return control;
    }

    private void BuildControls()
    {
        const int width = Width_;

        // The logo is drawn by this window in WM_PAINT rather than handed to a SS_BITMAP static.
        // That combination was tried first and produced an empty box: the static accepted the
        // bitmap handle (STM_SETIMAGE returned success) and painted nothing. Drawing it directly
        // removes the middleman — and the bitmap handle that had to be freed by hand.

        _name = Add("STATIC", Loc.T("About.Title"), Win32.SS_CENTER, 0,
            0, _nameTopPx, width, _nameHeightPx, _nameFont);

        _version = Add("STATIC", $"{Loc.T("About.Version")}: {ProductVersion}", Win32.SS_CENTER, 0,
            0, _versionTopPx, width, _bodyHeightPx);

        // A plain grey line, exactly as in the reference: its homepage row is a TextBlock, not a
        // hyperlink, so this one is not clickable either.
        _homePage = Add("STATIC", HomePage, Win32.SS_CENTER, 0,
            0, _homePageTopPx, width, _bodyHeightPx);

        _ok = Add("BUTTON", Loc.T("About.Ok"), Win32.BS_DEFPUSHBUTTON | Win32.WS_TABSTOP, IdOk,
            (width - ButtonWidth_) / 2, _buttonTopPx, ButtonWidth_, ButtonHeight_);
    }

    /// <summary>
    /// Paints the 64px logo slot.
    ///
    /// The .ico holds one 256px frame; <see cref="Icon"/> hands over its largest frame so the slot
    /// is filled from that rather than an enlarged 16px image — the same reasoning as the reference,
    /// which orders the decoder's frames by width. The window is opaque, so there is no alpha to
    /// composite against: whatever the icon leaves transparent simply shows the dialog face through.
    /// </summary>
    private void DrawLogo(Graphics g)
    {
        int size = Metrics.Round(LogoSize_);
        int x = (Metrics.Round(Width_) - size) / 2;

        using var icon = LoadPackedIcon();
        if (icon is null) return;

        g.DrawIcon(icon, new Rectangle(x, _logoTopPx, size, size));
    }

    /// <summary>
    /// The application icon, decoded from the resource packed into this assembly. Returns null when
    /// the resource is missing, which the caller treats as "draw no logo" rather than crashing.
    /// </summary>
    private static Icon? LoadPackedIcon()
    {
        var assembly = Assembly.GetExecutingAssembly();

        // The manifest name carries the root namespace, so it is found by suffix. Logged when it is
        // missing: a silently absent logo is exactly the failure this dialog was rebuilt to fix.
        string? name = Array.Find(assembly.GetManifestResourceNames(),
            n => n.EndsWith(LogoResourceSuffix, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            Log.Write("AboutWindow: no .ico packed into the assembly");
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            Log.Write($"AboutWindow: packed icon '{name}' could not be opened");
            return null;
        }

        using var buffer = new MemoryStream((int)stream.Length);
        stream.CopyTo(buffer);
        buffer.Position = 0;

        // The largest frame, so the 64px slot is not an upscaled 16px image.
        return new Icon(buffer, new Size(256, 256));
    }

    /// <summary>Centres the dialog on its owner, falling back to the primary work area.</summary>
    private void CentreOnOwner()
    {
        Win32.RECT anchor;
        if (_owner != IntPtr.Zero && Win32.TryGetRect(_owner, out var ownerRect))
        {
            anchor = ownerRect;
        }
        else
        {
            anchor = default;
            if (!Win32.SystemParametersInfo(Win32.SPI_GETWORKAREA, 0, ref anchor, 0)) return;
        }

        int x = anchor.Left + ((anchor.Width - _windowWidthPx) / 2);
        int y = anchor.Top + ((anchor.Height - _windowHeightPx) / 2);

        _ = Win32.SetWindowPos(_hwnd, IntPtr.Zero, x, y, _windowWidthPx, _windowHeightPx,
            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Version, read from the assembly rather than written down, so the box cannot drift from the
    /// build. <c>Version</c> in the project file is 1.0.2.
    /// </summary>
    internal static string ProductVersion
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            string? informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            return informational ?? assembly.GetName().Version?.ToString(3) ?? "1.0.1";
        }
    }

    // --------------------------------------------------------------- events

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (_hwnd == IntPtr.Zero) _hwnd = hWnd;

        switch (msg)
        {
            case Win32.WM_COMMAND:
                OnCommand((int)(wParam.ToInt64() & 0xFFFF), (int)((wParam.ToInt64() >> 16) & 0xFFFF));
                return IntPtr.Zero;

            case Win32.WM_PAINT:
            {
                IntPtr hdc = Win32.BeginPaint(hWnd, out var paint);
                try
                {
                    using var g = Graphics.FromHdc(hdc);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    DrawLogo(g);
                }
                finally
                {
                    _ = Win32.EndPaint(hWnd, ref paint);
                }
                return IntPtr.Zero;
            }

            case Win32.WM_CLOSE:
                // Hidden rather than destroyed: the dialog is created once and reused, and the
                // logo bitmap it owns has to outlive a dismissal.
                Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
                return IntPtr.Zero;

            case Win32.WM_CTLCOLORSTATIC:
                // The version and homepage lines are #6A6A6A in the reference; the name line keeps
                // the dialog's own colours, so only these two are tinted.
                if (lParam == _version || lParam == _homePage)
                {
                    _ = Win32.SetTextColor(wParam, MutedColor);
                    _ = Win32.SetBkColor(wParam, DialogFace);
                    if (_backgroundBrush != IntPtr.Zero) return _backgroundBrush;
                }
                break;

            case Win32.WM_SETCURSOR:
                // Answer only for our own client area: the button must keep its own cursor, and a
                // forwarded child message must not be answered for it.
                if (wParam == hWnd && (lParam.ToInt64() & 0xFFFF) == Win32.HTCLIENT) break;
                break;
        }

        return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void OnCommand(int id, int notification)
    {
        switch (id)
        {
            case IdOk when notification == BnClicked:
                Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
                break;
        }
    }

    // ------------------------------------------------------------- teardown

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_nameFont != IntPtr.Zero) { Win32.DeleteObject(_nameFont); _nameFont = IntPtr.Zero; }
        if (_bodyFont != IntPtr.Zero) { Win32.DeleteObject(_bodyFont); _bodyFont = IntPtr.Zero; }
        if (_backgroundBrush != IntPtr.Zero) { Win32.DeleteObject(_backgroundBrush); _backgroundBrush = IntPtr.Zero; }
    }
}
