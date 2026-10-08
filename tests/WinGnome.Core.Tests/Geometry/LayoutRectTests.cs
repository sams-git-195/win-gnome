using WinGnome.Core.Geometry;

namespace WinGnome.Core.Tests.Geometry;

public class LayoutRectTests
{
    [Fact]
    public void DerivedEdgesAndCentre()
    {
        var r = new LayoutRect(10, 20, 30, 50);

        Assert.Equal(40, r.Right);
        Assert.Equal(70, r.Bottom);
        Assert.Equal(25, r.CenterX);
        Assert.Equal(45, r.CenterY);
    }

    [Fact]
    public void FractionalValues()
    {
        var r = new LayoutRect(0.5, 0.25, 1, 2);

        Assert.Equal(1.5, r.Right);
        Assert.Equal(2.25, r.Bottom);
        Assert.Equal(1, r.CenterX);
        Assert.Equal(1.25, r.CenterY);
    }

    [Fact]
    public void ValueEquality()
    {
        Assert.Equal(new LayoutRect(1, 2, 3, 4), new LayoutRect(1, 2, 3, 4));
        Assert.Equal(new LayoutSize(3, 4), new LayoutSize(3, 4));
        Assert.NotEqual(new LayoutSize(3, 4), new LayoutSize(4, 3));
    }
}
