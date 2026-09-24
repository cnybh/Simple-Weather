using System.Drawing;
using Microsoft.Win32;
using SimpleWeather.Core;

namespace SimpleWeather.Ui;

/// <summary>
/// Shell colours and fonts. Windows keeps two independent theme switches under one key:
/// <c>SystemUsesLightTheme</c> drives the taskbar, <c>AppsUseLightTheme</c> drives normal
/// windows. Reading only the latter is the classic mistake that leaves light content on a
/// dark taskbar.
///
/// The strip, the card and the menu are all <b>shell</b> surfaces — they hang off the taskbar and
/// are read next to the shell's own battery/network flyout and its context menus — so every one of
/// them follows <c>SystemUsesLightTheme</c>. Measured on the reference machine (Windows 10 22H2,
/// dark taskbar with light apps) the shell draws those surfaces as:
///
/// <list type="bullet">
/// <item>taskbar context menu: fill #2B2B2B, 1px #A0A0A0 border, white 12px text, #414141 hover,
///       #808080 separators inset 10px, 32px rows, square corners;</item>
/// <item>battery flyout: square corners, a #2B2B2B base tint (its translucency is DWM acrylic,
///       which a layered window cannot reproduce — see README §3).</item>
/// </list>
/// </summary>
internal static class Theme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Read and logged for diagnostics; the shell surfaces below only use the system flag.</summary>
    public static bool AppsUseLightTheme { get; private set; }

    public static bool SystemUsesLightTheme { get; private set; }

    public static void Refresh()
    {
        AppsUseLightTheme = ReadDword("AppsUseLightTheme");
        SystemUsesLightTheme = ReadDword("SystemUsesLightTheme");
    }

    private static bool ReadDword(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
            if (key?.GetValue(name) is int value) return value != 0;
        }
        catch
        {
            // Registry unavailable -> assume the Windows 10 default (dark shell).
        }

        return false;
    }

    /// <summary>
    /// GDI+ has weak font fallback, so the CJK-capable family is selected explicitly for the
    /// Chinese table instead of relying on substitution.
    /// </summary>
    public static string UiFontFamily => Loc.IsChinese ? "Microsoft YaHei UI" : "Segoe UI";

    // Font sizes are 96-DPI design values. The card and the menu additionally draw inside a scale
    // transform (see FlyoutWindow.OnPaint / MenuWindow.OnPaint), so their fonts stay in design
    // units; the strip has no transform, hence the explicit Metrics.Px below.
    /// <summary>Strip text size in device pixels; the strip shrinks it further if it must fit.</summary>
    public static float StripFontSize => Metrics.Px(15f);

    /// <summary>
    /// Extra size on top of the card's design sizes.
    ///
    /// The card surface is scaled by <see cref="Metrics.CardBoost"/>, so shrinking the card also
    /// shrank its type — which is what made it read as blurry. This factor buys the type back
    /// (1.15 at a 1.2 boost restores the 1.5-boost on-screen text size) while the panel itself
    /// stays at the smaller size that was asked for. The row heights around these fonts are sized
    /// for it; see the row constants in FlyoutWindow.
    /// </summary>
    private const float CardFontBoost = 1.15f;

    private static float CardFontSize(float design) => design * CardFontBoost;

    public static Font CardTitleFont() => new(UiFontFamily, CardFontSize(14f), FontStyle.Regular, GraphicsUnit.Pixel);
    public static Font CardValueFont() => new(UiFontFamily, CardFontSize(13f), FontStyle.Regular, GraphicsUnit.Pixel);
    public static Font CardLargeFont() => new(UiFontFamily, CardFontSize(34f), FontStyle.Regular, GraphicsUnit.Pixel);
    public static Font CardLabelFont() => new(UiFontFamily, CardFontSize(11f), FontStyle.Regular, GraphicsUnit.Pixel);

    /// <summary>
    /// The forecast table's type: two design sizes below <see cref="CardLabelFont"/>, so a date,
    /// a condition name and a temperature range all fit their columns without being ellipsised.
    /// The seven-day table is the one place where the card is genuinely short of horizontal room,
    /// so it gets its own size rather than shrinking the shared label font.
    /// </summary>
    public static Font CardForecastValueFont() => new(UiFontFamily, CardFontSize(11f), FontStyle.Regular, GraphicsUnit.Pixel);

    /// <summary>The forecast table's date column, two design sizes below <see cref="CardLabelFont"/>.</summary>
    public static Font CardForecastLabelFont() => new(UiFontFamily, CardFontSize(9f), FontStyle.Regular, GraphicsUnit.Pixel);

    /// <summary>
    /// Menu font, in design units. Windows draws menus with <c>NONCLIENTMETRICS.lfMenuFont</c>, which
    /// is Segoe UI at lfHeight -12 (9pt) on the reference machine, and menus are the one place where
    /// the shell's own font is noticeably smaller than the flyout body text.
    /// </summary>
    public static Font MenuFont() => new(UiFontFamily, 12f, FontStyle.Regular, GraphicsUnit.Pixel);

    // ------------------------------------------------------------- colours

    public static Color StripForeground => SystemUsesLightTheme
        ? Color.FromArgb(0x1A, 0x1A, 0x1A)
        : Color.FromArgb(0xFF, 0xFF, 0xFF);

    /// <summary>
    /// Flyout surface. Follows the shell theme so the card matches the battery/network flyouts it
    /// is read next to; #2B2B2B is the dark shell surface tone measured from the taskbar's own
    /// context menu, and is what the reference screenshot's flyout resolves to over a mid-grey
    /// wallpaper. Square corners and no acrylic: see <see cref="Draw.RoundedRect"/> callers.
    ///
    /// The alpha is 70% (179/255), not opaque: the card is a layered window, so
    /// <c>UpdateLayeredWindow</c> composites it against whatever is behind it and the wallpaper
    /// shows through — the closest a non-DWM-blurred surface gets to the shell's own acrylic. The
    /// 1px edge is kept precisely because the fill is translucent now: it is what stops the card
    /// from dissolving into a busy wallpaper.
    /// </summary>
    public static Color CardBackground => SystemUsesLightTheme
        ? Color.FromArgb(CardOpacity, 0xF0, 0xF0, 0xF0)
        : Color.FromArgb(CardOpacity, 0x2B, 0x2B, 0x2B);

    /// <summary>
    /// Card fill opacity as a byte; 255 = fully opaque, which is what the user settled on after
    /// trying 70/90/95. The constant is kept rather than inlined so the value stays greppable and
    /// the translucent variants (<see cref="Color.FromArgb(int, int, int, int)"/> on the shell
    /// tones) remain one edit away.
    /// </summary>
    public const int CardOpacity = 255;

    /// <summary>
    /// Hairline around the card. The shell's own flyouts rely on their shadow, but a layered
    /// window's shadow is left on the desktop when the surface is opaque, so a 1px edge is drawn
    /// too — the light value is the #A0A0A0 the shell uses for its menu borders.
    /// </summary>
    public static Color CardBorder => SystemUsesLightTheme
        ? Color.FromArgb(0xA0, 0xA0, 0xA0)
        : Color.FromArgb(0x3D, 0x3D, 0x3D);

    /// <summary>Colour of the soft drop shadow beneath the card.</summary>
    public static Color CardShadow => SystemUsesLightTheme
        ? Color.FromArgb(0x30, 0x00, 0x00, 0x00)
        : Color.FromArgb(0x60, 0x00, 0x00, 0x00);

    public static Color CardForeground => SystemUsesLightTheme
        ? Color.FromArgb(0x1A, 0x1A, 0x1A)
        : Color.FromArgb(0xFF, 0xFF, 0xFF);

    public static Color CardSubtle => SystemUsesLightTheme
        ? Color.FromArgb(0x5F, 0x5F, 0x5F)
        : Color.FromArgb(0xA8, 0xA8, 0xA8);

    public static Color CardSeparator => SystemUsesLightTheme
        ? Color.FromArgb(0xE6, 0xE6, 0xE6)
        : Color.FromArgb(0x3D, 0x3D, 0x3D);

    // ---------------------------------------------------------- menu colours
    // Values measured from this machine's own menus: the dark taskbar menu (hover #414141,
    // separator #808080) and the light desktop menu (#F0F0F0 fill with a lighter #F5F5F5 hover).

    /// <summary>Menu surface: opaque, square, 1px border — a plain Win32 menu, not a Fluent popup.</summary>
    public static Color MenuBackground => SystemUsesLightTheme
        ? Color.FromArgb(0xF0, 0xF0, 0xF0)
        : Color.FromArgb(0x2B, 0x2B, 0x2B);

    public static Color MenuForeground => SystemUsesLightTheme
        ? Color.FromArgb(0x00, 0x00, 0x00)
        : Color.FromArgb(0xFF, 0xFF, 0xFF);

    /// <summary>Menu frame, the shell's #A0A0A0 border in either theme.</summary>
    public static Color MenuBorder => Color.FromArgb(0xA0, 0xA0, 0xA0);

    /// <summary>Hovered row: a full-width lighter rectangle, no rounded corners.</summary>
    public static Color MenuHover => SystemUsesLightTheme
        ? Color.FromArgb(0xF5, 0xF5, 0xF5)
        : Color.FromArgb(0x41, 0x41, 0x41);

    /// <summary>Separator line, drawn 10px in from both edges as the shell does.</summary>
    public static Color MenuSeparator => SystemUsesLightTheme
        ? Color.FromArgb(0xA0, 0xA0, 0xA0)
        : Color.FromArgb(0x80, 0x80, 0x80);

    public static Color Accent => SystemUsesLightTheme
        ? Color.FromArgb(0x00, 0x5F, 0xB8)
        : Color.FromArgb(0x4C, 0xC2, 0xFF);
}
