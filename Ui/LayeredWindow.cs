using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using SimpleWeather.Core;
using SimpleWeather.Interop;

namespace SimpleWeather.Ui;

/// <summary>
/// A top-level, per-pixel translucent window that we rasterise ourselves with GDI+ and hand to
/// the compositor through <c>UpdateLayeredWindow</c>.
///
/// Why top-level and not a child of the taskbar: measured on Windows 10 19045,
/// <c>CreateWindowEx(WS_EX_LAYERED, WS_CHILD, parent = Shell_TrayWnd)</c> fails outright with
/// ERROR_INVALID_PARAMETER (87), and <c>UpdateLayeredWindow</c> on a plain child also fails
/// with 87. Layered child windows are simply not available here, so a translucent strip must
/// be a popup window parked over the taskbar.
///
/// One non-obvious rule: the DIB handed to UpdateLayeredWindow must hold <b>premultiplied</b>
/// alpha. Measured behaviour is dst = src + dst*(1-a); with straight alpha a 50% source
/// saturates to full intensity. That is why the surface is wrapped as Format32bppPArgb.
/// </summary>
internal abstract class LayeredWindow : IDisposable
{
    /// <summary>
    /// Shape shown over every widget surface. Registering a class cursor and answering
    /// WM_SETCURSOR both matter: without a cursor of its own a window leaves the shape the shell
    /// last set, and over these popups that is the busy ring — measured on the reference machine
    /// as IDC_WAIT (32514) over the strip, the card and the menu while the desktop correctly
    /// showed IDC_ARROW. The window is not hung; it simply never reset the cursor.
    /// </summary>
    protected static readonly IntPtr ArrowCursor = Win32.LoadCursor(IntPtr.Zero, Win32.IDC_ARROW);

    private readonly string _className;
    private readonly Win32.WndProc _wndProc;   // must stay rooted: the OS holds a raw pointer
    private readonly bool _noActivate;
    private IntPtr _hwnd;

    private IntPtr _memDc;
    private IntPtr _dib;
    private IntPtr _oldBitmap;
    private IntPtr _bits;
    private int _surfaceWidth = -1;
    private int _surfaceHeight = -1;

    private int _width;
    private int _height;
    private int _x;
    private int _y;
    private bool _visible;
    private bool _disposed;

    protected LayeredWindow(string className, int width, int height, bool noActivate = true)
    {
        _className = className;
        _width = width;
        _height = height;
        _noActivate = noActivate;
        _wndProc = WindowProc;
    }

    public IntPtr Handle => _hwnd;
    public int Width => _width;
    public int Height => _height;
    public int X => _x;
    public int Y => _y;
    public bool IsVisible => _visible;

    /// <summary>Registers the class and creates the window. Returns false on failure.</summary>
    public bool Create()
    {
        var wc = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = Win32.GetModuleHandle(null),
            lpszClassName = _className,
            hCursor = ArrowCursor,
        };

        Marshal.SetLastPInvokeError(0);
        ushort atom = Win32.RegisterClassEx(ref wc);
        int classErr = Marshal.GetLastPInvokeError();
        if (atom == 0 && classErr != 1410 /* ERROR_CLASS_ALREADY_EXISTS */)
        {
            Log.Write($"{_className}: RegisterClassEx failed err={classErr}");
            return false;
        }

        Marshal.SetLastPInvokeError(0);
        int exStyle = Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST
                      | (_noActivate ? Win32.WS_EX_NOACTIVATE : 0);

        _hwnd = Win32.CreateWindowEx(
            exStyle,
            _className, null, Win32.WS_POPUP,   // created hidden; SetVisible shows it
            _x, _y, _width, _height,
            IntPtr.Zero, IntPtr.Zero, Win32.GetModuleHandle(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            Log.Write($"{_className}: CreateWindowEx failed err={Marshal.GetLastPInvokeError()}");
            return false;
        }

        // Start hidden so a freshly created window never flashes at the origin; the layered
        // surface is still populated, UpdateLayeredWindow works on hidden windows.
        _visible = false;
        return true;
    }

    /// <summary>Moves and/or resizes, then repaints. Keeps the window out of the way when hidden.</summary>
    public void SetBounds(int x, int y, int width, int height)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (_x == x && _y == y && _width == width && _height == height) return;

