namespace WinGnome.Core.ControlCenter;

/// <summary>A display path's refresh rate as the CCD API stores it, e.g. 60000/1001 for 59.94 Hz.</summary>
public readonly record struct RefreshRate(uint Numerator, uint Denominator);

/// <summary>How to rewrite one display's path for a change (its source mode is always rewritten).</summary>
/// <param name="WriteRefresh">Write <paramref name="Refresh"/> into the path.</param>
/// <param name="Refresh">The path's refresh rate after the change.</param>
/// <param name="InvalidateTargetMode">Let Windows choose a new target timing (needed for a new rate or resolution).</param>
public sealed record PathEdit(bool WriteRefresh, RefreshRate Refresh, bool InvalidateTargetMode);

/// <summary>
/// Decisions for editing a CCD display configuration. GDI (the mode lists the panel shows) reports refresh rates as
/// whole numbers, rounding fractional NTSC rates down (59.94 Hz is "59"), while the CCD path stores the exact fraction.
/// </summary>
public static class DisplayPathPlan
{
    /// <summary>GDI rates that stand for the NTSC fraction of the next whole rate (59 is 60000/1001).</summary>
    private static readonly HashSet<int> NtscRates = [23, 29, 47, 59, 119];

    /// <summary>The whole rate GDI reports for a path rate: the fraction rounded down; 0 when unknown.</summary>
    public static int GdiRate(RefreshRate rate) =>
        rate.Denominator == 0 ? 0 : (int)(rate.Numerator / rate.Denominator);

    /// <summary>The exact rate to write for a GDI rate: the NTSC fraction for 23, 29, 47, 59 and 119, else whole hertz.</summary>
    public static RefreshRate RationalFor(int hz) =>
        NtscRates.Contains(hz) ? new RefreshRate((uint)(hz + 1) * 1000, 1001) : new RefreshRate((uint)hz, 1);

    /// <summary>What to change in a display's path to go from <paramref name="current"/> to <paramref name="target"/>.</summary>
    /// <param name="currentPath">The path's refresh rate now.</param>
    public static PathEdit Plan(RefreshRate currentPath, DisplaySetting current, DisplaySetting target)
    {
        var newRate = GdiRate(currentPath) != target.RefreshHz;
        var newSize = current.Width != target.Width || current.Height != target.Height;
        return new PathEdit(newRate, newRate ? RationalFor(target.RefreshHz) : currentPath, newRate || newSize);
    }

    /// <summary>
    /// The displays a revert must show again: those of <paramref name="original"/> that were attached just before the
    /// revert. A display that goes dark during the revert then counts as a failure instead of being skipped.
    /// </summary>
    public static IReadOnlyList<DisplaySetting> ExpectedAfterRevert(IReadOnlyList<DisplaySetting> original, IReadOnlyCollection<string> attachedBefore) =>
        original.Where(o => attachedBefore.Contains(o.DeviceName, StringComparer.OrdinalIgnoreCase)).ToList();
}
