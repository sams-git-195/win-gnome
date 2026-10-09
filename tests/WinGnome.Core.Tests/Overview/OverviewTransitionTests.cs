using WinGnome.Core.Geometry;
using WinGnome.Core.Overview;

namespace WinGnome.Core.Tests.Overview;

public class OverviewTransitionTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.25, 0.4375)]
    [InlineData(0.5, 0.75)]
    [InlineData(1, 1)]
    [InlineData(-5, 0)]
    [InlineData(5, 1)]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    public void EaseOutQuad_EndpointsMiddleAndClamping(double progress, double expected)
    {
        Assert.Equal(expected, OverviewTransition.EaseOutQuad(progress), 9);
    }

    [Theory]
    [InlineData(0, 250, 0)]
    [InlineData(125, 250, 0.5)]
    [InlineData(250, 250, 1)]
    [InlineData(300, 250, 1)]
    [InlineData(-10, 250, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(-10, 0, 1)]
    [InlineData(10, -5, 1)]
    public void Progress_ElapsedOverDuration_Clamped(double elapsedMs, double durationMs, double expected)
    {
        var progress = OverviewTransition.Progress(TimeSpan.FromMilliseconds(elapsedMs), TimeSpan.FromMilliseconds(durationMs));

        Assert.Equal(expected, progress, 9);
    }

    [Theory]
    [InlineData(true, true, 250)]
    [InlineData(true, false, 200)]
    [InlineData(false, true, 0)]
    [InlineData(false, false, 0)]
    public void DurationFor_AnimationsSettingAndDirection(bool animationsEnabled, bool opening, double expectedMs)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), OverviewTransition.DurationFor(animationsEnabled, opening));
    }

    [Fact]
    public void At_Endpoints_ReturnFromAndTo()
    {
        var track = new ThumbnailTrack(new PixelRect(0, 0, 100, 100), new PixelRect(200, 100, 260, 140), 0, 255);

        Assert.Equal(new ThumbnailFrame(new PixelRect(0, 0, 100, 100), 0), track.At(0));
        Assert.Equal(new ThumbnailFrame(new PixelRect(200, 100, 260, 140), 255), track.At(1));
    }

    [Fact]
    public void At_Halfway_RoundsToWholePixelsAwayFromZero()
    {
        var track = new ThumbnailTrack(new PixelRect(0, 0, 101, 100), new PixelRect(100, 50, 200, 150), 255, 255);

        Assert.Equal(new ThumbnailFrame(new PixelRect(50, 25, 151, 125), 255), track.At(0.5));
    }

    [Fact]
    public void At_NegativeCoordinates_Interpolates()
    {
        // A maximised window on a monitor left of the primary: GetWindowRect overhangs by the resize border.
        var track = new ThumbnailTrack(new PixelRect(-1928, -8, -1003, 600), new PixelRect(100, 100, 300, 250), 255, 255);

        Assert.Equal(new ThumbnailFrame(new PixelRect(-914, 46, -352, 425), 255), track.At(0.5));
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(2, 0, 10, 255)]
    [InlineData(double.NaN, 0, 10, 255)]
    public void At_ClampsProgress(double eased, int expectedLeft, int expectedRight, byte expectedOpacity)
    {
        var track = new ThumbnailTrack(new PixelRect(0, 0, 0, 0), new PixelRect(0, 0, 10, 10), 0, 255);

        var frame = track.At(eased);

        Assert.Equal((expectedLeft, expectedRight, expectedOpacity), (frame.Rect.Left, frame.Rect.Right, frame.Opacity));
    }

    [Theory]
    [InlineData(0, 255, 0.5, 128)]
    [InlineData(255, 0, 0.5, 128)]
    [InlineData(255, 0, 0.25, 191)]
    [InlineData(0, 255, 0.25, 64)]
    [InlineData(255, 255, 0.5, 255)]
    public void At_Opacity_FadesBothWays(byte from, byte to, double eased, byte expected)
    {
        var rect = new PixelRect(0, 0, 10, 10);

        Assert.Equal(expected, new ThumbnailTrack(rect, rect, from, to).At(eased).Opacity);
    }

    [Fact]
    public void Still_StaysOpaqueAtTheRect()
    {
        var rect = new PixelRect(-5, 3, 40, 50);

        Assert.Equal(new ThumbnailTrack(rect, rect, 255, 255), ThumbnailTrack.Still(rect));
    }

    [Fact]
    public void Reversed_SwapsEndsAndOpacities()
    {
        var track = new ThumbnailTrack(new PixelRect(0, 0, 10, 10), new PixelRect(5, 5, 8, 8), 0, 255);

        Assert.Equal(new ThumbnailTrack(new PixelRect(5, 5, 8, 8), new PixelRect(0, 0, 10, 10), 255, 0), track.Reversed());
    }

    [Fact]
    public void Retarget_FromMidway_StartsWhereTheThumbnailIs()
    {
        var track = new ThumbnailTrack(new PixelRect(0, 0, 100, 100), new PixelRect(200, 200, 300, 300), 255, 0);

        var back = OverviewTransition.Retarget(track, 0.5, track.From, track.FromOpacity);

        Assert.Equal(new ThumbnailTrack(new PixelRect(100, 100, 200, 200), new PixelRect(0, 0, 100, 100), 128, 255), back);
    }

    [Fact]
    public void Retarget_FromTheEnd_EqualsReversed()
    {
        var track = new ThumbnailTrack(new PixelRect(-50, 0, 100, 100), new PixelRect(200, 200, 300, 300), 0, 255);

        Assert.Equal(track.Reversed(), OverviewTransition.Retarget(track, 1, track.From, track.FromOpacity));
    }

    [Theory]
    [InlineData(0, 0.6, 0, 0)]
    [InlineData(0, 0.6, 1, 153)]
    [InlineData(0.6, 0, 1, 0)]
    [InlineData(0.6, 0, 0, 153)]
    [InlineData(0, 0.6, 0.5, 77)]
    [InlineData(0, 0.6, 2, 153)]
    [InlineData(0, 0.6, double.NaN, 153)]
    [InlineData(0, 1.5, 1, 255)]
    [InlineData(0.5, -1, 1, 0)]
    public void DimAlpha_InterpolatesAndClamps(double from, double to, double eased, byte expected)
    {
        Assert.Equal(expected, OverviewTransition.DimAlpha(from, to, eased));
    }
}
