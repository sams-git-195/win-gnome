namespace WinGnome.Core.Monitors;

/// <summary>
/// An immutable, normalised snapshot of the connected monitors: no empty rectangles, no duplicate keys or bounds
/// (clone mode reports one monitor, a glitchy read can report two), and exactly one primary when any monitor exists.
/// </summary>
public sealed class MonitorLayout
{
    private MonitorLayout(IReadOnlyList<MonitorInfo> monitors)
    {
        Monitors = monitors;
        Primary = monitors.FirstOrDefault(m => m.IsPrimary);
    }

    /// <summary>No monitors (a read that failed, or the moment between unplugging the last one and the next).</summary>
    public static MonitorLayout Empty { get; } = new([]);

    /// <summary>The monitors in the order they were read, after normalisation.</summary>
    public IReadOnlyList<MonitorInfo> Monitors { get; }

    /// <summary>The primary monitor, or null when there are no monitors.</summary>
    public MonitorInfo? Primary { get; }

    /// <summary>
    /// Normalises a raw read: drops monitors with empty bounds, keeps the first of any duplicate key or identical
    /// bounds (preferring the one flagged primary), and makes exactly one monitor primary. When none is flagged, the
    /// monitor containing (0,0) becomes primary (Windows puts the primary there), else the first one.
    /// </summary>
    public static MonitorLayout Create(IEnumerable<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        // Primary-flagged entries first, so a duplicate keeps the flagged copy; then restore the read order.
        var input = monitors.Where(m => m is not null && !m.Bounds.IsEmpty && !string.IsNullOrEmpty(m.Key)).ToList();
        var kept = new List<MonitorInfo>();
        foreach (var monitor in input.OrderByDescending(m => m.IsPrimary))
        {
            if (!kept.Any(k => string.Equals(k.Key, monitor.Key, StringComparison.OrdinalIgnoreCase) || k.Bounds == monitor.Bounds))
            {
                kept.Add(monitor);
            }
        }

        kept.Sort((a, b) => input.IndexOf(a).CompareTo(input.IndexOf(b)));
        if (kept.Count == 0)
        {
            return Empty;
        }

        var primary = kept.FirstOrDefault(m => m.IsPrimary)
            ?? kept.FirstOrDefault(m => m.Bounds.Contains(0, 0))
            ?? kept[0];
        return new MonitorLayout(kept.Select(m => m with { IsPrimary = ReferenceEquals(m, primary) }).ToList());
    }

    /// <summary>The monitor with <paramref name="key"/> (case-insensitive, like device names), or null.</summary>
    public MonitorInfo? Find(string? key) =>
        key is null ? null : Monitors.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The monitor containing the pixel, or null. Half-open like <c>MonitorFromPoint</c>: the shared edge of two
    /// neighbouring monitors belongs to the right or lower one.
    /// </summary>
    public MonitorInfo? At(int x, int y) => Monitors.FirstOrDefault(m => m.Bounds.Contains(x, y));
}
