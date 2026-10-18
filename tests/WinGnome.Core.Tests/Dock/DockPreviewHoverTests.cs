using WinGnome.Core.Dock;

namespace WinGnome.Core.Tests.Dock;

public class DockPreviewHoverTests
{
    private const string A = "pin:a";
    private const string B = "run:b";

    private static DockPreviewHover OpenOn(string key, long hoverAtMs = 1000)
    {
        var hover = new DockPreviewHover();
        hover.PointerOverIcon(key, hasPreview: true, hoverAtMs);
        Assert.Equal(DockPreviewChange.Show, hover.Tick(hoverAtMs + DockPreviewHover.ShowDelayMs));
        return hover;
    }

    [Fact]
    public void NewMachine_IsIdleWithNoDeadline()
    {
        var hover = new DockPreviewHover();

        Assert.Equal(DockPreviewPhase.Idle, hover.Phase);
        Assert.Null(hover.Key);
        Assert.Null(hover.Deadline);
    }

    [Fact]
    public void PointerOverIcon_WithPreview_StartsTheShowDelay()
    {
        var hover = new DockPreviewHover();

        var change = hover.PointerOverIcon(A, hasPreview: true, nowMs: 1000);

        Assert.Equal(DockPreviewChange.None, change);
        Assert.Equal(DockPreviewPhase.Pending, hover.Phase);
        Assert.Equal(A, hover.Key);
        Assert.Equal(1400, hover.Deadline);
    }

    [Fact]
    public void Tick_OneMillisecondBeforeTheDelay_DoesNotShow()
    {
        var hover = new DockPreviewHover();
        hover.PointerOverIcon(A, hasPreview: true, nowMs: 1000);

        Assert.Equal(DockPreviewChange.None, hover.Tick(1399));
        Assert.Equal(DockPreviewPhase.Pending, hover.Phase);
    }

    [Fact]
    public void Tick_AtTheDelay_Shows()
    {
        var hover = new DockPreviewHover();
        hover.PointerOverIcon(A, hasPreview: true, nowMs: 1000);

        Assert.Equal(DockPreviewChange.Show, hover.Tick(1400));
        Assert.Equal(DockPreviewPhase.Open, hover.Phase);
        Assert.Equal(A, hover.Key);
        Assert.Null(hover.Deadline);
    }

    [Fact]
    public void Tick_LongAfterTheDelay_StillShowsOnce()
    {
        var hover = new DockPreviewHover();
        hover.PointerOverIcon(A, hasPreview: true, nowMs: 0);

        Assert.Equal(DockPreviewChange.Show, hover.Tick(5000));
        Assert.Equal(DockPreviewChange.None, hover.Tick(5001));
    }

    [Fact]
    public void PointerOverIcon_WithoutPreview_StaysIdle()
    {
        var hover = new DockPreviewHover();

        Assert.Equal(DockPreviewChange.None, hover.PointerOverIcon(A, hasPreview: false, nowMs: 1000));
        Assert.Equal(DockPreviewPhase.Idle, hover.Phase);
        Assert.Null(hover.Deadline);
        Assert.Equal(DockPreviewChange.None, hover.Tick(5000));
    }

    [Fact]
    public void PointerOverIcon_NullKey_StaysIdle()
    {
        var hover = new DockPreviewHover();

        Assert.Equal(DockPreviewChange.None, hover.PointerOverIcon(null, hasPreview: true, nowMs: 1000));
        Assert.Equal(DockPreviewPhase.Idle, hover.Phase);
    }

    [Fact]
    public void Pending_PointerLeavesTheIcon_CancelsTheShow()
    {
        var hover = new DockPreviewHover();
        hover.PointerOverIcon(A, hasPreview: true, nowMs: 1000);

        Assert.Equal(DockPreviewChange.None, hover.PointerOverIcon(null, hasPreview: false, nowMs: 1200));

        Assert.Equal(DockPreviewPhase.Idle, hover.Phase);
        Assert.Null(hover.Deadline);
        Assert.Equal(DockPreviewChange.None, hover.Tick(1400));
    }

