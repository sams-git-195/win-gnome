using WinGnome.Core.TopBar;

namespace WinGnome.Core.Tests.TopBar;

public class PopupPlacementTests
{
    // A 100 DIP wide anchor and a 360 DIP popup whose card has a 12 DIP shadow margin on each side.
    private const double Anchor = 100;
    private const double Popup = 360;
    private const double Margin = 12;

    [Fact]
    public void Start_AlignsCardLeftEdges()
    {
        var offset = PopupPlacement.HorizontalOffset(PopupAlignment.Start, Anchor, Popup, Margin);

        Assert.Equal(0, offset + Margin);
    }

    [Fact]
    public void End_AlignsCardRightEdges()
    {
        var offset = PopupPlacement.HorizontalOffset(PopupAlignment.End, Anchor, Popup, Margin);

        Assert.Equal(Anchor, offset + Popup - Margin);
    }

    [Fact]
    public void Center_CentresCardUnderAnchor()
    {
        var offset = PopupPlacement.HorizontalOffset(PopupAlignment.Center, Anchor, Popup, Margin);

        Assert.Equal(Anchor / 2, offset + (Popup / 2));
    }
}
