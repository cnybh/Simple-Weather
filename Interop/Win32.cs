using System.Runtime.InteropServices;
using System.Text;

namespace SimpleWeather.Interop;

/// <summary>
/// All Win32 surface used by the native build. Kept in one place so the rest of the code
/// reads like application logic rather than P/Invoke soup.
/// </summary>
internal static partial class Win32
{
    // ------------------------------------------------------------- constants

    public const int WS_OVERLAPPED = 0x00000000;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_CHILD = 0x40000000;
    public const int WS_MINIMIZE = 0x20000000;
    public const int WS_VISIBLE = 0x10000000;
    public const int WS_DISABLED = 0x08000000;
    public const int WS_CLIPSIBLINGS = 0x04000000;
    public const int WS_CLIPCHILDREN = 0x02000000;
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_BORDER = 0x00800000;
    public const int WS_DLGFRAME = 0x00400000;
    public const int WS_VSCROLL = 0x00200000;
    public const int WS_HSCROLL = 0x00100000;
    public const int WS_SYSMENU = 0x00080000;
    public const int WS_THICKFRAME = 0x00040000;
    public const int WS_MINIMIZEBOX = 0x00020000;
    public const int WS_MAXIMIZEBOX = 0x00010000;
    public const int WS_GROUP = 0x00020000;
    public const int WS_TABSTOP = 0x00010000;
    public const int WS_OVERLAPPEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;

    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_APPWINDOW = 0x00040000;
    public const int WS_EX_CLIENTEDGE = 0x00000200;

