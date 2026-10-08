using WinGnome.Core.Dock;
using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;

namespace WinGnome.Core.Tests.Dock;

public class DockVisibilityPolicyTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);
    private static readonly PixelRect Dock = new(800, 1000, 1120, 1072);

    private static DockVisibilityInputs Inputs(bool pointer = false, bool interacting = false, bool fullScreen = false, bool obstructed = false) =>
        new(pointer, interacting, fullScreen, obstructed);

    [Fact]
    public void AlwaysVisible_ShowsEvenWhenObstructed()
    {
        Assert.True(DockVisibilityPolicy.ShouldShow(DockVisibility.AlwaysVisible, Inputs(obstructed: true)));
    }

    [Theory]
    [InlineData(DockVisibility.AlwaysVisible)]
    [InlineData(DockVisibility.Intellihide)]
    [InlineData(DockVisibility.Autohide)]
    public void FullScreen_HidesInEveryMode_EvenWithThePointerAtTheEdge(DockVisibility mode)
    {
        Assert.False(DockVisibilityPolicy.ShouldShow(mode, Inputs(pointer: true, fullScreen: true)));
    }

    [Theory]
    [InlineData(DockVisibility.AlwaysVisible)]
    [InlineData(DockVisibility.Intellihide)]
    [InlineData(DockVisibility.Autohide)]
    public void Interacting_AlwaysShows(DockVisibility mode)
    {
        Assert.True(DockVisibilityPolicy.ShouldShow(mode, Inputs(interacting: true, fullScreen: true, obstructed: true)));
    }

    [Fact]
    public void Intellihide_ShowsWhenNothingOverlaps()
    {
        Assert.True(DockVisibilityPolicy.ShouldShow(DockVisibility.Intellihide, Inputs()));
    }

    [Fact]
    public void Intellihide_HidesWhenObstructed()
    {
        Assert.False(DockVisibilityPolicy.ShouldShow(DockVisibility.Intellihide, Inputs(obstructed: true)));
    }

    [Fact]
    public void Intellihide_PointerRevealsAnObstructedDock()
    {
        Assert.True(DockVisibilityPolicy.ShouldShow(DockVisibility.Intellihide, Inputs(pointer: true, obstructed: true)));
    }

    [Fact]
    public void Autohide_HiddenUntilThePointerArrives()
    {
        Assert.False(DockVisibilityPolicy.ShouldShow(DockVisibility.Autohide, Inputs()));
        Assert.True(DockVisibilityPolicy.ShouldShow(DockVisibility.Autohide, Inputs(pointer: true)));
    }

    [Theory]
    [InlineData(DockVisibility.AlwaysVisible, false, false, false, false)]
    [InlineData(DockVisibility.Autohide, false, false, false, true)]
    [InlineData(DockVisibility.Autohide, true, false, false, false)]
    [InlineData(DockVisibility.Autohide, true, true, false, true)]
    [InlineData(DockVisibility.Intellihide, false, false, true, false)]
    [InlineData(DockVisibility.Intellihide, false, false, false, true)]
    public void NeedsPointerPolling(DockVisibility mode, bool shown, bool engaged, bool fullScreen, bool expected)
    {
        Assert.Equal(expected, DockVisibilityPolicy.NeedsPointerPolling(mode, shown, engaged, fullScreen));
    }

    [Fact]
    public void IsFullScreen_ExactMonitorBounds()
    {
        Assert.True(DockVisibilityPolicy.IsFullScreen(Monitor, Monitor, isMaximized: false));
    }

    [Fact]
    public void IsFullScreen_LargerThanTheMonitor()
    {
        Assert.True(DockVisibilityPolicy.IsFullScreen(Monitor.Inflate(8, 8), Monitor, isMaximized: false));
    }

    [Fact]
    public void IsFullScreen_MaximisedWindowsAreNotFullScreen()
    {
        Assert.False(DockVisibilityPolicy.IsFullScreen(Monitor.Inflate(8, 8), Monitor, isMaximized: true));
    }

    [Fact]
    public void IsFullScreen_SmallerWindowIsNot()
    {
        Assert.False(DockVisibilityPolicy.IsFullScreen(Monitor with { Bottom = 1040 }, Monitor, isMaximized: false));
    }

    [Fact]
    public void IsFullScreen_EmptyMonitorIsNever()
    {
        Assert.False(DockVisibilityPolicy.IsFullScreen(Monitor, default, isMaximized: false));
    }

    [Fact]
    public void IsObstructed_FocusedWindowOverlappingTheDock()
    {
        Assert.True(DockVisibilityPolicy.IsObstructed(Dock, PixelRect.FromSize(700, 600, 400, 401), anyMaximizedOnMonitor: false));
    }

    [Fact]
    public void IsObstructed_FocusedWindowAboveTheDock()
    {
        Assert.False(DockVisibilityPolicy.IsObstructed(Dock, PixelRect.FromSize(700, 600, 400, 400), anyMaximizedOnMonitor: false));
    }

    [Fact]
    public void IsObstructed_NoFocusedWindow()
    {
        Assert.False(DockVisibilityPolicy.IsObstructed(Dock, null, anyMaximizedOnMonitor: false));
    }

    [Fact]
    public void IsObstructed_MaximisedWindowOnTheMonitor()
    {
        Assert.True(DockVisibilityPolicy.IsObstructed(Dock, null, anyMaximizedOnMonitor: true));
    }
}
