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
/// Location + forecast from free, key-less services:
/// ipwho.is / ipinfo.io / freeipapi.com / ip-api.com for the position,
/// Open-Meteo for the weather.
///
/// Geolocation providers go down, rate-limit, or are blocked by a given network — measured on
/// the reference machine, ip-api.com accepted the TCP connection but never sent an HTTP
/// response, and ipapi.co answered 403. A single hard-coded provider therefore left the widget
/// stuck on "Locating…" until the HTTP timeout expired. Each provider now gets its own short
/// timeout and the list is walked in order.
/// </summary>
public sealed class WeatherService
{
    private static readonly HttpClient Http = CreateClient();

    /// <summary>
    /// Per-request ceiling. Must stay short: a provider that accepts the connection and then
    /// never answers would otherwise hold the whole locate chain for the full timeout. 6 s is
    /// comfortably above the measured 0.2–1.1 s latency of the working providers.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(6);

    /// <summary>Overall ceiling for the whole locate chain, so a dead network cannot spin.</summary>
    private static readonly TimeSpan LocateBudget = TimeSpan.FromSeconds(20);

    private static HttpClient CreateClient()
    {
        // The client-level timeout is a backstop only; each call passes its own linked token.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SimpleWeather/0.1 (+https://open-meteo.com)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    /// <summary>
    /// Issues one GET bounded by both the caller's token and <see cref="RequestTimeout"/>, so a
    /// provider that hangs mid-response cannot stall the chain.
    /// </summary>
    private static async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            return await Http.GetStringAsync(url, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Distinguish "this provider was too slow" from "the caller cancelled the refresh".
            throw new TimeoutException($"no response within {RequestTimeout.TotalSeconds:0}s");
        }
    }

    // ------------------------------------------------------------- location

