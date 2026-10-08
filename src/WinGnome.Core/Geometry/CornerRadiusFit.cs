namespace WinGnome.Core.Geometry;

/// <summary>Caps a corner radius so rounded corners stay circular.</summary>
public static class CornerRadiusFit
{
    /// <summary>
    /// The requested radius, at most half the shorter side, so the largest setting gives a pill.
    /// WPF's <c>Border</c> shrinks an oversized radius per side instead, which turns the ends of a wide box
    /// into flat ellipses. Invalid radii or sizes give 0 (square).
    /// </summary>
    public static double Fit(double requested, double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || !(requested > 0))
        {
            return 0;
        }

        return Math.Max(0, Math.Min(requested, Math.Min(width, height) / 2));
    }
}
