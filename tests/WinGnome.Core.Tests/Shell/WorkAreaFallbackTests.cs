using WinGnome.Core.Geometry;
using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class WorkAreaFallbackTests
{
    // The user's primary (2560x1600 physical, its taskbar taking the bottom 60 px) and the 3440x1440 monitor above it.
    private static readonly PixelRect PrimaryWork = new(0, 0, 2560, 1540);
    private static readonly PixelRect PrimaryBar = new(0, 0, 2560, 40);
    private static readonly PixelRect UpperWork = new(-447, -1440, 2993, 0);
    private static readonly PixelRect UpperBar = new(-447, -1440, 2993, -1408);

    [Fact]
    public void Shrink_Top_MovesOnlyTheTopEdge()
    {
        Assert.Equal(new PixelRect(0, 40, 2560, 1540), WorkAreaFallback.Shrink(AppBarEdge.Top, PrimaryBar, PrimaryWork));
    }

    [Fact]
    public void Shrink_Bottom_MovesOnlyTheBottomEdge()
    {
        var dock = new PixelRect(0, 1504, 2560, 1600);

        Assert.Equal(new PixelRect(0, 0, 2560, 1504), WorkAreaFallback.Shrink(AppBarEdge.Bottom, dock, new PixelRect(0, 0, 2560, 1600)));
    }

    [Fact]
    public void Shrink_Left_MovesOnlyTheLeftEdge()
    {
        var dock = new PixelRect(0, 0, 96, 1600);

        Assert.Equal(new PixelRect(96, 0, 2560, 1600), WorkAreaFallback.Shrink(AppBarEdge.Left, dock, new PixelRect(0, 0, 2560, 1600)));
    }

    [Fact]
    public void Shrink_Right_MovesOnlyTheRightEdge()
    {
        var dock = new PixelRect(2464, 0, 2560, 1600);

        Assert.Equal(new PixelRect(0, 0, 2464, 1600), WorkAreaFallback.Shrink(AppBarEdge.Right, dock, new PixelRect(0, 0, 2560, 1600)));
    }

    [Theory]
    [InlineData(39, 40)]  // One pixel short of the strip.
    [InlineData(0, 40)]   // Nothing reserved at all.
    public void Shrink_Top_WorkAreaAboveTheStripEnd_MovesItDown(int workTop, int expectedTop)
    {
        var work = new PixelRect(0, workTop, 2560, 1540);

        Assert.Equal(new PixelRect(0, expectedTop, 2560, 1540), WorkAreaFallback.Shrink(AppBarEdge.Top, PrimaryBar, work));
    }

    [Theory]
    [InlineData(40)] // Exactly at the strip's end.
    [InlineData(80)] // Stacked below another top bar: the work area ends beyond both.
    public void Shrink_Top_WorkAreaAlreadyPastTheStrip_ReturnsNull(int workTop)
    {
        Assert.Null(WorkAreaFallback.Shrink(AppBarEdge.Top, PrimaryBar, new PixelRect(0, workTop, 2560, 1540)));
    }

    [Fact]
    public void Shrink_EmptyStrip_ReturnsNull()
    {
        // Zero height, but its bottom edge lies below the work area's top, so it reserves nothing.
        Assert.Null(WorkAreaFallback.Shrink(AppBarEdge.Top, new PixelRect(0, 40, 2560, 40), new PixelRect(0, 0, 2560, 1600)));
    }

    [Fact]
    public void Shrink_StripTallerThanTheMonitor_ReturnsNull()
    {
        // Shrinking past the bottom edge would invert the work area and hide the whole desktop.
        Assert.Null(WorkAreaFallback.Shrink(AppBarEdge.Top, new PixelRect(0, 0, 2560, 1700), new PixelRect(0, 0, 2560, 1600)));
    }

    [Fact]
    public void Shrink_StripExactlyTheMonitor_ReturnsNull()
    {
        Assert.Null(WorkAreaFallback.Shrink(AppBarEdge.Top, new PixelRect(0, 0, 2560, 1600), new PixelRect(0, 0, 2560, 1600)));
    }

    [Fact]
    public void Shrink_EmptyWorkArea_ReturnsNull()
    {
        Assert.Null(WorkAreaFallback.Shrink(AppBarEdge.Top, PrimaryBar, default));
    }

    [Fact]
    public void Shrink_NegativeCoordinates_MovesTheTopEdgeUp()
    {
        // The upper monitor sits above the primary, so its work area has negative coordinates throughout.
        Assert.Equal(new PixelRect(-447, -1408, 2993, 0), WorkAreaFallback.Shrink(AppBarEdge.Top, UpperBar, UpperWork));
    }

    [Fact]
    public void Shrink_KeepsTheTaskbarsOwnStrip()
    {
        // Native taskbar mode: the fresh work area already ends above the taskbar, and a bottom dock stacks on it.
        var dock = new PixelRect(0, 1440, 2560, 1540);

        Assert.Equal(new PixelRect(0, 0, 2560, 1440), WorkAreaFallback.Shrink(AppBarEdge.Bottom, dock, PrimaryWork));
    }

    [Fact]
    public void Shrink_SecondBarOnTheSameEdge_StacksOnTheFirst()
    {
        // Another top bar already took 0..40, so ours (40..80) only moves the work area's top down to 80.
        var lower = new PixelRect(0, 40, 2560, 80);

        Assert.Equal(new PixelRect(0, 80, 2560, 1540), WorkAreaFallback.Shrink(AppBarEdge.Top, lower, PrimaryWork));
    }

    [Fact]
    public void Shrink_StripReachingPastTheMonitor_LeavesTheOtherAxisAlone()
    {
        var tooWide = new PixelRect(-500, 0, 3000, 40);

        Assert.Equal(new PixelRect(0, 40, 2560, 1540), WorkAreaFallback.Shrink(AppBarEdge.Top, tooWide, PrimaryWork));
    }

    [Fact]
    public void Shrink_StripBelowTheWorkAreasTopEdge_StillReservesIt()
    {
        // Our bar is stacked under another one, so its strip starts below the work area's top edge.
        var lower = new PixelRect(0, 100, 2560, 140);

        Assert.Equal(new PixelRect(0, 140, 2560, 1540), WorkAreaFallback.Shrink(AppBarEdge.Top, lower, new PixelRect(0, 40, 2560, 1540)));
    }

    [Fact]
    public void Shrink_NeverGrowsAWorkArea()
    {
        // A strip the work area already ends below changes nothing, however far up the strip reaches.
        var high = new PixelRect(0, -200, 2560, 20);

        Assert.Null(WorkAreaFallback.Shrink(AppBarEdge.Top, high, new PixelRect(0, 40, 2560, 1540)));
    }
}
