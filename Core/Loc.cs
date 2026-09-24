using System.Globalization;
using SimpleWeather.Interop;

namespace SimpleWeather.Core;

/// <summary>
/// Interface language follows the Windows *display* language: Chinese gets the zh table,
/// every other language falls back to English.
/// </summary>
public static class Loc
{
    private static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
    {
        ["App.Name"] = "Simple Weather",
        ["Weather.Clear"] = "Clear",
        ["Weather.MainlyClear"] = "Mainly clear",
        ["Weather.PartlyCloudy"] = "Partly cloudy",
        ["Weather.Overcast"] = "Overcast",
        ["Weather.Fog"] = "Fog",
        ["Weather.Drizzle"] = "Drizzle",
        ["Weather.FreezingDrizzle"] = "Freezing drizzle",
        ["Weather.Rain"] = "Rain",
        ["Weather.FreezingRain"] = "Freezing rain",
        ["Weather.Snow"] = "Snow",
        ["Weather.SnowGrains"] = "Snow grains",
        ["Weather.RainShowers"] = "Rain showers",
        ["Weather.SnowShowers"] = "Snow showers",
        ["Weather.Thunderstorm"] = "Thunderstorm",
        ["Weather.ThunderstormHail"] = "Thunderstorm with hail",
        ["Weather.Unknown"] = "Unknown",
        ["Label.FeelsLike"] = "Feels like",
        ["Label.Humidity"] = "Humidity",
        ["Label.Wind"] = "Wind",
        ["Label.Pressure"] = "Pressure",
        ["Label.Today"] = "Today",
        ["Label.Tomorrow"] = "Tomorrow",
        ["Label.Updated"] = "Updated",
        ["Label.High"] = "High",
        ["Label.Low"] = "Low",
        ["Label.NextDays"] = "Next 3 days",
        ["Menu.Settings"] = "Settings",
        ["Menu.Refresh"] = "Refresh now",
        ["Menu.Exit"] = "Exit",
        ["Status.Locating"] = "Locating\u2026",
        ["Status.Loading"] = "Loading weather\u2026",
        ["Status.Error"] = "Could not load weather",
        ["Settings.Title"] = "Simple Weather \u2013 Settings",
        ["Settings.General"] = "General",
        ["Settings.Units"] = "Units",
        ["Settings.Location"] = "Location",
        ["Settings.About"] = "About",
        ["Settings.StartWithWindows"] = "Start with Windows",
        ["Settings.StartWithWindows.Desc"] = "Launch automatically when you sign in.",
        ["Settings.TemperatureUnit"] = "Temperature unit",
        ["Settings.RefreshInterval"] = "Refresh interval",
        ["Settings.RefreshInterval.Desc"] = "How often the weather is re-fetched.",
        ["Settings.Language"] = "Interface language",
        ["Settings.Language.Desc"] = "Follows the Windows display language.",
        ["Settings.Minutes"] = "minutes",
        ["Settings.AutoLocation"] = "Detect automatically from my network",
        ["Settings.AutoLocation.Desc"] = "Uses IP-based geolocation (city level accuracy).",
        ["Settings.ManualLocation"] = "Use a fixed location",
        ["Settings.Search"] = "Search",
        ["Settings.Searching"] = "Searching\u2026",
        ["Settings.NoResults"] = "No matching city",
        ["Settings.Close"] = "Close",
        ["Settings.CurrentLocation"] = "Current Location Setting",
        ["Settings.AboutButton"] = "About",
        ["Settings.ReleasePage"] = "Software Release Page",
        ["Settings.AboutText"] = "Weather data by Open-Meteo. Location by ip-api. Both are free and need no API key.",
        ["About.Title"] = "Simple Weather by Bohang",
        ["About.Version"] = "Software Version",
        ["About.Ok"] = "OK",
    };

