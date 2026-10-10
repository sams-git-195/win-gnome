namespace WinGnome.Infrastructure;

/// <summary>
/// Rate-limited log lines. Some callers run for every window on the desktop, often dozens of times a second
/// while one is dragged, and some events (Explorer re-showing the taskbar) repeat in bursts, so a line that
/// fires on every one of them must not flood the log.
/// </summary>
internal static class ThrottledLog
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly Dictionary<string, (DateTime Last, int Suppressed)> Entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Logs <paramref name="message"/> at most once per minute per <paramref name="key"/>, reporting how many
    /// repeats were swallowed in between. UI thread only.
    /// </summary>
    public static void Warn(string key, string message) => Write(key, message, "warnings", line => Log.Warn(line));

    /// <summary>
    /// Logs <paramref name="message"/> at most once per minute per <paramref name="key"/>, reporting how many
    /// repeats were swallowed in between. UI thread only: the tray host's own thread must use <see cref="Log"/>.
    /// </summary>
    public static void Info(string key, string message) => Write(key, message, "messages", Log.Info);

    private static void Write(string key, string message, string noun, Action<string> log)
    {
        var now = DateTime.UtcNow;
        if (Entries.TryGetValue(key, out var entry) && now - entry.Last < Interval)
        {
            Entries[key] = (entry.Last, entry.Suppressed + 1);
            return;
        }

        var suffix = entry.Suppressed > 0 ? $" ({entry.Suppressed} similar {noun} suppressed)" : string.Empty;
        Entries[key] = (now, 0);
        log(message + suffix);
    }
}
