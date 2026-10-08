using WinGnome.Core.Geometry;
using WinGnome.Core.Overview;

namespace WinGnome.Core.Tests.Overview;

public class ThumbnailTransitionTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(-5, 0)]
    [InlineData(5, 1)]
    [InlineData(double.NaN, 1)]
    public void EaseOut_EndpointsAndClamping(double progress, double expected)
    {
        Assert.Equal(expected, ThumbnailTransition.EaseOut(progress), 9);
    }

    [Fact]
    public void EaseOut_IsAheadOfLinearInTheMiddle()
    {
        Assert.Equal(0.875, ThumbnailTransition.EaseOut(0.5), 9);
    }

    [Fact]
    public void Interpolate_Endpoints()
    {
        var from = new PixelRect(0, 0, 100, 100);
        var to = new PixelRect(200, 100, 260, 140);

        Assert.Equal(from, ThumbnailTransition.Interpolate(from, to, 0));
        Assert.Equal(to, ThumbnailTransition.Interpolate(from, to, 1));
    }

    [Fact]
    public void Interpolate_Halfway_RoundsToWholePixels()
    {
        var result = ThumbnailTransition.Interpolate(new PixelRect(0, 0, 101, 100), new PixelRect(100, 50, 200, 150), 0.5);

        Assert.Equal(new PixelRect(50, 25, 151, 125), result);
    }

    [Fact]
    public void Interpolate_ClampsProgress()
    {
        var from = new PixelRect(0, 0, 10, 10);
        var to = new PixelRect(10, 10, 20, 20);

        Assert.Equal(to, ThumbnailTransition.Interpolate(from, to, 3));
        Assert.Equal(from, ThumbnailTransition.Interpolate(from, to, -1));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, 255)]
    [InlineData(0, 0.5, 128)]
    [InlineData(255, 0, 255)]
    public void FadeIn_InterpolatesOpacity(byte from, double progress, byte expected)
    {
        Assert.Equal(expected, ThumbnailTransition.FadeIn(from, progress));
    }
}
