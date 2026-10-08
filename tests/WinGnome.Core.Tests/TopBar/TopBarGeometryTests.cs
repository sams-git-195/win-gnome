using WinGnome.Core.Geometry;
using WinGnome.Core.TopBar;

namespace WinGnome.Core.Tests.TopBar;

public class TopBarGeometryTests
{
    [Fact]
    public void ClassicBar_FillsTheStrip()
    {
        var g = TopBarGeometry.Compute(32, 0, 0, 1.0);

        Assert.Equal(new TopBarGeometry(32, 0, 32, 0), g);
        Assert.Equal(new PixelRect(0, 0, 1920, 32), g.BodyRect(1920));
    }

    [Fact]
    public void FloatingBar_ReservesMarginAboveAndBelow()
    {
        var g = TopBarGeometry.Compute(32, 8, 12, 1.5);

        Assert.Equal(new TopBarGeometry(48 + 24, 12, 48, 18), g);
        Assert.Equal(new PixelRect(12, 12, 2868, 60), g.BodyRect(2880));
    }

    [Fact]
    public void CornerRadius_IsAtMostHalfTheHeight()
    {
        Assert.Equal(12, TopBarGeometry.Compute(24, 0, 24, 1).CornerRadiusPx);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void InvalidScale_MeansOne(double scale)
    {
        Assert.Equal(new TopBarGeometry(32, 0, 32, 0), TopBarGeometry.Compute(32, 0, 0, scale));
    }

    [Fact]
    public void RoundsToWholePixels()
    {
        Assert.Equal(new TopBarGeometry(40 + 6, 3, 40, 0), TopBarGeometry.Compute(32, 2.5, 0, 1.25));
    }

    [Fact]
    public void BodyRect_NeverInverts()
    {
        Assert.Equal(new PixelRect(10, 10, 10, 42), TopBarGeometry.Compute(32, 10, 0, 1).BodyRect(5));
    }
}
