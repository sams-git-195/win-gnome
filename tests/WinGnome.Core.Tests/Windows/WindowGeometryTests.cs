using WinGnome.Core.Geometry;
using WinGnome.Core.Windows;

namespace WinGnome.Core.Tests.Windows;

public class WindowGeometryTests
{
    private static readonly PixelRect WorkArea = new(0, 32, 1920, 1080);

    [Fact]
    public void CenteredOrigin_WithoutInvisibleBorders_CentresTheWindow()
    {
        var window = PixelRect.FromSize(10, 40, 800, 600);

        var (x, y) = WindowGeometry.CenteredOrigin(window, window, WorkArea);

        Assert.Equal(560, x);
        Assert.Equal(256, y);
    }

    [Fact]
    public void CenteredOrigin_CentresTheVisibleFrame_AndKeepsTheBorderOffset()
    {
        // Windows 10/11: 7 px invisible borders left, right and bottom; none at the top.
        var visible = PixelRect.FromSize(100, 100, 800, 600);
        var window = new PixelRect(visible.Left - 7, visible.Top, visible.Right + 7, visible.Bottom + 7);

        var (x, y) = WindowGeometry.CenteredOrigin(window, visible, WorkArea);

        Assert.Equal(560 - 7, x);
        Assert.Equal(256, y);
    }

    [Fact]
    public void CenteredOrigin_EmptyVisibleBounds_FallsBackToTheWindowRect()
    {
        var window = PixelRect.FromSize(0, 0, 800, 600);

        var (x, y) = WindowGeometry.CenteredOrigin(window, default, WorkArea);

        Assert.Equal(560, x);
        Assert.Equal(256, y);
    }

    [Fact]
    public void CenteredOrigin_WindowLargerThanWorkArea_PinsToTopLeft()
    {
        var window = PixelRect.FromSize(300, 300, 2500, 1200);

        var (x, y) = WindowGeometry.CenteredOrigin(window, window, WorkArea);

        Assert.Equal(0, x);
        Assert.Equal(32, y);
    }

    [Fact]
    public void IsFullScreen_ExactlyCoversMonitor()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);

        Assert.True(WindowGeometry.IsFullScreen(monitor, monitor));
        Assert.True(WindowGeometry.IsFullScreen(new PixelRect(-8, -8, 1928, 1088), monitor));
    }

    [Fact]
    public void IsFullScreen_FalseForMaximisedOrSmallerWindows()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);

        Assert.False(WindowGeometry.IsFullScreen(new PixelRect(0, 32, 1920, 1080), monitor));
        Assert.False(WindowGeometry.IsFullScreen(PixelRect.FromSize(100, 100, 800, 600), monitor));
    }

    [Fact]
    public void IsFullScreen_FalseForEmptyMonitor()
    {
        Assert.False(WindowGeometry.IsFullScreen(new PixelRect(0, 0, 10, 10), default));
    }
}
