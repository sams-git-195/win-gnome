using WinGnome.Core.Geometry;

namespace WinGnome.Core.TopBar;

/// <summary>
/// Physical-pixel geometry of the top bar. The AppBar reserves a strip of <see cref="ThicknessPx"/> across the
/// top of the monitor; the visible bar body sits inside it, inset by the floating margin on the top, left and
/// right, with the same margin left free below it so maximised windows never touch a floating bar.
/// </summary>
/// <param name="ThicknessPx">Height of the reserved AppBar strip (body + 2 × inset).</param>
/// <param name="InsetPx">Floating margin around the body.</param>
/// <param name="BodyHeightPx">Height of the visible bar.</param>
/// <param name="CornerRadiusPx">Corner radius of the body, at most half its height.</param>
public readonly record struct TopBarGeometry(int ThicknessPx, int InsetPx, int BodyHeightPx, int CornerRadiusPx)
{
    /// <summary>Converts the DIP settings to whole physical pixels for a monitor with the given DPI scale.</summary>
    public static TopBarGeometry Compute(double heightDip, double marginDip, double cornerRadiusDip, double scale)
    {
        scale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        var body = Math.Max(1, (int)Math.Round(Math.Max(0, heightDip) * scale));
        var inset = (int)Math.Round(Math.Max(0, marginDip) * scale);
        var radius = Math.Min((int)Math.Round(Math.Max(0, cornerRadiusDip) * scale), body / 2);
        return new TopBarGeometry(body + (2 * inset), inset, body, radius);
    }

    /// <summary>The visible bar body relative to the window's top-left corner, for a window this wide.</summary>
    public PixelRect BodyRect(int windowWidthPx) =>
        new(InsetPx, InsetPx, Math.Max(InsetPx, windowWidthPx - InsetPx), InsetPx + BodyHeightPx);
}
