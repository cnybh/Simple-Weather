using System.Runtime.InteropServices;

namespace SimpleWeather.Interop;

/// <summary>
/// Control-level Win32 surface (stock controls, combo/list messages, fonts).
/// Kept separate from the core interop so each file stays readable.
/// </summary>
internal static partial class Win32
{
    // ------------------------------------------------------------ messages

    public const uint WM_SETFONT = 0x0030;
    public const uint WM_GETFONT = 0x0031;
    public const uint WM_CTLCOLORSTATIC = 0x0138;

    public const uint CB_ADDSTRING = 0x0143;
    public const uint CB_SETCURSEL = 0x014E;
    public const uint CB_GETCURSEL = 0x0147;
    public const uint CB_RESETCONTENT = 0x014B;

    public const uint LB_ADDSTRING = 0x0180;
    public const uint LB_RESETCONTENT = 0x0184;
    public const uint LB_SETCURSEL = 0x0186;
    public const uint LB_GETCURSEL = 0x0188;

    public const uint BM_SETCHECK = 0x00F1;
    public const uint BM_GETCHECK = 0x00F0;
    public const int BST_CHECKED = 1;
    public const int BST_UNCHECKED = 0;

    public const uint EM_SETLIMITTEXT = 0x00C5;

    public static readonly IntPtr TRUE = new(1);
    public static readonly IntPtr FALSE = IntPtr.Zero;

    // -------------------------------------------------------------- structs

    [StructLayout(LayoutKind.Sequential)]
    public struct CREATESTRUCT
    {
        public IntPtr lpCreateParams;
        public IntPtr hInstance;
        public IntPtr hMenu;
        public IntPtr hwndParent;
        public int cy, cx, y, x;
        public int style;
        public IntPtr lpszName, lpszClass;
        public uint dwExStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NMHDR
    {
        public IntPtr hwndFrom;
        public IntPtr idFrom;
        public uint code;
    }

    // -------------------------------------------------------------- imports

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFontIndirect(ref LOGFONT lplf);

    [DllImport("gdi32.dll")]
    public static extern uint SetTextColor(IntPtr hdc, uint color);

    [DllImport("gdi32.dll")]
    public static extern uint SetBkColor(IntPtr hdc, uint color);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(uint color);