    [Fact]
    public void Pending_PointerMovesToAnIconWithoutPreview_CancelsTheShow()
    {
        var hover = new DockPreviewHover();
        hover.PointerOverIcon(A, hasPreview: true, nowMs: 1000);

        hover.PointerOverIcon(B, hasPreview: false, nowMs: 1200);

        Assert.Equal(DockPreviewPhase.Idle, hover.Phase);
        Assert.Equal(DockPreviewChange.None, hover.Tick(1400));
    }

    [Fact]
    public void Pending_PointerMovesToAnotherPreviewIcon_RestartsTheDelayForIt()
    {
        var hover = new DockPreviewHover();
        hover.PointerOverIcon(A, hasPreview: true, nowMs: 1000);

        hover.PointerOverIcon(B, hasPreview: true, nowMs: 1300);

        Assert.Equal(B, hover.Key);
        Assert.Equal(1700, hover.Deadline);
        Assert.Equal(DockPreviewChange.None, hover.Tick(1400));
        Assert.Equal(DockPreviewChange.Show, hover.Tick(1700));
        Assert.Equal(B, hover.Key);
    }

    [Fact]
    public void Pending_SameIconReportedAgain_DoesNotRestartTheDelay()
    {
        var hover = new DockPreviewHover();
        hover.PointerOverIcon(A, hasPreview: true, nowMs: 1000);

        hover.PointerOverIcon(A, hasPreview: true, nowMs: 1300);

        Assert.Equal(1400, hover.Deadline);
        Assert.Equal(DockPreviewChange.Show, hover.Tick(1400));
    }

    [Fact]
    public void Open_PointerMovesToAnotherPreviewIcon_SwitchesAtOnce()
    {
        var hover = OpenOn(A);

        var change = hover.PointerOverIcon(B, hasPreview: true, nowMs: 1500);

        Assert.Equal(DockPreviewChange.Switch, change);
        Assert.Equal(DockPreviewPhase.Open, hover.Phase);
        Assert.Equal(B, hover.Key);
        Assert.Null(hover.Deadline);
    }

    [Fact]
    public void Open_SameIconReportedAgain_ChangesNothing()
    {
        var hover = OpenOn(A);

        Assert.Equal(DockPreviewChange.None, hover.PointerOverIcon(A, hasPreview: true, nowMs: 1500));
        Assert.Equal(DockPreviewPhase.Open, hover.Phase);
    }

    [Fact]
    public void Open_PointerLeavesTheDock_StartsTheGrace()
    {
        var hover = OpenOn(A);

        var change = hover.PointerOverIcon(null, hasPreview: false, nowMs: 2000);

        Assert.Equal(DockPreviewChange.None, change);
        Assert.Equal(DockPreviewPhase.Leaving, hover.Phase);
        Assert.Equal(A, hover.Key);
        Assert.Equal(2300, hover.Deadline);
    }

    [Fact]
    public void Open_PointerMovesToAnIconWithoutPreview_StartsTheGrace()
    {
        var hover = OpenOn(A);

        hover.PointerOverIcon(B, hasPreview: false, nowMs: 2000);

        Assert.Equal(DockPreviewPhase.Leaving, hover.Phase);
        Assert.Equal(2300, hover.Deadline);
    }

    [Fact]
    public void Leaving_TickBeforeTheGraceEnds_KeepsThePopover()
    {
        var hover = OpenOn(A);
        hover.PointerOverIcon(null, hasPreview: false, nowMs: 2000);

        Assert.Equal(DockPreviewChange.None, hover.Tick(2299));
        Assert.Equal(DockPreviewPhase.Leaving, hover.Phase);
    }

    [Fact]
    public void Leaving_TickAtTheGraceEnd_Hides()
    {
        var hover = OpenOn(A);
        hover.PointerOverIcon(null, hasPreview: false, nowMs: 2000);

        Assert.Equal(DockPreviewChange.Hide, hover.Tick(2300));

        Assert.Equal(DockPreviewPhase.Idle, hover.Phase);
        Assert.Null(hover.Key);
        Assert.Null(hover.Deadline);
    }

