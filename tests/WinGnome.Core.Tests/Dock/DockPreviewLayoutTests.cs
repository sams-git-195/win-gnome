using WinGnome.Core.Dock;
using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;

namespace WinGnome.Core.Tests.Dock;

public class DockPreviewLayoutTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    private static DockPreviewSourceSize?[] Windows(int count, int width = 1600, int height = 900) =>
        Enumerable.Repeat<DockPreviewSourceSize?>(new DockPreviewSourceSize(width, height), count).ToArray();

    private static DockPreviewPlan Bottom(int anchor, IReadOnlyList<DockPreviewSourceSize?> sources) =>
        DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(760, 980, 1160, 1072), anchor, Monitor, Monitor, 1, sources);

    private static DockPreviewPlan Left(int anchor, IReadOnlyList<DockPreviewSourceSize?> sources, PixelRect? workArea = null) =>
        DockPreviewLayout.Compute(DockPosition.Left, new PixelRect(0, 400, 100, 700), anchor, Monitor, workArea ?? Monitor, 1, sources);

    [Fact]
    public void Compute_BottomDockTwoWindows_MatchesTheSpecExample()
    {
        var plan = Bottom(960, Windows(2));

        Assert.Equal(new PixelRect(710, 772, 1210, 972), plan.Popover);
        Assert.Equal(0, plan.OverflowCount);
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(
            new DockPreviewItemLayout(
                Item: new PixelRect(8, 8, 248, 192),
                Caption: new PixelRect(16, 16, 240, 44),
                Close: new PixelRect(216, 18, 240, 42),
                Box: new PixelRect(16, 44, 240, 184),
                Thumbnail: new PixelRect(16, 51, 240, 177),
                IsPlaceholder: false),
            plan.Items[0]);
        Assert.Equal(
            new DockPreviewItemLayout(
                Item: new PixelRect(252, 8, 492, 192),
                Caption: new PixelRect(260, 16, 484, 44),
                Close: new PixelRect(460, 18, 484, 42),
                Box: new PixelRect(260, 44, 484, 184),
                Thumbnail: new PixelRect(260, 51, 484, 177),
                IsPlaceholder: false),
            plan.Items[1]);
    }

    [Fact]
    public void Compute_BottomDock150Percent_ScalesEveryEdge()
    {
        var monitor = new PixelRect(0, 0, 2880, 1620);

        var plan = DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(1140, 1470, 1740, 1608), 1440, monitor, monitor, 1.5,
            Windows(2));

        Assert.Equal(new PixelRect(1065, 1158, 1815, 1458), plan.Popover);
        Assert.Equal(
            new DockPreviewItemLayout(
                Item: new PixelRect(12, 12, 372, 288),
                Caption: new PixelRect(24, 24, 360, 66),
                Close: new PixelRect(324, 27, 360, 63),
                Box: new PixelRect(24, 66, 360, 276),
                Thumbnail: new PixelRect(24, 76, 360, 265),
                IsPlaceholder: false),
            plan.Items[0]);
        Assert.Equal(new PixelRect(378, 12, 738, 288), plan.Items[1].Item);
    }

    [Fact]
    public void Compute_BottomDock125Percent_RoundsHalfPixelsAwayFromZero()
    {
        var monitor = new PixelRect(0, 0, 2400, 1350);

        var plan = DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(950, 1225, 1450, 1340), 1200, monitor, monitor, 1.25,
            Windows(2));

        Assert.Equal(new PixelRect(888, 965, 1513, 1215), plan.Popover);
        Assert.Equal(
            new DockPreviewItemLayout(
                Item: new PixelRect(10, 10, 310, 240),
                Caption: new PixelRect(20, 20, 300, 55),
                Close: new PixelRect(270, 23, 300, 53),
                Box: new PixelRect(20, 55, 300, 230),
                Thumbnail: new PixelRect(20, 63, 300, 221),
                IsPlaceholder: false),
            plan.Items[0]);
        Assert.Equal(new PixelRect(575, 23, 605, 53), plan.Items[1].Close);
    }

    [Fact]
    public void Compute_LeftDock_ColumnRightOfTheDockWindow()
    {
        var plan = Left(550, Windows(2));

        Assert.Equal(new PixelRect(108, 356, 364, 744), plan.Popover);
        Assert.Equal(new PixelRect(8, 8, 248, 192), plan.Items[0].Item);
        Assert.Equal(new PixelRect(8, 196, 248, 380), plan.Items[1].Item);
        Assert.Equal(new PixelRect(16, 44, 240, 184), plan.Items[0].Box);
        Assert.Equal(new PixelRect(16, 232, 240, 372), plan.Items[1].Box);
    }

    [Fact]
    public void Compute_RightDock_ColumnLeftOfTheDockWindow()
    {
        var plan = DockPreviewLayout.Compute(DockPosition.Right, new PixelRect(1820, 400, 1920, 700), 550, Monitor, Monitor, 1,
            Windows(2));

        Assert.Equal(new PixelRect(1556, 356, 1812, 744), plan.Popover);
        Assert.Equal(new PixelRect(8, 196, 248, 380), plan.Items[1].Item);
    }

    [Fact]
    public void Compute_BottomDockNearTheLeftEdge_IsClampedWithTheMargin()
    {
        var plan = Bottom(100, Windows(2));

        Assert.Equal(new PixelRect(8, 772, 508, 972), plan.Popover);
    }

    [Fact]
    public void Compute_BottomDockNearTheRightEdge_IsClampedWithTheMargin()
    {
        var plan = Bottom(1900, Windows(2));

        Assert.Equal(new PixelRect(1412, 772, 1912, 972), plan.Popover);
    }

    [Theory]
    [InlineData(259, 9)]
    [InlineData(258, 8)]
    [InlineData(257, 8)]
    public void Compute_BottomDockAtTheMarginBoundary_MovesOnlyWhenItWouldCrossIt(int anchor, int expectedLeft)
    {
        var plan = Bottom(anchor, Windows(2));

        Assert.Equal(expectedLeft, plan.Popover.Left);
        Assert.Equal(expectedLeft + 500, plan.Popover.Right);
    }

    [Fact]
    public void Compute_BottomDockOnAMonitorWithNegativeCoordinates_StaysOnThatMonitor()
    {
        var monitor = new PixelRect(-1920, 0, 0, 1080);
        var dock = new PixelRect(-1160, 980, -760, 1072);

        Assert.Equal(new PixelRect(-1210, 772, -710, 972),
            DockPreviewLayout.Compute(DockPosition.Bottom, dock, -960, monitor, monitor, 1, Windows(2)).Popover);
        Assert.Equal(new PixelRect(-1912, 772, -1412, 972),
            DockPreviewLayout.Compute(DockPosition.Bottom, dock, -1900, monitor, monitor, 1, Windows(2)).Popover);
        Assert.Equal(new PixelRect(-508, 772, -8, 972),
            DockPreviewLayout.Compute(DockPosition.Bottom, dock, -10, monitor, monitor, 1, Windows(2)).Popover);
    }

    [Fact]
    public void Compute_RightDockOnAMonitorAboveAndLeftOfThePrimary_UsesItsOwnCoordinates()
    {
        var monitor = new PixelRect(-1920, -300, 0, 780);

        var plan = DockPreviewLayout.Compute(DockPosition.Right, new PixelRect(-100, 100, 0, 400), 250, monitor, monitor, 1, Windows(2));

        Assert.Equal(new PixelRect(-364, 56, -108, 444), plan.Popover);
    }

    [Fact]
    public void Compute_SideDock_IsClampedToTheWorkAreaNotTheMonitor()
    {
        var workArea = new PixelRect(0, 40, 1920, 1080);

        Assert.Equal(new PixelRect(108, 48, 364, 436), Left(20, Windows(2), workArea).Popover);
        Assert.Equal(new PixelRect(108, 684, 364, 1072), Left(1070, Windows(2), workArea).Popover);
    }

    [Fact]
    public void Compute_EmptyWorkArea_FallsBackToTheMonitorBounds()
    {
        var plan = Left(20, Windows(2), workArea: default(PixelRect));

        Assert.Equal(new PixelRect(108, 8, 364, 396), plan.Popover);
    }

    [Fact]
    public void Compute_TooNarrowForFullSize_ShrinksTheBoxesTogether()
    {
        // Along space 688 - 16 = 672 holds three items of box width 200: 3 * 216 + 2 * 4 + 16.
        var monitor = new PixelRect(0, 0, 688, 1080);

        var plan = DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(200, 980, 488, 1072), 344, monitor, monitor, 1,
            Windows(3));

        Assert.Equal(new PixelRect(8, 787, 680, 972), plan.Popover);
        Assert.Equal(0, plan.OverflowCount);
        Assert.Equal(new PixelRect(16, 44, 216, 169), plan.Items[0].Box);
        Assert.Equal(new PixelRect(236, 44, 436, 169), plan.Items[1].Box);
        Assert.Equal(new PixelRect(456, 44, 656, 169), plan.Items[2].Box);
        Assert.Equal(new PixelRect(448, 8, 664, 177), plan.Items[2].Item);
    }

    [Fact]
    public void Compute_LittleSpaceAcross_ShrinksTheBoxesToFit()
    {
        // Between the dock window (top 176, 8 gap) and the work area's top margin there are 160 DIP: box width 160.
        var plan = DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(760, 176, 1160, 1072), 960, Monitor, Monitor, 1,
            Windows(2));

        Assert.Equal(new PixelRect(774, 8, 1146, 168), plan.Popover);
        Assert.Equal(new PixelRect(16, 44, 176, 144), plan.Items[0].Box);
        Assert.Equal(new PixelRect(188, 8, 364, 152), plan.Items[1].Item);
    }

    [Fact]
    public void Compute_SpaceAcrossIsMeasuredFromTheWorkArea()
    {
        var workArea = new PixelRect(0, 100, 1920, 1080);

        var plan = DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(760, 276, 1160, 1072), 960, Monitor, workArea, 1,
            Windows(2));

        Assert.Equal(160, plan.Popover.Height);
        Assert.Equal(108, plan.Popover.Top);
    }

    [Fact]
    public void Compute_NoSpaceAcross_NeverShrinksBelowTheMinimumBox()
    {
        var plan = DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(760, 100, 1160, 1072), 960, Monitor, Monitor, 1,
            Windows(2));

        Assert.Equal(new PixelRect(16, 44, 176, 144), plan.Items[0].Box);
        Assert.Equal(160, plan.Popover.Height);
        Assert.Equal(92, plan.Popover.Bottom);
    }

    [Fact]
    public void Compute_ExactlyFitsOneRowAtTheMinimumBox_KeepsOneRow()
    {
        // 3 * 176 + 2 * 4 + 16 = 552 = 568 - 16.
        var monitor = new PixelRect(0, 0, 568, 1080);

        var plan = DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(100, 980, 468, 1072), 284, monitor, monitor, 1,
            Windows(3));

        Assert.Equal(new PixelRect(8, 812, 560, 972), plan.Popover);
        Assert.Equal(3, plan.Items.Count);
        Assert.Equal(0, plan.OverflowCount);
        Assert.Equal(new PixelRect(16, 44, 176, 144), plan.Items[0].Box);
        Assert.Equal(new PixelRect(368, 8, 544, 152), plan.Items[2].Item);
    }

    [Fact]
    public void Compute_OnePixelTooNarrowForOneRow_WrapsIntoTwoRowsAtFullSize()
    {
        var monitor = new PixelRect(0, 0, 567, 1080);

        var plan = DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(100, 980, 467, 1072), 283, monitor, monitor, 1,
            Windows(3));

        Assert.Equal(new PixelRect(33, 584, 533, 972), plan.Popover);
        Assert.Equal(3, plan.Items.Count);
        Assert.Equal(0, plan.OverflowCount);
        Assert.Equal(new PixelRect(8, 8, 248, 192), plan.Items[0].Item);
        Assert.Equal(new PixelRect(252, 8, 492, 192), plan.Items[1].Item);
        Assert.Equal(new PixelRect(8, 196, 248, 380), plan.Items[2].Item);
    }

    [Fact]
    public void Compute_ThirteenWindows_WrapIntoTwoRowsOfFullSizeThumbnails()
    {
        // One row of 13 would need boxes of 125.5 DIP; two rows of seven fit at the full 224.
        var plan = Bottom(960, Windows(13));

        Assert.Equal(new PixelRect(100, 584, 1820, 972), plan.Popover);
        Assert.Equal(13, plan.Items.Count);
        Assert.Equal(0, plan.OverflowCount);
        Assert.Equal(new PixelRect(8, 8, 248, 192), plan.Items[0].Item);
        Assert.Equal(new PixelRect(1472, 8, 1712, 192), plan.Items[6].Item);
        Assert.Equal(new PixelRect(8, 196, 248, 380), plan.Items[7].Item);
        Assert.Equal(
            new DockPreviewItemLayout(
                Item: new PixelRect(1228, 196, 1468, 380),
                Caption: new PixelRect(1236, 204, 1460, 232),
                Close: new PixelRect(1436, 206, 1460, 230),
                Box: new PixelRect(1236, 232, 1460, 372),
                Thumbnail: new PixelRect(1236, 239, 1460, 365),
                IsPlaceholder: false),
            plan.Items[12]);
    }

    [Fact]
    public void Compute_ThirteenWindowsAt150Percent_WrapIntoTwoRowsThatFitThePopover()
    {
        var monitor = new PixelRect(0, 0, 2880, 1620);

        var plan = DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(1140, 1470, 1740, 1608), 1440, monitor, monitor, 1.5,
            Windows(13));

        Assert.Equal(new PixelRect(150, 876, 2730, 1458), plan.Popover);
        Assert.Equal(13, plan.Items.Count);
        Assert.Equal(new PixelRect(1842, 294, 2202, 570), plan.Items[12].Item);
        Assert.True(plan.Items[12].Item.Right <= plan.Popover.Width && plan.Items[12].Item.Bottom <= plan.Popover.Height);
    }

    [Fact]
    public void Compute_ThirteenWindowsAt200Percent_WrapIntoTwoRowsThatFitThePopover()
    {
        var monitor = new PixelRect(0, 0, 3840, 2160);

        var plan = DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(1600, 1960, 2240, 2144), 1920, monitor, monitor, 2,
            Windows(13));

        Assert.Equal(new PixelRect(200, 1168, 3640, 1944), plan.Popover);
        Assert.Equal(new PixelRect(2456, 392, 2936, 760), plan.Items[12].Item);
        Assert.True(plan.Items[12].Item.Right <= plan.Popover.Width && plan.Items[12].Item.Bottom <= plan.Popover.Height);
    }

    [Fact]
    public void Compute_RightDockOnANegativeMonitor_WrapsIntoTwoColumnsLeftOfTheDock()
    {
        // Seven windows still fit one column at 163 DIP; eight need two columns of four, at the full 224.
        var monitor = new PixelRect(-1920, -300, 0, 780);

        var plan = DockPreviewLayout.Compute(DockPosition.Right, new PixelRect(-100, 100, 0, 400), 250, monitor, monitor, 1, Windows(8));

        Assert.Equal(new PixelRect(-608, -132, -108, 632), plan.Popover);
        Assert.Equal(8, plan.Items.Count);
        Assert.Equal(new PixelRect(8, 8, 248, 192), plan.Items[0].Item);
        Assert.Equal(new PixelRect(252, 572, 492, 756), plan.Items[7].Item);
        Assert.True(plan.Items[7].Item.Right <= plan.Popover.Width && plan.Items[7].Item.Bottom <= plan.Popover.Height);
    }

    [Fact]
    public void Compute_SideDockWithoutRoomForOneColumn_WrapsIntoColumnsAwayFromTheDock()
    {
        // One column of four would need 61 DIP boxes; two columns of two fit at 200.
        var workArea = new PixelRect(0, 0, 1920, 374);

        var plan = DockPreviewLayout.Compute(DockPosition.Left, new PixelRect(0, 100, 100, 300), 190, Monitor, workArea, 1, Windows(4));

        Assert.Equal(new PixelRect(108, 8, 560, 366), plan.Popover);
        Assert.Equal(new PixelRect(8, 8, 224, 177), plan.Items[0].Item);
        Assert.Equal(new PixelRect(8, 181, 224, 350), plan.Items[1].Item);
        Assert.Equal(new PixelRect(228, 8, 444, 177), plan.Items[2].Item);
        Assert.Equal(new PixelRect(228, 181, 444, 350), plan.Items[3].Item);
        Assert.Equal(new PixelRect(236, 44, 436, 169), plan.Items[2].Box);
    }

    [Fact]
    public void Compute_OneWindowTooManyForTheGrid_ReplacesTheLastSlotWithPlusNMore()
    {
        // Room for three minimum items along the dock and one row across: two windows and "+2 more".
        var monitor = new PixelRect(0, 0, 568, 1080);

        var plan = DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(100, 216, 468, 1072), 284, monitor, monitor, 1,
            Windows(4));

        Assert.Equal(new PixelRect(8, 48, 560, 208), plan.Popover);
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(2, plan.OverflowCount);
        Assert.Equal(new PixelRect(188, 8, 364, 152), plan.Items[1].Item);
        Assert.Equal(new PixelRect(368, 8, 544, 152), plan.OverflowItem);
    }

    [Fact]
    public void Compute_ManyMoreWindowsThanFit_CountsTheHiddenOnes()
    {
        var monitor = new PixelRect(0, 0, 568, 1080);

        var plan = DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(100, 216, 468, 1072), 284, monitor, monitor, 1,
            Windows(7));

        Assert.Equal(new PixelRect(8, 48, 560, 208), plan.Popover);
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(5, plan.OverflowCount);
    }

    [Fact]
    public void Compute_SideDockOverflow_StacksPlusNMoreBelowTheLastWindow()
    {
        // Room for two minimum items along the dock (350 DIP) and one column across (268 DIP).
        var workArea = new PixelRect(0, 0, 400, 382);

        var plan = DockPreviewLayout.Compute(DockPosition.Left, new PixelRect(0, 100, 100, 300), 190, Monitor, workArea, 1, Windows(4));

        Assert.Equal(new PixelRect(108, 36, 300, 344), plan.Popover);
        Assert.Equal(new PixelRect(8, 8, 184, 152), Assert.Single(plan.Items).Item);
        Assert.Equal(3, plan.OverflowCount);
        Assert.Equal(new PixelRect(8, 156, 184, 300), plan.OverflowItem);
    }

    [Fact]
    public void Compute_MonitorTooNarrowForOneMinimumItem_StacksTheWindowsAtTheMinimumBox()
    {
        // Not even one 176 DIP item fits along a 200 px monitor: the windows stack, pinned to the left margin.
        var monitor = new PixelRect(0, 0, 200, 1080);

        var plan = DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(20, 980, 180, 1072), 100, monitor, monitor, 1, Windows(2));

        Assert.Equal(new PixelRect(8, 664, 200, 972), plan.Popover);
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(0, plan.OverflowCount);
        Assert.Equal(new PixelRect(8, 156, 184, 300), plan.Items[1].Item);
    }

    [Fact]
    public void Compute_NoOverflow_LeavesTheOverflowItemEmpty()
    {
        var plan = Bottom(960, Windows(2));

        Assert.Equal(default, plan.OverflowItem);
    }

    [Fact]
    public void Compute_UnusableSources_ArePlaceholders()
    {
        DockPreviewSourceSize?[] sources =
        [
            null,
            new DockPreviewSourceSize(31, 500),
            new DockPreviewSourceSize(500, 31),
            new DockPreviewSourceSize(32, 32),
            new DockPreviewSourceSize(1600, 900),
        ];

        var plan = DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(400, 980, 1500, 1072), 960, Monitor, Monitor, 1, sources);

        Assert.Equal(5, plan.Items.Count);
        Assert.Equal([true, true, true, false, false], plan.Items.Select(i => i.IsPlaceholder).ToArray());
        Assert.Equal(default, plan.Items[0].Thumbnail);
        Assert.Equal(default, plan.Items[1].Thumbnail);
        Assert.Equal(default, plan.Items[2].Thumbnail);
        Assert.Equal(new PixelRect(748, 44, 972, 184), plan.Items[3].Box);
        Assert.Equal(new PixelRect(790, 44, 930, 184), plan.Items[3].Thumbnail);
        Assert.Equal(new PixelRect(992, 51, 1216, 177), plan.Items[4].Thumbnail);
    }

    [Fact]
    public void Compute_TallSource_IsCentredHorizontallyInTheBox()
    {
        var plan = Bottom(960, Windows(1, width: 800, height: 1600));

        Assert.Equal(new PixelRect(93, 44, 163, 184), plan.Items[0].Thumbnail);
    }

    [Fact]
    public void Compute_WideSource_IsCentredVerticallyInTheBox()
    {
        var plan = Bottom(960, Windows(1, width: 3200, height: 900));

        Assert.Equal(new PixelRect(16, 82, 240, 145), plan.Items[0].Thumbnail);
    }

    [Fact]
    public void Compute_SingleWindow_IsOneItem()
    {
        var plan = Bottom(960, Windows(1));

        Assert.Equal(new PixelRect(832, 772, 1088, 972), plan.Popover);
        Assert.Equal(new PixelRect(8, 8, 248, 192), Assert.Single(plan.Items).Item);
    }

    [Fact]
    public void Compute_NoWindows_IsEmpty()
    {
        var plan = Bottom(960, []);

        Assert.Equal(default, plan.Popover);
        Assert.Empty(plan.Items);
        Assert.Equal(0, plan.OverflowCount);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Compute_InvalidScale_IsTreatedAsOne(double scale)
    {
        var plan = DockPreviewLayout.Compute(DockPosition.Bottom, new PixelRect(760, 980, 1160, 1072), 960, Monitor, Monitor, scale,
            Windows(2));

        Assert.Equal(new PixelRect(710, 772, 1210, 972), plan.Popover);
    }
}