    /// <summary>
    /// Scratch memory DC + a compatible bitmap of the given size, for painting into a raw HBITMAP.
    ///
    /// This avoids <c>Bitmap.GetHbitmap()</c> on purpose: that can only be released together with
    /// the managed Bitmap, so handing the handle to a static control leaks a GDI object.
    /// <paramref name="previous"/> is what <c>SelectObject</c> displaced and must be put back.
    /// </summary>
    public static IntPtr CreateScratchBitmap(int width, int height, out IntPtr dc, out IntPtr previous)
    {
        dc = IntPtr.Zero;
        previous = IntPtr.Zero;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return IntPtr.Zero;

        try
        {
            dc = CreateCompatibleDC(screenDc);
            if (dc == IntPtr.Zero) return IntPtr.Zero;

            IntPtr bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (bitmap == IntPtr.Zero)
            {
                _ = DeleteDC(dc);
                dc = IntPtr.Zero;
                return IntPtr.Zero;
            }

            previous = SelectObject(dc, bitmap);
            return bitmap;
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Undoes <see cref="CreateScratchBitmap"/>: restores the DC, then frees bitmap and DC.</summary>
    public static void ReleaseScratchBitmap(IntPtr bitmap, IntPtr dc, IntPtr previous)
    {
        if (bitmap == IntPtr.Zero) return;
        if (dc != IntPtr.Zero) _ = SelectObject(dc, previous);

        _ = DeleteObject(bitmap);
        if (dc != IntPtr.Zero) _ = DeleteDC(dc);
    }

    /// <summary>Replaces the bitmap a static control shows, deleting the one it replaced.</summary>
    public static void SetStaticImage(IntPtr staticControl, IntPtr bitmap)
    {
        IntPtr replaced = SendMessage(staticControl, STM_SETIMAGE, IMAGE_BITMAP, bitmap);
        if (replaced != IntPtr.Zero) _ = DeleteObject(replaced);
    }

    /// <summary>Removes and frees the bitmap a static control still holds.</summary>
    public static void ClearStaticImage(IntPtr staticControl)
    {
        IntPtr current = SendMessage(staticControl, STM_SETIMAGE, IMAGE_BITMAP, IntPtr.Zero);
        if (current != IntPtr.Zero) _ = DeleteObject(current);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ShellExecute(IntPtr hwnd, string? lpOperation, string lpFile,
        string? lpParameters, string? lpDirectory, int nShowCmd);

    /// <summary>Opens a URL or file with the shell's default handler.</summary>
    public static bool OpenWithShell(string target)
        => ShellExecute(IntPtr.Zero, "open", target, null, null, SW_SHOWNORMAL).ToInt64() > 32;

    public const uint MB_OK = 0x00000000;
    public const uint MB_ICONINFORMATION = 0x00000040;

    // ------------------------------------------------------------- helpers

    /// <summary>Reads a stock control's text into a managed string.</summary>
    public static string TextOf(IntPtr hWnd)
    {
        int length = GetWindowTextLength(hWnd);
        if (length <= 0) return string.Empty;
        var sb = new System.Text.StringBuilder(length + 2);
        _ = GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static int ComboSelection(IntPtr combo) => (int)SendMessage(combo, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero);

    public static void ComboSelect(IntPtr combo, int index) => _ = SendMessage(combo, CB_SETCURSEL, new IntPtr(index), IntPtr.Zero);

    public static void ComboAdd(IntPtr combo, string text)
    {
        // The control copies the string, so the native buffer is released immediately after.
        IntPtr buffer = Marshal.StringToHGlobalUni(text);
        try { _ = SendMessage(combo, CB_ADDSTRING, IntPtr.Zero, buffer); }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static int ListSelection(IntPtr list) => (int)SendMessage(list, LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero);

    public static void ListAdd(IntPtr list, string text)
    {
        IntPtr buffer = Marshal.StringToHGlobalUni(text);
        try { _ = SendMessage(list, LB_ADDSTRING, IntPtr.Zero, buffer); }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static void ListClear(IntPtr list) => _ = SendMessage(list, LB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);

    public static bool IsChecked(IntPtr control) => SendMessage(control, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == TRUE;

    public static void SetChecked(IntPtr control, bool value)
        => _ = SendMessage(control, BM_SETCHECK, value ? TRUE : FALSE, IntPtr.Zero);

    public static void SetFont(IntPtr control, IntPtr font) => _ = SendMessage(control, WM_SETFONT, font, TRUE);

    /// <summary>
    /// The shell's own dialog font, so the window looks native rather than themed.
    /// <paramref name="scale"/> multiplies the reported heights: <c>SPI_GETNONCLIENTMETRICS</c>
    /// answers for the system DPI, so a window on a different-DPI monitor needs the ratio.
    /// </summary>
    public static IntPtr CreateMessageFont(out LOGFONT logFont, float scale = 1f)
    {
        var metrics = new NONCLIENTMETRICS
        {
            cbSize = (uint)Marshal.SizeOf<NONCLIENTMETRICS>(),
        };

        if (!SystemParametersInfo(SPI_GETNONCLIENTMETRICS, metrics.cbSize, ref metrics, 0))
        {
            logFont = default;
            return IntPtr.Zero;
        }

        logFont = metrics.lfMessageFont;
        if (Math.Abs(scale - 1f) > 0.001f)
        {
            logFont.lfHeight = (int)Math.Round(logFont.lfHeight * scale);
            logFont.lfWidth = (int)Math.Round(logFont.lfWidth * scale);
        }

        return CreateFontIndirect(ref logFont);
    }
}
