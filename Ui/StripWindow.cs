using System.Drawing;
using System.Drawing.Drawing2D;
using SimpleWeather.Core;
using SimpleWeather.Interop;

namespace SimpleWeather.Ui;

/// <summary>
/// The taskbar strip itself: weather glyph on the left, today's temperature range on the right.
///
/// The background is genuinely transparent (alpha 1/255, invisible to the eye) rather than a
/// sampled colour, so the taskbar's own translucency shows through. The near-zero alpha is
/// deliberate: a layered window only receives mouse input where alpha &gt; 0, and collapsing it
/// to 0 would leave only the glyph and text clickable.
/// </summary>
internal sealed class StripWindow : LayeredWindow
{
    /// <summary>Smallest non-zero alpha; invisible but still hit-testable.</summary>
    private const int HitTestAlpha = 1;

    /// <summary>Timer the host arms for the once-a-second taskbar follow.</summary>
    public const int WatchTimerId = 1;

    /// <summary>Timer the host arms for the fast "stay on top / keep the band" guard.</summary>
    public const int GuardTimerId = 4;

    /// <summary>
    /// Guard period. The shell raises Shell_TrayWnd over the strip and resets the button band
    /// whenever it re-lays-out the taskbar; measured while opening or closing the notification
    /// overflow flyout, the strip stayed covered from ~0.2 s until the next one-second follow tick
    /// (~0.7 s) — that gap is the "refresh" users see. A tick this short restores both before the
    /// user can notice, and the work per tick is two cheap window queries.
    /// </summary>
    public const int GuardIntervalMs = 100;

    private bool _hovering;
    private bool _pressed;

    private string _caption = "\u2014";
    private Bitmap? _glyph;

    public StripWindow(int width, int height) : base("SimpleWeatherStrip", width, height)
    {
    }

    public event EventHandler? Activated;
    public event EventHandler? ContextRequested;
    public event EventHandler? HoverChanged;

    /// <summary>Raised when the controller reports new weather state.</summary>
    public event EventHandler? WeatherUpdated;

    /// <summary>Raised once a second so the host can follow the taskbar.</summary>
    public event EventHandler? WatchTick;

    /// <summary>Raised several times a second so the host can reclaim z-order and the band.</summary>
    public event EventHandler? GuardTick;

    public bool IsHovering => _hovering;

