using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SimpleWeather.Core;

namespace SimpleWeather.Ui;

/// <summary>Colour set for the vector weather glyphs.</summary>
internal sealed record WeatherPalette(
    Color Sun, Color Cloud, Color CloudDark, Color Rain, Color Snow, Color Bolt, Color Fog)
{
    public static readonly WeatherPalette Dark = new(
        Sun: Color.FromArgb(0xFF, 0xC1, 0x07),
        Cloud: Color.FromArgb(0xEC, 0xEF, 0xF1),
        CloudDark: Color.FromArgb(0xA9, 0xB4, 0xBD),
        Rain: Color.FromArgb(0x5A, 0xBC, 0xFA),
        Snow: Color.FromArgb(0xFF, 0xFF, 0xFF),
        Bolt: Color.FromArgb(0xFF, 0xD1, 0x4A),
        Fog: Color.FromArgb(0xC2, 0xC9, 0xCE));

    public static readonly WeatherPalette Light = new(
        Sun: Color.FromArgb(0xF5, 0xA6, 0x23),
        Cloud: Color.FromArgb(0x8A, 0x93, 0x9C),
        CloudDark: Color.FromArgb(0x6B, 0x74, 0x7D),
        Rain: Color.FromArgb(0x1E, 0x88, 0xE5),
        Snow: Color.FromArgb(0x54, 0x6E, 0x7A),
        Bolt: Color.FromArgb(0xE6, 0xA4, 0x00),
        Fog: Color.FromArgb(0x7A, 0x84, 0x8C));

    public static WeatherPalette For(bool lightTheme) => lightTheme ? Light : Dark;
}

/// <summary>
/// GDI+ port of the vector weather glyphs, drawn on a 24x24 design grid and scaled to the
/// requested pixel size. GDI+ is used rather than plain GDI because only GDI+ writes a correct
/// alpha channel, which the layered surface depends on.
/// </summary>
internal static class WeatherIcons
{
    private const float Canvas = 24f;

    /// <summary>Renders a glyph. The caller owns the returned bitmap.</summary>
    public static Bitmap Render(int wmoCode, bool isDay, bool lightTheme, int size)
        => Render(WmoCodes.Kind(wmoCode), isDay, lightTheme, size);

    public static Bitmap Render(WeatherKind kind, bool isDay, bool lightTheme, int size)
    {
        var bitmap = new Bitmap(Math.Max(1, size), Math.Max(1, size), PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        g.ScaleTransform(size / Canvas, size / Canvas);

        Draw(g, kind, isDay, WeatherPalette.For(lightTheme));
        return bitmap;
    }

    public static string Describe(int code) => Loc.T(WmoCodes.DescriptionKey(code));

    // ------------------------------------------------------------ assembly

    private static void Draw(Graphics g, WeatherKind kind, bool isDay, WeatherPalette p)
    {
        switch (kind)
        {
            case WeatherKind.Clear:
                if (isDay) DrawSun(g, p, 12f, 12f, 4.6f, 7.0f, 10.0f);
                else DrawMoon(g, p, 12f, 12f, 7.2f);
                break;

            case WeatherKind.MainlyClear:
                if (isDay) DrawSun(g, p, 9.2f, 8.4f, 3.1f, 5.0f, 7.4f);
                else DrawMoon(g, p, 8.6f, 8.2f, 5.0f);
                DrawCloud(g, p, 0.82f, 3.4f, 5.6f, dark: false);
                break;

            case WeatherKind.PartlyCloudy:
                DrawSun(g, p, 8.8f, 8.2f, 3.0f, 4.9f, 7.2f);
                DrawCloud(g, p, 0.86f, 3.8f, 5.2f, dark: false);
                break;

            case WeatherKind.Overcast:
                DrawCloud(g, p, 0.74f, -2.6f, -0.4f, dark: true);
                DrawCloud(g, p, 0.94f, 1.0f, 2.4f, dark: false);
                break;

            case WeatherKind.Fog:
                DrawCloud(g, p, 0.90f, 0.6f, -2.2f, dark: false);
                DrawFogBars(g, p);
                break;

            case WeatherKind.Drizzle:
                DrawCloud(g, p, 0.94f, 0.6f, -1.6f, dark: false);
                DrawRain(g, p, 3, 1.05f, shortDrops: true);
                break;

            case WeatherKind.Rain:
            case WeatherKind.RainShowers:
                DrawCloud(g, p, 0.94f, 0.6f, -1.6f, dark: false);
                DrawRain(g, p, 3, 1.35f, shortDrops: false);
                break;

            case WeatherKind.FreezingRain:
            case WeatherKind.FreezingDrizzle:
                DrawCloud(g, p, 0.94f, 0.6f, -1.6f, dark: false);
                DrawRain(g, p, 3, 1.0f, shortDrops: true);
                DrawSnowDots(g, p, 2, 22.4f, 0.85f);
                break;

            case WeatherKind.Snow:
            case WeatherKind.SnowGrains:
            case WeatherKind.SnowShowers:
                DrawCloud(g, p, 0.94f, 0.6f, -1.6f, dark: false);
                DrawSnowflake(g, p, 9.0f, 20.4f, 2.3f);
                DrawSnowflake(g, p, 15.0f, 20.9f, 2.3f);
                break;

            case WeatherKind.Thunderstorm:
            case WeatherKind.ThunderstormHail:
                DrawCloud(g, p, 0.94f, 0.6f, -2.0f, dark: false);
                DrawBolt(g, p);
                if (kind == WeatherKind.ThunderstormHail)
                    DrawSnowDots(g, p, 2, 22.6f, 0.8f);
                break;

            default:
                DrawCloud(g, p, 0.94f, 0.6f, 0.4f, dark: false);
                break;
        }
    }

    // -------------------------------------------------------------- pieces

    private static SolidBrush Brush(Color c) => new(c);

    private static Pen Stroke(Color c, float thickness) => new(c, thickness)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
    };

