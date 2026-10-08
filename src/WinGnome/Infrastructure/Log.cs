using System.Diagnostics;
using System.IO;
using System.Text;

namespace WinGnome.Infrastructure;

/// <summary>
/// Minimal thread-safe file logger. Writes to "wingnome.log" in the settings directory and
/// rolls the file over once it exceeds 1 MB. Logging never throws.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static string? FilePath => _path;

    public static void Initialize(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "wingnome.log");
            var info = new FileInfo(_path);
            if (info.Exists && info.Length > MaxBytes)
            {
                File.Move(_path, _path + ".old", overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _path = null;
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture))
            .Append(' ').Append(level).Append(' ').Append(message);
        if (ex is not null)
        {
            // Errors get the full ToString (inner exceptions and stack) so crash reports are actionable;
            // warnings are frequent and expected, so they stay on one line.
            line.Append(" | ");
            if (level == "ERROR")
            {
                line.Append(ex);
            }
            else
            {
                line.Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            }
        }

        var text = line.ToString();
        Debug.WriteLine(text);
        if (_path is null)
        {
            return;
        }

        lock (Gate)
        {
            try
            {
                File.AppendAllText(_path, text + Environment.NewLine);
            }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the app down.
            }
        }
    }
}
