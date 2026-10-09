namespace WinGnome.Core.Monitors;

/// <summary>Which monitors have a hot corner (GNOME's rule).</summary>
public static class HotCornerRules
{
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
}