    private static void DrawSun(Graphics g, WeatherPalette p, float cx, float cy, float radius, float rayInner, float rayOuter)
    {
        using var pen = Stroke(p.Sun, 1.5f);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4.0;
            g.DrawLine(pen,
                cx + (float)(rayInner * Math.Cos(a)), cy + (float)(rayInner * Math.Sin(a)),
                cx + (float)(rayOuter * Math.Cos(a)), cy + (float)(rayOuter * Math.Sin(a)));
        }

        using var brush = Brush(p.Sun);
        g.FillEllipse(brush, cx - radius, cy - radius, radius * 2, radius * 2);
    }

    private static void DrawMoon(Graphics g, WeatherPalette p, float cx, float cy, float radius)
    {
        // Crescent: disc minus an offset disc, via a GDI+ Region difference.
        using var outer = new GraphicsPath();
        outer.AddEllipse(cx - radius, cy - radius, radius * 2, radius * 2);

        float bx = cx + (radius * 0.62f), by = cy - (radius * 0.42f), br = radius * 0.92f;
        using var bite = new GraphicsPath();
        bite.AddEllipse(bx - br, by - br, br * 2, br * 2);

        using var region = new Region(outer);
        region.Exclude(bite);

        using var brush = Brush(p.Sun);
        g.FillRegion(brush, region);
    }

    private static void DrawCloud(Graphics g, WeatherPalette p, float scale, float offsetX, float offsetY, bool dark)
    {
        float X(float v) => (v * scale) + offsetX;
        float Y(float v) => (v * scale) + offsetY;
        float S(float v) => v * scale;

        using var brush = Brush(dark ? p.CloudDark : p.Cloud);

        void Puff(float cx, float cy, float r)
            => g.FillEllipse(brush, X(cx) - S(r), Y(cy) - S(r), S(r) * 2, S(r) * 2);

        Puff(8.6f, 13.2f, 3.5f);
        Puff(12.9f, 11.4f, 4.3f);
        Puff(16.8f, 13.6f, 2.9f);

        // Flat base fusing the puffs.
        g.FillRectangle(brush, X(8.6f), Y(13.0f), X(16.8f) - X(8.6f), Y(16.7f) - Y(13.0f));
    }

    private static void DrawRain(Graphics g, WeatherPalette p, int count, float length, bool shortDrops)
    {
        using var pen = Stroke(p.Rain, 1.6f);
        float startX = count switch { 2 => 10.0f, _ => 8.6f };
        float step = count switch { 2 => 5.2f, _ => 3.4f };
        float top = shortDrops ? 18.6f : 18.2f;
        float len = length * (shortDrops ? 1.5f : 2.1f);

        for (int i = 0; i < count; i++)
        {
            float x = startX + (i * step);
            g.DrawLine(pen, x + 0.9f, top, x - 0.3f, top + len);
        }
    }

    private static void DrawSnowflake(Graphics g, WeatherPalette p, float cx, float cy, float size)
    {
        using var pen = Stroke(p.Snow, 1.35f);
        for (int i = 0; i < 3; i++)
        {
            double a = i * Math.PI / 3.0;
            float dx = (float)(Math.Cos(a) * size / 2.0);
            float dy = (float)(Math.Sin(a) * size / 2.0);
            g.DrawLine(pen, cx - dx, cy - dy, cx + dx, cy + dy);
        }
    }

    private static void DrawSnowDots(Graphics g, WeatherPalette p, int count, float y, float radius)
    {
        using var brush = Brush(p.Snow);
        float startX = count == 2 ? 10.4f : 8.8f;
        float step = count == 2 ? 4.6f : 3.2f;

        for (int i = 0; i < count; i++)
        {
            float x = startX + (i * step);
            g.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
        }
    }

    private static void DrawBolt(Graphics g, WeatherPalette p)
    {
        PointF[] points =
        [
            new(13.0f, 16.4f),
            new(9.8f, 20.6f),
            new(12.5f, 20.6f),
            new(10.9f, 23.6f),
            new(15.2f, 19.0f),
            new(12.6f, 19.0f),
            new(15.0f, 16.4f),
        ];

        using var brush = Brush(p.Bolt);
        g.FillPolygon(brush, points);
    }

    private static void DrawFogBars(Graphics g, WeatherPalette p)
    {
        using var pen = Stroke(p.Fog, 1.5f);
        g.DrawLine(pen, 6.0f, 18.3f, 18.6f, 18.3f);
        g.DrawLine(pen, 8.2f, 21.0f, 17.0f, 21.0f);
    }
}
