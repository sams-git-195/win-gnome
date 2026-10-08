namespace WinGnome.Core.TopBar;

/// <summary>
/// Turns mouse-wheel deltas into brightness levels. One notch is 5 %; precision touchpads report fractions of
/// a notch, so the part of the wheel travel that has not yet bought a whole step is kept and added to the next
/// delta. Not thread-safe: use from the UI thread.
/// </summary>
public sealed class BrightnessWheel
{
    /// <summary>Brightness change, in percent, for one standard mouse-wheel notch.</summary>
    public const int PercentPerNotch = 5;

    /// <summary>Wheel delta that is worth one whole percent.</summary>
    private const int DeltaPerPercent = VolumeLevel.WheelDelta / PercentPerNotch;

    private int _banked;

    /// <summary>The level after applying <paramref name="wheelDelta"/> on top of any banked remainder.</summary>
    public int Apply(int current, int wheelDelta, IReadOnlyList<int> levels)
    {
        if (levels.Count == 0 || wheelDelta == 0)
        {
            return current;
        }

        _banked += wheelDelta;
        var direction = Math.Sign(_banked);

        // Nothing further to reach this way: do not bank travel that would delay the first notch back.
        if (BrightnessScale.Step(current, direction, levels) == current)
        {
            _banked = 0;
            return current;
        }

        var wholePercent = _banked / DeltaPerPercent;
        var target = BrightnessScale.Snap(current + wholePercent, levels);
        if (target != BrightnessScale.Snap(current, levels))
        {
            // Use up the travel that paid for the move; a coarse panel may jump further than the travel covers.
            var used = Math.Min(Math.Abs(_banked), Math.Abs(target - current) * DeltaPerPercent);
            _banked -= direction * used;
            return target;
        }

        if (Math.Abs(_banked) < VolumeLevel.WheelDelta)
        {
            return current;
        }

        // A whole notch is smaller than the gap to the next level on a coarse panel: step one level instead.
        _banked -= direction * VolumeLevel.WheelDelta;
        return BrightnessScale.Step(current, direction, levels);
    }

    /// <summary>Forgets any banked wheel travel.</summary>
    public void Reset() => _banked = 0;
}
