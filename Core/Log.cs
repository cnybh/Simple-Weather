using System.IO;
using System.Text;

namespace SimpleWeather.Core;

/// <summary>
/// Minimal append-only diagnostics log at %APPDATA%\SimpleWeather\log.txt.
/// The widget lives in the taskbar's screen real estate, so a plain file is the only
/// reliable way to see what happened when something goes wrong.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SimpleWeather", "log.txt");

    public static bool VerboseEnabled { get; set; }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 256 * 1024)
                    File.Delete(LogPath);

                File.AppendAllText(
                    LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never take the app down.
        }
    }

    public static void Write(string message, Exception ex)
        => Write($"{message}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    /// <summary>High-frequency tracing, off unless explicitly enabled.</summary>
    public static void Verbose(string message)
    {
        if (VerboseEnabled) Write(message);
    }
}
