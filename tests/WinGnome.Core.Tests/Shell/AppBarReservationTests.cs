using WinGnome.Core.Geometry;
using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class AppBarReservationTests
{
    // The user's primary (2560x1600 physical) with a 40 px top bar, and the 3440x1440 monitor above it.
    private static readonly PixelRect PrimaryBar = new(0, 0, 2560, 40);
    private static readonly PixelRect UpperBar = new(-447, -1440, 2993, -1408);

    [Fact]
    public void Top_WorkAreaBelowTheStrip_IsReserved()
    {
        Assert.True(AppBarReservation.IsReserved(AppBarEdge.Top, PrimaryBar, new PixelRect(0, 40, 2560, 1600)));
    }

    [Fact]
    public void Top_WorkAreaResetToTheWholeMonitor_IsNotReserved()
    {
        // What Explorer left after the upper monitor was unplugged.
        Assert.False(AppBarReservation.IsReserved(AppBarEdge.Top, PrimaryBar, new PixelRect(0, 0, 2560, 1600)));
    }

    [Theory]
    [InlineData(39, false)]
    [InlineData(40, true)]
    [InlineData(80, true)] // Stacked below another top bar: the work area ends beyond both.
    public void Top_Boundary(int workTop, bool expected)
    {
        Assert.Equal(expected, AppBarReservation.IsReserved(AppBarEdge.Top, PrimaryBar, new PixelRect(0, workTop, 2560, 1600)));
    }

    [Theory]
    [InlineData(-1408, true)]
    [InlineData(-1409, false)]
    [InlineData(-1440, false)]
    public void Top_NegativeCoordinates(int workTop, bool expected)
    {
        Assert.Equal(expected, AppBarReservation.IsReserved(AppBarEdge.Top, UpperBar, new PixelRect(-447, workTop, 2993, 0)));
    }

    [Theory]
    [InlineData(1504, true)]
    [InlineData(1505, false)]
    [InlineData(1600, false)]
    public void Bottom(int workBottom, bool expected)
    {
        var dock = new PixelRect(0, 1504, 2560, 1600);
        Assert.Equal(expected, AppBarReservation.IsReserved(AppBarEdge.Bottom, dock, new PixelRect(0, 40, 2560, workBottom)));
    }

    [Theory]
    [InlineData(96, true)]
    [InlineData(95, false)]
    public void Left(int workLeft, bool expected)
    {
        var dock = new PixelRect(0, 40, 96, 1600);
        Assert.Equal(expected, AppBarReservation.IsReserved(AppBarEdge.Left, dock, new PixelRect(workLeft, 40, 2560, 1600)));
    }

    [Theory]
    [InlineData(2464, true)]
    [InlineData(2465, false)]
    public void Right(int workRight, bool expected)
    {
        var dock = new PixelRect(2464, 40, 2560, 1600);
        Assert.Equal(expected, AppBarReservation.IsReserved(AppBarEdge.Right, dock, new PixelRect(0, 40, workRight, 1600)));
    }

    private static readonly PixelRect Lower = new(0, 40, 2560, 80);

    [Fact]
    public void ShouldMove_SameSlot_IsFalse()
    {
        Assert.False(AppBarReservation.ShouldMove(PrimaryBar, PrimaryBar, PrimaryBar));
    }

    [Fact]
    public void ShouldMove_ShellOffersAnotherSlot_IsTrue()
    {
        // Another top bar arrived above ours: the shell now offers the strip below it.
        Assert.True(AppBarReservation.ShouldMove(Lower, PrimaryBar, PrimaryBar));
    }

    [Fact]
    public void ShouldMove_ShellAdjustedOurRequest_IsFalse()
    {
        // We asked for Lower, SETPOS granted PrimaryBar; QUERYPOS answering Lower again is our own request.
        Assert.False(AppBarReservation.ShouldMove(Lower, PrimaryBar, Lower));
    }

    [Fact]
    public void Recovery_ReservedStrip_DoesNothing()
    {
        var recovery = new StripRecovery();

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.None), recovery.Update(reserved: true, shrinkAllowed: true, 1000));
        Assert.False(recovery.IsMissing);
    }

    [Fact]
    public void Recovery_MissingStrip_WaitsBeforeActing()
    {
        // Explorer applies a strip within about 0.3 s in the steady state, so acting at once would only race it.
        var recovery = new StripRecovery();

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Wait, 1500), recovery.Update(reserved: false, shrinkAllowed: true, 0));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Wait, 1500), recovery.Update(reserved: false, shrinkAllowed: true, 1499));
        Assert.True(recovery.IsMissing);
    }

    [Fact]
    public void Recovery_ExplorerAppliesTheStripBeforeTheFirstAction_NeverActs()
    {
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, shrinkAllowed: true, 0);

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.None), recovery.Update(reserved: true, shrinkAllowed: true, 1499));
        Assert.False(recovery.IsMissing);
    }

    [Fact]
    public void Recovery_StillMissing_SetsTheWorkAreaThreeTimes_ThenGivesUp()
    {
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, shrinkAllowed: true, 0);

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Shrink, 6500), recovery.Update(reserved: false, shrinkAllowed: true, 1500));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Wait, 6500), recovery.Update(reserved: false, shrinkAllowed: true, 6499));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Shrink, 26500), recovery.Update(reserved: false, shrinkAllowed: true, 6500));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Shrink, 46500), recovery.Update(reserved: false, shrinkAllowed: true, 26500));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.GiveUp, 346_500), recovery.Update(reserved: false, shrinkAllowed: true, 46500));
        // Long after the cool-down ended: the check starts a fresh episode instead of giving up forever (KI-102).
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Wait, 10_001_500), recovery.Update(reserved: false, shrinkAllowed: true, 10_000_000));
    }

    [Fact]
    public void Recovery_ShrinkNotAllowed_RegistersAgainInstead()
    {
        // Safe mode changes no system state, so re-registering is the only tool it has.
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, shrinkAllowed: false, 0);

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Reregister, 6500), recovery.Update(reserved: false, shrinkAllowed: false, 1500));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Reregister, 26500), recovery.Update(reserved: false, shrinkAllowed: false, 6500));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Reregister, 46500), recovery.Update(reserved: false, shrinkAllowed: false, 26500));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.GiveUp, 346_500), recovery.Update(reserved: false, shrinkAllowed: false, 46500));
    }

    [Fact]
    public void Recovery_FrequentChecks_NeverActMoreOftenThanTheSchedule()
    {
        // Notifications and display passes can arrive many times a second; only the schedule decides.
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, shrinkAllowed: true, 0);

        Assert.Equal(StripRecoveryKind.Shrink, recovery.Update(reserved: false, shrinkAllowed: true, 1500).Kind);
        Assert.Equal(StripRecoveryKind.Wait, recovery.Update(reserved: false, shrinkAllowed: true, 1501).Kind);
        Assert.Equal(StripRecoveryKind.Wait, recovery.Update(reserved: false, shrinkAllowed: true, 6000).Kind);
    }

    [Fact]
    public void Recovery_StripReservedAgain_StartsAfreshNextTime()
    {
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, shrinkAllowed: true, 0);
        recovery.Update(reserved: false, shrinkAllowed: true, 1500);
        recovery.Update(reserved: true, shrinkAllowed: true, 2000);

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Wait, 101_500), recovery.Update(reserved: false, shrinkAllowed: true, 100_000));
    }

    [Fact]
    public void Recovery_Reset_ForgetsTheAttempts()
    {
        // Used up every attempt, then undocked (Reset) and docked again: the new strip gets every attempt again.
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, shrinkAllowed: true, 0);
        recovery.Update(reserved: false, shrinkAllowed: true, 1500);
        recovery.Update(reserved: false, shrinkAllowed: true, 6500);
        recovery.Update(reserved: false, shrinkAllowed: true, 26500);
        Assert.Equal(StripRecoveryKind.GiveUp, recovery.Update(reserved: false, shrinkAllowed: true, 46500).Kind);

        recovery.Reset();

        Assert.False(recovery.IsMissing);
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Wait, 501_500), recovery.Update(reserved: false, shrinkAllowed: true, 500_000));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Shrink, 506_500), recovery.Update(reserved: false, shrinkAllowed: true, 501_500));
    }

    /// <summary>Drives one episode to its first give-up, stamped 46500 + ReArmDelayMs = 346500.</summary>
    private static StripRecovery ExhaustedAt46500()
    {
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, shrinkAllowed: true, 0);
        recovery.Update(reserved: false, shrinkAllowed: true, 1500);
        recovery.Update(reserved: false, shrinkAllowed: true, 6500);
        recovery.Update(reserved: false, shrinkAllowed: true, 26500);
        recovery.Update(reserved: false, shrinkAllowed: true, 46500);
        return recovery;
    }

    [Fact]
    public void Recovery_GiveUp_CarriesTheReArmDueTime()
    {
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, shrinkAllowed: true, 0);
        recovery.Update(reserved: false, shrinkAllowed: true, 1500);
        recovery.Update(reserved: false, shrinkAllowed: true, 6500);
        recovery.Update(reserved: false, shrinkAllowed: true, 26500);

        // Every post-exhaustion give-up carries the stamped deadline (never DueMs = 0): CheckStrip re-points its
        // one-shot timer at it verbatim.
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.GiveUp, 346_500), recovery.Update(reserved: false, shrinkAllowed: true, 46500));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.GiveUp, 346_500), recovery.Update(reserved: false, shrinkAllowed: true, 100_000));
    }

    [Fact]
    public void Recovery_GiveUp_DoesNotSlideTheCoolDown()
    {
        var recovery = ExhaustedAt46500();

        // A forced-pass storm during a fight calls Update repeatedly; re-scheduling may shorten the remaining
        // interval but the deadline itself must never move later.
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.GiveUp, 346_500), recovery.Update(reserved: false, shrinkAllowed: true, 200_000));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.GiveUp, 346_500), recovery.Update(reserved: false, shrinkAllowed: true, 346_499));
    }

    [Fact]
    public void Recovery_AfterTheCoolDown_StartsAFreshEpisode()
    {
        var recovery = ExhaustedAt46500();

        // The KI-102(b) regression: at the deadline the next check starts a fresh, fully bounded episode instead
        // of giving up for the rest of the run.
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Wait, 348_000), recovery.Update(reserved: false, shrinkAllowed: true, 346_500));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Shrink, 353_000), recovery.Update(reserved: false, shrinkAllowed: true, 348_000));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Shrink, 373_000), recovery.Update(reserved: false, shrinkAllowed: true, 353_000));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Shrink, 393_000), recovery.Update(reserved: false, shrinkAllowed: true, 373_000));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.GiveUp, 693_000), recovery.Update(reserved: false, shrinkAllowed: true, 393_000));
    }

    [Fact]
    public void Recovery_BeforeTheCoolDownEnds_StillGivesUp()
    {
        var recovery = ExhaustedAt46500();

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.GiveUp, 346_500), recovery.Update(reserved: false, shrinkAllowed: true, 346_499));
    }

    [Fact]
    public void Recovery_ReservedDuringTheCoolDown_EndsItAtOnce()
    {
        var recovery = ExhaustedAt46500();

        // Explorer applied the strip during the cool-down: it ends at once.
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.None), recovery.Update(reserved: true, shrinkAllowed: true, 50_000));

        // And it took the deadline with it: the next give-up stamps a new one from the new time, not the stale one.
        recovery.Update(reserved: false, shrinkAllowed: true, 1_000_000);
        recovery.Update(reserved: false, shrinkAllowed: true, 1_001_500);
        recovery.Update(reserved: false, shrinkAllowed: true, 1_006_500);
        recovery.Update(reserved: false, shrinkAllowed: true, 1_026_500);
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.GiveUp, 1_346_500), recovery.Update(reserved: false, shrinkAllowed: true, 1_046_500));
    }

    [Fact]
    public void Recovery_Reset_ClearsTheCoolDown()
    {
        var recovery = ExhaustedAt46500();

        // The bar was undocked during the cool-down.
        recovery.Reset();

        // Docked afresh much later: the new give-up stamps a new deadline from the new time, not the stale one.
        recovery.Update(reserved: false, shrinkAllowed: true, 1_000_000);
        recovery.Update(reserved: false, shrinkAllowed: true, 1_001_500);
        recovery.Update(reserved: false, shrinkAllowed: true, 1_006_500);
        recovery.Update(reserved: false, shrinkAllowed: true, 1_026_500);
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.GiveUp, 1_346_500), recovery.Update(reserved: false, shrinkAllowed: true, 1_046_500));
    }

    [Fact]
    public void EmptyStrip_IsAlwaysReserved()
    {
        // Zero height, but its bottom edge lies below the work area's top.
        Assert.True(AppBarReservation.IsReserved(AppBarEdge.Top, new PixelRect(0, 40, 2560, 40), new PixelRect(0, 0, 2560, 1600)));
    }
}
