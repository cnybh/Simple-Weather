using System.Net.Http;
using System.Text.Json;

namespace SimpleWeather.Core;

public sealed record GeoLocation(
    string Name,
    string Region,
    string Country,
    double Latitude,
    double Longitude,
    string TimeZone)
{
    /// <summary>
    /// "City, Country" — deliberately without the region. ip-api only localises the city name
    /// per its <c>lang</c> parameter, so mixing in an English region produced labels like
    /// "吉隆坡, Kuala Lumpur, 马来西亚" in the Chinese UI.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var parts = new[] { Name, Country }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return parts.Length == 0 ? "?" : string.Join(", ", parts);
        }
    }

    /// <summary>Full label including the region, used for tooltips.</summary>
    public string FullName
    {
        get
        {
            var parts = new[] { Name, Region, Country }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return parts.Length == 0 ? "?" : string.Join(", ", parts);
        }
    }
}

/// <summary>One forecast day. <see cref="Date"/> is the local date at the forecast location.</summary>
public sealed record DailyForecast(DateOnly Date, int WeatherCode, double MinC, double MaxC);

public sealed record WeatherSnapshot(
    GeoLocation Location,
    DateTimeOffset ObservedAt,
    double TemperatureC,
    double ApparentC,
    int Humidity,
    double WindKph,
    double PressureHpa,
    int CurrentCode,
    bool IsDay,
    int TodayCode,
    double TodayMinC,
    double TodayMaxC,
    /// <summary>The next three days, tomorrow first. Excludes today.</summary>
    IReadOnlyList<DailyForecast> Days);

