namespace WinGnome.Core.Monitors;

/// <summary>What changed on a monitor that stayed connected.</summary>
[Flags]
public enum MonitorChange
{
    None = 0,
    /// <summary>Moved or resized (rearranged, resolution changed, or shifted by a primary swap).</summary>
    Bounds = 1,
    /// <summary>Scale changed.</summary>
    Dpi = 2,
    /// <summary>Became or stopped being the primary monitor.</summary>
    Primary = 4,
}

/// <summary>A monitor present in both layouts, with what changed between them.</summary>
public sealed record ChangedMonitor(MonitorInfo Old, MonitorInfo New, MonitorChange Changes);

/// <summary>
/// The difference between two monitor layouts, matched by key. Work-area changes are ignored: WinGnome's own
/// AppBars cause them, and reacting to them would loop.
/// </summary>
public sealed record MonitorLayoutDiff(
    IReadOnlyList<MonitorInfo> Removed,
    IReadOnlyList<ChangedMonitor> Changed,
    IReadOnlyList<MonitorInfo> Added)
{
    public bool IsEmpty => Removed.Count == 0 && Changed.Count == 0 && Added.Count == 0;

    public static MonitorLayoutDiff Compute(MonitorLayout previous, MonitorLayout current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var removed = previous.Monitors.Where(m => current.Find(m.Key) is null).ToList();
        var added = current.Monitors.Where(m => previous.Find(m.Key) is null).ToList();
        var changed = new List<ChangedMonitor>();
        foreach (var now in current.Monitors)
        {
            if (previous.Find(now.Key) is not { } before)
            {
                continue;
            }

            var changes = MonitorChange.None;
            if (before.Bounds != now.Bounds)
            {
                changes |= MonitorChange.Bounds;
            }

            if (before.Dpi != now.Dpi)
            {
                changes |= MonitorChange.Dpi;
            }

            if (before.IsPrimary != now.IsPrimary)
            {
                changes |= MonitorChange.Primary;
            }

            if (changes != MonitorChange.None)
            {
                changed.Add(new ChangedMonitor(before, now, changes));
            }
        }

        return new MonitorLayoutDiff(removed, changed, added);
    }
}
