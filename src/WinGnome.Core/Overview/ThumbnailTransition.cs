using WinGnome.Core.Geometry;

namespace WinGnome.Core.Overview;

/// <summary>
/// The overview's opening animation: each thumbnail glides from where its window really is to its slot
/// in the grid (GNOME's "zoom out"), easing out so it settles gently.
/// </summary>
public static class ThumbnailTransition
{
    /// <summary>Cubic ease-out of a progress value; inputs outside 0..1 are clamped.</summary>
    public static double EaseOut(double progress)
    {
        var t = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 1;
        var inverse = 1 - t;
        return 1 - (inverse * inverse * inverse);
    }

    /// <summary>
    /// The rectangle at <paramref name="progress"/> (0..1, already eased) between <paramref name="from"/>
    /// and <paramref name="to"/>, rounded to whole pixels. Progress outside 0..1 is clamped.
    /// </summary>
    public static PixelRect Interpolate(PixelRect from, PixelRect to, double progress)
    {
        var t = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 1;
        return new PixelRect(
            Lerp(from.Left, to.Left, t),
            Lerp(from.Top, to.Top, t),
            Lerp(from.Right, to.Right, t),
            Lerp(from.Bottom, to.Bottom, t));
    }

    /// <summary>Opacity (0..255) at <paramref name="progress"/> when fading from <paramref name="from"/> to fully opaque.</summary>
    public static byte FadeIn(byte from, double progress)
    {
        var t = double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 1;
        return (byte)Lerp(from, 255, t);
    }

    private static int Lerp(int from, int to, double t) => (int)Math.Round(from + ((to - from) * t), MidpointRounding.AwayFromZero);
}
