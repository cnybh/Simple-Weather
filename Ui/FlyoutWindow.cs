using System.Drawing;
using System.Drawing.Drawing2D;
using SimpleWeather.Core;
using SimpleWeather.Interop;

namespace SimpleWeather.Ui;

/// <summary>Shared GDI+ helpers for the rounded, shadowed Fluent-style surfaces.</summary>
internal static class Draw
{
    /// <summary>Single line, clipped with an ellipsis when it does not fit.</summary>
    public static StringFormat LeftEllipsis() => new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoWrap,
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Near,
        Trimming = StringTrimming.EllipsisCharacter,
    };

    public static readonly StringFormat Left = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Near,
    };

    public static readonly StringFormat Right = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        Alignment = StringAlignment.Far,
        LineAlignment = StringAlignment.Near,
    };

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();

        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }

        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Soft drop shadow built from progressively larger, very faint rounded rectangles.
    /// A real Gaussian blur would mean pulling in a graphics stack this build deliberately avoids.
    /// The card itself is borderless, so the shadow is what separates it from the desktop.
    /// </summary>
    public static void Shadow(Graphics g, RectangleF card, float radius, Color color, int layers = 10)
    {
        int baseAlpha = color.A;
        for (int i = layers; i >= 1; i--)
        {
            // Falloff: each ring is fainter than the one inside it.
            int alpha = Math.Max(1, (int)(baseAlpha * (1.0 - ((double)i / (layers + 1))) / 2.0));
            var r = RectangleF.Inflate(card, i, i);
            using var path = RoundedRect(r, radius + i);
            using var brush = new SolidBrush(Color.FromArgb(alpha, color));
            g.FillPath(brush, path);
        }
    }
}

/// <summary>
/// The detail card opened from the strip: current conditions, humidity / wind / pressure and the
/// next three days. Drawn entirely with GDI+ onto a translucent layered surface.
///
/// Styling is the Windows 10 shell flyout, not a Fluent/Win11 one: square corners, one flat
/// opaque surface in the shell's tone, a 1px edge and a tight shadow, content separated by
/// hairlines rather than boxes. (Windows 10's own flyouts are additionally acrylic-blurred; a
/// layered window cannot do that, so the fill is the measured base tint instead.)
///
/// The card is anchored to the desktop, not to the strip: <see cref="Apply"/> parks it in the
/// bottom-right corner of the monitor work area so it never sits on the taskbar.
///
/// Every constant below is a 96-DPI design pixel. The surface is sized by
/// <see cref="Metrics.CardScale"/> and <c>OnPaint</c> installs the matching scale transform, so the
/// card is enlarged by <see cref="Metrics.CardBoost"/> and again with monitor DPI, while the layout
/// maths and the GDI+ fonts stay in design units and are rasterised at the final size — no bitmap
/// stretching.
/// </summary>
internal sealed class FlyoutWindow : LayeredWindow
{
    private const int ShadowPad = 16;
    private const float PadX = 22f;
    private const float PadTop = 16f;
    private const float PadBottom = 14f;

    /// <summary>Windows 10 flyouts have square corners; this is deliberately zero.</summary>
    private const float CardRadius = 0f;

    // Row heights, in design units, and their un-scaled values.
    //
    // They are fields rather than constants because the card has to fit the monitor's work area:
    // seven forecast rows plus four sections is taller than a 1080p work area once the DPI scale
    // is applied, and clipping the card off the top of the screen is worse than tightening it.
    // AdaptTo() shrinks the row scale until the whole card fits; with room to spare every value
    // below is exactly its design figure and the layout is unchanged.
    //
    // They are static because the card is a single instance and its row geometry is a property of
    // the surface, not of a particular window: the static drawing helpers below read them.

    private const float LocationRowBase = 28f;
    private const float DayRowBase = 34f;
    private const float FooterRowBase = 22f;

