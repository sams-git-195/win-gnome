using WinGnome.Infrastructure;

namespace WinGnome.Features.WindowButtons;

/// <summary>
/// Rate-limited warnings. The window-buttons code runs for every window on the desktop, often dozens of times
/// a second while one is dragged, so a persistent failure must not flood the log.
/// </summary>
internal static class ThrottledLog
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly Dictionary<string, (DateTime Last, int Suppressed)> Entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Logs <paramref name="message"/> at most once per minute per <paramref name="key"/>, reporting how many
    /// repeats were swallowed in between. UI thread only.
    /// </summary>
    public static void Warn(string key, string message)
    {
        var now = DateTime.UtcNow;
        if (Entries.TryGetValue(key, out var entry) && now - entry.Last < Interval)
        {
            Entries[key] = (entry.Last, entry.Suppressed + 1);
            return;
        }

        var suffix = entry.Suppressed > 0 ? $" ({entry.Suppressed} similar warnings suppressed)" : string.Empty;
        Entries[key] = (now, 0);
        Log.Warn(message + suffix);
    }
}
