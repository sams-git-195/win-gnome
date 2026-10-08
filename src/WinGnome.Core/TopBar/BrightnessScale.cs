namespace WinGnome.Core.TopBar;

/// <summary>Maps the quick-settings brightness slider and mouse wheel onto the levels a panel supports.</summary>
public static class BrightnessScale
{
    /// <summary>Brightness change, in percent, for one standard mouse-wheel notch.</summary>
    public const int WheelStep = 5;

    /// <summary>Sorts, de-duplicates and drops anything outside 0..100 from a panel's reported levels.</summary>
    public static IReadOnlyList<int> NormalizeLevels(IEnumerable<int> reported) =>
        reported.Where(level => level is >= 0 and <= 100).Distinct().Order().ToArray();

    /// <summary>The supported level nearest to <paramref name="percent"/>; ties go to the brighter one.</summary>
    public static int Snap(double percent, IReadOnlyList<int> levels)
    {
        if (double.IsNaN(percent))
        {
            percent = 0;
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

    /// <summary>
    /// New brightness after a mouse-wheel event. Proportional to the delta like the volume wheel, and always
    /// reaches the neighbouring supported level even when the panel only offers a few coarse ones.
    /// </summary>
    public static int Nudge(int current, int wheelDelta, IReadOnlyList<int> levels)
    {
        if (wheelDelta == 0)
        {
            return current;
        }

        var target = Snap(current + (wheelDelta / (double)VolumeLevel.WheelDelta * WheelStep), levels);
        if (target != Snap(current, levels))
        {
            return target;
        }

        // The step was smaller than the gap to the next level: step one level instead of going nowhere.
        return wheelDelta > 0
            ? levels.FirstOrDefault(level => level > current, current)
            : levels.LastOrDefault(level => level < current, current);
    }
}