    /// <summary>
    /// Height of the humidity / wind / pressure row: a label above a value. Tightened from 50 to
    /// 44 and then 42 — three two-line columns, not a paragraph.
    /// </summary>
    private const float DetailRowBase = 42f;

    /// <summary>
    /// Space around a separator line. 20 was "give the sections air"; the user asked for the
    /// distance between the card's three areas to come down to about 40% of that, which puts it at
    /// 8 — a separator now reads as a hairline between blocks rather than as a gap of its own.
    /// </summary>
    private const float SeparatorGapBase = 8f;

    /// <summary>
    /// Space above every section (location / current / details / forecast). Reduced from 16 to 6
    /// together with <see cref="SeparatorGapBase"/>, to the same ~40% of the previous distance.
    /// </summary>
    private const float SectionGapBase = 6f;

    // --- current-conditions block -------------------------------------------------------------

    /// <summary>Current-conditions glyph, design pixels.</summary>
    private const float GlyphSize = 56f;

    /// <summary>Gap between the glyph column and the text column beside it.</summary>
    private const float GlyphTextGap = 14f;

    /// <summary>Left edge of the text column: past the glyph, with a gap.</summary>
    private const float CurrentTextX = GlyphSize + GlyphTextGap;

    // Row offsets inside the current block, and the smallest block that can hold them.
    private const float CurrentConditionDyBase = 50f;
    private const float CurrentFeelsDyBase = 80f;
    private const float CurrentHighLowDyBase = 102f;

    /// <summary>
    /// Smallest current block that does not clip its last row.
    ///
    /// This is derived, not chosen: the high/low row sits at <see cref="CurrentHighLowDyBase"/>
    /// and draws into a 20px rect, so the block must be at least that tall. It used to be a flat
    /// 108 against a 102+20 requirement, which silently clipped the bottom 14px of that line —
    /// visible as the "High/Low" row crowding the separator under it.
    /// </summary>
    private const float CurrentBlockMin = CurrentHighLowDyBase + 20f;

    /// <summary>
    /// Current block height when there is room. <see cref="CurrentBlockMin"/> keeps the last row
    /// inside the block; the extra is what stops the four rows from reading as a stack.
    /// </summary>
    private const float CurrentBlockBase = CurrentBlockMin + 6f;

    /// <summary>
    /// Floor for the row scale. Below this the rows would collide with their own type; a card that
    /// cannot fit even at this scale is accepted rather than compressed further, and the layout
    /// section of the README records that case.
    /// </summary>
    private const float MinRowScale = 0.55f;

    private static float _rowScale = 1f;
    private static float _locationRow = LocationRowBase;
    private static float _dayRow = DayRowBase;
    private static float _footerRow = FooterRowBase;
    private static float _detailRow = DetailRowBase;
    private static float _separatorGap = SeparatorGapBase;
    private static float _sectionGap = SectionGapBase;
    private static float _currentBlock = CurrentBlockBase;
    private static float _currentConditionDy = CurrentConditionDyBase;
    private static float _currentFeelsDy = CurrentFeelsDyBase;
    private static float _currentHighLowDy = CurrentHighLowDyBase;

    /// <summary>
    /// How many forecast days the card shows. Matches WeatherService.ForecastDays - 1 so the
    /// request and the drawing cannot drift apart.
    /// </summary>
    private const int DayCount = 7;

    /// <summary>
    /// Card width. The forecast rows carry a condition name between a date column and a
    /// temperature-range column, and that table is what sets the width: the seven-day layout has
    /// to fit "Thunderstorm" without ellipsising it at the 1.2 boost.
    /// </summary>
    private const int CardWidth = 340;

    /// <summary>Timer that watches for a click landing outside the card.</summary>
    private const int OutsideClickTimerId = 2;
    private const int OutsideClickIntervalMs = 120;

