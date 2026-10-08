using WinGnome.Core.Geometry;
using WinGnome.Core.Tests.TestData;
using WinGnome.Core.Windows;

namespace WinGnome.Core.Tests.Windows;

public class WindowFilterTests
{
    private static readonly string[] NoExclusions = [];

    [Fact]
    public void NormalWindow_IsTaskSwitcherWindow()
    {
        Assert.True(WindowFilter.IsTaskSwitcherWindow(TestWindows.Normal()));
    }

    [Fact]
    public void Invisible_IsExcluded()
    {
        Assert.False(WindowFilter.IsTaskSwitcherWindow(TestWindows.Normal() with { IsVisible = false }));
    }

    [Fact]
    public void Cloaked_IsExcluded()
    {
        Assert.False(WindowFilter.IsTaskSwitcherWindow(TestWindows.Normal() with { IsCloaked = true }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyTitle_IsExcluded(string title)
    {
        Assert.False(WindowFilter.IsTaskSwitcherWindow(TestWindows.Normal() with { Title = title }));
    }

    [Fact]
    public void ToolWindow_IsExcluded_UnlessItAsksToBeAnAppWindow()
    {
        var tool = TestWindows.Normal() with { IsToolWindow = true };

        Assert.False(WindowFilter.IsTaskSwitcherWindow(tool));
        Assert.True(WindowFilter.IsTaskSwitcherWindow(tool with { IsAppWindow = true }));
    }

    [Fact]
    public void OwnedWindow_IsExcluded_UnlessItAsksToBeAnAppWindow()
    {
        var owned = TestWindows.Normal() with { HasOwner = true };

        Assert.False(WindowFilter.IsTaskSwitcherWindow(owned));
        Assert.True(WindowFilter.IsTaskSwitcherWindow(owned with { IsAppWindow = true }));
    }

    [Fact]
    public void NoActivateWindow_IsExcluded_UnlessItAsksToBeAnAppWindow()
    {
        var noActivate = TestWindows.Normal() with { IsNoActivate = true };

        Assert.False(WindowFilter.IsTaskSwitcherWindow(noActivate));
        Assert.True(WindowFilter.IsTaskSwitcherWindow(noActivate with { IsAppWindow = true }));
    }

    [Fact]
    public void AppWindow_StillNeedsVisibilityAndTitle()
    {
        var appWindow = TestWindows.Normal() with { IsAppWindow = true };

        Assert.False(WindowFilter.IsTaskSwitcherWindow(appWindow with { IsVisible = false }));
        Assert.False(WindowFilter.IsTaskSwitcherWindow(appWindow with { IsCloaked = true }));
        Assert.False(WindowFilter.IsTaskSwitcherWindow(appWindow with { Title = "" }));
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("Windows.UI.Core.CoreWindow")]
    [InlineData("XamlExplorerHostIslandWindow")]
    public void ShellClasses_AreAlwaysExcluded(string className)
    {
        var window = TestWindows.Normal() with { ClassName = className };

        Assert.False(WindowFilter.IsTaskSwitcherWindow(window));
        Assert.False(WindowFilter.IsTaskSwitcherWindow(window with { IsAppWindow = true }));
    }

    [Fact]
    public void NullClassName_IsFine()
    {
        Assert.True(WindowFilter.IsTaskSwitcherWindow(TestWindows.Normal() with { ClassName = null }));
    }

    [Fact]
    public void SimilarClassName_IsNotExcluded()
    {
        Assert.True(WindowFilter.IsTaskSwitcherWindow(TestWindows.Normal() with { ClassName = "MyProgman" }));
    }

    [Fact]
    public void CanDecorate_NormalWindow()
    {
        Assert.True(WindowFilter.CanDecorate(TestWindows.Normal(), NoExclusions));
    }

    [Fact]
    public void CanDecorate_MaximisedWindowIsAllowed()
    {
        Assert.True(WindowFilter.CanDecorate(TestWindows.Normal() with { IsMaximized = true }, NoExclusions));
    }

    [Fact]
    public void CanDecorate_RejectsNonTaskSwitcherWindows()
    {
        Assert.False(WindowFilter.CanDecorate(TestWindows.Normal() with { IsVisible = false }, NoExclusions));
        Assert.False(WindowFilter.CanDecorate(TestWindows.Normal() with { ClassName = "Progman" }, NoExclusions));
    }

    [Theory]
    [InlineData(nameof(WindowInfo.HasCaption))]
    [InlineData(nameof(WindowInfo.HasSystemMenu))]
    public void CanDecorate_RequiresCaptionAndSystemMenu(string missing)
    {
        var window = missing == nameof(WindowInfo.HasCaption)
            ? TestWindows.Normal() with { HasCaption = false }
            : TestWindows.Normal() with { HasSystemMenu = false };

        Assert.False(WindowFilter.CanDecorate(window, NoExclusions));
    }

    [Fact]
    public void CanDecorate_DrawsOwnButtons_DoesNotNeedASystemMenu()
    {
        // Claude desktop (Electron) has WS_CAPTION but no WS_SYSMENU.
        Assert.True(WindowFilter.CanDecorate(TestWindows.Normal() with { HasSystemMenu = false }, NoExclusions, drawsOwnButtons: true));
    }

    [Fact]
    public void CanDecorate_DrawsOwnButtons_StillNeedsACaptionAndAnUnelevatedWindow()
    {
        Assert.False(WindowFilter.CanDecorate(TestWindows.Normal() with { HasCaption = false }, NoExclusions, drawsOwnButtons: true));
        Assert.False(WindowFilter.CanDecorate(TestWindows.Normal() with { IsElevated = true }, NoExclusions, drawsOwnButtons: true));
    }

    [Fact]
    public void CanDecorate_RejectsMinimised()
    {
        Assert.False(WindowFilter.CanDecorate(TestWindows.Normal() with { IsMinimized = true }, NoExclusions));
    }

    [Fact]
    public void CanDecorate_RejectsElevated()
    {
        Assert.False(WindowFilter.CanDecorate(TestWindows.Normal() with { IsElevated = true }, NoExclusions));
    }

    [Theory]
    [InlineData(120, 60, true)]
    [InlineData(119, 60, false)]
    [InlineData(120, 59, false)]
    [InlineData(0, 0, false)]
    [InlineData(1920, 1080, true)]
    public void CanDecorate_EnforcesMinimumSize(int width, int height, bool expected)
    {
        var window = TestWindows.Normal() with { Bounds = PixelRect.FromSize(0, 0, width, height) };
        Assert.Equal(expected, WindowFilter.CanDecorate(window, NoExclusions));
    }

    [Theory]
    [InlineData("notepad")]
    [InlineData("NOTEPAD")]
    [InlineData("Notepad.exe")]
    [InlineData("  notepad  ")]
    public void CanDecorate_HonoursExcludedProcessNames(string excluded)
    {
        Assert.False(WindowFilter.CanDecorate(TestWindows.Normal(), [excluded]));
    }

    [Fact]
    public void CanDecorate_IgnoresUnrelatedAndBlankExclusions()
    {
        Assert.True(WindowFilter.CanDecorate(TestWindows.Normal(), ["code", "", "  ", "notepad2"]));
    }

    [Fact]
    public void CanDecorate_ExclusionMatchesWholeNameOnly()
    {
        Assert.True(WindowFilter.CanDecorate(TestWindows.Normal(), ["note", "pad"]));
    }

    [Fact]
    public void CanDecorate_UnknownProcessPath_IsNotExcluded()
    {
        var window = TestWindows.Normal() with { ProcessPath = null };
        Assert.True(WindowFilter.CanDecorate(window, ["notepad"]));
    }

    [Fact]
    public void CanDecorate_ExclusionUsesFileNameNotFolder()
    {
        var window = TestWindows.Normal() with { ProcessPath = @"C:\notepad\other.exe" };
        Assert.True(WindowFilter.CanDecorate(window, ["notepad"]));
    }
}
