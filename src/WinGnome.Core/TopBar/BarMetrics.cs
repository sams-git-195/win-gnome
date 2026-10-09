namespace WinGnome.Core.TopBar;

/// <summary>
/// Whole-device-pixel sizes for the top bar's text, symbolic icons and hover pills. The bar is translucent, so text
/// is grayscale-antialiased: anything that lands on a fractional pixel (13.5 DIP × 1.25 = 16.875 px) is visibly soft.
/// </summary>
public static class BarMetrics
{
    /// <summary>The bar's default text size in DIPs (TopBarSettings.FontSize).</summary>
    public const double DefaultFontSize = 13.5;

    /// <summary>Symbolic icons are drawn on a grid of this many units, GNOME's 16 px icon size.</summary>
    public const int IconGridUnits = 16;

    // GNOME's panel icons are 16 px next to ~13.5 px text; icons keep that ratio when the text size changes.
    private const double IconToFontRatio = IconGridUnits / DefaultFontSize;
    private const int MinIconPx = 8;

    // Hover pills leave ~10 % of the bar height free above and below, and never less than 2 DIPs.
    private const double PillInsetToHeightRatio = 0.1;
    private const double MinPillInsetDip = 2;

    /// <summary>Rounds a length to the nearest whole device pixel and returns it in DIPs; invalid lengths give 0.</summary>
    public static double SnapToDevice(double dip, double scale)
    {
        scale = ValidScale(scale);
        return double.IsFinite(dip) && dip > 0 ? Px(dip * scale) / scale : 0;
    }

    /// <summary>Edge length in device pixels of the bar's symbolic icons for a text size.</summary>
    public static int SymbolicIconPx(double fontDip, double scale)
    {
        fontDip = double.IsFinite(fontDip) && fontDip > 0 ? fontDip : DefaultFontSize;
        return Math.Max(MinIconPx, Px(fontDip * IconToFontRatio * ValidScale(scale)));
    }

    /// <summary>
    /// Maps an icon-grid coordinate (0..16) to the nearest whole device pixel of an icon <paramref name="iconPx"/>
    /// wide, so a shape's straight edges stay sharp at any icon size. Invalid input gives 0.
    /// </summary>
    public static double SnapIconUnit(double unit, int iconPx) =>
        iconPx > 0 && double.IsFinite(unit) ? Px(unit * iconPx / IconGridUnits) : 0;

    /// <summary>
    /// Gap in device pixels between a hover pill and the bar's top and bottom edges. A whole number of pixels on both
    /// sides keeps the pill vertically symmetric (a fractional DIP margin rounds up on one side and down on the other).
    /// </summary>
    public static int PillInsetPx(double barHeightDip, double scale)
    {
        scale = ValidScale(scale);
        barHeightDip = double.IsFinite(barHeightDip) && barHeightDip > 0 ? barHeightDip : 0;
        return Math.Max(Px(MinPillInsetDip * scale), Px(barHeightDip * PillInsetToHeightRatio * scale));
    }

    private static double ValidScale(double scale) => double.IsFinite(scale) && scale > 0 ? scale : 1;

    private static int Px(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