    /// <summary>
    /// How long the strip ignores clicks after the card closes.
    ///
    /// The card sits in the corner of the desktop while the strip is on the taskbar, so a user who
    /// just closed the card and reaches back for the strip can easily double-click straight
    /// through it. Without this the second click would instantly re-open what was just closed,
    /// which reads as the widget being unable to make up its mind.
    /// </summary>
    private const int ReopenSuppressMs = 1000;

    private WeatherController? _controller;

    /// <summary>Tick at which the card last closed; 0 while it is open. Gates re-opening.</summary>
    private long _hiddenAt;

    /// <summary>
    /// True while a click on the strip must be ignored because the card was just closed.
    /// Stamped in <see cref="HideCard"/> — covering the outside click and the click-on-strip case
    /// alike — and cleared by <see cref="ShowCard"/>.
    /// </summary>
    public bool IsReopenSuppressed
        => _hiddenAt != 0 && Environment.TickCount64 - _hiddenAt < ReopenSuppressMs;

    public FlyoutWindow()
        // NoActivate: the card must never steal focus from the user's work, and it keeps the
        // strip's own no-activate behaviour consistent.
        : base("SimpleWeatherFlyout", WidthPx, ContentHeightPx() + (ShadowPadPx * 2), noActivate: true)
    {
    }

    /// <summary>Design-space content height with the current row scale applied.</summary>
    private static float ContentHeightUnits() =>
        PadTop + _locationRow + _sectionGap + _currentBlock
        + _separatorGap + 1 + _separatorGap
        + _detailRow
        + _separatorGap + 1 + _separatorGap
        + (_dayRow * DayCount)
        + _sectionGap + _footerRow + PadBottom;

    private static int ContentHeightPx() => Metrics.RoundCard(ContentHeightUnits());

    private static int WidthPx => Metrics.RoundCard(CardWidth);
    private static int ShadowPadPx => Metrics.RoundCard(ShadowPad);

    /// <summary>
    /// Fits the card inside <paramref name="availablePx"/> by shrinking the row scale, never below
    /// <see cref="MinRowScale"/>.
    ///
    /// Seven forecast rows make the card taller than a work area can be once the DPI boost is
    /// applied (measured: 612×1105 device pixels at 144 DPI, against a 1040px work area). The
    /// alternative to tightening is a card whose top is off the screen, so the rows give way —
    /// first the gaps, then the row heights, all by the same factor, which keeps the layout
    /// proportional. Font sizes are untouched: text that is too small to read is a worse outcome
    /// than text with less air around it.
    ///
    /// The solution is searched rather than derived because the scale feeds back into the height
    /// through both the rows and the shadow padding.
    /// </summary>
    private static void AdaptTo(int availablePx)
    {
        _rowScale = 1f;
        ApplyRowScale(_rowScale);

        if (availablePx <= 0) return;
        if (TotalHeightPx() <= availablePx) return;

        // Smallest scale that still fits: walk the scale down until the card stops exceeding the
        // work area. 25 steps is fine-grained enough that the result is within a pixel or two of
        // the ideal, and this runs on show and on a DPI or work-area change, not per frame.
        for (float s = 0.99f; s >= MinRowScale; s -= 0.01f)
        {
            ApplyRowScale(s);
            if (TotalHeightPx() <= availablePx)
            {
                _rowScale = s;
                return;
            }
        }

        _rowScale = MinRowScale;
        ApplyRowScale(_rowScale);
    }

    /// <summary>Window height in device pixels at the current row scale.</summary>
    private static int TotalHeightPx() => ContentHeightPx() + (ShadowPadPx * 2);

    /// <summary>Writes one row scale into every row height and offset the drawing code reads.</summary>
    private static void ApplyRowScale(float s)
    {
        _locationRow = LocationRowBase * s;
        _dayRow = DayRowBase * s;
        _footerRow = FooterRowBase * s;
        _detailRow = DetailRowBase * s;
        _separatorGap = SeparatorGapBase * s;
        _sectionGap = SectionGapBase * s;

        _currentConditionDy = CurrentConditionDyBase * s;
        _currentFeelsDy = CurrentFeelsDyBase * s;
        _currentHighLowDy = CurrentHighLowDyBase * s;

        // The block always stays tall enough for its own last row, whatever the scale does to the
        // offsets: 102+20 at scale 1, and never less than the scaled requirement.
        _currentBlock = Math.Max(CurrentBlockMin * s, _currentHighLowDy + 20f);
    }