    [Fact]
    public void Leaving_PointerReachesThePopoverWithinTheGrace_KeepsItOpen()
    {
        var hover = OpenOn(A);
        hover.PointerOverIcon(null, hasPreview: false, nowMs: 2000);

        var change = hover.PointerOverPopover(over: true, nowMs: 2250);

        Assert.Equal(DockPreviewChange.None, change);
        Assert.Equal(DockPreviewPhase.Open, hover.Phase);
        Assert.Null(hover.Deadline);
        Assert.Equal(DockPreviewChange.None, hover.Tick(2300));
        Assert.Equal(DockPreviewPhase.Open, hover.Phase);
    }

    [Fact]
    public void Open_LeaveOfTheDockReportedAfterThePopoverWasEntered_StaysOpen()
    {
        // The popover's mouse-enter can arrive before the dock's mouse-leave.
        var hover = OpenOn(A);
        hover.PointerOverPopover(over: true, nowMs: 2000);

        hover.PointerOverIcon(null, hasPreview: false, nowMs: 2001);

        Assert.Equal(DockPreviewPhase.Open, hover.Phase);
        Assert.Null(hover.Deadline);
    }

    [Fact]
    public void Open_PointerLeavesThePopover_StartsTheGrace()
    {
        var hover = OpenOn(A);
        hover.PointerOverPopover(over: true, nowMs: 2000);
        hover.PointerOverIcon(null, hasPreview: false, nowMs: 2001);

        var change = hover.PointerOverPopover(over: false, nowMs: 3000);

        Assert.Equal(DockPreviewChange.None, change);
        Assert.Equal(DockPreviewPhase.Leaving, hover.Phase);
        Assert.Equal(3300, hover.Deadline);
        Assert.Equal(DockPreviewChange.Hide, hover.Tick(3300));
    }

    [Fact]
    public void Open_PointerLeavesThePopoverOntoItsIcon_StaysOpenWhicheverEventComesFirst()
    {
        // The icon's enter may be reported before the popover's leave.
        var hover = OpenOn(A);
        hover.PointerOverPopover(over: true, nowMs: 2000);
        hover.PointerOverIcon(null, hasPreview: false, nowMs: 2001);

        hover.PointerOverIcon(A, hasPreview: true, nowMs: 3000);
        var change = hover.PointerOverPopover(over: false, nowMs: 3001);

        Assert.Equal(DockPreviewChange.None, change);
        Assert.Equal(DockPreviewPhase.Open, hover.Phase);
        Assert.Null(hover.Deadline);
    }

    [Fact]
    public void Leaving_PointerComesBackToTheSameIcon_ReopensWithoutChange()
    {
        var hover = OpenOn(A);
        hover.PointerOverIcon(null, hasPreview: false, nowMs: 2000);

        var change = hover.PointerOverIcon(A, hasPreview: true, nowMs: 2100);

        Assert.Equal(DockPreviewChange.None, change);
        Assert.Equal(DockPreviewPhase.Open, hover.Phase);
        Assert.Null(hover.Deadline);
        Assert.Equal(DockPreviewChange.None, hover.Tick(2300));
    }

    [Fact]
    public void Leaving_PointerArrivesOnAnotherPreviewIcon_SwitchesAtOnce()
    {
        var hover = OpenOn(A);
        hover.PointerOverIcon(null, hasPreview: false, nowMs: 2000);

        var change = hover.PointerOverIcon(B, hasPreview: true, nowMs: 2100);

        Assert.Equal(DockPreviewChange.Switch, change);
        Assert.Equal(DockPreviewPhase.Open, hover.Phase);
        Assert.Equal(B, hover.Key);
        Assert.Null(hover.Deadline);
    }

    [Fact]
    public void Leaving_MovingAcrossIconsWithoutPreview_DoesNotExtendTheGrace()
    {
        var hover = OpenOn(A);
        hover.PointerOverIcon(B, hasPreview: false, nowMs: 2000);

        hover.PointerOverIcon("run:c", hasPreview: false, nowMs: 2200);

        Assert.Equal(2300, hover.Deadline);
        Assert.Equal(DockPreviewChange.Hide, hover.Tick(2300));
    }

