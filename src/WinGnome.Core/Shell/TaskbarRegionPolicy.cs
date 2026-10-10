namespace WinGnome.Core.Shell;

/// <summary>
/// When WinGnome keeps Explorer's taskbar windows at an empty window region (so a taskbar Explorer re-shows draws
/// nothing), and which recorded windows a restore may give their region back. Pure decisions: the caller reads
/// <c>GetWindowRgn</c> and applies the answers.
/// </summary>
public static class TaskbarRegionPolicy
{
    /// <summary>GetWindowRgn result: the window has no region (or the call failed).</summary>
    public const int ErrorRegion = 0;

    /// <summary>GetWindowRgn result: an empty region. Explorer never sets this itself, so it is the mark of ours.</summary>
    public const int NullRegion = 1;

    /// <summary>GetWindowRgn result: a one-rectangle region (what Explorer sets on a secondary taskbar).</summary>
    public const int SimpleRegion = 2;

    /// <summary>GetWindowRgn result: a region of several rectangles.</summary>
    public const int ComplexRegion = 3;

    /// <summary>Regions are managed only in WinGnome dock mode (taskbar hidden), never in safe mode, and only when the setting is on.</summary>
    public static bool ShouldManage(bool safeMode, bool taskbarHidden, bool settingEnabled) =>
        !safeMode && taskbarHidden && settingEnabled;

    /// <summary>
    /// True when the window's current region is not the empty one: it has none yet, or Explorer replaced ours. Checking
    /// before every apply is also what stops our own change (which can raise window events) from looping.
    /// </summary>
    public static bool NeedsEmptying(int regionType) => regionType != NullRegion;

    /// <summary>
    /// The recorded windows whose region may be removed now: still present (a key in <paramref name="presentRegionTypes"/>,
    /// which holds only live Explorer taskbar windows) and currently empty. A recorded window with any other region was
    /// reset by Explorer, and one that is gone may be a reused handle; neither is touched. Order of
    /// <paramref name="recorded"/> is kept and duplicates dropped.
    /// </summary>
    public static IReadOnlyList<long> RegionsToClear(IEnumerable<long> recorded, IReadOnlyDictionary<long, int> presentRegionTypes)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(presentRegionTypes);
        var result = new List<long>();
        foreach (var handle in recorded)
        {
            if (presentRegionTypes.TryGetValue(handle, out var type) && type == NullRegion && !result.Contains(handle))
            {
                result.Add(handle);
            }
        }

        return result;
    }
}