    /// <summary>
    /// Re-reads state from the controller, re-anchors the card, and repaints.
    ///
    /// The card is parked in the bottom-right corner of the monitor's <b>work area</b>. It used to
    /// open next to the strip (<c>PositionNear</c>); it is now anchored to the desktop instead. The
    /// work area — not the full monitor rectangle — is what keeps it out of the taskbar (and out of
    /// an auto-hidden bar), so the panel never lands on the bar.
    ///
    /// Re-anchoring on every call is not just for the first show: the work area shrinks when the
    /// taskbar grows a second row, and a monitor or DPI change moves it. <c>SetBounds</c> is a
    /// no-op when nothing moved, so the steady state costs nothing.
    /// </summary>
    public void Apply(WeatherController controller)
    {
        _controller = controller;

        var info = TaskbarBand.Probe();

        // Tighten the rows first — the card's height depends on them — then place the card so its
        // bottom-right corner lands on the work-area corner.
        if (info is not null) AdaptTo(info.WorkArea.Height);

        int width = WidthPx;
        int height = TotalHeightPx();

        int x = X;
        int y = Y;
        if (info is not null)
        {
            // The window is wider than the card by ShadowPad on every side, so the offset added
            // back here is what puts the card's *visible* edge — not the window's transparent
            // border — on the corner. The shadow then hangs outside the work area instead of inside
            // it; next to a taskbar that means its outermost, faintest rings sit behind the bar,
            // which is not noticeable and is the price of a corner-accurate card.
            x = info.WorkArea.Right - width + ShadowPadPx;
            y = info.WorkArea.Bottom - height + ShadowPadPx;
        }

        // SetBounds re-renders internally, so the Render below only runs on the no-op path.
        if (X != x || Y != y || Width != width || Height != height)
        {
            SetBounds(x, y, width, height);
            return;
        }

        Render();
    }

    protected override void OnPaint(Graphics g, int width, int height)
    {
        // Everything from here on is written in 96-DPI design units; the transform maps them onto
        // the real surface, and GDI+ rasterises the fonts at the resulting size.
        float s = Metrics.CardScale;
        g.ScaleTransform(s, s);

        float w = width / s;
        float h = height / s;
        var card = new RectangleF(ShadowPad, ShadowPad, w - (ShadowPad * 2), h - (ShadowPad * 2));

        // No drop shadow: it was what separated an opaque card from the desktop, but the fill is
        // translucent now and a translucent panel cannot have a convincing shadow drawn *under*
        // it — the shadow shows through the panel and reads as a dark vignette inside the card.
        // The 1px edge below does the separating job instead.

        // Translucent surface with a 1px edge.
        using (var path = Draw.RoundedRect(card, CardRadius))
        using (var fill = new SolidBrush(Theme.CardBackground))
        {
            g.FillPath(fill, path);
        }

        // The edge stays a true hairline: one device pixel, not one design pixel.
        using (var pen = new Pen(Theme.CardBorder, 1f / s))
        {
            var edge = RectangleF.Inflate(card, -0.5f / s, -0.5f / s);
            g.DrawRectangle(pen, edge.X, edge.Y, edge.Width, edge.Height);
        }

        float left = card.Left + PadX;
        float right = card.Right - PadX;
        float contentWidth = right - left;
        float y = card.Top + PadTop;

        var controller = _controller;
        var snapshot = controller?.Snapshot;

        y = DrawHeader(g, left, contentWidth, y, snapshot);
        y = DrawCurrent(g, left, contentWidth, y, controller, snapshot);
        y = DrawSeparator(g, left, right, y);
        y = DrawDetails(g, left, contentWidth, y, snapshot);
        y = DrawSeparator(g, left, right, y);
        y = DrawForecast(g, left, right, contentWidth, y, controller, snapshot);

        DrawFooter(g, left, right, contentWidth, y, snapshot);
    }

