using WinGnome.Core.Geometry;

namespace WinGnome.Core.Monitors;

/// <summary>
/// One display monitor as WinGnome lays surfaces out on it. All rectangles are physical pixels in virtual-screen
/// coordinates (they may be negative).
/// </summary>
/// <param name="Key">The GDI device name (<c>\\.\DISPLAY3</c>). Matches instances across a re-read; never persisted.</param>
/// <param name="Bounds">The monitor's full rectangle.</param>
/// <param name="WorkArea">The rectangle left after AppBars (ours included) took their strips.</param>
/// <param name="Dpi">Effective DPI (96 = 100 %).</param>
/// <param name="IsPrimary">True for the primary monitor (the one at the origin).</param>
public sealed record MonitorInfo(string Key, PixelRect Bounds, PixelRect WorkArea, int Dpi, bool IsPrimary)
{
    /// <summary>DPI scale factor (1.0 = 100 %); 1.0 when the DPI is unknown.</summary>
    public double Scale => Dpi > 0 ? Dpi / 96.0 : 1.0;
}
