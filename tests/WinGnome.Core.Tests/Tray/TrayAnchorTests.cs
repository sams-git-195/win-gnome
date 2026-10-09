using WinGnome.Core.Geometry;
using WinGnome.Core.Tray;

namespace WinGnome.Core.Tests.Tray;

public class TrayAnchorTests
{
    [Fact]
    public void For_IconInsideTheBar_AnchorsAtTheIconCentreOnTheBarsBottomEdge()
    {
        var bar = new PixelRect(0, 0, 2560, 40);
        var icon = new PixelRect(2000, 6, 2030, 34);

        Assert.Equal((2015, 40), TrayAnchor.For(icon, bar));
    }

    [Fact]
    public void For_BarAboveThePrimary_AnchorsOnThatBar()
    {
        // A monitor above the primary: its bar is at negative Y. Anchoring on the primary's bar (y = 40) would open
        // the app's menu on the other monitor.
        var bar = new PixelRect(-447, -1440, 2993, -1408);
        var icon = new PixelRect(2800, -1436, 2824, -1412);

        Assert.Equal((2812, -1408), TrayAnchor.For(icon, bar));
    }

    [Fact]
    public void For_IconReachingBelowTheBar_AnchorsAtTheIconsBottom()
    {
        // PointToScreen on a scaled monitor can put the icon's edge a pixel past the strip.
        var bar = new PixelRect(0, 0, 1920, 32);
        var icon = new PixelRect(100, 2, 125, 33);

        Assert.Equal((112, 33), TrayAnchor.For(icon, bar));
    }
}