    // -------------------------------------------------------------- sections

    private static float DrawHeader(Graphics g, float left, float contentWidth, float y, WeatherSnapshot? snapshot)
    {
        using var font = Theme.CardTitleFont();
        using var brush = new SolidBrush(Theme.CardForeground);

        string location = snapshot?.Location.DisplayName ?? Loc.T("App.Name");
        g.DrawString(location, font, brush, new RectangleF(left, y, contentWidth, _locationRow), Draw.LeftEllipsis());

        return y + _locationRow + _sectionGap;
    }

    private static float DrawCurrent(Graphics g, float left, float contentWidth, float y, WeatherController? controller, WeatherSnapshot? snapshot)
    {
        var unit = controller?.Settings.Unit ?? TemperatureUnit.Celsius;

        if (snapshot is null)
        {
            string status = Loc.T(string.IsNullOrEmpty(controller?.StatusKey) ? "Status.Loading" : controller!.StatusKey);
            using var glyph = WeatherIcons.Render(WeatherKind.Unknown, true, Theme.SystemUsesLightTheme, (int)GlyphSize);
            g.DrawImage(glyph, left, y + ((_currentBlock - GlyphSize) / 2f), GlyphSize, GlyphSize);

            using var font = Theme.CardValueFont();
            using var brush = new SolidBrush(Theme.CardSubtle);
            // Centred on the block, like the loaded layout's headline row.
            g.DrawString(status, font, brush,
                new RectangleF(left + CurrentTextX, y + 40, contentWidth - CurrentTextX, 26), Draw.Left);
            return y + _currentBlock + _separatorGap;
        }

        // The icon gets its own vertical centre in the block; the four text rows beside it are
        // positioned by the Current*Dy offsets below, which are what keep them from crowding each
        // other.
        float glyphTop = y + ((_currentBlock - GlyphSize) / 2f);

        using (var glyph = WeatherIcons.Render(snapshot.CurrentCode, snapshot.IsDay, Theme.SystemUsesLightTheme, (int)GlyphSize))
            g.DrawImage(glyph, left, glyphTop, GlyphSize, GlyphSize);

        float tx = left + CurrentTextX;
        float textWidth = Math.Max(40f, contentWidth - CurrentTextX);

        // 1. Current temperature — the block's headline.
        using (var font = Theme.CardLargeFont())
        using (var brush = new SolidBrush(Theme.CardForeground))
        {
            g.DrawString(TemperatureUnits.Format(snapshot.TemperatureC, unit), font, brush,
                new RectangleF(tx, y, textWidth, 46), Draw.Left);
        }

        // 2. Condition ("Overcast").
        using (var font = Theme.CardValueFont())
        using (var brush = new SolidBrush(Theme.CardSubtle))
        {
            g.DrawString(WeatherIcons.Describe(snapshot.CurrentCode), font, brush,
                new RectangleF(tx + 2, y + _currentConditionDy, textWidth, 26), Draw.LeftEllipsis());
        }

        // 3 & 4. "Feels like" and "High / Low".
        using (var font = Theme.CardLabelFont())
        using (var brush = new SolidBrush(Theme.CardSubtle))
        {
            string line1 = $"{Loc.T("Label.FeelsLike")} {TemperatureUnits.Format(snapshot.ApparentC, unit)}";
            string line2 = $"{Loc.T("Label.High")} {TemperatureUnits.Format(snapshot.TodayMaxC, unit)}"
                           + $"   {Loc.T("Label.Low")} {TemperatureUnits.Format(snapshot.TodayMinC, unit)}";
            g.DrawString(line1, font, brush, new RectangleF(tx + 2, y + _currentFeelsDy, textWidth, 20), Draw.LeftEllipsis());
            g.DrawString(line2, font, brush, new RectangleF(tx + 2, y + _currentHighLowDy, textWidth, 20), Draw.LeftEllipsis());
        }

        return y + _currentBlock + _separatorGap;
    }

