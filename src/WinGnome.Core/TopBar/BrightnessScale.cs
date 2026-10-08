namespace WinGnome.Core.TopBar;

/// <summary>Maps the quick-settings brightness slider and mouse wheel onto the levels a panel supports.</summary>
public static class BrightnessScale
{
    /// <summary>Sorts, de-duplicates and drops anything outside 0..100 from a panel's reported levels.</summary>
    public static IReadOnlyList<int> NormalizeLevels(IEnumerable<int> reported) =>
        reported.Where(level => level is >= 0 and <= 100).Distinct().Order().ToArray();

    /// <summary>
    /// The supported level nearest to <paramref name="percent"/>; ties go to the brighter one. NaN counts as 0,
    /// out-of-range and infinite input clamps to the ends, and with no levels the clamped whole percent is returned.
    /// </summary>
    public static int Snap(double percent, IReadOnlyList<int> levels)
    {
        percent = double.IsNaN(percent) ? 0 : Math.Clamp(percent, 0, 100);
        if (levels.Count == 0)
        {
            return (int)Math.Round(percent, MidpointRounding.AwayFromZero);
        }

        var best = levels[0];
        foreach (var level in levels)
        {
            // Levels ascend, so a later level that is at least as close is the brighter one on a tie.
            if (Math.Abs(level - percent) <= Math.Abs(best - percent))
            {
                best = level;
            }
        }

        return best;
    }

    /// <summary>The neighbouring supported level above (<paramref name="direction"/> &gt; 0) or below (&lt; 0) <paramref name="current"/>; the same level at the end or for direction 0.</summary>
    public static int Step(int current, int direction, IReadOnlyList<int> levels)
    {
        if (direction == 0)
        {
            return current;
        }

        return direction > 0
            ? levels.FirstOrDefault(level => level > current, current)
            : levels.LastOrDefault(level => level < current, current);
    }
}
