namespace WinGnome.Core.Monitors;

/// <summary>What kind of hot corner a monitor's top-left corner is.</summary>
public enum HotCornerKind
{
    /// <summary>No hot corner: another monitor continues past it, and it is not the primary.</summary>
    None,
    /// <summary>A real screen corner: the pointer stops there, so the 1-pixel corner and the configured delay apply.</summary>
    Corner,
    /// <summary>
    /// The primary's corner where another monitor continues past it: the pointer does not stop there, so it triggers
    /// only after resting in a small box at the corner, inside the primary, for at least
    /// <see cref="HotCornerRules.GuardedMinimumDwellMs"/>. Passing through to the other monitor never rests that long.
    /// </summary>
    Guarded,
}

/// <summary>Which monitors have a hot corner, and how it triggers.</summary>
public static class HotCornerRules
{
    /// <summary>Edge length of a guarded corner's box, in physical pixels, inside the primary at its top-left.</summary>
    public const int GuardedSizePx = 8;

    /// <summary>Shortest dwell for a guarded corner, whatever the configured delay (even 0).</summary>
    public const int GuardedMinimumDwellMs = 300;

    /// <summary>
    /// True when the monitor's top-left corner is a real screen corner: no other monitor lies directly to its left
    /// (covers <c>(Left-1, Top)</c>) or directly above it (covers <c>(Left, Top-1)</c>). A monitor touching only
    /// diagonally keeps the corner, because the pointer still stops there.
    /// </summary>
    public static bool IsTrueCorner(MonitorInfo monitor, MonitorLayout layout)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(layout);

        var left = monitor.Bounds.Left;
        var top = monitor.Bounds.Top;
        return !layout.Monitors.Any(other =>
            !string.Equals(other.Key, monitor.Key, StringComparison.OrdinalIgnoreCase)
            && (other.Bounds.Contains(left - 1, top) || other.Bounds.Contains(left, top - 1)));
    }

    /// <summary>
    /// The hot corner of <paramref name="monitor"/>: a real corner on any monitor; on the primary always one
    /// (user decision 2026-10-09), guarded when it is not a real corner; none on other monitors otherwise.
    /// </summary>
    public static HotCornerKind KindOf(MonitorInfo monitor, MonitorLayout layout)
    {
        if (IsTrueCorner(monitor, layout))
        {
            return HotCornerKind.Corner;
        }

        return monitor.IsPrimary ? HotCornerKind.Guarded : HotCornerKind.None;
    }

    /// <summary>The dwell a guarded corner needs for a configured delay of <paramref name="delayMs"/>.</summary>
    public static int GuardedDwellMs(int delayMs) => Math.Max(delayMs, GuardedMinimumDwellMs);
}
