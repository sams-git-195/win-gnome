using WinGnome.Core.Geometry;
using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class AppBarNegotiatorTests
{
    private static readonly AppBarMonitor Main = new(1, new PixelRect(0, 0, 1920, 1080));
    private static readonly AppBarMonitor Right = new(2, new PixelRect(1920, 0, 3840, 1080));
    private static readonly AppBarMonitor LeftOfMain = new(3, new PixelRect(-1280, -100, 0, 924));

    private static AppBarNegotiator With(params AppBarMonitor[] monitors)
    {
        var negotiator = new AppBarNegotiator();
        negotiator.SetMonitors(monitors);
        return negotiator;
    }

    private static PixelRect Area(AppBarNegotiator n, int monitorId) =>
        n.ComputeWorkAreas().Single(a => a.MonitorId == monitorId).WorkArea;

    [Fact]
    public void ComputeWorkAreas_NoBars_IsTheWholeMonitor()
    {
        var areas = With(Main, Right).ComputeWorkAreas();

        Assert.Equal(
            [new MonitorWorkArea(1, Main.Bounds, Main.Bounds), new MonitorWorkArea(2, Right.Bounds, Right.Bounds)],
            areas);
    }

    [Fact]
    public void ComputeWorkAreas_NoMonitors_IsEmpty()
    {
        Assert.Empty(new AppBarNegotiator().ComputeWorkAreas());
    }

    [Fact]
    public void Register_Twice_FailsTheSecondTime()
    {
        var n = With(Main);

        Assert.True(n.Register(10));
        Assert.False(n.Register(10));
    }

    [Fact]
    public void Remove_Unregistered_ReturnsFalse()
    {
        Assert.False(With(Main).Remove(10));
    }

    [Fact]
    public void QueryPos_Unregistered_ReturnsNull()
    {
        Assert.Null(With(Main).QueryPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32)));
    }

    [Fact]
    public void SetPos_Unregistered_ReturnsNullAndReservesNothing()
    {
        var n = With(Main);

        Assert.Null(n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32)));
        Assert.Equal(Main.Bounds, Area(n, 1));
    }

    [Fact]
    public void QueryPos_NoMonitors_ReturnsTheProposalUnchanged()
    {
        var n = new AppBarNegotiator();
        n.Register(10);

        Assert.Equal(new PixelRect(0, 0, 100, 32), n.QueryPos(10, AppBarEdge.Top, new PixelRect(0, 0, 100, 32)));
    }

    [Fact]
    public void SetPos_TopBar_ReservesTheTopOfTheWorkArea()
    {
        var n = With(Main);
        n.Register(10);

        var granted = n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));

        Assert.Equal(new PixelRect(0, 0, 1920, 32), granted);
        Assert.Equal(new PixelRect(0, 32, 1920, 1080), Area(n, 1));
    }

    [Theory]
    [InlineData(AppBarEdge.Left, 0, 0, 64, 1080, 64, 0, 1920, 1080)]
    [InlineData(AppBarEdge.Right, 1856, 0, 1920, 1080, 0, 0, 1856, 1080)]
    [InlineData(AppBarEdge.Bottom, 0, 1000, 1920, 1080, 0, 0, 1920, 1000)]
    public void SetPos_EachEdge_ShrinksTheWorkAreaFromThatSide(
        AppBarEdge edge, int l, int t, int r, int b, int wl, int wt, int wr, int wb)
    {
        var n = With(Main);
        n.Register(10);

        n.SetPos(10, edge, new PixelRect(l, t, r, b));

        Assert.Equal(new PixelRect(wl, wt, wr, wb), Area(n, 1));
    }

    [Fact]
    public void QueryPos_DoesNotChangeTheWorkArea()
    {
        var n = With(Main);
        n.Register(10);

        n.QueryPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));

        Assert.Equal(Main.Bounds, Area(n, 1));
    }

    [Fact]
    public void QueryPos_SecondTopBar_StacksBelowTheFirst()
    {
        var n = With(Main);
        n.Register(10);
        n.Register(11);
        n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));

        var granted = n.QueryPos(11, AppBarEdge.Top, new PixelRect(0, 0, 1920, 40));

        Assert.Equal(new PixelRect(0, 32, 1920, 40), granted);
    }

    [Fact]
    public void SetPos_ThreeStackedBottomBars_AccumulateUpwards()
    {
        var n = With(Main);
        foreach (var id in new long[] { 1, 2, 3 })
        {
            n.Register(id);
        }

        var first = n.SetPos(1, AppBarEdge.Bottom, new PixelRect(0, 1040, 1920, 1080));
        var second = n.SetPos(2, AppBarEdge.Bottom, new PixelRect(0, 1000, 1920, 1080));
        var third = n.SetPos(3, AppBarEdge.Bottom, new PixelRect(0, 980, 1920, 1080));

        Assert.Equal(new PixelRect(0, 1040, 1920, 1080), first);
        Assert.Equal(new PixelRect(0, 1000, 1920, 1040), second);
        Assert.Equal(new PixelRect(0, 980, 1920, 1000), third);
        Assert.Equal(new PixelRect(0, 0, 1920, 980), Area(n, 1));
    }

    [Fact]
    public void SetPos_SameEdgeSameRectangle_PushesTheEdgeSideAndKeepsTheFarSide()
    {
        var n = With(Main);
        n.Register(1);
        n.Register(2);
        n.SetPos(1, AppBarEdge.Bottom, new PixelRect(0, 1040, 1920, 1080));

        var second = n.SetPos(2, AppBarEdge.Bottom, new PixelRect(0, 1040, 1920, 1080));

        Assert.Equal(new PixelRect(0, 1040, 1920, 1040), second);
    }

    [Fact]
    public void QueryPos_InnerBarRequeryingItsOwnRectangle_IsNotPushedPastTheOuterOne()
    {
        var n = With(Main);
        n.Register(10);
        n.Register(11);
        n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));
        n.SetPos(11, AppBarEdge.Top, new PixelRect(0, 32, 1920, 64));

        Assert.Equal(new PixelRect(0, 0, 1920, 32), n.QueryPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32)));
        Assert.Equal(new PixelRect(0, 32, 1920, 64), n.QueryPos(11, AppBarEdge.Top, new PixelRect(0, 32, 1920, 64)));
    }

    [Fact]
    public void SetPos_EarlierBarResetAfterALaterOne_KeepsBothReservations()
    {
        var n = With(Main);
        n.Register(10);
        n.Register(11);
        n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));
        n.SetPos(11, AppBarEdge.Top, new PixelRect(0, 32, 1920, 64));

        n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));

        Assert.Equal(new PixelRect(0, 64, 1920, 1080), Area(n, 1));
    }

    [Fact]
    public void SetPos_ReRegisteredBar_CountsAsLatest()
    {
        var n = With(Main);
        n.Register(10);
        n.Register(11);
        n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));
        n.Remove(10);
        n.Register(10);

        Assert.Equal(new PixelRect(0, 0, 1920, 0), n.SetPos(11, AppBarEdge.Top, new PixelRect(0, 0, 1920, 0)));
        n.SetPos(11, AppBarEdge.Top, new PixelRect(0, 0, 1920, 20));
        Assert.Equal(new PixelRect(0, 20, 1920, 52), n.QueryPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 52)));
    }

    [Fact]
    public void QueryPos_SameBarRequeried_IgnoresItsOwnPreviousRectangle()
    {
        var n = With(Main);
        n.Register(10);
        n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));

        var granted = n.QueryPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 48));

        Assert.Equal(new PixelRect(0, 0, 1920, 48), granted);
    }

    [Fact]
    public void SetPos_SameBarResized_ReplacesItsReservation()
    {
        var n = With(Main);
        n.Register(10);
        n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));

        n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 48));

        Assert.Equal(new PixelRect(0, 48, 1920, 1080), Area(n, 1));
    }

    [Fact]
    public void SetPos_BarMovedToAnotherEdge_ReleasesTheOldEdge()
    {
        var n = With(Main);
        n.Register(10);
        n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));

        n.SetPos(10, AppBarEdge.Left, new PixelRect(0, 0, 50, 1080));

        Assert.Equal(new PixelRect(50, 0, 1920, 1080), Area(n, 1));
    }

    [Fact]
    public void SetPos_LeftBarAfterTopBar_DoesNotOverlapIt()
    {
        var n = With(Main);
        n.Register(10);
        n.Register(11);
        n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));

        var granted = n.SetPos(11, AppBarEdge.Left, new PixelRect(0, 0, 64, 1080));

        Assert.Equal(new PixelRect(0, 32, 64, 1080), granted);
        Assert.Equal(new PixelRect(64, 32, 1920, 1080), Area(n, 1));
    }

    [Fact]
    public void SetPos_TopBarAfterRightBar_DoesNotOverlapIt()
    {
        var n = With(Main);
        n.Register(10);
        n.Register(11);
        n.SetPos(10, AppBarEdge.Right, new PixelRect(1840, 0, 1920, 1080));

        var granted = n.SetPos(11, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));

        Assert.Equal(new PixelRect(0, 0, 1840, 32), granted);
    }

    [Fact]
    public void SetPos_BottomBarAfterLeftBar_StartsAfterIt()
    {
        var n = With(Main);
        n.Register(10);
        n.Register(11);
        n.SetPos(10, AppBarEdge.Left, new PixelRect(0, 0, 60, 1080));

        var granted = n.SetPos(11, AppBarEdge.Bottom, new PixelRect(0, 1040, 1920, 1080));

        Assert.Equal(new PixelRect(60, 1040, 1920, 1080), granted);
    }

    [Fact]
    public void SetPos_OppositeEdges_DoNotAffectEachOther()
    {
        var n = With(Main);
        n.Register(10);
        n.Register(11);
        n.SetPos(10, AppBarEdge.Left, new PixelRect(0, 0, 60, 1080));

        var granted = n.SetPos(11, AppBarEdge.Right, new PixelRect(1860, 0, 1920, 1080));

        Assert.Equal(new PixelRect(1860, 0, 1920, 1080), granted);
        Assert.Equal(new PixelRect(60, 0, 1860, 1080), Area(n, 1));
    }

    [Fact]
    public void SetPos_RectLargerThanTheMonitor_IsClampedToIt()
    {
        var n = With(Main);
        n.Register(10);

        var granted = n.SetPos(10, AppBarEdge.Top, new PixelRect(-500, -50, 5000, 40));

        Assert.Equal(new PixelRect(0, 0, 1920, 40), granted);
    }

    [Fact]
    public void SetPos_BarsFillTheMonitor_WorkAreaCollapsesInsteadOfGoingNegative()
    {
        var n = With(Main);
        n.Register(10);
        n.Register(11);
        n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 700));
        n.SetPos(11, AppBarEdge.Bottom, new PixelRect(0, 700, 1920, 1080));

        var area = Area(n, 1);

        Assert.Equal(new PixelRect(0, 700, 1920, 700), area);
        Assert.True(area.IsEmpty);
    }

    [Fact]
    public void SetPos_BarOnSecondMonitor_OnlyShrinksThatMonitor()
    {
        var n = With(Main, Right);
        n.Register(10);

        var granted = n.SetPos(10, AppBarEdge.Top, new PixelRect(1920, 0, 3840, 32));

        Assert.Equal(new PixelRect(1920, 0, 3840, 32), granted);
        Assert.Equal(Main.Bounds, Area(n, 1));
        Assert.Equal(new PixelRect(1920, 32, 3840, 1080), Area(n, 2));
    }

    [Fact]
    public void SetPos_BarsOnDifferentMonitorsSameEdge_DoNotStack()
    {
        var n = With(Main, Right);
        n.Register(10);
        n.Register(11);
        n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));

        var granted = n.SetPos(11, AppBarEdge.Top, new PixelRect(1920, 0, 3840, 32));

        Assert.Equal(new PixelRect(1920, 0, 3840, 32), granted);
    }

    [Fact]
    public void SetPos_NegativeCoordinateMonitor_WorksInVirtualScreenSpace()
    {
        var n = With(Main, LeftOfMain);
        n.Register(10);
        n.Register(11);

        n.SetPos(10, AppBarEdge.Left, new PixelRect(-1280, -100, -1230, 924));
        var second = n.SetPos(11, AppBarEdge.Left, new PixelRect(-1280, -100, -1200, 924));

        Assert.Equal(new PixelRect(-1230, -100, -1200, 924), second);
        Assert.Equal(new PixelRect(-1200, -100, 0, 924), Area(n, 3));
        Assert.Equal(Main.Bounds, Area(n, 1));
    }

    [Fact]
    public void SetPos_NegativeMonitorBottomBar_ReservesFromItsOwnBottom()
    {
        var n = With(Main, LeftOfMain);
        n.Register(10);

        n.SetPos(10, AppBarEdge.Bottom, new PixelRect(-1280, 884, 0, 924));

        Assert.Equal(new PixelRect(-1280, -100, 0, 884), Area(n, 3));
    }

    [Fact]
    public void SetPos_RectStraddlingTwoMonitors_GoesToTheOneItOverlapsMost()
    {
        var n = With(Main, Right);
        n.Register(10);

        n.SetPos(10, AppBarEdge.Top, new PixelRect(1800, 0, 3000, 30));

        Assert.Equal(Main.Bounds, Area(n, 1));
        Assert.Equal(new PixelRect(1920, 30, 3840, 1080), Area(n, 2));
    }

    [Fact]
    public void SetPos_RectOffEveryMonitor_IsReturnedAsProposedAndReservesNothing()
    {
        var n = With(Main, Right);
        n.Register(10);

        var granted = n.SetPos(10, AppBarEdge.Top, new PixelRect(9000, 9000, 9100, 9032));

        Assert.Equal(new PixelRect(9000, 9000, 9100, 9032), granted);
        Assert.Equal(Main.Bounds, Area(n, 1));
        Assert.Equal(Right.Bounds, Area(n, 2));
    }

    [Fact]
    public void SetPos_RectStraddlingTwoMonitorsEqually_GoesToTheEarlierOne()
    {
        var n = With(Main, Right);
        n.Register(10);

        var granted = n.SetPos(10, AppBarEdge.Top, new PixelRect(1820, 0, 2020, 30));

        Assert.Equal(new PixelRect(1820, 0, 1920, 30), granted);
        Assert.Equal(new PixelRect(0, 30, 1920, 1080), Area(n, 1));
        Assert.Equal(Right.Bounds, Area(n, 2));
    }

    [Fact]
    public void QueryPos_ThinnerThanTheBarBeforeIt_CollapsesToZeroWidthNotNegative()
    {
        var n = With(Main);
        n.Register(10);
        n.Register(11);
        n.SetPos(10, AppBarEdge.Left, new PixelRect(0, 0, 100, 1080));

        var granted = n.QueryPos(11, AppBarEdge.Left, new PixelRect(0, 0, 60, 1080));

        Assert.Equal(new PixelRect(100, 0, 100, 1080), granted);
    }

    [Fact]
    public void Remove_ReleasesTheReservedSpace()
    {
        var n = With(Main);
        n.Register(10);
        n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));

        Assert.True(n.Remove(10));

        Assert.Equal(Main.Bounds, Area(n, 1));
    }

    [Fact]
    public void Remove_MiddleOfStack_LetsTheNextBarTakeTheGap()
    {
        var n = With(Main);
        foreach (var id in new long[] { 1, 2 })
        {
            n.Register(id);
        }

        n.SetPos(1, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));
        n.SetPos(2, AppBarEdge.Top, new PixelRect(0, 0, 1920, 64));
        n.Remove(1);

        Assert.Equal(new PixelRect(0, 0, 1920, 64), n.QueryPos(2, AppBarEdge.Top, new PixelRect(0, 0, 1920, 64)));
    }

    [Fact]
    public void Remove_ThenRegisterAgain_Works()
    {
        var n = With(Main);
        n.Register(10);
        n.Remove(10);

        Assert.True(n.Register(10));
        Assert.Equal(Main.Bounds, Area(n, 1));
    }

    [Fact]
    public void SetMonitors_DisplayChange_RecomputesWorkAreasFromBarRectangles()
    {
        var n = With(Main);
        n.Register(10);
        n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));

        n.SetMonitors([new AppBarMonitor(1, new PixelRect(0, 0, 2560, 1440))]);

        Assert.Equal(new PixelRect(0, 32, 2560, 1440), Area(n, 1));
    }

    [Fact]
    public void SetAutoHide_ReservesNoSpace()
    {
        var n = With(Main);
        n.Register(10);

        Assert.True(n.SetAutoHide(10, AppBarEdge.Top, Main.Bounds, true));

        Assert.Equal(Main.Bounds, Area(n, 1));
        Assert.Equal(10, n.GetAutoHideBar(AppBarEdge.Top, Main.Bounds));
    }

    [Fact]
    public void SetAutoHide_SecondBarOnTheSameEdgeAndMonitor_IsRefused()
    {
        var n = With(Main);
        n.Register(10);
        n.Register(11);
        n.SetAutoHide(10, AppBarEdge.Top, Main.Bounds, true);

        Assert.False(n.SetAutoHide(11, AppBarEdge.Top, Main.Bounds, true));
        Assert.Equal(10, n.GetAutoHideBar(AppBarEdge.Top, Main.Bounds));
    }

    [Fact]
    public void SetAutoHide_SameBarAgain_Succeeds()
    {
        var n = With(Main);
        n.Register(10);
        n.SetAutoHide(10, AppBarEdge.Top, Main.Bounds, true);

        Assert.True(n.SetAutoHide(10, AppBarEdge.Top, Main.Bounds, true));
    }

    [Fact]
    public void SetAutoHide_OtherEdgeOrOtherMonitor_IsAllowed()
    {
        var n = With(Main, Right);
        n.Register(10);
        n.Register(11);
        n.Register(12);
        n.SetAutoHide(10, AppBarEdge.Top, Main.Bounds, true);

        Assert.True(n.SetAutoHide(11, AppBarEdge.Bottom, Main.Bounds, true));
        Assert.True(n.SetAutoHide(12, AppBarEdge.Top, Right.Bounds, true));
    }

    [Fact]
    public void SetAutoHide_Disable_FreesTheEdgeForAnotherBar()
    {
        var n = With(Main);
        n.Register(10);
        n.Register(11);
        n.SetAutoHide(10, AppBarEdge.Top, Main.Bounds, true);

        Assert.True(n.SetAutoHide(10, AppBarEdge.Top, Main.Bounds, false));

        Assert.Null(n.GetAutoHideBar(AppBarEdge.Top, Main.Bounds));
        Assert.True(n.SetAutoHide(11, AppBarEdge.Top, Main.Bounds, true));
    }

    [Fact]
    public void SetAutoHide_DisableByANonOwner_LeavesTheOwnerInPlace()
    {
        var n = With(Main);
        n.Register(10);
        n.Register(11);
        n.SetAutoHide(10, AppBarEdge.Top, Main.Bounds, true);

        n.SetAutoHide(11, AppBarEdge.Top, Main.Bounds, false);

        Assert.Equal(10, n.GetAutoHideBar(AppBarEdge.Top, Main.Bounds));
    }

    [Fact]
    public void SetAutoHide_UnregisteredBar_Fails()
    {
        Assert.False(With(Main).SetAutoHide(10, AppBarEdge.Top, Main.Bounds, true));
    }

    [Fact]
    public void SetAutoHide_OnADockedBar_ReleasesItsReservation()
    {
        var n = With(Main);
        n.Register(10);
        n.SetPos(10, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32));

        n.SetAutoHide(10, AppBarEdge.Top, Main.Bounds, true);

        Assert.Equal(Main.Bounds, Area(n, 1));
    }

    [Fact]
    public void QueryPos_AutoHideBarsAreIgnored()
    {
        var n = With(Main);
        n.Register(10);
        n.Register(11);
        n.SetAutoHide(10, AppBarEdge.Top, Main.Bounds, true);

        Assert.Equal(new PixelRect(0, 0, 1920, 32), n.QueryPos(11, AppBarEdge.Top, new PixelRect(0, 0, 1920, 32)));
    }

    [Fact]
    public void Remove_AutoHideBar_FreesItsEdge()
    {
        var n = With(Main);
        n.Register(10);
        n.SetAutoHide(10, AppBarEdge.Top, Main.Bounds, true);

        n.Remove(10);

        Assert.Null(n.GetAutoHideBar(AppBarEdge.Top, Main.Bounds));
    }

    [Fact]
    public void SetMonitors_MonitorRemoved_DropsItsAutoHideClaims()
    {
        var n = With(Main, Right);
        n.Register(10);
        n.SetAutoHide(10, AppBarEdge.Top, Right.Bounds, true);

        n.SetMonitors([Main]);
        n.SetMonitors([Main, Right]);

        Assert.Null(n.GetAutoHideBar(AppBarEdge.Top, Right.Bounds));
    }
}
