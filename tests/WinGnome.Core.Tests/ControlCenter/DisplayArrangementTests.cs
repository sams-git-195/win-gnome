using WinGnome.Core.ControlCenter;
using WinGnome.Core.Geometry;

namespace WinGnome.Core.Tests.ControlCenter;

public class DisplayArrangementTests
{
    private static DisplayPlacement D(string id, int x, int y, int w, int h) => new(id, PixelRect.FromSize(x, y, w, h));

    [Fact]
    public void MakePrimary_MovesTheChosenDisplayToTheOrigin()
    {
        var displays = new[] { D("A", 0, 0, 1920, 1080), D("B", 1920, 0, 2560, 1440) };

        var result = DisplayArrangement.MakePrimary(displays, "B");

        Assert.Equal([D("A", -1920, 0, 1920, 1080), D("B", 0, 0, 2560, 1440)], result);
    }

    [Fact]
    public void MakePrimary_AlreadyAtOrigin_ChangesNothing()
    {
        var displays = new[] { D("A", 0, 0, 1920, 1080), D("B", -1280, 200, 1280, 1024) };

        Assert.Equal(displays, DisplayArrangement.MakePrimary(displays, "A"));
    }

    [Fact]
    public void MakePrimary_UnknownId_Throws()
    {
        Assert.Throws<ArgumentException>(() => DisplayArrangement.MakePrimary([D("A", 0, 0, 10, 10)], "Z"));
    }

    [Fact]
    public void Resize_Wider_ShiftsDisplaysOnTheRight()
    {
        var displays = new[] { D("A", 0, 0, 1920, 1080), D("B", 1920, 0, 1920, 1080), D("C", 3840, 0, 1920, 1080) };

        var result = DisplayArrangement.Resize(displays, "A", 2560, 1440);

        Assert.Equal([D("A", 0, 0, 2560, 1440), D("B", 2560, 0, 1920, 1080), D("C", 4480, 0, 1920, 1080)], result);
    }

    [Fact]
    public void Resize_Narrower_PullsDisplaysOnTheRightIn()
    {
        var displays = new[] { D("A", 0, 0, 1920, 1080), D("B", 1920, 0, 1920, 1080) };

        var result = DisplayArrangement.Resize(displays, "A", 1280, 720);

        Assert.Equal([D("A", 0, 0, 1280, 720), D("B", 1280, 0, 1920, 1080)], result);
    }

    [Fact]
    public void Resize_Taller_ShiftsDisplaysBelow()
    {
        var displays = new[] { D("A", 0, 0, 1920, 1080), D("B", 0, 1080, 1920, 1080) };

        var result = DisplayArrangement.Resize(displays, "A", 1920, 1200);

        Assert.Equal([D("A", 0, 0, 1920, 1200), D("B", 0, 1200, 1920, 1080)], result);
    }

    [Fact]
    public void Resize_LeavesDisplaysOnTheLeftAlone()
    {
        var displays = new[] { D("L", -1280, 0, 1280, 1024), D("A", 0, 0, 1920, 1080) };

        var result = DisplayArrangement.Resize(displays, "A", 2560, 1440);

        Assert.Equal([D("L", -1280, 0, 1280, 1024), D("A", 0, 0, 2560, 1440)], result);
    }

    [Fact]
    public void Move_SnapsFlushToTheNearestEdge()
    {
        var displays = new[] { D("A", 0, 0, 1920, 1080), D("B", 1920, 0, 1920, 1080) };

        // Dropped with a gap above-right of A: B lands flush against A's right edge, keeping its height offset.
        var result = DisplayArrangement.Move(displays, "B", 2000, -300, snapDistance: 0);

        Assert.Equal([D("A", 0, 0, 1920, 1080), D("B", 1920, -300, 1920, 1080)], result);
    }

    [Fact]
    public void Move_ToTheLeftSide()
    {
        var displays = new[] { D("A", 0, 0, 1920, 1080), D("B", 1920, 0, 1280, 1024) };

        var result = DisplayArrangement.Move(displays, "B", -1500, 40, snapDistance: 0);

        Assert.Equal([D("A", 0, 0, 1920, 1080), D("B", -1280, 40, 1280, 1024)], result);
    }

    [Fact]
    public void Move_Below_SnapsToTheBottomEdge()
    {
        var displays = new[] { D("A", 0, 0, 1920, 1080), D("B", 1920, 0, 1920, 1080) };

        var result = DisplayArrangement.Move(displays, "B", 100, 1200, snapDistance: 0);

        Assert.Equal([D("A", 0, 0, 1920, 1080), D("B", 100, 1080, 1920, 1080)], result);
    }

