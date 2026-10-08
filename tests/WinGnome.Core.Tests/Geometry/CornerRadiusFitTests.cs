using WinGnome.Core.Geometry;

namespace WinGnome.Core.Tests.Geometry;

public class CornerRadiusFitTests
{
    [Fact]
    public void RadiusWithinHalfTheShorterSide_IsUnchanged()
    {
        Assert.Equal(6, CornerRadiusFit.Fit(6, width: 120, height: 24));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(100)]
    [InlineData(double.PositiveInfinity)]
    public void RadiusAtOrOverHalfTheHeight_IsCappedToMakeAPill(double requested)
    {
        Assert.Equal(12, CornerRadiusFit.Fit(requested, width: 120, height: 24));
    }

    [Fact]
    public void TallNarrowBox_IsCappedAtHalfTheWidth()
    {
        Assert.Equal(10, CornerRadiusFit.Fit(100, width: 20, height: 60));
    }

    [Fact]
    public void OddHeight_KeepsTheFractionalHalf()
    {
        Assert.Equal(12.5, CornerRadiusFit.Fit(100, width: 80, height: 25));
    }

    [Theory]
    [InlineData(-4, 120, 24)]
    [InlineData(double.NaN, 120, 24)]
    [InlineData(100, 0, 24)]
    [InlineData(100, double.NaN, 24)]
    [InlineData(100, 120, -24)]
    public void InvalidRadiusOrSize_GivesSquareCorners(double requested, double width, double height)
    {
        Assert.Equal(0, CornerRadiusFit.Fit(requested, width, height));
    }
}