    [Fact]
    public void Dismiss_WhileOpen_HidesAndIsIdle()
    {
        var hover = OpenOn(A);

        Assert.Equal(DockPreviewChange.Hide, hover.Dismiss());

        Assert.Equal(DockPreviewPhase.Idle, hover.Phase);
        Assert.Null(hover.Key);
        Assert.Null(hover.Deadline);
    }

    [Fact]
    public void Dismiss_WhileLeaving_Hides()
    {
        var hover = OpenOn(A);
        hover.PointerOverIcon(null, hasPreview: false, nowMs: 2000);

        Assert.Equal(DockPreviewChange.Hide, hover.Dismiss());
        Assert.Equal(DockPreviewChange.None, hover.Tick(2300));
    }

    [Fact]
    public void Dismiss_WhilePending_CancelsWithoutAHide()
    {
        var hover = new DockPreviewHover();
        hover.PointerOverIcon(A, hasPreview: true, nowMs: 1000);

        Assert.Equal(DockPreviewChange.None, hover.Dismiss());
        Assert.Equal(DockPreviewChange.None, hover.Tick(1400));
        Assert.Equal(DockPreviewPhase.Idle, hover.Phase);
    }

    [Fact]
    public void Dismiss_WhileIdle_DoesNothing()
    {
        Assert.Equal(DockPreviewChange.None, new DockPreviewHover().Dismiss());
    }

    [Fact]
    public void Dismiss_IconUnderThePointerDoesNotReopenUntilThePointerLeavesIt()
    {
        var hover = OpenOn(A);
        hover.Dismiss();

        Assert.Equal(DockPreviewChange.None, hover.PointerOverIcon(A, hasPreview: true, nowMs: 2000));

        Assert.Equal(DockPreviewPhase.Idle, hover.Phase);
        Assert.Null(hover.Deadline);
        Assert.Equal(DockPreviewChange.None, hover.Tick(9000));
    }

    [Fact]
    public void Dismiss_PointerLeavesTheDisarmedIcon_ArmsItAgain()
    {
        var hover = OpenOn(A);
        hover.Dismiss();
        hover.PointerOverIcon(null, hasPreview: false, nowMs: 2000);

        hover.PointerOverIcon(A, hasPreview: true, nowMs: 3000);

        Assert.Equal(DockPreviewPhase.Pending, hover.Phase);
        Assert.Equal(3400, hover.Deadline);
    }

    [Fact]
    public void Dismiss_PointerMovesToAnotherIcon_ArmsBothIcons()
    {
        var hover = OpenOn(A);
        hover.Dismiss();

        hover.PointerOverIcon(B, hasPreview: true, nowMs: 2000);
        Assert.Equal(DockPreviewPhase.Pending, hover.Phase);
        Assert.Equal(B, hover.Key);

        hover.PointerOverIcon(A, hasPreview: true, nowMs: 2100);
        Assert.Equal(DockPreviewPhase.Pending, hover.Phase);
        Assert.Equal(A, hover.Key);
    }

    [Fact]
    public void Dismiss_WhilePending_DisarmsTheIconUnderThePointer()
    {
        // A click on the icon during the delay must not open the popover afterwards.
        var hover = new DockPreviewHover();
        hover.PointerOverIcon(A, hasPreview: true, nowMs: 1000);
        hover.Dismiss();

        hover.PointerOverIcon(A, hasPreview: true, nowMs: 1100);

        Assert.Equal(DockPreviewPhase.Idle, hover.Phase);
        Assert.Equal(DockPreviewChange.None, hover.Tick(1500));
    }

    [Fact]
    public void Dismiss_WhenThePointerWasOnThePopover_DisarmsNoIcon()
    {
        var hover = OpenOn(A);
        hover.PointerOverPopover(over: true, nowMs: 2000);
        hover.PointerOverIcon(null, hasPreview: false, nowMs: 2001);
        hover.Dismiss();

        hover.PointerOverIcon(A, hasPreview: true, nowMs: 3000);

        Assert.Equal(DockPreviewPhase.Pending, hover.Phase);
    }