/// <summary>
/// Location + forecast from two free, key-less services:
/// ip-api.com (or ipapi.co as fallback) for the position, Open-Meteo for the weather.
/// </summary>
public sealed class WeatherService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SimpleWeather/0.1 (+https://open-meteo.com)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    // ------------------------------------------------------------- location

    /// <summary>Resolves the current position from the public IP. City-level accuracy by design.</summary>
    public async Task<GeoLocation> LocateAsync(CancellationToken ct)
    {
        Exception? primary = null;

        try
        {
            return await LocateViaIpApiAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            primary = ex;
        }

        try
        {
            return await LocateViaIpApiCoAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new WeatherException($"{primary?.Message} / {ex.Message}", ex);
        }
    }

    private static async Task<GeoLocation> LocateViaIpApiAsync(CancellationToken ct)
    {
        // ip-api's free tier is HTTP-only; asking for HTTPS returns 403.
        string lang = Loc.IsChinese ? "zh-CN" : "en";
        string url = "http://ip-api.com/json/?fields=status,message,country,regionName,city,lat,lon,timezone&lang=" + lang;

        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct).ConfigureAwait(false));
        var root = doc.RootElement;

        if (root.TryGetProperty("status", out var status) && status.GetString() != "success")
        {
            string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "unknown" : "unknown";
            throw new WeatherException($"ip-api: {message}");
        }

        return new GeoLocation(
            root.TryGetProperty("city", out var city) ? city.GetString() ?? "" : "",
            root.TryGetProperty("regionName", out var region) ? region.GetString() ?? "" : "",
            root.TryGetProperty("country", out var country) ? country.GetString() ?? "" : "",
            root.GetProperty("lat").GetDouble(),
            root.GetProperty("lon").GetDouble(),
            root.TryGetProperty("timezone", out var tz) ? tz.GetString() ?? "auto" : "auto");
    }

    private static async Task<GeoLocation> LocateViaIpApiCoAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(
            await Http.GetStringAsync("https://ipapi.co/json/", ct).ConfigureAwait(false));
        var root = doc.RootElement;

        return new GeoLocation(
            root.TryGetProperty("city", out var city) ? city.GetString() ?? "" : "",
            root.TryGetProperty("region", out var region) ? region.GetString() ?? "" : "",
            root.TryGetProperty("country_name", out var country) ? country.GetString() ?? "" : "",
            root.GetProperty("latitude").GetDouble(),
            root.GetProperty("longitude").GetDouble(),
            root.TryGetProperty("timezone", out var tz) ? tz.GetString() ?? "auto" : "auto");
    }

    /// <summary>City lookup backed by Open-Meteo's geocoding API, used by the settings window.</summary>
    public async Task<IReadOnlyList<GeoLocation>> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        string lang = Loc.IsChinese ? "zh" : "en";
        string url = "https://geocoding-api.open-meteo.com/v1/search?count=8&format=json"
                     + "&name=" + Uri.EscapeDataString(query.Trim())
                     + "&language=" + lang;

        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct).ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<GeoLocation>();
        foreach (var item in results.EnumerateArray())
        {
            list.Add(new GeoLocation(
                item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                item.TryGetProperty("admin1", out var a) ? a.GetString() ?? "" : "",
                item.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "",
                item.GetProperty("latitude").GetDouble(),
                item.GetProperty("longitude").GetDouble(),
                item.TryGetProperty("timezone", out var tz) ? tz.GetString() ?? "auto" : "auto"));
        }

        return list;
    }

    // -------------------------------------------------------------- weather

    /// <summary>
    /// Number of forecast days requested: today plus the seven shown on the card. Must stay one
    /// more than <c>FlyoutWindow.DayCount</c>; index 0 of the daily block is today.
    /// </summary>
    private const int ForecastDays = 8;

    public async Task<WeatherSnapshot> GetAsync(GeoLocation location, CancellationToken ct)
    {
        string url = "https://api.open-meteo.com/v1/forecast"
                     + "?latitude=" + location.Latitude.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
                     + "&longitude=" + location.Longitude.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
                     + "&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m,pressure_msl"
                     + "&daily=weather_code,temperature_2m_max,temperature_2m_min"
                     + "&timezone=auto&forecast_days=" + ForecastDays;

        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct).ConfigureAwait(false));
        var root = doc.RootElement;

        var current = root.GetProperty("current");
        var daily = root.GetProperty("daily");

        var dates = daily.GetProperty("time");
        var dayCodes = daily.GetProperty("weather_code");
        var dayMax = daily.GetProperty("temperature_2m_max");
        var dayMin = daily.GetProperty("temperature_2m_min");

        // Index 0 is today; the card shows the next three days.
        int dayCount = dates.GetArrayLength();
        var days = new List<DailyForecast>(Math.Max(0, dayCount - 1));

        for (int i = 1; i < dayCount; i++)
        {
            if (!DateOnly.TryParse(dates[i].GetString(), System.Globalization.CultureInfo.InvariantCulture, out var date))
                date = DateOnly.FromDateTime(DateTime.Today.AddDays(i));

            days.Add(new DailyForecast(
                Date: date,
                WeatherCode: dayCodes[i].GetInt32(),
                MinC: dayMin[i].GetDouble(),
                MaxC: dayMax[i].GetDouble()));
        }

        return new WeatherSnapshot(
            Location: location,
            ObservedAt: DateTimeOffset.TryParse(current.GetProperty("time").GetString(), out var observed)
                ? observed
                : DateTimeOffset.Now,
            TemperatureC: current.GetProperty("temperature_2m").GetDouble(),
            ApparentC: current.GetProperty("apparent_temperature").GetDouble(),
            Humidity: current.GetProperty("relative_humidity_2m").GetInt32(),
            WindKph: current.GetProperty("wind_speed_10m").GetDouble(),
            PressureHpa: current.GetProperty("pressure_msl").GetDouble(),
            CurrentCode: current.GetProperty("weather_code").GetInt32(),
            IsDay: current.GetProperty("is_day").GetInt32() == 1,
            TodayCode: dayCodes[0].GetInt32(),
            TodayMinC: dayMin[0].GetDouble(),
            TodayMaxC: dayMax[0].GetDouble(),
            Days: days);
    }
}

public sealed class WeatherException : Exception
{
    public WeatherException(string message, Exception? inner = null) : base(message, inner) { }
}