    public const int BS_AUTOCHECKBOX = 0x00000003;
    public const int BS_AUTORADIOBUTTON = 0x00000009;
    public const int BS_GROUPBOX = 0x00000007;
    public const int BS_PUSHBUTTON = 0x00000000;
    public const int BS_DEFPUSHBUTTON = 0x00000001;
    public const int CBS_DROPDOWNLIST = 0x00000003;
    public const int CBS_HASSTRINGS = 0x00000200;
    public const int ES_AUTOHSCROLL = 0x00000080;
    public const int ES_LEFT = 0x00000000;
    public const int SS_LEFT = 0x00000000;
    /// <summary>Static styles used by the About dialog.</summary>
    public const int SS_CENTER = 0x00000001;
    public const int SS_RIGHT = 0x00000002;
    public const int SS_BITMAP = 0x0000000E;
    public const int SS_CENTERIMAGE = 0x00000200;
    /// <summary>Static styles used by the About dialog.</summary>
    public const int SS_NOTIFY = 0x00000100;
    public const int STN_CLICKED = 0;
    public const int WS_EX_STATICEDGE = 0x00020000;
    public const int LBS_NOTIFY = 0x00000001;
    public const int WS_VSCROLL_LBS = 0x00200000;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    /// <summary>Resizes without reusing the saved client-area bits, i.e. with a clean repaint.</summary>
    public const uint SWP_NOCOPYBITS = 0x0100;
    public const uint SWP_NOOWNERZORDER = 0x0200;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);
    public static readonly IntPtr Zero = IntPtr.Zero;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_SHOW = 5;
    public const int SW_SHOWNA = 8;

    public const uint GW_CHILD = 5;
    public const uint GW_HWNDNEXT = 2;
    public const uint GW_HWNDPREV = 3;
    public const uint GW_OWNER = 4;

    /// <summary><see cref="GetAncestor"/> flag: walk up to the top-level (root) owner.</summary>
    public const uint GA_ROOT = 2;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    public const uint WM_NULL = 0x0000;
    public const uint WM_CREATE = 0x0001;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUERYENDSESSION = 0x0011;
    public const uint WM_ENDSESSION = 0x0016;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_MOUSELEAVE = 0x02A3;
    public const uint WM_DPICHANGED = 0x02E0;

    /// <summary>Sent to the window under the cursor so it can pick a cursor shape.</summary>
    public const uint WM_SETCURSOR = 0x0020;

    /// <summary>Sent to a static control to replace the bitmap it displays.</summary>
    public const uint STM_SETIMAGE = 0x0172;

    /// <summary>Sent to a static control to read the bitmap handle it currently holds.</summary>
    public const uint STM_GETIMAGE = 0x0173;

    /// <summary><see cref="STM_SETIMAGE"/>/<see cref="STM_GETIMAGE"/> image type: a bitmap.</summary>
    public static readonly IntPtr IMAGE_BITMAP = IntPtr.Zero;

    /// <summary>Hit-test code in the low word of WM_SETCURSOR's lParam: the client area.</summary>
    public const int HTCLIENT = 1;

    /// <summary>IDC_ARROW, as a MAKEINTRESOURCE value for <see cref="LoadCursor"/>.</summary>
    public const int IDC_ARROW = 32512;

    // ------------------------------------------------------ LoadImage / icons

    /// <summary><see cref="LoadImage"/> type: an icon resource.</summary>
    public const uint IMAGE_ICON = 1;

    /// <summary><see cref="LoadImage"/>: use the system metric size for the requested image type.</summary>
    public const uint LR_DEFAULTSIZE = 0x00000040;

    /// <summary>
    /// <see cref="LoadImage"/>: share the handle with the system instead of owning it. Required for
    /// an icon that is handed to several window classes, and it means the handle must not be freed.
    /// </summary>
    public const uint LR_SHARED = 0x00008000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadImage(IntPtr hinst, string name, uint type, int cx, int cy, uint flags);

    /// <summary>
    /// The executable's own icon. <c>#32512</c> is <c>IDI_APPLICATION</c>, which the resource
    /// compiler rewrites to the lowest-numbered icon group — the one <c>ApplicationIcon</c> stamps
    /// into the module — so this returns whatever icon the build put on the .exe.
    /// </summary>
    public static IntPtr LoadAppIcon()
        => LoadImage(GetModuleHandle(null), "#32512", IMAGE_ICON, 0, 0, LR_DEFAULTSIZE | LR_SHARED);

    // ------------------------------------------------- client/window size

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AdjustWindowRectEx(ref RECT lpRect, int dwStyle, bool bMenu, int dwExStyle);

    /// <summary>
    /// DPI-aware variant. Requires Windows 10 1607; callers fall back when it fails, so a
    /// non-DPI-aware machine still gets a correct (if unscaled) non-client margin.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AdjustWindowRectExForDpi(ref RECT lpRect, int dwStyle, bool bMenu, int dwExStyle, uint dpi);

    /// <summary>
    /// How tall a window must be to have a client area of <paramref name="clientHeight"/>.
    ///
    /// <c>CreateWindowEx</c>'s height is the whole window including its title bar and borders, so a
    /// client-area figure handed to it directly is short by exactly that margin — which is how the
    /// About box ended up clipping its bottom row.
    /// </summary>
    public static int ClientToWindowHeight(int clientWidth, int clientHeight, int style)
    {
        var rect = new RECT { Left = 0, Top = 0, Right = clientWidth, Bottom = clientHeight };

        uint dpi = GetDpiForSystem();
        if (dpi == 0) dpi = 96;

        bool ok = AdjustWindowRectExForDpi(ref rect, style, false, 0, dpi);
        if (!ok) ok = AdjustWindowRectEx(ref rect, style, false, 0);

        return ok ? rect.Height : clientHeight;
    }

    public const int WM_APP = 0x8000;
    public const int WM_TRAYICON = WM_APP + 1;

    public const uint ULW_ALPHA = 0x00000002;
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;

    public const uint ABM_GETSTATE = 0x00000004;
    public const uint ABM_GETTASKBARPOS = 0x00000005;
    public const int ABS_AUTOHIDE = 0x0000001;
    public const int ABS_ALWAYSONTOP = 0x0000002;

    public const int SPI_GETNONCLIENTMETRICS = 0x0029;
    public const int SPI_GETWORKAREA = 0x0030;

    public const uint MF_STRING = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_NONOTIFY = 0x0080;

    public const int DT_LEFT = 0x00000000;
    public const int DT_CENTER = 0x00000001;
    public const int DT_RIGHT = 0x00000002;
    public const int DT_VCENTER = 0x00000004;
    public const int DT_SINGLELINE = 0x00000020;
    public const int DT_NOPREFIX = 0x00000800;
    public const int DT_END_ELLIPSIS = 0x00008000;

    public const int TRANSPARENT = 1;

    public const int DEFAULT_CHARSET = 1;
    public const int CLEARTYPE_QUALITY = 5;

    // ------------------------------------------------------------ structures

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public readonly bool IsEmpty => Width <= 0 || Height <= 0;
        public override readonly string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}";
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors1, bmiColors2, bmiColors3; }

    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

    [StructLayout(LayoutKind.Sequential)] public struct PAINTSTRUCT { public IntPtr hdc; public int fErase; public RECT rcPaint; public int fRestore, fIncUpdate; public IntPtr rgbReserved1, rgbReserved2, rgbReserved3, rgbReserved4, rgbReserved5, rgbReserved6, rgbReserved7, rgbReserved8; }

    [StructLayout(LayoutKind.Sequential)] public struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }

    [StructLayout(LayoutKind.Sequential)] public struct APPBARDATA { public uint cbSize; public IntPtr hWnd; public uint uCallbackMessage, uEdge; public RECT rc; public int lParam; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct LOGFONT
    {
        public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
        public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NONCLIENTMETRICS
    {
        public uint cbSize; public int iBorderWidth, iScrollWidth, iScrollHeight, iCaptionWidth, iCaptionHeight;
        public LOGFONT lfCaptionFont;
        public int iSmCaptionWidth, iSmCaptionHeight;
        public LOGFONT lfSmCaptionFont;
        public int iMenuWidth, iMenuHeight;
        public LOGFONT lfMenuFont, lfStatusFont, lfMessageFont;
        public int iPaddedBorderWidth;
    }

    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize; public uint style; public WndProc lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName, lpszClassName;
        public IntPtr hIconSm;
    }

    // ------------------------------------------------------------- user32

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string? lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr DispatchMessage(ref MSG lpMsg);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int nExitCode);

    /// <summary>
    /// Posts to another thread's message queue. <see cref="PostQuitMessage"/> only ever reaches the
    /// *calling* thread, so anything that wants to stop the UI loop from a worker must use this.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();

    /// <summary>
    /// Makes <paramref name="hwnd"/> the foreground window even though it is WS_EX_NOACTIVATE.
    /// TrackPopupMenu silently refuses to show when the calling thread does not own the
    /// foreground window, so the input queues are briefly attached to borrow that ownership.
    /// </summary>
    public static void ForceForeground(IntPtr hwnd)
    {
        IntPtr foreground = GetForegroundWindow();
        uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        uint currentThread = GetCurrentThreadId();

        if (foregroundThread == 0 || foregroundThread == currentThread)
        {
            _ = SetForegroundWindow(hwnd);
            return;
        }

        _ = AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            _ = SetForegroundWindow(hwnd);
            _ = SetFocus(hwnd);
        }
        finally
        {
            _ = AttachThreadInput(currentThread, foregroundThread, false);
        }
    }
    [DllImport("user32.dll")] public static extern IntPtr SetActiveWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetFocus();
    [DllImport("user32.dll")] public static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    /// <summary>
    /// Sets the cursor shape for the window under the mouse. A window that never calls this keeps
    /// whatever shape was last set — measured here as the shell's "busy" ring, which is the
    /// spinning cursor users see over the widget.
    /// </summary>
    [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
    [DllImport("user32.dll")] public static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);
    [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>The DPI the shell's own metrics (SPI_GETNONCLIENTMETRICS) are expressed in.</summary>
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    public const int VK_LBUTTON = 0x01;
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

    [DllImport("user32.dll")] public static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);
    [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr hWnd, IntPtr uIDEvent);

    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")]
    public static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref NONCLIENTMETRICS pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

    [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
    [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint pcrKey, out byte pbAlpha, out uint pdwFlags);

    // ------------------------------------------------------------- shell32

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    /// <summary>
    /// Reports whether the shell wants notifications suppressed — a fullscreen D3D app,
    /// presentation mode, a locked session. The only reliable way for a popup parked over the
    /// taskbar to know it should get out of the way.
    /// </summary>
    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);

    public const int QUNS_NOT_PRESENT = 1;
    public const int QUNS_BUSY = 2;
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    public const int QUNS_PRESENTATION_MODE = 4;

    public static bool IsShellBusy()
    {
        if (SHQueryUserNotificationState(out int state) != 0) return false;   // 0 == S_OK
        return state is QUNS_NOT_PRESENT or QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public uint cbSize; public IntPtr hWnd; public uint uID, uFlags, uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }

    // --------------------------------------------------------------- gdi32

    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr ho);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);
    [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    // ------------------------------------------------------------ kernel32

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? lpModuleName);
    [DllImport("kernel32.dll")] public static extern uint GetLastError();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern ushort GetUserDefaultUILanguage();

    // --------------------------------------------------------------- utils

    public static string ClassNameOf(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool TryGetRect(IntPtr hWnd, out RECT rect)
    {
        rect = default;
        return hWnd != IntPtr.Zero && GetWindowRect(hWnd, out rect);
    }

    public static IntPtr FindChild(IntPtr parent, string className)
        => parent == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(parent, IntPtr.Zero, className, null);

    public static bool IsTaskbarAutoHide()
    {
        var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        var state = SHAppBarMessage(ABM_GETSTATE, ref data).ToInt64();
        return (state & ABS_AUTOHIDE) != 0;
    }

    /// <summary>Scale factor of a window's monitor (1.0 at 96 DPI).</summary>
    public static double DpiScaleOf(IntPtr hWnd)
    {
        uint dpi = hWnd != IntPtr.Zero ? GetDpiForWindow(hWnd) : 0;
        if (dpi == 0)
        {
            IntPtr dc = GetDC(IntPtr.Zero);
            dpi = dc != IntPtr.Zero ? (uint)GetDeviceCaps(dc, 88 /*LOGPIXELSX*/) : 96;
            if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc);
        }
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }
}
