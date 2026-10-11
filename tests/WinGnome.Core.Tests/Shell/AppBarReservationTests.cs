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
    public void Recovery_MissingStrip_ClaimsTheSlotAgain_ThenWaitsBeforeActing()
    {
        // Explorer can lose the rectangle a registered bar claimed; one ABM_SETPOS has it apply the strip again.
        var recovery = new StripRecovery();

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Claim, 1500), recovery.Update(reserved: false, shrinkAllowed: true, 0));
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
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Claim, 10_001_500), recovery.Update(reserved: false, shrinkAllowed: true, 10_000_000));
    }

    [Fact]
    public void Recovery_ShrinkNotAllowed_RegistersAgainInstead()
    {
        // Safe mode changes no system state, so claiming the slot and re-registering are the only tools it has.
        var recovery = new StripRecovery();
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Claim, 1500), recovery.Update(reserved: false, shrinkAllowed: false, 0));

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
    public void Recovery_ChecksWhileMissing_ClaimOnlyOncePerEpisode()
    {
        // Our SETPOS makes Explorer notify every AppBar, ours included; the check that notification runs must not
        // claim again, or bars answering each other's notifications would loop.
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, shrinkAllowed: true, 0);

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Wait, 1500), recovery.Update(reserved: false, shrinkAllowed: true, 1));
    }

    [Theory]
    [InlineData(29_999, StripRecoveryKind.Wait)]  // A new episode one millisecond inside the interval only waits.
    [InlineData(30_000, StripRecoveryKind.Claim)] // Exactly the interval after the last claim: claims again.
    public void Recovery_StripLostAgainSoonAfterAClaim_DoesNotClaimAgain(long nowMs, StripRecoveryKind expected)
    {
        // Two bars whose claims each make Explorer drop the other's strip must not claim in turn for ever.
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, shrinkAllowed: true, 0);
        recovery.Update(reserved: true, shrinkAllowed: true, 500);

        Assert.Equal(new StripRecoveryStep(expected, nowMs + 1500), recovery.Update(reserved: false, shrinkAllowed: true, nowMs));
    }

    [Fact]
    public void Recovery_StripLostAgainSoonAfterAClaim_StillSetsTheWorkAreaOnSchedule()
    {
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, shrinkAllowed: true, 0);
        recovery.Update(reserved: true, shrinkAllowed: true, 500);
        recovery.Update(reserved: false, shrinkAllowed: true, 10_000);

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Shrink, 16_500), recovery.Update(reserved: false, shrinkAllowed: true, 11_500));
    }

    [Fact]
    public void Recovery_StripReservedAgain_StartsAfreshNextTime()
    {
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, shrinkAllowed: true, 0);
        recovery.Update(reserved: false, shrinkAllowed: true, 1500);
        recovery.Update(reserved: true, shrinkAllowed: true, 2000);

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Claim, 101_500), recovery.Update(reserved: false, shrinkAllowed: true, 100_000));
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
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Claim, 501_500), recovery.Update(reserved: false, shrinkAllowed: true, 500_000));
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
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Claim, 348_000), recovery.Update(reserved: false, shrinkAllowed: true, 346_500));
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

    [Theory]
    [InlineData(AppBarEdge.Top, 0, 0, 2560, 40, 0, 0, 2560, 41)]
    [InlineData(AppBarEdge.Bottom, 0, 1540, 2560, 1600, 0, 1539, 2560, 1600)]
    [InlineData(AppBarEdge.Left, 0, 0, 60, 1600, 0, 0, 61, 1600)]
    [InlineData(AppBarEdge.Right, 2500, 0, 2560, 1600, 2499, 0, 2560, 1600)]
    public void OnePixelThicker_GrowsAwayFromTheEdge(AppBarEdge edge, int l, int t, int r, int b, int el, int et, int er, int eb)
    {
        Assert.Equal(new PixelRect(el, et, er, eb), AppBarReservation.OnePixelThicker(edge, new PixelRect(l, t, r, b)));
    }

    [Fact]
    public void OnePixelThicker_MonitorAboveThePrimary()
    {
        Assert.Equal(new PixelRect(-447, -1440, 2993, -1407),
            AppBarReservation.OnePixelThicker(AppBarEdge.Top, new PixelRect(-447, -1440, 2993, -1408)));
    }

    [Theory]
    [InlineData(AppBarEdge.Top, 0, 0, 2560, 40, 0, 80, 2560, 1600, true)]      // KI-114: reserved twice.
    [InlineData(AppBarEdge.Top, 0, 0, 2560, 40, 0, 40, 2560, 1600, false)]     // Exactly the strip.
    [InlineData(AppBarEdge.Top, 0, 0, 2560, 40, 0, 41, 2560, 1600, true)]      // One pixel more.
    [InlineData(AppBarEdge.Top, 0, 0, 2560, 40, 0, 0, 2560, 1600, false)]      // Missing is not over-reserved.
    [InlineData(AppBarEdge.Bottom, 0, 1530, 2560, 1600, 0, 40, 2560, 1490, true)]
    [InlineData(AppBarEdge.Bottom, 0, 1530, 2560, 1600, 0, 40, 2560, 1530, false)]
    [InlineData(AppBarEdge.Left, 0, 0, 60, 1600, 61, 0, 2560, 1600, true)]
    [InlineData(AppBarEdge.Right, 2500, 0, 2560, 1600, 0, 0, 2499, 1600, true)]
    [InlineData(AppBarEdge.Top, 0, 40, 2560, 40, 0, 80, 2560, 1600, false)]    // An empty strip never is.
    public void IsOverReserved(AppBarEdge edge, int sl, int st, int sr, int sb, int wl, int wt, int wr, int wb, bool expected)
    {
        Assert.Equal(expected, AppBarReservation.IsOverReserved(edge, new PixelRect(sl, st, sr, sb), new PixelRect(wl, wt, wr, wb)));
    }

    private static readonly PixelRect Doubled = new(0, 80, 2560, 1600);

    [Fact]
    public void OverReservation_FirstSighting_Claims()
    {
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Claim, 101_500), new StripRecovery().UpdateOverReservation(Doubled, 100_000));
    }

    [Fact]
    public void OverReservation_StillThereAfterTheClaim_CorrectsOnce()
    {
        var recovery = new StripRecovery();
        recovery.UpdateOverReservation(Doubled, 100_000);

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Wait, 101_500), recovery.UpdateOverReservation(Doubled, 101_499));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Correct), recovery.UpdateOverReservation(Doubled, 101_500));
        recovery.SettleOverReservation();
        // A legitimately stacked AppBar keeps the edge over-reserved: one claim and one look, not one every check.
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.None), recovery.UpdateOverReservation(Doubled, 1_000_000));
    }

    [Fact]
    public void OverReservation_CorrectionNotSettled_IsTriedAgain()
    {
        // The budget refused the write: the next check corrects again instead of leaving the doubled band for good.
        var recovery = new StripRecovery();
        recovery.UpdateOverReservation(Doubled, 100_000);
        recovery.UpdateOverReservation(Doubled, 101_500);

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Correct), recovery.UpdateOverReservation(Doubled, 106_500));
    }

    [Fact]
    public void OverReservation_GoneAndBack_StartsAgain()
    {
        var recovery = new StripRecovery();
        recovery.UpdateOverReservation(Doubled, 100_000);
        recovery.UpdateOverReservation(Doubled, 101_500);
        recovery.SettleOverReservation();
        recovery.UpdateOverReservation(null, 200_000);

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Claim, 301_500), recovery.UpdateOverReservation(Doubled, 300_000));
    }

    [Fact]
    public void OverReservation_DifferentValue_StartsAgain()
    {
        var recovery = new StripRecovery();
        recovery.UpdateOverReservation(Doubled, 100_000);
        recovery.UpdateOverReservation(Doubled, 101_500);
        recovery.SettleOverReservation();

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Claim, 201_500), recovery.UpdateOverReservation(new PixelRect(0, 81, 2560, 1600), 200_000));
    }

    [Theory]
    [InlineData(29_999, StripRecoveryKind.Wait)]
    [InlineData(30_000, StripRecoveryKind.Claim)]
    public void OverReservation_SharesTheClaimInterval(long nowMs, StripRecoveryKind expected)
    {
        // A missing-strip claim at 0 counts against an over-reservation claim.
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, shrinkAllowed: true, 0);

        Assert.Equal(new StripRecoveryStep(expected, nowMs + 1500), recovery.UpdateOverReservation(Doubled, nowMs));
    }

    [Fact]
    public void OverReservation_ClaimSkippedForTheInterval_StillCorrects()
    {
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, shrinkAllowed: true, 0);
        recovery.UpdateOverReservation(Doubled, 10_000);

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Correct), recovery.UpdateOverReservation(Doubled, 11_500));
    }

    [Fact]
    public void OverReservation_Claim_DelaysTheNextMissingStripClaim()
    {
        var recovery = new StripRecovery();
        recovery.UpdateOverReservation(Doubled, 0);

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Wait, 11_500), recovery.Update(reserved: false, shrinkAllowed: true, 10_000));
    }

    [Theory]
    [InlineData(AppBarEdge.Top, 0, 0, 2560, 40, 0, 80, 2560, 1530, 0, 40, 2560, 80)]
    [InlineData(AppBarEdge.Bottom, 0, 1530, 2560, 1600, 0, 40, 2560, 1490, 0, 1490, 2560, 1530)]
    [InlineData(AppBarEdge.Left, 0, 0, 60, 1600, 120, 0, 2560, 1600, 60, 0, 120, 1600)]
    [InlineData(AppBarEdge.Right, 2500, 0, 2560, 1600, 0, 0, 2440, 1600, 2440, 0, 2500, 1600)]
    public void OverReservedGap_IsTheBandBeyondTheStrip(AppBarEdge edge, int sl, int st, int sr, int sb, int wl, int wt, int wr, int wb, int gl, int gt, int gr, int gb)
    {
        Assert.Equal(new PixelRect(gl, gt, gr, gb), AppBarReservation.OverReservedGap(edge, new PixelRect(sl, st, sr, sb), new PixelRect(wl, wt, wr, wb)));
    }

    [Theory]
    [InlineData(0, 40, 2560, 80, true)]       // Another 40 px top AppBar exactly in the band.
    [InlineData(-8, 32, 2568, 88, true)]      // The same with an 8 px frame.
    [InlineData(-10, -10, 2570, 1610, false)] // A window maximised before the work area shrank (the live false positive).
    [InlineData(0, 40, 2000, 80, false)]      // Does not span the band.
    [InlineData(0, 41, 2560, 80, false)]      // Misses its first row.
    [InlineData(0, 0, 2560, 120, true)]       // At the slack on both sides.
    [InlineData(0, -1, 2560, 120, false)]     // One past it.
    public void IsBandOccupant_TopBand(int l, int t, int r, int b, bool expected)
    {
        Assert.Equal(expected, AppBarReservation.IsBandOccupant(AppBarEdge.Top, new PixelRect(0, 40, 2560, 80), new PixelRect(l, t, r, b)));
    }

    [Theory]
    [InlineData(60, 0, 120, 1600, true)]
    [InlineData(-10, -10, 2570, 1610, false)]
    public void IsBandOccupant_LeftBand(int l, int t, int r, int b, bool expected)
    {
        Assert.Equal(expected, AppBarReservation.IsBandOccupant(AppBarEdge.Left, new PixelRect(60, 0, 120, 1600), new PixelRect(l, t, r, b)));
    }

    [Theory]
    [InlineData(AppBarEdge.Top, 0, 0, 2560, 40, 0, 40, 2560, 1530)]
    [InlineData(AppBarEdge.Bottom, 0, 1500, 2560, 1600, 0, 80, 2560, 1500)]
    [InlineData(AppBarEdge.Left, 0, 0, 60, 1600, 60, 80, 2560, 1530)]
    [InlineData(AppBarEdge.Right, 2500, 0, 2560, 1600, 0, 80, 2500, 1530)]
    public void FittedToStrip_MovesOnlyThatEdge(AppBarEdge edge, int sl, int st, int sr, int sb, int l, int t, int r, int b)
    {
        Assert.Equal(new PixelRect(l, t, r, b), AppBarReservation.FittedToStrip(edge, new PixelRect(sl, st, sr, sb), new PixelRect(0, 80, 2560, 1530)));
    }

    [Theory]
    [InlineData(AppBarEdge.Top, 0, 0, 2560, 40, 60, 0, 2500, 1530)]
    [InlineData(AppBarEdge.Bottom, 0, 1530, 2560, 1600, 60, 40, 2500, 1600)]
    [InlineData(AppBarEdge.Left, 0, 0, 60, 1600, 0, 40, 2500, 1530)]
    [InlineData(AppBarEdge.Right, 2500, 0, 2560, 1600, 60, 40, 2560, 1530)]
    // A dock above a visible taskbar (1552-1600): the taskbar's strip stays out of the value given back.
    [InlineData(AppBarEdge.Bottom, 0, 1472, 2560, 1552, 60, 40, 2500, 1552)]
    public void WithoutStrip_MovesOnlyThatEdgeOutToTheStripsOuterEdge(AppBarEdge edge, int sl, int st, int sr, int sb, int l, int t, int r, int b)
    {
        Assert.Equal(new PixelRect(l, t, r, b), AppBarReservation.WithoutStrip(edge, new PixelRect(sl, st, sr, sb), new PixelRect(60, 40, 2500, 1530)));
    }

    [Fact]
    public void EmptyStrip_IsAlwaysReserved()
    {
        // Zero height, but its bottom edge lies below the work area's top.
        Assert.True(AppBarReservation.IsReserved(AppBarEdge.Top, new PixelRect(0, 40, 2560, 40), new PixelRect(0, 0, 2560, 1600)));
    }
}
