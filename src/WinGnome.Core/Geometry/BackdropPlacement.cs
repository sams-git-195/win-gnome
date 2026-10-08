namespace WinGnome.Core.Geometry;

/// <summary>The corner rounding DWM can give a window on Windows 11 (it has no arbitrary radius).</summary>
public enum BackdropCorners
{
    /// <summary>DWMWCP_DONOTROUND.</summary>
    Square,

    /// <summary>DWMWCP_ROUNDSMALL, a 4 DIP radius.</summary>
    Small,

    /// <summary>DWMWCP_ROUND, an 8 DIP radius.</summary>
    Round,
}

/// <summary>
/// Where a blur backdrop window goes under a rounded surface body, and which DWM corner rounding it uses.
/// </summary>
/// <remarks>
/// The accent blur fills its window's whole rectangle and ignores window regions, so the only way to give it
/// rounded corners is DWM's own rounding, which comes in fixed radii. The backdrop therefore uses the closest
/// fixed radius and is inset just enough that its corners stay inside the body's rounded outline; the surface
/// draws its tint over the whole body, so the thin unblurred rim this leaves is not noticeable.
/// </remarks>
/// <param name="Bounds">Backdrop rectangle in screen pixels (empty when the body is empty).</param>
/// <param name="Corners">DWM corner rounding for the backdrop.</param>
public readonly record struct BackdropPlacement(PixelRect Bounds, BackdropCorners Corners)
{
    /// <summary>DWM's radius for <see cref="BackdropCorners.Round"/>, in DIPs.</summary>
    public const double RoundRadiusDip = 8;

    /// <summary>DWM's radius for <see cref="BackdropCorners.Small"/>, in DIPs.</summary>
    public const double SmallRadiusDip = 4;

    /// <summary>Body radii (DIP) below which DWM's small rounding, or none, is the closer match.</summary>
    private const double SmallCornerThresholdDip = 6;
    private const double SquareCornerThresholdDip = 2;

    /// <summary>
    /// Places a backdrop under a body occupying <paramref name="body"/> (screen pixels) whose corners have a
    /// radius of <paramref name="cornerRadiusPx"/>, on a monitor with DPI scale <paramref name="scale"/>.
    /// </summary>
    public static BackdropPlacement Compute(PixelRect body, double cornerRadiusPx, double scale)
    {
        scale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        var radiusDip = double.IsFinite(cornerRadiusPx) ? Math.Max(0, cornerRadiusPx) / scale : 0;

        // DWM's radii are fixed in DIPs, so the choice is made in DIPs too.
        var (corners, systemRadiusDip) = radiusDip < SquareCornerThresholdDip ? (BackdropCorners.Square, 0.0)
            : radiusDip < SmallCornerThresholdDip ? (BackdropCorners.Small, SmallRadiusDip)
            : (BackdropCorners.Round, RoundRadiusDip);

        // A corner of radius r inset by k stays inside a corner of radius R when k >= (R - r)(1 - 1/sqrt 2):
        // the inner arc's 45° point is the one that reaches furthest into the outer corner.
        var insetDip = Math.Max(0, radiusDip - systemRadiusDip) * (1 - (1 / Math.Sqrt(2)));
        var inset = (int)Math.Ceiling(insetDip * scale);
        var bounds = body.Inflate(-inset, -inset);
        return new BackdropPlacement(bounds.IsEmpty ? default : bounds, corners);
    }
}