        _x = x;
        _y = y;
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        Render();
    }

    public void Move(int x, int y) => SetBounds(x, y, _width, _height);

    /// <summary>Shows or hides without destroying, so the layered surface is preserved.</summary>
    public void SetVisible(bool visible)
    {
        if (_hwnd == IntPtr.Zero || _visible == visible) return;
        _visible = visible;
        Win32.ShowWindow(_hwnd, visible ? Win32.SW_SHOWNOACTIVATE : Win32.SW_HIDE);
        if (visible) Render();
    }

    /// <summary>Re-asserts topmost placement. The taskbar can steal z-order back.</summary>
    public void ReassertTopMost()
    {
        if (_hwnd == IntPtr.Zero || !_visible) return;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
    }

    /// <summary>Repaints the whole surface and pushes it to the compositor.</summary>
    public void Render()
    {
        if (_hwnd == IntPtr.Zero) return;

        EnsureSurface();

        int stride = _width * 4;
        using (var bitmap = new Bitmap(_width, _height, stride, PixelFormat.Format32bppPArgb, _bits))
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            // ClearType would write coloured sub-pixel fringes that look wrong once the
            // surrounding pixels are transparent; greyscale AA composites correctly. Grid fitting
            // is what keeps small text crisp, and the contrast bump thickens the stems slightly —
            // both matter because these surfaces sit on top of an arbitrary wallpaper.
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.TextContrast = 4;
            g.Clear(Color.Transparent);
            OnPaint(g, _width, _height);
        }

        IntPtr screenDc = Win32.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return;
        try
        {
            var dst = new Win32.POINT(_x, _y);
            var size = new Win32.SIZE { cx = _width, cy = _height };
            var src = new Win32.POINT(0, 0);
            var blend = new Win32.BLENDFUNCTION
            {
                BlendOp = Win32.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = Win32.AC_SRC_ALPHA,
            };

            if (!Win32.UpdateLayeredWindow(_hwnd, screenDc, ref dst, ref size, _memDc, ref src, 0, ref blend, Win32.ULW_ALPHA))
            {
                Log.Verbose($"{_className}: UpdateLayeredWindow failed err={Marshal.GetLastPInvokeError()}");
            }
        }
        finally
        {
            Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void EnsureSurface()
    {
        if (_memDc != IntPtr.Zero && _surfaceWidth == _width && _surfaceHeight == _height)
            return;

        ReleaseSurface();

        IntPtr screenDc = Win32.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return;
        try
        {
            _memDc = Win32.CreateCompatibleDC(screenDc);

            var bmi = new Win32.BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = _width;
            bmi.bmiHeader.biHeight = -_height;   // top-down, so row 0 is the top
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;     // BI_RGB

            _dib = Win32.CreateDIBSection(_memDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
            if (_dib != IntPtr.Zero)
                _oldBitmap = Win32.SelectObject(_memDc, _dib);

            _surfaceWidth = _width;
            _surfaceHeight = _height;
        }
        finally
        {
            Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void ReleaseSurface()
    {
        if (_oldBitmap != IntPtr.Zero && _memDc != IntPtr.Zero)
        {
            Win32.SelectObject(_memDc, _oldBitmap);
            _oldBitmap = IntPtr.Zero;
        }
        if (_dib != IntPtr.Zero)
        {
            Win32.DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }
        if (_memDc != IntPtr.Zero)
        {
            Win32.DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
        _bits = IntPtr.Zero;
        _surfaceWidth = -1;
        _surfaceHeight = -1;
    }

    /// <summary>Draws the window content. GDI+ coordinates are device pixels here.</summary>
    protected abstract void OnPaint(Graphics g, int width, int height);

    /// <summary>Receives raw window messages. Return value is passed back to the system.</summary>
    protected virtual IntPtr OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
        => Win32.DefWindowProc(_hwnd, msg, wParam, lParam);

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // WM_NCCREATE/WM_CREATE arrive *before* CreateWindowEx returns, so the handle must be
        // adopted here. Leaving it null meant DefWindowProc was handed a NULL hwnd, WM_NCCREATE
        // never returned TRUE, and CreateWindowEx failed with ERROR_INVALID_WINDOW_HANDLE (1400).
        if (_hwnd == IntPtr.Zero) _hwnd = hWnd;

        switch (msg)
        {
            case Win32.WM_ERASEBKGND:
                return new IntPtr(1);   // never erase: the layered surface owns every pixel

            case Win32.WM_SETCURSOR:
                // Hit code lives in the low word of lParam; wParam is the window under the cursor,
                // which for a forwarded (child) message is not us — none of these windows have
                // children, and anything forwarded must keep DefWindowProc's own choice.
                if (wParam == hWnd && (lParam.ToInt64() & 0xFFFF) == Win32.HTCLIENT)
                {
                    Win32.SetCursor(ArrowCursor);
                    return new IntPtr(1);
                }
                break;

            case Win32.WM_DESTROY:
                _hwnd = IntPtr.Zero;
                return IntPtr.Zero;
        }

        return OnMessage(msg, wParam, lParam);
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ReleaseSurface();

        if (_hwnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }
}