    [Fact]
    public void Move_KeepsAtLeastOnePixelOfSharedEdge()
    {
        var displays = new[] { D("A", 0, 0, 1920, 1080), D("B", 1920, 0, 1920, 1080) };

        // Far below A's bottom-right corner: B can only touch A along an edge (here one pixel of its bottom), never just at a corner.
        var result = DisplayArrangement.Move(displays, "B", 1920, 5000, snapDistance: 0);

        Assert.Equal(D("B", 1919, 1080, 1920, 1080), result[1]);
    }

    [Theory]
    [InlineData(-30, 0)]
    [InlineData(30, 0)]
    [InlineData(-61, -61)]
    public void Move_AlignsTopEdgesWithinTheSnapDistance(int proposedTop, int expectedTop)
    {
        var displays = new[] { D("A", 0, 0, 1920, 1080), D("B", 1920, 0, 1920, 1080) };

        var result = DisplayArrangement.Move(displays, "B", 1920, proposedTop, snapDistance: 60);

        Assert.Equal(expectedTop, result[1].Bounds.Top);
    }

    [Fact]
    public void Move_AlignsBottomEdgesWithinTheSnapDistance()
    {
        var displays = new[] { D("A", 0, 0, 1920, 1080), D("B", 1920, 0, 1280, 1024) };

        var result = DisplayArrangement.Move(displays, "B", 1920, 70, snapDistance: 60);

        Assert.Equal(56, result[1].Bounds.Top);
    }

    [Fact]
    public void Move_NeverOverlapsAThirdDisplay()
    {
        var displays = new[] { D("A", 0, 0, 1920, 1080), D("B", 1920, 0, 1920, 1080), D("C", 0, 1080, 1920, 1080) };

        // Dropped where the nearest flush spot (below A) is taken by C: B goes to the next nearest free spot, below C.
        var result = DisplayArrangement.Move(displays, "B", 0, 1100, snapDistance: 0);

        Assert.False(result[1].Bounds.Intersects(result[2].Bounds));
        Assert.False(result[1].Bounds.Intersects(result[0].Bounds));
        Assert.Equal(D("B", 0, 2160, 1920, 1080), result[1]);
    }

    [Fact]
    public void Move_SingleDisplay_ChangesNothing()
    {
        var displays = new[] { D("A", 0, 0, 1920, 1080) };

        Assert.Equal(displays, DisplayArrangement.Move(displays, "A", 500, 500, snapDistance: 0));
    }

    [Fact]
    public void IsValid_TouchingDisplays_True()
    {
        Assert.True(DisplayArrangement.IsValid([D("A", 0, 0, 1920, 1080), D("B", 1920, 500, 1920, 1080)]));
    }

    [Fact]
    public void IsValid_Overlap_False()
    {
        Assert.False(DisplayArrangement.IsValid([D("A", 0, 0, 1920, 1080), D("B", 1900, 0, 1920, 1080)]));
    }

    [Fact]
    public void IsValid_Gap_False()
    {
        Assert.False(DisplayArrangement.IsValid([D("A", 0, 0, 1920, 1080), D("B", 1921, 0, 1920, 1080)]));
    }

    [Fact]
    public void IsValid_CornerOnly_False()
    {
        Assert.False(DisplayArrangement.IsValid([D("A", 0, 0, 1920, 1080), D("B", 1920, 1080, 1920, 1080)]));
    }

    [Fact]
    public void IsValid_ChainOfThree_True()
    {
        Assert.True(DisplayArrangement.IsValid([D("A", 0, 0, 100, 100), D("B", 100, 0, 100, 100), D("C", 200, 0, 100, 100)]));
    }

    [Fact]
    public void IsValid_TwoSeparateIslands_False()
    {
        Assert.False(DisplayArrangement.IsValid(
            [D("A", 0, 0, 100, 100), D("B", 100, 0, 100, 100), D("C", 500, 0, 100, 100), D("D", 600, 0, 100, 100)]));
    }

    [Fact]
    public void Fit_ScalesAndCentresTheArrangement()
    {
        var displays = new[] { D("A", 0, 0, 1920, 1080), D("B", -1920, 0, 1920, 1080) };

        // Bounding box 3840 x 1080 into 400 x 200 with 20 padding: scale = 360 / 3840 = 0.09375, height 101.25.
        var result = DisplayArrangement.Fit(displays, 400, 200, 20);

        Assert.Equal(new LayoutRect(200, 49.375, 180, 101.25), result[0]);
        Assert.Equal(new LayoutRect(20, 49.375, 180, 101.25), result[1]);
    }

    [Fact]
    public void Fit_Empty_ReturnsEmpty()
    {
        Assert.Empty(DisplayArrangement.Fit([], 400, 200, 20));
    }
}
