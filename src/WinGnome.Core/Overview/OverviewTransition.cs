using WinGnome.Core.Geometry;

namespace WinGnome.Core.Overview;

/// <summary>Where a thumbnail is drawn in one animation frame, and how opaque (0..255).</summary>
public readonly record struct ThumbnailFrame(PixelRect Rect, byte Opacity);

/// <summary>One thumbnail's path through an overview transition.</summary>
public readonly record struct ThumbnailTrack(PixelRect From, PixelRect To, byte FromOpacity, byte ToOpacity)
{
    /// <summary>A track that stays fully opaque at <paramref name="rect"/>.</summary>
    public static ThumbnailTrack Still(PixelRect rect) => new(rect, rect, 255, 255);

    /// <summary>
    /// The frame at <paramref name="eased"/> progress (0..1, clamped; NaN counts as finished), rounded to whole pixels.
    /// </summary>
    public ThumbnailFrame At(double eased)
    {
        var t = OverviewTransition.Clamp01(eased);
        var rect = new PixelRect(
            Lerp(From.Left, To.Left, t),
            Lerp(From.Top, To.Top, t),
            Lerp(From.Right, To.Right, t),
            Lerp(From.Bottom, To.Bottom, t));
        return new ThumbnailFrame(rect, (byte)Lerp(FromOpacity, ToOpacity, t));
    }

    /// <summary>The same path travelled the other way.</summary>
    public ThumbnailTrack Reversed() => new(To, From, ToOpacity, FromOpacity);

    private static int Lerp(int from, int to, double t) =>
        (int)Math.Round(from + ((to - from) * t), MidpointRounding.AwayFromZero);
}

/// <summary>
/// Timing and interpolation for the overview's open and close animations: thumbnails glide between the
/// windows' real positions and the grid while the backdrop's dim layer eases in or out, all on GNOME's
/// overview curve (ease-out-quad).
/// </summary>
public static class OverviewTransition
{
    /// <summary>Opening: real positions into the grid. GNOME's overview uses 250 ms.</summary>
    public static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(250);

    /// <summary>Closing: back from the grid to the windows.</summary>
    public static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(200);

    /// <summary>Quadratic ease-out of a progress value; inputs outside 0..1 are clamped and NaN counts as finished.</summary>
    public static double EaseOutQuad(double progress)
    {
        var inverse = 1 - Clamp01(progress);
        return 1 - (inverse * inverse);
    }

    /// <summary>
    /// Linear progress (0..1) after <paramref name="elapsed"/> of an animation lasting <paramref name="duration"/>.
    /// A zero or negative duration is already finished.
    /// </summary>
    public static double Progress(TimeSpan elapsed, TimeSpan duration) =>
        duration <= TimeSpan.Zero ? 1 : Clamp01(elapsed / duration);

    /// <summary>How long opening or closing takes; zero (instant) when Windows animations are turned off.</summary>
    public static TimeSpan DurationFor(bool animationsEnabled, bool opening) =>
        !animationsEnabled ? TimeSpan.Zero : opening ? OpenDuration : CloseDuration;

    /// <summary>
    /// A new track that starts where <paramref name="track"/> is at <paramref name="eased"/> progress and heads for
    /// <paramref name="to"/>, so a reversed or redirected animation never jumps.
    /// </summary>
    public static ThumbnailTrack Retarget(ThumbnailTrack track, double eased, PixelRect to, byte toOpacity)
    {
        var current = track.At(eased);
        return new ThumbnailTrack(current.Rect, to, current.Opacity, toOpacity);
    }

    /// <summary>
    /// Alpha (0..255) of the black dim layer at <paramref name="eased"/> progress between the opacities
    /// <paramref name="from"/> and <paramref name="to"/> (0..1, clamped).
    /// </summary>
    public static byte DimAlpha(double from, double to, double eased)
    {
        // Interpolate in alpha units so exact halves round predictably (0.3 * 255 is not 76.5 in binary).
        var fromAlpha = Clamp01(from) * 255;
        var toAlpha = Clamp01(to) * 255;
        return (byte)Math.Round(fromAlpha + ((toAlpha - fromAlpha) * Clamp01(eased)), MidpointRounding.AwayFromZero);
    }

    /// <summary>Clamps to 0..1; NaN and +infinity count as finished.</summary>
    internal static double Clamp01(double value) => double.IsNaN(value) ? 1 : Math.Clamp(value, 0, 1);
}
