using WinGnome.Core.Geometry;

namespace WinGnome.Core.Tests.Geometry;

public class PixelRectTests
{
    [Fact]
    public void FromSize_BuildsEdges()
    {
        var r = PixelRect.FromSize(10, 20, 100, 50);

        Assert.Equal(new PixelRect(10, 20, 110, 70), r);
        Assert.Equal(100, r.Width);
        Assert.Equal(50, r.Height);
    }

    [Fact]
    public void Size_NeverNegative()
    {
        var inverted = new PixelRect(50, 50, 10, 10);

        Assert.Equal(0, inverted.Width);
        Assert.Equal(0, inverted.Height);
        Assert.True(inverted.IsEmpty);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, true)]
    [InlineData(0, 0, 1, 0, true)]
    [InlineData(0, 0, 0, 1, true)]
    [InlineData(0, 0, 1, 1, false)]
    [InlineData(5, 5, 5, 9, true)]
    [InlineData(-10, -10, -5, -5, false)]
    public void IsEmpty(int l, int t, int r, int b, bool expected)
    {
        Assert.Equal(expected, new PixelRect(l, t, r, b).IsEmpty);
    }

    [Fact]
    public void Center_UsesIntegerHalf()
    {
        var r = new PixelRect(10, 20, 111, 71);

        Assert.Equal(60, r.CenterX); // 10 + 101/2
        Assert.Equal(45, r.CenterY); // 20 + 51/2
    }

    [Fact]
    public void Center_OfNegativeCoordinates()
    {
        var r = PixelRect.FromSize(-1920, 0, 1920, 1080);

        Assert.Equal(-960, r.CenterX);
        Assert.Equal(540, r.CenterY);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(9, 9, true)]
    [InlineData(10, 5, false)]
    [InlineData(5, 10, false)]
    [InlineData(-1, 5, false)]
    [InlineData(5, -1, false)]
    public void Contains_RightAndBottomAreExclusive(int x, int y, bool expected)
    {
        Assert.Equal(expected, new PixelRect(0, 0, 10, 10).Contains(x, y));
    }

    [Fact]
    public void Contains_EmptyRectContainsNothing()
    {
        Assert.False(new PixelRect(5, 5, 5, 5).Contains(5, 5));
    }

    [Fact]
    public void Intersects_OverlapAndContainment()
    {
        var a = new PixelRect(0, 0, 10, 10);

        Assert.True(a.Intersects(new PixelRect(5, 5, 15, 15)));
        Assert.True(a.Intersects(new PixelRect(2, 2, 4, 4)));
        Assert.True(new PixelRect(2, 2, 4, 4).Intersects(a));
    }

    [Fact]
    public void Intersects_TouchingEdgesDoNotIntersect()
    {
        var a = new PixelRect(0, 0, 10, 10);

        Assert.False(a.Intersects(new PixelRect(10, 0, 20, 10)));
        Assert.False(a.Intersects(new PixelRect(0, 10, 10, 20)));
        Assert.False(a.Intersects(new PixelRect(-10, 0, 0, 10)));
    }

    [Fact]
    public void Intersects_EmptyNeverIntersects()
    {
        var a = new PixelRect(0, 0, 10, 10);
        Assert.False(a.Intersects(new PixelRect(5, 5, 5, 5)));
        Assert.False(new PixelRect(5, 5, 5, 5).Intersects(a));
    }

    [Fact]
    public void Intersect_ReturnsOverlap()
    {
        var result = new PixelRect(0, 0, 10, 10).Intersect(new PixelRect(5, 6, 20, 8));
        Assert.Equal(new PixelRect(5, 6, 10, 8), result);
    }

    [Fact]
    public void Intersect_ContainedRectIsReturnedUnchanged()
    {
        var inner = new PixelRect(2, 2, 4, 4);
        Assert.Equal(inner, new PixelRect(0, 0, 10, 10).Intersect(inner));
    }

    [Fact]
    public void Intersect_DisjointIsEmpty()
    {
        var result = new PixelRect(0, 0, 10, 10).Intersect(new PixelRect(20, 20, 30, 30));
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Intersect_TouchingIsEmpty()
    {
        Assert.True(new PixelRect(0, 0, 10, 10).Intersect(new PixelRect(10, 0, 20, 10)).IsEmpty);
    }

    [Fact]
    public void Offset_MovesBothCorners()
    {
        Assert.Equal(new PixelRect(5, -5, 15, 5), new PixelRect(0, 0, 10, 10).Offset(5, -5));
    }

    [Fact]
    public void Inflate_GrowsAndShrinks()
    {
        var r = new PixelRect(10, 10, 20, 20);

        Assert.Equal(new PixelRect(8, 7, 22, 23), r.Inflate(2, 3));
        Assert.Equal(new PixelRect(12, 13, 18, 17), r.Inflate(-2, -3));
    }

    [Fact]
    public void Inflate_ShrinkingPastZeroBecomesEmpty()
    {
        Assert.True(new PixelRect(0, 0, 4, 4).Inflate(-3, 0).IsEmpty);
    }

    [Fact]
    public void CenteredIn_CentresSameSize()
    {
        var window = PixelRect.FromSize(0, 0, 100, 50);
        var area = new PixelRect(0, 0, 1000, 600);

        Assert.Equal(PixelRect.FromSize(450, 275, 100, 50), window.CenteredIn(area));
    }

    [Fact]
    public void CenteredIn_RespectsAreaOrigin()
    {
        var window = PixelRect.FromSize(300, 300, 200, 100);
        var area = new PixelRect(1920, 40, 3840, 1080);

        var result = window.CenteredIn(area);

        Assert.Equal(200, result.Width);
        Assert.Equal(100, result.Height);
        Assert.Equal(1920 + ((1920 - 200) / 2), result.Left);
        Assert.Equal(40 + ((1040 - 100) / 2), result.Top);
    }

    [Fact]
    public void CenteredIn_OddLeftoverRoundsDown()
    {
        var result = PixelRect.FromSize(0, 0, 101, 11).CenteredIn(new PixelRect(0, 0, 1000, 100));

        Assert.Equal(449, result.Left);
        Assert.Equal(44, result.Top);
    }

    [Fact]
    public void CenteredIn_LargerThanArea_PinsTopLeftToArea()
    {
        var result = PixelRect.FromSize(0, 0, 2000, 1000).CenteredIn(new PixelRect(100, 50, 1100, 650));

        Assert.Equal(new PixelRect(100, 50, 2100, 1050), result);
    }

    [Fact]
    public void CenteredIn_LargerOnOneAxisOnly_CentresTheOther()
    {
        var result = PixelRect.FromSize(0, 0, 2000, 100).CenteredIn(new PixelRect(0, 0, 1000, 600));

        Assert.Equal(0, result.Left);
        Assert.Equal(250, result.Top);
    }

    [Fact]
    public void CenteredIn_ExactFit_KeepsAreaOrigin()
    {
        var area = new PixelRect(10, 10, 110, 110);
        Assert.Equal(area, PixelRect.FromSize(500, 500, 100, 100).CenteredIn(area));
    }
}
