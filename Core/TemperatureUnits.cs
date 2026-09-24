namespace SimpleWeather.Core;

public enum TemperatureUnit
{
    Celsius,
    Fahrenheit,
    Kelvin,
}

public static class TemperatureUnits
{
    public static readonly TemperatureUnit[] All =
    [
        TemperatureUnit.Celsius,
        TemperatureUnit.Fahrenheit,
        TemperatureUnit.Kelvin,
    ];

    public const double KelvinOffset = 273.15;

    public static double FromCelsius(double celsius, TemperatureUnit unit) => unit switch
    {
        TemperatureUnit.Fahrenheit => celsius * 9.0 / 5.0 + 32.0,
        TemperatureUnit.Kelvin => celsius + KelvinOffset,
        _ => celsius,
    };

    public static double ToCelsius(double value, TemperatureUnit unit) => unit switch
    {
        TemperatureUnit.Fahrenheit => (value - 32.0) * 5.0 / 9.0,
        TemperatureUnit.Kelvin => value - KelvinOffset,
        _ => value,
    };

    /// <summary>Unit suffix. Celsius/Fahrenheit use the single-glyph forms U+2103 / U+2109.</summary>
    public static string Symbol(TemperatureUnit unit) => unit switch
    {
        TemperatureUnit.Fahrenheit => "\u2109",
        TemperatureUnit.Kelvin => "K",
        _ => "\u2103",
    };

    public static string Format(double celsius, TemperatureUnit unit)
        => $"{Math.Round(FromCelsius(celsius, unit))}{Symbol(unit)}";

    /// <summary>The "20℃-26℃" range shown on the taskbar strip.</summary>
    public static string FormatRange(double minCelsius, double maxCelsius, TemperatureUnit unit)
        => $"{Format(minCelsius, unit)}-{Format(maxCelsius, unit)}";
}
