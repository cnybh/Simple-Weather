namespace SimpleWeather.Core;

/// <summary>Coarse icon buckets, mapped from WMO 4677 codes as returned by Open-Meteo.</summary>
public enum WeatherKind
{
    Unknown,
    Clear,
    MainlyClear,
    PartlyCloudy,
    Overcast,
    Fog,
    Drizzle,
    FreezingDrizzle,
    Rain,
    FreezingRain,
    Snow,
    SnowGrains,
    RainShowers,
    SnowShowers,
    Thunderstorm,
    ThunderstormHail,
}

public static class WmoCodes
{
    /// <summary>Maps a WMO weather code to its icon bucket. Unknown codes degrade to <see cref="WeatherKind.Unknown"/>.</summary>
    public static WeatherKind Kind(int code) => code switch
    {
        0 => WeatherKind.Clear,
        1 => WeatherKind.MainlyClear,
        2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Overcast,
        45 or 48 => WeatherKind.Fog,
        51 or 53 or 55 => WeatherKind.Drizzle,
        56 or 57 => WeatherKind.FreezingDrizzle,
        61 or 63 or 65 => WeatherKind.Rain,
        66 or 67 => WeatherKind.FreezingRain,
        71 or 73 or 75 => WeatherKind.Snow,
        77 => WeatherKind.SnowGrains,
        80 or 81 or 82 => WeatherKind.RainShowers,
        85 or 86 => WeatherKind.SnowShowers,
        95 => WeatherKind.Thunderstorm,
        96 or 99 => WeatherKind.ThunderstormHail,
        _ => WeatherKind.Unknown,
    };

    /// <summary>Localisation key describing the code in words.</summary>
    public static string DescriptionKey(int code) => Kind(code) switch
    {
        WeatherKind.Clear => "Weather.Clear",
        WeatherKind.MainlyClear => "Weather.MainlyClear",
        WeatherKind.PartlyCloudy => "Weather.PartlyCloudy",
        WeatherKind.Overcast => "Weather.Overcast",
        WeatherKind.Fog => "Weather.Fog",
        WeatherKind.Drizzle => "Weather.Drizzle",
        WeatherKind.FreezingDrizzle => "Weather.FreezingDrizzle",
        WeatherKind.Rain => "Weather.Rain",
        WeatherKind.FreezingRain => "Weather.FreezingRain",
        WeatherKind.Snow => "Weather.Snow",
        WeatherKind.SnowGrains => "Weather.SnowGrains",
        WeatherKind.RainShowers => "Weather.RainShowers",
        WeatherKind.SnowShowers => "Weather.SnowShowers",
        WeatherKind.Thunderstorm => "Weather.Thunderstorm",
        WeatherKind.ThunderstormHail => "Weather.ThunderstormHail",
        _ => "Weather.Unknown",
    };

    /// <summary>True for kinds whose icon should be the night variant when the sun is down.</summary>
    public static bool IsPrecipitation(WeatherKind kind) => kind switch
    {
        WeatherKind.Drizzle or WeatherKind.FreezingDrizzle or WeatherKind.Rain
            or WeatherKind.FreezingRain or WeatherKind.Snow or WeatherKind.SnowGrains
            or WeatherKind.RainShowers or WeatherKind.SnowShowers
            or WeatherKind.Thunderstorm or WeatherKind.ThunderstormHail => true,
        _ => false,
    };
}
