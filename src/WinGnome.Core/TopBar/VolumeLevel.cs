namespace WinGnome.Core.TopBar;

/// <summary>Which speaker icon represents the current volume.</summary>
public enum VolumeIcon
{
    Muted,
    Silent,
    Low,
    Medium,
    High,
}

/// <summary>Volume maths shared by the top bar indicator, its scroll wheel handling and the quick-settings slider.</summary>
public static class VolumeLevel
{
    /// <summary>Volume change for one standard mouse-wheel notch (GNOME uses 2 %).</summary>
    public const double WheelStep = 0.02;

    /// <summary>The <c>WHEEL_DELTA</c> reported for one notch of a standard mouse wheel.</summary>
    public const int WheelDelta = 120;

    /// <summary>Picks the icon for a scalar volume (0..1). Muted wins over the level.</summary>
    public static VolumeIcon IconFor(double level, bool muted)
    {
        if (muted)
        {
            return VolumeIcon.Muted;
        }

        var percent = ToPercent(level);
        return percent switch
        {
            0 => VolumeIcon.Silent,
            < 34 => VolumeIcon.Low,
            < 67 => VolumeIcon.Medium,
            _ => VolumeIcon.High,
        };
    }

    /// <summary>
    /// New volume after a mouse-wheel event. Deltas are proportional so high-resolution wheels and touchpads
    /// (which report fractions of a notch) change the volume smoothly instead of 2 % per tiny event.
    /// </summary>
    public static double Nudge(double level, int wheelDelta) =>
        Clamp(Clamp(level) + (wheelDelta / (double)WheelDelta * WheelStep));

    /// <summary>Rounds a scalar volume to a whole percentage (0..100).</summary>
    public static int ToPercent(double level) => (int)Math.Round(Clamp(level) * 100, MidpointRounding.AwayFromZero);

    /// <summary>Clamps to 0..1, mapping NaN to 0.</summary>
    public static double Clamp(double level) => double.IsNaN(level) ? 0 : Math.Clamp(level, 0, 1);
}
