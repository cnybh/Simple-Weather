namespace SimpleWeather.Ui;

/// <summary>
/// Pixel scaling for the surfaces the widget draws itself (strip, card, menu) and for the stock
/// controls of the settings window.
///
/// The app declares per-monitor-v2 DPI awareness in <c>app.manifest</c>, so Windows never
/// bitmap-stretches it: every coordinate, font size and band reservation here is a device pixel.
/// Without scaling that keeps the widget physically tiny on a high-DPI monitor (the manifest
/// deliberately gives up the blurry alternative), so all shell-facing sizes are derived from the
/// taskbar's DPI with 96 as the design baseline. Measured on the reference machine the taskbar
/// reports 96, i.e. scale 1.0 and everything below is a no-op there.
///
/// The card additionally gets <see cref="CardBoost"/> on top of the DPI factor: its 11-14px body
/// text is uncomfortably small to read from taskbar distance.
/// </summary>
internal static class Metrics
{
    /// <summary>Design baseline; every number in the drawing code is a 96-DPI pixel.</summary>
    public const int BaselineDpi = 96;

    /// <summary>
    /// Card enlargement on top of DPI scaling. Was 1.5 and is now 1.2, i.e. the card comes out at
    /// 80% of the size it had at 1.5 (user request). The 1.2 factor is design-space: it scales the
    /// surface, the layout maths and the GDI+ fonts together, so the card is uniformly smaller
    /// rather than a cropped or letterboxed version of the old one.
    /// </summary>
    public const float CardBoost = 1.2f;

    /// <summary>Monitors below 75% or above 400% are clamped rather than trusted.</summary>
    private const float MinScale = 0.75f;
    private const float MaxScale = 4f;

    /// <summary>Current device-pixel scale. 1.0 on a 96-DPI taskbar.</summary>
    public static float DpiScale { get; private set; } = 1f;

    /// <summary>Current DPI the scale came from, for logging and change detection.</summary>
    public static int Dpi { get; private set; } = BaselineDpi;

    /// <summary>Card scale: DPI scaling times <see cref="CardBoost"/>.</summary>
    public static float CardScale => DpiScale * CardBoost;

    /// <summary>Sets the scale from a monitor DPI.</summary>
    public static void SetDpi(int dpi)
    {
        Dpi = dpi > 0 ? dpi : BaselineDpi;
        DpiScale = Math.Clamp(Dpi / (float)BaselineDpi, MinScale, MaxScale);
    }

    /// <summary>Design pixels to device pixels.</summary>
    public static float Px(float design) => design * DpiScale;

    /// <summary>Design pixels to device pixels in card space (includes the 1.5x boost).</summary>
    public static float Card(float design) => design * CardScale;

    /// <summary>Rounds a design length to whole device pixels.</summary>
    public static int Round(float design) => (int)Math.Round(Px(design), MidpointRounding.AwayFromZero);

    /// <summary>Rounds a design length to whole device pixels in card space.</summary>
    public static int RoundCard(float design) => (int)Math.Round(Card(design), MidpointRounding.AwayFromZero);
}