    private static readonly Dictionary<string, string> Zh = new(StringComparer.Ordinal)
    {
        ["App.Name"] = "简易天气",
        ["Weather.Clear"] = "晴",
        ["Weather.MainlyClear"] = "晴间多云",
        ["Weather.PartlyCloudy"] = "多云",
        ["Weather.Overcast"] = "阴",
        ["Weather.Fog"] = "雾",
        ["Weather.Drizzle"] = "毛毛雨",
        ["Weather.FreezingDrizzle"] = "冻毛毛雨",
        ["Weather.Rain"] = "雨",
        ["Weather.FreezingRain"] = "冻雨",
        ["Weather.Snow"] = "雪",
        ["Weather.SnowGrains"] = "米雪",
        ["Weather.RainShowers"] = "阵雨",
        ["Weather.SnowShowers"] = "阵雪",
        ["Weather.Thunderstorm"] = "雷暴",
        ["Weather.ThunderstormHail"] = "雷暴伴冰雹",
        ["Weather.Unknown"] = "未知",
        ["Label.FeelsLike"] = "体感温度",
        ["Label.Humidity"] = "湿度",
        ["Label.Wind"] = "风速",
        ["Label.Pressure"] = "气压",
        ["Label.Today"] = "今天",
        ["Label.Tomorrow"] = "明天",
        ["Label.Updated"] = "更新于",
        ["Label.High"] = "最高",
        ["Label.Low"] = "最低",
        ["Label.NextDays"] = "未来三天",
        ["Menu.Settings"] = "设置",
        ["Menu.Refresh"] = "立即刷新",
        ["Menu.Exit"] = "退出",
        ["Status.Locating"] = "正在定位\u2026",
        ["Status.Loading"] = "正在获取天气\u2026",
        ["Status.Error"] = "无法获取天气",
        ["Settings.Title"] = "简易天气 \u2013 设置",
        ["Settings.General"] = "常规",
        ["Settings.Units"] = "单位",
        ["Settings.Location"] = "位置",
        ["Settings.About"] = "关于",
        ["Settings.StartWithWindows"] = "设为开机启动",
        ["Settings.StartWithWindows.Desc"] = "登录 Windows 后自动运行。",
        ["Settings.TemperatureUnit"] = "温度单位",
        ["Settings.RefreshInterval"] = "刷新间隔",
        ["Settings.RefreshInterval.Desc"] = "每隔多久重新获取一次天气。",
        ["Settings.Language"] = "界面语言",
        ["Settings.Language.Desc"] = "跟随 Windows 显示语言。",
        ["Settings.Minutes"] = "分钟",
        ["Settings.AutoLocation"] = "根据当前网络自动定位",
        ["Settings.AutoLocation.Desc"] = "使用 IP 定位，精度到城市级别。",
        ["Settings.ManualLocation"] = "使用固定位置",
        ["Settings.Search"] = "搜索",
        ["Settings.Searching"] = "搜索中\u2026",
        ["Settings.NoResults"] = "未找到匹配的城市",
        ["Settings.Close"] = "关闭",
        ["Settings.CurrentLocation"] = "当前位置设置",
        ["Settings.AboutButton"] = "关于",
        ["Settings.ReleasePage"] = "软件发布页",
        ["Settings.AboutText"] = "天气数据来自 Open-Meteo，定位数据来自 ip-api，均为免费且无需密钥。",
        ["About.Title"] = "Simple Weather by Bohang",
        ["About.Version"] = "软件版本",
        ["About.Ok"] = "确定",
    };

    private static bool _isChinese;

    public static bool IsChinese => _isChinese;

    public static string LanguageTag => _isChinese ? "zh" : "en";

    /// <summary>
    /// Resolves the interface language once at startup. SIMPLEWEATHER_LANG=zh|en overrides it,
    /// which is handy for testing either table on the same machine.
    /// </summary>
    public static void Initialize()
    {
        string? forced = Environment.GetEnvironmentVariable("SIMPLEWEATHER_LANG");
        if (!string.IsNullOrEmpty(forced))
        {
            _isChinese = forced.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            return;
        }

        // A process with no explicit UI culture falls back to the Win32 display language.
        ushort langId = Win32.GetUserDefaultUILanguage();
        _isChinese = (langId & 0x3FF) == 0x04;   // LANG_CHINESE
    }

    public static string T(string key)
    {
        var table = _isChinese ? Zh : En;
        return table.TryGetValue(key, out var value) ? value : key;
    }

    public static string Format(string key, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, T(key), args);
}
