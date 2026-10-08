using WinGnome.Core.Dock;
using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;

namespace WinGnome.Core.Tests.Dock;

public class DockFrameLayoutTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    private static DockFrame Frame(DockPosition position, double magnification, bool extend = false)
    {
        var geometry = DockLayout.Compute(Monitor, position, 5, 48, 1, extend);
        return DockFrameLayout.Compute(Monitor, position, geometry, cellLengthPx: 60, iconSizePx: 48, magnification, extend);
    }

    [Fact]
    public void NoMagnification_WindowIsTheBodyPlusTheGap()
    {
        var frame = Frame(DockPosition.Bottom, 1);

        // Body (802, 1004, 1118, 1072); the gap below it runs to the monitor's bottom edge.
        Assert.Equal(new PixelRect(802, 1004, 1118, 1080), frame.Window);
        Assert.Equal(76, frame.ReservedThickness);
    }

    [Fact]
    public void Magnification_AddsHeadroomAwayFromTheEdgeAndAlongTheDock()
    {
        var frame = Frame(DockPosition.Bottom, 2);

        // Across: 60 * 1 = 60. Along: ceil(1 * (2.5 * 48 + 60) / 2) = 90 per side.
        Assert.Equal(new PixelRect(802 - 90, 1004 - 60, 1118 + 90, 1080), frame.Window);
        Assert.Equal(76, frame.ReservedThickness);
    }

    [Fact]
    public void Magnification_LeftDock_GrowsRightwards()
    {
        var frame = Frame(DockPosition.Left, 1.5);

        // Body (8, 382, 76, 698): across ceil(60 * 0.5) = 30, along ceil(0.5 * 180 / 2) = 45.
        Assert.Equal(new PixelRect(0, 382 - 45, 76 + 30, 698 + 45), frame.Window);
        Assert.Equal(76, frame.ReservedThickness);
    }

    [Fact]
    public void Magnification_RightDock_GrowsLeftwards()
    {
        var frame = Frame(DockPosition.Right, 1.5);

        Assert.Equal(new PixelRect(1844 - 30, 382 - 45, 1920, 698 + 45), frame.Window);
        Assert.Equal(76, frame.ReservedThickness);
    }

    [Fact]
    public void PanelMode_WindowSpansTheEdge_AndHasNoGap()
    {
        var frame = Frame(DockPosition.Bottom, 2, extend: true);

        Assert.Equal(new PixelRect(0, 1012 - 60, 1920, 1080), frame.Window);
        Assert.Equal(68, frame.ReservedThickness);
    }

    [Theory]
    [InlineData(DockPosition.Bottom)]
    [InlineData(DockPosition.Left)]
    [InlineData(DockPosition.Right)]
    public void Window_ContainsTheBody_AndStaysOnTheMonitor(DockPosition position)
    {
        var geometry = DockLayout.Compute(Monitor, position, 40, 64, 1, false);
        var frame = DockFrameLayout.Compute(Monitor, position, geometry, 76, 64, 2, false);

        Assert.Equal(frame.Window, Monitor.Intersect(frame.Window));
        Assert.Equal(geometry.Bounds, frame.Window.Intersect(geometry.Bounds));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void InvalidMagnification_MeansNoHeadroom(double magnification)
    {
        Assert.Equal(Frame(DockPosition.Bottom, 1).Window, Frame(DockPosition.Bottom, magnification).Window);
    }

    [Fact]
    public void FitIconSize_KeepsTheRequestedSizeWhenItFits()
    {
        Assert.Equal(48, DockFrameLayout.FitIconSize(1920, 10, 12, 48, 6, 8));
    }

    [Fact]
    public void FitIconSize_ShrinksWhenTooManyItems()
    {
        // available = 1000 - 32 - 8 = 960; 960 / 16 = 60 per cell; minus 2 * 6 padding = 48.
        Assert.Equal(48, DockFrameLayout.FitIconSize(1000, 16, 8, 64, 6, 8), 9);
    }

    [Fact]
    public void FitIconSize_NeverGoesBelowTheMinimum()
    {
        Assert.Equal(DockFrameLayout.MinIconSizeDip, DockFrameLayout.FitIconSize(400, 100, 0, 48, 6, 8));
    }

    [Fact]
    public void FitIconSize_NeverGrowsASmallRequestedSize()
    {
        Assert.Equal(12, DockFrameLayout.FitIconSize(400, 100, 0, 12, 6, 8));
    }

    [Fact]
    public void FitIconSize_NoCells_ReturnsTheRequestedSize()
    {
        Assert.Equal(48, DockFrameLayout.FitIconSize(100, 0, 0, 48, 6, 8));
    }
}
