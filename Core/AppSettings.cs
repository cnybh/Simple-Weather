using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace SimpleWeather.Core;

/// <summary>User-visible preferences, persisted as JSON under %APPDATA%\SimpleWeather.</summary>
public sealed class AppSettings
{
    public TemperatureUnit Unit { get; set; } = TemperatureUnit.Celsius;

    public bool StartWithWindows { get; set; }

    public int RefreshMinutes { get; set; } = 15;

    /// <summary>When false the location comes from IP geolocation on every refresh.</summary>
    public bool UseManualLocation { get; set; }

    public string ManualLocationName { get; set; } = string.Empty;

    public double ManualLatitude { get; set; }

    public double ManualLongitude { get; set; }

    /// <summary>
    /// Size of the strip along the taskbar's <b>length</b>, in device pixels.
    /// On a left/right (vertical) taskbar that is the strip's height; its width comes from the
    /// taskbar's own thickness. 40px comfortably fits the glyph plus the temperature range.
    /// </summary>
    public int BandThicknessPx { get; set; } = 40;

    /// <summary>
    /// Size of the strip along the length of a top/bottom (horizontal) taskbar — its width there.
    /// This must be larger than the vertical height because the content is laid out sideways:
    /// a 21px glyph plus "24℃-33℃" needs roughly 85px.
    /// </summary>
    public int HorizontalBandWidthPx { get; set; } = 96;

    /// <summary>Blank space left between the strip and the taskbar's own controls, in device pixels.</summary>
    public int BandGapPx { get; set; } = 10;

    [JsonIgnore]
    public bool CoordinatesValid => Math.Abs(ManualLatitude) <= 90 && Math.Abs(ManualLongitude) <= 180
                                    && !(ManualLatitude == 0 && ManualLongitude == 0);

    // ---------------------------------------------------------------- storage

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleWeather");

    public static string FilePath => Path.Combine(Directory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch
        {
            // A corrupt settings file must never prevent the widget from starting.
        }

        return new AppSettings();
    }

    public void Normalize()
    {
        RefreshMinutes = Math.Clamp(RefreshMinutes, 5, 240);
        BandThicknessPx = Math.Clamp(BandThicknessPx, 28, 64);
        HorizontalBandWidthPx = Math.Clamp(HorizontalBandWidthPx, 70, 200);
        BandGapPx = Math.Clamp(BandGapPx, 0, 40);
        if (!Enum.IsDefined(Unit)) Unit = TemperatureUnit.Celsius;
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Persisting is best-effort; the running instance keeps its in-memory values.
        }
    }
}

/// <summary>
/// Run-at-login via HKCU\...\Run. Deliberately user-scope only so the app never needs elevation.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SimpleWeather";

    private static string ExecutablePath
        => Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return false;

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{ExecutablePath}\"", RegistryValueKind.String);
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