    [Fact]
    public void SetSuppressed_WhileOpen_Hides()
    {
        var hover = OpenOn(A);

        Assert.Equal(DockPreviewChange.Hide, hover.SetSuppressed(true));
        Assert.Equal(DockPreviewPhase.Idle, hover.Phase);
        Assert.Null(hover.Deadline);
    }

    [Fact]
    public void SetSuppressed_WhilePending_CancelsWithoutAHide()
    {
        var hover = new DockPreviewHover();
        hover.PointerOverIcon(A, hasPreview: true, nowMs: 1000);

        Assert.Equal(DockPreviewChange.None, hover.SetSuppressed(true));
        Assert.Equal(DockPreviewChange.None, hover.Tick(1400));
    }

    [Fact]
    public void Suppressed_PointerOverPreviewIcon_NeverOpens()
    {
        var hover = new DockPreviewHover();
        hover.SetSuppressed(true);

        Assert.Equal(DockPreviewChange.None, hover.PointerOverIcon(A, hasPreview: true, nowMs: 1000));

        Assert.Equal(DockPreviewPhase.Idle, hover.Phase);
        Assert.Null(hover.Deadline);
        Assert.Equal(DockPreviewChange.None, hover.Tick(5000));
    }

    [Fact]
    public void Suppressed_PointerOverPopover_DoesNothing()
    {
        var hover = new DockPreviewHover();
        hover.SetSuppressed(true);

        Assert.Equal(DockPreviewChange.None, hover.PointerOverPopover(over: true, nowMs: 1000));
        Assert.Equal(DockPreviewPhase.Idle, hover.Phase);
    }

    [Fact]
    public void SetSuppressed_Lifted_AllowsTheNextHoverToOpen()
    {
        var hover = new DockPreviewHover();
        hover.SetSuppressed(true);
        hover.PointerOverIcon(A, hasPreview: true, nowMs: 1000);

        Assert.Equal(DockPreviewChange.None, hover.SetSuppressed(false));
        Assert.Equal(DockPreviewPhase.Idle, hover.Phase);

        hover.PointerOverIcon(B, hasPreview: true, nowMs: 2000);
        Assert.Equal(DockPreviewChange.Show, hover.Tick(2400));
    }

    [Fact]
    public void SetSuppressed_False_WhileOpen_ChangesNothing()
    {
        var hover = OpenOn(A);

        Assert.Equal(DockPreviewChange.None, hover.SetSuppressed(false));
        Assert.Equal(DockPreviewPhase.Open, hover.Phase);
    }

    [Fact]
    public void Tick_WhileIdle_DoesNothing()
    {
        Assert.Equal(DockPreviewChange.None, new DockPreviewHover().Tick(10_000));
    }

    [Fact]
    public void Tick_WhileOpen_DoesNothing()
    {
        var hover = OpenOn(A);

        Assert.Equal(DockPreviewChange.None, hover.Tick(60_000));
        Assert.Equal(DockPreviewPhase.Open, hover.Phase);
    }

    [Fact]
    public void PointerOverPopover_WhileIdle_DoesNothing()
    {
        var hover = new DockPreviewHover();

        Assert.Equal(DockPreviewChange.None, hover.PointerOverPopover(over: true, nowMs: 1000));
        Assert.Equal(DockPreviewChange.None, hover.PointerOverPopover(over: false, nowMs: 1100));
        Assert.Equal(DockPreviewPhase.Idle, hover.Phase);
        Assert.Null(hover.Deadline);
    }

    [Fact]
    public void FullCycle_HoverShowLeaveHideHoverAgain()
    {
        var hover = new DockPreviewHover();

        hover.PointerOverIcon(A, hasPreview: true, nowMs: 0);
        Assert.Equal(DockPreviewChange.Show, hover.Tick(400));
        hover.PointerOverIcon(null, hasPreview: false, nowMs: 1000);
        Assert.Equal(DockPreviewChange.Hide, hover.Tick(1300));

        hover.PointerOverIcon(A, hasPreview: true, nowMs: 2000);
        Assert.Equal(DockPreviewChange.None, hover.Tick(2399));
        Assert.Equal(DockPreviewChange.Show, hover.Tick(2400));
    }
}
