using WinGnome.Core.Dock;
using WinGnome.Core.Geometry;
using WinGnome.Core.Windows;

namespace WinGnome.Core.Tests.Dock;

public class DockPreviewContentTests
{
    private static DockApp App(params nint[] windows) =>
        new("path:c:\\notepad.exe", "Notepad", null, @"c:\notepad.exe", null, IsPinned: false, windows, IsFocused: false);

    private static WindowInfo Window(nint handle, string title = "Doc", int pid = 40, bool minimized = false, bool elevated = false) =>
        new(handle, title, "Notepad", pid, @"c:\notepad.exe", null, IsVisible: true, IsCloaked: false,
            IsMinimized: minimized, IsMaximized: false, IsToolWindow: false, IsAppWindow: false, IsNoActivate: false,
            HasOwner: false, HasCaption: true, HasSystemMenu: true, IsElevated: elevated, new PixelRect(0, 0, 800, 600));

    private static DockPreviewItem Item(nint handle, string title = "Doc", bool minimized = false) =>
        new(new CloseTarget(handle, 40, false), title, minimized);

    [Theory]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, false)]
    [InlineData(true, 2, true)]
    [InlineData(true, 3, true)]
    [InlineData(false, 2, false)]
    [InlineData(false, 3, false)]
    public void ShowsPreview_NeedsTheSettingAndTwoWindows(bool enabled, int windowCount, bool expected)
    {
        nint[] windows = windowCount switch
        {
            0 => [],
            1 => [1],
            2 => [1, 2],
            _ => [1, 2, 3],
        };

        Assert.Equal(expected, DockPreviewContent.ShowsPreview(enabled, App(windows)));
    }

    [Fact]
    public void Build_KeepsTheAppsWindowOrderNotTheTrackers()
    {
        var items = DockPreviewContent.Build(App(2, 1), [Window(1, "One"), Window(2, "Two")]);

        Assert.Equal([Item(2, "Two"), Item(1, "One")], items);
    }

    [Fact]
    public void Build_WindowTheTrackerNoLongerLists_IsLeftOut()
    {
        var items = DockPreviewContent.Build(App(1, 2, 3), [Window(1), Window(3)]);

        Assert.Equal([Item(1), Item(3)], items);
    }

    [Fact]
    public void Build_NoWindows_IsEmpty()
    {
        Assert.Empty(DockPreviewContent.Build(App(), [Window(1)]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_UntitledWindow_UsesTheAppName(string title)
    {
        var items = DockPreviewContent.Build(App(1), [Window(1, title)]);

        Assert.Equal([Item(1, "Notepad")], items);
    }

    [Fact]
    public void Build_TitleIsTrimmed()
    {
        var items = DockPreviewContent.Build(App(1), [Window(1, "  notes.txt - Notepad ")]);

        Assert.Equal("notes.txt - Notepad", Assert.Single(items).Title);
    }

    [Fact]
    public void Build_CapturesTheTrackersCloseTarget()
    {
        var item = Assert.Single(DockPreviewContent.Build(App(7), [Window(7, pid: 1234)]));

        Assert.Equal(new CloseTarget(7, 1234, false), item.Target);
        Assert.True(item.CanClose);
    }

    [Fact]
    public void Build_ElevatedWindow_CannotBeClosed()
    {
        var item = Assert.Single(DockPreviewContent.Build(App(7), [Window(7, pid: 1234, elevated: true)]));

        Assert.Equal(new CloseTarget(7, 1234, true), item.Target);
        Assert.False(item.CanClose);
    }

    [Fact]
    public void Build_MinimisedWindow_IsMarked()
    {
        var items = DockPreviewContent.Build(App(1, 2), [Window(1, minimized: true), Window(2)]);

        Assert.Equal([Item(1, minimized: true), Item(2)], items);
    }

    [Fact]
    public void Build_HandleListedTwice_AppearsOnce()
    {
        var items = DockPreviewContent.Build(App(1, 1, 2), [Window(1), Window(2), Window(1, "Duplicate")]);

        Assert.Equal([Item(1), Item(2)], items);
    }

    [Fact]
    public void NeedsRebuild_OnlyTitlesChanged_IsFalse()
    {
        Assert.False(DockPreviewContent.NeedsRebuild([Item(1, "a"), Item(2, "b")], [Item(1, "c"), Item(2, "d")]));
    }

    [Fact]
    public void NeedsRebuild_WindowClosed_IsTrue()
    {
        Assert.True(DockPreviewContent.NeedsRebuild([Item(1), Item(2)], [Item(1)]));
    }

    [Fact]
    public void NeedsRebuild_WindowAdded_IsTrue()
    {
        Assert.True(DockPreviewContent.NeedsRebuild([Item(1)], [Item(1), Item(2)]));
    }

    [Fact]
    public void NeedsRebuild_SameCountDifferentWindow_IsTrue()
    {
        Assert.True(DockPreviewContent.NeedsRebuild([Item(1), Item(2)], [Item(1), Item(3)]));
    }

    [Fact]
    public void NeedsRebuild_OrderChanged_IsTrue()
    {
        Assert.True(DockPreviewContent.NeedsRebuild([Item(1), Item(2)], [Item(2), Item(1)]));
    }

    // A 60 DIP cell at 125 % is 75 px; half of it is the tolerance.
    private const int HalfCellPx = 37;

    [Theory]
    [InlineData(300)]
    [InlineData(312)] // Magnification releasing: the icon drifts a few pixels.
    [InlineData(263)] // Exactly half a cell either side still counts as the same slot.
    [InlineData(337)]
    public void NeedsRelayout_SameWindowsIconWithinHalfACell_IsFalse(int currentAnchor)
    {
        Assert.False(DockPreviewContent.NeedsRelayout([Item(1, "a"), Item(2)], [Item(1, "b"), Item(2)], 300, currentAnchor, HalfCellPx));
    }

    [Theory]
    [InlineData(262)]
    [InlineData(338)]
    [InlineData(375)] // Another app left and one arrived: same cell count, but the icon is a whole cell over.
    [InlineData(-300)]
    public void NeedsRelayout_IconMovedMoreThanHalfACell_IsTrue(int currentAnchor)
    {
        Assert.True(DockPreviewContent.NeedsRelayout([Item(1), Item(2)], [Item(1), Item(2)], 300, currentAnchor, HalfCellPx));
    }

    [Fact]
    public void NeedsRelayout_WindowClosedIconStill_IsTrue()
    {
        Assert.True(DockPreviewContent.NeedsRelayout([Item(1), Item(2), Item(3)], [Item(1), Item(3)], 300, 300, HalfCellPx));
    }

    [Fact]
    public void NeedsRelayout_NegativeTolerance_IsTreatedAsZero()
    {
        Assert.True(DockPreviewContent.NeedsRelayout([Item(1)], [Item(1)], 300, 301, -5));
        Assert.False(DockPreviewContent.NeedsRelayout([Item(1)], [Item(1)], 300, 300, -5));
    }

    [Fact]
    public void NeedsRebuild_WindowMinimised_IsTrue()
    {
        Assert.True(DockPreviewContent.NeedsRebuild([Item(1), Item(2)], [Item(1), Item(2, minimized: true)]));
    }
}