    /// <summary>
    /// True when the taskbar itself is painted above this window. Only the taskbar is worth
    /// fighting: a fullscreen app legitimately covers the strip, and that case is handled by the
    /// once-a-second shell-busy check instead.
    /// </summary>
    public bool IsCoveredByTaskbar()
    {
        if (Handle == IntPtr.Zero || !IsVisible) return false;

        var centre = new Win32.POINT(X + (Width / 2), Y + (Height / 2));
        IntPtr hit = Win32.WindowFromPoint(centre);
        if (hit == IntPtr.Zero || hit == Handle) return false;

        // WindowFromPoint reports the deepest child under the cursor, so walk to the top level.
        IntPtr root = Win32.GetAncestor(hit, Win32.GA_ROOT);
        if (root == IntPtr.Zero) root = hit;

        string cls = Win32.ClassNameOf(root);
        return cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    /// <summary>Sets the rendered content.</summary>
    public void SetContent(string caption, Bitmap? glyph)
    {
        _caption = caption;
        _glyph?.Dispose();
        _glyph = glyph;
    }

    protected override void OnPaint(Graphics g, int width, int height)
    {
        // 1. Hit-test backing: one alpha step, imperceptible over the taskbar.
        using (var backing = new SolidBrush(Color.FromArgb(HitTestAlpha, 255, 255, 255)))
            g.FillRectangle(backing, 0, 0, width, height);

        // 2. Hover / pressed wash, genuinely translucent so it composites over the taskbar.
        if (_pressed || _hovering)
        {
            int alpha = _pressed ? 60 : 36;
            using var wash = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
            g.FillRectangle(wash, 0, 0, width, height);
        }

        // 3. Content, centred as a pair. Sizes scale with the DPI like the reserved band length,
        //    but the length available for the pair is the taskbar's own thickness — which we do not
        //    control — so the pair is shrunk to fit when the bar is narrower than the scaled content.
        const float GlyphDesign = 21f;
        const float GapDesign = 4f;
        const float PadDesign = 2f;

        float glyphSize = Metrics.Px(GlyphDesign);
        float gap = Metrics.Px(GapDesign);
        float pad = Metrics.Px(PadDesign);
        float fontSize = Theme.StripFontSize;

        SizeF Measure(float size)
        {
            using var probe = new Font(Theme.UiFontFamily, size, FontStyle.Regular, GraphicsUnit.Pixel);
            return g.MeasureString(_caption, probe, new PointF(0, 0), StringFormat.GenericTypographic);
        }

        SizeF textSize = Measure(fontSize);
        float contentWidth = (_glyph is not null ? glyphSize + gap : 0f) + (float)Math.Ceiling(textSize.Width);
        float available = Math.Max(8f, width - (pad * 2));

        if (contentWidth > available)
        {
            float fit = available / contentWidth;
            glyphSize = Math.Max(8f, glyphSize * fit);
            gap = Math.Max(1f, gap * fit);
            fontSize = Math.Max(7f, fontSize * fit);
            textSize = Measure(fontSize);
            contentWidth = (_glyph is not null ? glyphSize + gap : 0f) + (float)Math.Ceiling(textSize.Width);
        }

        int glyphPx = Math.Max(1, (int)Math.Round(glyphSize));
        int gapPx = Math.Max(1, (int)Math.Round(gap));
        int startX = Math.Max((int)pad, (int)((width - contentWidth) / 2f));
        int centerY = height / 2;

        if (_glyph is not null)
        {
            g.DrawImage(_glyph, new Rectangle(startX, centerY - (glyphPx / 2), glyphPx, glyphPx));
            startX += glyphPx + gapPx;
        }

        using var font = new Font(Theme.UiFontFamily, fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Theme.StripForeground);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        };
        var textRect = new RectangleF(startX, centerY - (textSize.Height / 2f), textSize.Width + 2, textSize.Height);
        g.DrawString(_caption, font, textBrush, textRect, format);
    }

    protected override IntPtr OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WeatherController.WmWeatherUpdated:
                WeatherUpdated?.Invoke(this, EventArgs.Empty);
                return IntPtr.Zero;

            case UiSynchronizationContext.WmInvoke:
                // Owns the UI thread's async continuations; see UiSynchronizationContext.
                UiSynchronizationContext.Installed?.Drain();
                return IntPtr.Zero;

            case Win32.WM_TIMER:
                if (wParam.ToInt64() == GuardTimerId) GuardTick?.Invoke(this, EventArgs.Empty);
                else WatchTick?.Invoke(this, EventArgs.Empty);
                return IntPtr.Zero;

            case Win32.WM_MOUSEMOVE:
                if (!_hovering)
                {
                    _hovering = true;
                    var tme = new Win32.TRACKMOUSEEVENT
                    {
                        cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.TRACKMOUSEEVENT>(),
                        dwFlags = 0x00000002, // TME_LEAVE
                        hwndTrack = Handle,
                    };
                    Win32.TrackMouseEvent(ref tme);
                    HoverChanged?.Invoke(this, EventArgs.Empty);
                    Render();
                }
                return IntPtr.Zero;

            case Win32.WM_MOUSELEAVE:
                _hovering = false;
                _pressed = false;
                HoverChanged?.Invoke(this, EventArgs.Empty);
                Render();
                return IntPtr.Zero;

            case Win32.WM_LBUTTONDOWN:
                _pressed = true;
                Render();
                return IntPtr.Zero;

            case Win32.WM_LBUTTONUP:
                _pressed = false;
                Render();
                Activated?.Invoke(this, EventArgs.Empty);
                return IntPtr.Zero;

            case Win32.WM_RBUTTONUP:
                ContextRequested?.Invoke(this, EventArgs.Empty);
                return IntPtr.Zero;
        }

        return base.OnMessage(msg, wParam, lParam);
    }

    public override void Dispose()
    {
        _glyph?.Dispose();
        _glyph = null;
        base.Dispose();
    }
}