    /// <summary>
    /// Resolves the current position from the public IP, trying every provider in turn.
    /// City-level accuracy by design. Throws only when all providers fail.
    /// </summary>
    public async Task<GeoLocation> LocateAsync(CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(LocateBudget);

        var failures = new List<string>();

        // Ordered by measured latency and reliability on the reference machine. Providers that
        // need no API key and return a country name directly come first.
        (string Name, Func<CancellationToken, Task<GeoLocation>> Call)[] providers =
        [
            ("ipwho.is", LocateViaIpWhoIsAsync),
            ("ipinfo.io", LocateViaIpInfoAsync),
            ("freeipapi.com", LocateViaFreeIpApiAsync),
            ("ip-api.com", LocateViaIpApiAsync),
            ("ipapi.co", LocateViaIpApiCoAsync),
        ];

        foreach (var (name, call) in providers)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var location = await call(budget.Token).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                Log.Verbose($"locate ok via {name}: {location.DisplayName}");
                return location;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Either this provider hit RequestTimeout or the whole locate budget ran out.
                failures.Add($"{name}: timed out");
                if (budget.IsCancellationRequested) break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add($"{name}: {ex.Message}");
            }
        }

        throw new WeatherException("all location providers failed — " + string.Join("; ", failures));
    }

    /// <summary>https://ipwho.is — returns the country name directly; no key required.</summary>
    private static async Task<GeoLocation> LocateViaIpWhoIsAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await GetStringAsync("https://ipwho.is/", ct).ConfigureAwait(false));
        var root = doc.RootElement;

        if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "unknown" : "unknown";
            throw new WeatherException($"ipwho.is: {message}");
        }

        return new GeoLocation(
            Text(root, "city"),
            Text(root, "region"),
            Text(root, "country"),
            Number(root, "latitude"),
            Number(root, "longitude"),
            TextOr(root, "auto", "timezone", "timezone.id"));
    }

    /// <summary>
    /// https://ipinfo.io/json — free tier returns country as an ISO code ("MY"), so the label is
    /// mapped when the code is known. City/region arrive separately.
    /// </summary>
    private static async Task<GeoLocation> LocateViaIpInfoAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await GetStringAsync("https://ipinfo.io/json", ct).ConfigureAwait(false));
        var root = doc.RootElement;

        // "loc" carries "lat,lon" as a single string.
        double lat = 0, lon = 0;
        if (root.TryGetProperty("loc", out var loc) && loc.GetString() is { } pair)
        {
            var parts = pair.Split(',');
            if (parts.Length == 2)
            {
                _ = double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out lat);
                _ = double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out lon);
            }
        }

        return new GeoLocation(
            Text(root, "city"),
            Text(root, "region"),
            CountryName(Text(root, "country")),
            lat,
            lon,
            TextOr(root, "auto", "timezone"));
    }

    /// <summary>https://freeipapi.com/api/json — returns a country name; no key required.</summary>
    private static async Task<GeoLocation> LocateViaFreeIpApiAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(
            await GetStringAsync("https://freeipapi.com/api/json", ct).ConfigureAwait(false));
        var root = doc.RootElement;

        return new GeoLocation(
            Text(root, "cityName", "city"),
            Text(root, "regionName", "region"),
            Text(root, "countryName", "country"),
            Number(root, "latitude"),
            Number(root, "longitude"),
            TextOr(root, "auto", "timeZone", "timezone"));
    }

    /// <summary>Legacy provider, kept last: free tier is HTTP-only and has been unreliable.</summary>
    private static async Task<GeoLocation> LocateViaIpApiAsync(CancellationToken ct)
    {
        // ip-api's free tier is HTTP-only; asking for HTTPS returns 403.
        string lang = Loc.IsChinese ? "zh-CN" : "en";
        string url = "http://ip-api.com/json/?fields=status,message,country,regionName,city,lat,lon,timezone&lang=" + lang;

        using var doc = JsonDocument.Parse(await GetStringAsync(url, ct).ConfigureAwait(false));
        var root = doc.RootElement;

        if (root.TryGetProperty("status", out var status) && status.GetString() != "success")
        {
            string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "unknown" : "unknown";
            throw new WeatherException($"ip-api: {message}");
        }

        return new GeoLocation(
            Text(root, "city"),
            Text(root, "regionName"),
            Text(root, "country"),
            Number(root, "lat"),
            Number(root, "lon"),
            TextOr(root, "auto", "timezone"));
    }

    /// <summary>Legacy provider, kept last: measured 403 on the reference network.</summary>
    private static async Task<GeoLocation> LocateViaIpApiCoAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(
            await GetStringAsync("https://ipapi.co/json/", ct).ConfigureAwait(false));
        var root = doc.RootElement;

        return new GeoLocation(
            Text(root, "city"),
            Text(root, "region"),
            Text(root, "country_name"),
            Number(root, "latitude"),
            Number(root, "longitude"),
            TextOr(root, "auto", "timezone"));
    }

    // ------------------------------------------------- provider field helpers

    /// <summary>First present, non-empty JSON string among <paramref name="names"/>.</summary>
    private static string Text(JsonElement root, params string[] names)
    {
        foreach (string name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                string? s = value.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }

        return string.Empty;
    }

    /// <summary>Like <see cref="Text"/>, but falls back when every field is missing or empty.</summary>
    private static string TextOr(JsonElement root, string fallback, params string[] names)
    {
        string value = Text(root, names);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    /// <summary>Numeric field lookup that tolerates a provider returning it as a string.</summary>
    private static double Number(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return 0;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed) => parsed,
            _ => 0,
        };
    }

    /// <summary>
    /// ipinfo.io's free tier reports an ISO 3166-1 alpha-2 country code. Only the codes worth
    /// mapping for this widget's audience are listed; anything else is shown as-is.
    /// </summary>
    private static string CountryName(string code) => code.ToUpperInvariant() switch
    {
        "MY" => "Malaysia",
        "CN" => "China",
        "SG" => "Singapore",
        "TW" => "Taiwan",
        "HK" => "Hong Kong",
        "JP" => "Japan",
        "KR" => "South Korea",
        "US" => "United States",
        "GB" => "United Kingdom",
        "AU" => "Australia",
        "CA" => "Canada",
        "DE" => "Germany",
        "FR" => "France",
        _ => code,
    };

    /// <summary>City lookup backed by Open-Meteo's geocoding API, used by the settings window.</summary>
    public async Task<IReadOnlyList<GeoLocation>> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        string lang = Loc.IsChinese ? "zh" : "en";
        string url = "https://geocoding-api.open-meteo.com/v1/search?count=8&format=json"
                     + "&name=" + Uri.EscapeDataString(query.Trim())
                     + "&language=" + lang;

        using var doc = JsonDocument.Parse(await GetStringAsync(url, ct).ConfigureAwait(false));
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

        using var doc = JsonDocument.Parse(await GetStringAsync(url, ct).ConfigureAwait(false));
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