    private static float DrawSeparator(Graphics g, float left, float right, float y)
    {
        using var pen = new Pen(Theme.CardSeparator, 1f);
        g.DrawLine(pen, left, y, right, y);
        return y + 1 + _separatorGap;
    }

    private static float DrawDetails(Graphics g, float left, float contentWidth, float y, WeatherSnapshot? snapshot)
    {
        float column = contentWidth / 3f;
        string[] labels = [Loc.T("Label.Humidity"), Loc.T("Label.Wind"), Loc.T("Label.Pressure")];
        string[] values =
        [
            snapshot is null ? "--" : $"{snapshot.Humidity}%",
            snapshot is null ? "--" : $"{snapshot.WindKph:0.#} km/h",
            snapshot is null ? "--" : $"{snapshot.PressureHpa:0} hPa",
        ];

        for (int i = 0; i < 3; i++)
        {
            float cx = left + (column * i);

            using (var font = Theme.CardLabelFont())
            using (var brush = new SolidBrush(Theme.CardSubtle))
                g.DrawString(labels[i], font, brush, new RectangleF(cx, y, column - 6, 18), Draw.Left);

            using (var font = Theme.CardValueFont())
            using (var brush = new SolidBrush(Theme.CardForeground))
                g.DrawString(values[i], font, brush, new RectangleF(cx, y + 17, column - 6, 22), Draw.Left);
        }

        return y + _detailRow + _separatorGap;
    }

    /// <summary>Forecast rows: date, glyph, condition, temperature range.</summary>
    private static float DrawForecast(Graphics g, float left, float right, float contentWidth, float y, WeatherController? controller, WeatherSnapshot? snapshot)
    {
        var unit = controller?.Settings.Unit ?? TemperatureUnit.Celsius;

        if (snapshot is null || snapshot.Days.Count == 0)
        {
            using var font = Theme.CardValueFont();
            using var brush = new SolidBrush(Theme.CardSubtle);
            g.DrawString(Loc.T("Label.NextDays"), font, brush, new RectangleF(left, y + 6, contentWidth, 24), Draw.Left);
            return y + (_dayRow * DayCount) + _sectionGap;
        }

        var culture = Loc.IsChinese ? System.Globalization.CultureInfo.GetCultureInfo("zh-CN")
                                    : System.Globalization.CultureInfo.CurrentCulture;
        // "Sat 9/26" in English, "9/26 周六" in Chinese — the latter stays compact.
        string pattern = Loc.IsChinese ? "M/d ddd" : "ddd M/d";

        // The forecast table is the one place on the card that is short of horizontal room: seven
        // rows, each with a date, a condition name and a temperature range. Its type is two design
        // sizes below the shared label font (Theme.CardForecastValueFont / CardForecastLabelFont)
        // so all three columns fit whole rather than being ellipsised, and the date/range columns
        // are sized for the widest value each can hold at that size.
        const float glyphSize = 26f;
        const float dateWidth = 62f;
        const float rangeWidth = 78f;

        float dateX = left + glyphSize + 8;
        float textX = dateX + dateWidth;
        float textWidth = Math.Max(20f, right - rangeWidth - 8 - textX);

        for (int i = 0; i < DayCount; i++)
        {
            float rowY = y + (i * _dayRow);

            if (i >= snapshot.Days.Count) break;
            var day = snapshot.Days[i];

            using (var glyph = WeatherIcons.Render(day.WeatherCode, true, Theme.SystemUsesLightTheme, (int)glyphSize))
                g.DrawImage(glyph, left, rowY + ((_dayRow - glyphSize) / 2f), glyphSize, glyphSize);

            using (var font = Theme.CardForecastLabelFont())
            using (var brush = new SolidBrush(Theme.CardSubtle))
                g.DrawString(day.Date.ToString(pattern, culture), font, brush,
                    new RectangleF(dateX, rowY + 9, dateWidth, 18), Draw.Left);

            using (var font = Theme.CardForecastValueFont())
            using (var brush = new SolidBrush(Theme.CardForeground))
            {
                g.DrawString(WeatherIcons.Describe(day.WeatherCode), font, brush,
                    new RectangleF(textX, rowY + 8, textWidth, 22), Draw.LeftEllipsis());
                g.DrawString(TemperatureUnits.FormatRange(day.MinC, day.MaxC, unit), font, brush,
                    new RectangleF(right - rangeWidth, rowY + 8, rangeWidth, 22), Draw.Right);
            }
        }

        return y + (_dayRow * DayCount) + _sectionGap;
    }

