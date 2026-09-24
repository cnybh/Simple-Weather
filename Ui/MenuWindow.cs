using System.Drawing;
using SimpleWeather.Core;
using SimpleWeather.Interop;

namespace SimpleWeather.Ui;

/// <summary>One row of the context menu. A null <paramref name="CommandId"/> renders a separator.</summary>
internal sealed record MenuEntry(string Text, int? CommandId);

/// <summary>
/// The strip's context menu, drawn ourselves as a layered popup.
///
/// Why not TrackPopupMenu: it only appears when the calling thread owns the foreground window,
/// and the strip is WS_EX_NOACTIVATE, so the foreground had to be forced with AttachThreadInput.
/// That had a nasty side effect — leaving our process as the foreground owner dragged *every*
/// window we own to the front, so the settings window would pop out of nowhere when the user
/// only right-clicked the strip. Drawing the menu removes the need for any foreground juggling.
///
/// The drawing reproduces the plain Windows 10 menu the shell itself shows over the taskbar
/// (measured on the reference machine): square corners, opaque #2B2B2B fill with a 1px #A0A0A0
/// frame, the system menu font, white text, a full-width lighter hover row and 1px separators
/// inset 10px. Nothing here is rounded, translucent or accent-tinted — that was the Fluent look
/// this replaced.
/// </summary>
internal sealed class MenuWindow : LayeredWindow
{
    private const int Width_ = 186;
    private const int RowHeight = 32;
    private const int SeparatorHeight = 9;
    private const int PadY = 6;
    private const int TextPadX = 14;

    /// <summary>Measured inset of the shell's menu separators from both edges.</summary>
    private const int SeparatorInset = 10;

    private const int OutsideClickTimerId = 3;
    private const int OutsideClickIntervalMs = 100;

    private List<MenuEntry> _entries = [];
    private int _hoverIndex = -1;

    public MenuWindow() : base("SimpleWeatherMenu", WidthPx, Metrics.Round(100f), noActivate: true)
    {
    }

    /// <summary>Width in device pixels, for the host's on-screen clamping.</summary>
    public static int WidthPx => Metrics.Round(Width_);

    /// <summary>Height the given entries need, in device pixels.</summary>
    public static int MeasureHeight(IReadOnlyList<MenuEntry> entries)
        => Metrics.Round((PadY * 2) + entries.Sum(e => e.CommandId is null ? SeparatorHeight : RowHeight));

    /// <summary>Raised with the chosen command id. Not raised when the menu is dismissed.</summary>
    public event EventHandler<int>? ItemChosen;

    /// <summary>Sizes, positions and shows the menu with the given entries.</summary>
    public void ShowMenu(int x, int y, IReadOnlyList<MenuEntry> entries)
    {
        _entries = [.. entries];
        _hoverIndex = -1;

        SetBounds(x, y, WidthPx, MeasureHeight(_entries));
        Render();
        SetVisible(true);

        // Keep the menu inside the monitor it was opened on.
        Win32.SetTimer(Handle, OutsideClickTimerId, OutsideClickIntervalMs, IntPtr.Zero);
    }

    public void HideMenu()
    {
        Win32.KillTimer(Handle, new IntPtr(OutsideClickTimerId));
        SetVisible(false);
    }

    /// <summary>Top-left corner the menu should use so it stays on screen.</summary>
    public static Point ClampToMonitor(int x, int y, int width, int height)
    {
        IntPtr monitor = Win32.MonitorFromPoint(new Win32.POINT(x, y), Win32.MONITOR_DEFAULTTONEAREST);
        var mi = new Win32.MONITORINFOEX
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFOEX>(),
            szDevice = string.Empty,
        };
        if (!Win32.GetMonitorInfo(monitor, ref mi)) return new Point(x, y);

        var work = mi.rcWork;
        int nx = Math.Clamp(x, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4));
        int ny = Math.Clamp(y, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - height - 4));
        return new Point(nx, ny);
    }

    protected override void OnPaint(Graphics g, int width, int height)
    {
        // Design units, mapped to device pixels by the DPI transform (see Metrics).
        float s = Metrics.DpiScale;
        g.ScaleTransform(s, s);
        float w = width / s;
        float h = height / s;
        float hair = 1f / s;   // a true one-device-pixel frame at any DPI

        // Opaque fill, square corners, 1px frame — exactly the shell's own menu construction.
        using (var fill = new SolidBrush(Theme.MenuBackground))
            g.FillRectangle(fill, 0, 0, w, h);

        using (var frame = new Pen(Theme.MenuBorder, hair))
            g.DrawRectangle(frame, hair / 2f, hair / 2f, w - hair, h - hair);

        using var font = Theme.MenuFont();
        float y = PadY;

        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];

            if (entry.CommandId is null)
            {
                using var pen = new Pen(Theme.MenuSeparator, hair);
                float lineY = y + (SeparatorHeight / 2f);
                g.DrawLine(pen, SeparatorInset, lineY, w - SeparatorInset, lineY);
                y += SeparatorHeight;
                continue;
            }

            if (i == _hoverIndex)
            {
                // Full-width row inside the frame, matching the measured highlight rectangle
                // (x1610..1916 inside a border at 1608/1918).
                using var hover = new SolidBrush(Theme.MenuHover);
                g.FillRectangle(hover, hair, y, w - (hair * 2), RowHeight);
            }

            using var brush = new SolidBrush(Theme.MenuForeground);
            g.DrawString(entry.Text, font, brush,
                new RectangleF(TextPadX, y + ((RowHeight - 16) / 2f), w - (TextPadX * 2), 16), Draw.LeftEllipsis());

            y += RowHeight;
        }
    }

    protected override IntPtr OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32.WM_MOUSEMOVE:
                UpdateHover(lParam);
                return IntPtr.Zero;

            case Win32.WM_LBUTTONUP:
            {
                int index = IndexAt(lParam);
                if (index >= 0 && _entries[index].CommandId is int command)
                {
                    HideMenu();
                    ItemChosen?.Invoke(this, command);
                }
                return IntPtr.Zero;
            }

            case Win32.WM_TIMER:
                if (wParam.ToInt64() == OutsideClickTimerId && IsClickOutside())
                {
                    // A click anywhere else dismisses without choosing.
                    HideMenu();
                }
                return IntPtr.Zero;
        }

        return base.OnMessage(msg, wParam, lParam);
    }

    private void UpdateHover(IntPtr lParam)
    {
        int index = IndexAt(lParam);
        if (index == _hoverIndex) return;
        _hoverIndex = index;
        Render();
    }

    private int IndexAt(IntPtr lParam)
    {
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        if (x < 0 || x >= Width) return -1;

        // The message carries device pixels; the row table is in design units.
        float design = y / Metrics.DpiScale;
        float cursor = PadY;
        for (int i = 0; i < _entries.Count; i++)
        {
            float rowHeight = _entries[i].CommandId is null ? SeparatorHeight : RowHeight;
            if (design >= cursor && design < cursor + rowHeight) return i;
            cursor += rowHeight;
        }

        return -1;
    }

    private bool IsClickOutside()
    {
        if ((Win32.GetAsyncKeyState(Win32.VK_LBUTTON) & 0x8000) == 0
            && (Win32.GetAsyncKeyState(0x02 /* VK_RBUTTON */) & 0x8000) == 0)
        {
            return false;
        }

        if (!Win32.GetCursorPos(out var pt)) return false;
        return pt.X < X || pt.X >= X + Width || pt.Y < Y || pt.Y >= Y + Height;
    }
}
