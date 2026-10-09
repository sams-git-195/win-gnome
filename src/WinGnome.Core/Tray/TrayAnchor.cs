using WinGnome.Core.Geometry;

namespace WinGnome.Core.Tray;

/// <summary>The anchor point a version 4 tray app receives with a click (where it places its menu).</summary>
public static class TrayAnchor
{
    /// <summary>
    /// The icon's horizontal centre at the bottom edge of the bar that was clicked, so menus open just under that
    /// bar, like macOS menu bar extras. Uses the clicked bar's strip, which may lie above the primary (negative Y).
    /// </summary>
    public static (int X, int Y) For(PixelRect iconBounds, PixelRect barBounds) =>
        (iconBounds.Left + (iconBounds.Width / 2), Math.Max(iconBounds.Bottom, barBounds.Bottom));
}