    private static void DrawFooter(Graphics g, float left, float right, float contentWidth, float y, WeatherSnapshot? snapshot)
    {
        using var font = Theme.CardLabelFont();
        using var brush = new SolidBrush(Theme.CardSubtle);

        string updated = snapshot is null
            ? string.Empty
            : $"{Loc.T("Label.Updated")} {snapshot.ObservedAt.ToLocalTime():HH:mm}";

        // "Simple Weather" is wider than the "Open-Meteo" it replaced, so the right slot grew;
        // LeftEllipsis is still not used because a clipped product name would be worse than a
        // slightly wider rect, and the rect cannot collide with the timestamp on the left.
        const float RightSlot = 130f;

        g.DrawString(updated, font, brush, new RectangleF(left, y, contentWidth - RightSlot - 8, _footerRow), Draw.LeftEllipsis());
        g.DrawString(Loc.T("App.Name"), font, brush, new RectangleF(right - RightSlot, y, RightSlot, _footerRow), Draw.Right);
    }

    // ---------------------------------------------------------------- input

    protected override IntPtr OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32.WM_TIMER:
                if (wParam.ToInt64() == OutsideClickTimerId && IsClickOutside()) HideCard();
                return IntPtr.Zero;
        }

        return base.OnMessage(msg, wParam, lParam);
    }

    /// <summary>
    /// The card is WS_EX_NOACTIVATE, so it never receives WM_ACTIVATE and cannot rely on losing
    /// focus to dismiss itself. Polling the left mouse button and testing it against the card
    /// rectangle is the reliable equivalent.
    /// </summary>
    private bool IsClickOutside()
    {
        if ((Win32.GetAsyncKeyState(Win32.VK_LBUTTON) & 0x8000) == 0) return false;
        if (!Win32.GetCursorPos(out var pt)) return false;

        return pt.X < X || pt.X >= X + Width || pt.Y < Y || pt.Y >= Y + Height;
    }

    /// <summary>
    /// Shows the card and arms the outside-click watcher.
    ///
    /// There is deliberately no auto-dismiss timer: the card stays until it is dismissed by a click
    /// on the strip or a click outside it. (An earlier revision closed it after five seconds; that
    /// was removed at the user's request.)
    /// </summary>
    public void ShowCard()
    {
        SetVisible(true);
        _hiddenAt = 0;

        Win32.SetTimer(Handle, OutsideClickTimerId, OutsideClickIntervalMs, IntPtr.Zero);
    }

    /// <summary>
    /// Hides the card, disarms the outside-click watcher, and starts the re-open grace period.
    ///
    /// Stamping the close here (rather than at the call sites) is what makes the grace period
    /// cover every way the card can go away: a click outside it and a click on the strip itself.
    /// Calling it twice is harmless — the stamp simply moves, and an already hidden window stays
    /// hidden.
    /// </summary>
    public void HideCard()
    {
        Win32.KillTimer(Handle, new IntPtr(OutsideClickTimerId));
        SetVisible(false);
        _hiddenAt = Environment.TickCount64;
    }
}
