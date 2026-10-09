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

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.None), recovery.Update(reserved: true, 1000));
        Assert.False(recovery.IsMissing);
    }

    [Fact]
    public void Recovery_MissingStrip_WaitsForExplorerFirst()
    {
        // The third live run: Explorer applied the strips ~35 s after the taskbar went auto-hide, on its own.
        var recovery = new StripRecovery();

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Wait, 46_000), recovery.Update(reserved: false, 1000));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Wait, 46_000), recovery.Update(reserved: false, 45_999));
        Assert.True(recovery.IsMissing);
    }

    [Fact]
    public void Recovery_ExplorerAppliesTheStripDuringTheGrace_NeverRegistersAgain()
    {
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, 1000);

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.None), recovery.Update(reserved: true, 36_000));
        Assert.False(recovery.IsMissing);
    }

    [Fact]
    public void Recovery_StillMissing_RegistersAgainWithADoublingBackoff_ThenGivesUp()
    {
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, 0);

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Reregister, 105_000), recovery.Update(reserved: false, 45_000));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Wait, 105_000), recovery.Update(reserved: false, 104_999));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Reregister, 225_000), recovery.Update(reserved: false, 105_000));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Reregister, 465_000), recovery.Update(reserved: false, 225_000));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.GiveUp), recovery.Update(reserved: false, 465_000));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.GiveUp), recovery.Update(reserved: false, 10_000_000));
    }

    [Fact]
    public void Recovery_FrequentChecks_NeverRegisterMoreOftenThanTheBackoff()
    {
        // Notifications and display passes can arrive many times a second; only the schedule decides.
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, 0);

        Assert.Equal(StripRecoveryKind.Reregister, recovery.Update(reserved: false, 45_000).Kind);
        Assert.Equal(StripRecoveryKind.Wait, recovery.Update(reserved: false, 45_001).Kind);
        Assert.Equal(StripRecoveryKind.Wait, recovery.Update(reserved: false, 60_000).Kind);
    }

    [Fact]
    public void Recovery_StripReservedAgain_StartsAfreshNextTime()
    {
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, 0);
        recovery.Update(reserved: false, 45_000);
        recovery.Update(reserved: true, 50_000);

        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Wait, 145_000), recovery.Update(reserved: false, 100_000));
    }

    [Fact]
    public void Recovery_Reset_ForgetsTheAttempts()
    {
        // Used up every attempt, then undocked (Reset) and docked again: the new strip gets every attempt again.
        var recovery = new StripRecovery();
        recovery.Update(reserved: false, 0);
        recovery.Update(reserved: false, 45_000);
        recovery.Update(reserved: false, 105_000);
        recovery.Update(reserved: false, 225_000);
        Assert.Equal(StripRecoveryKind.GiveUp, recovery.Update(reserved: false, 465_000).Kind);

        recovery.Reset();

        Assert.False(recovery.IsMissing);
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Wait, 545_000), recovery.Update(reserved: false, 500_000));
        Assert.Equal(new StripRecoveryStep(StripRecoveryKind.Reregister, 605_000), recovery.Update(reserved: false, 545_000));
    }

    [Fact]
    public void EmptyStrip_IsAlwaysReserved()
    {
        // Zero height, but its bottom edge lies below the work area's top.
        Assert.True(AppBarReservation.IsReserved(AppBarEdge.Top, new PixelRect(0, 40, 2560, 40), new PixelRect(0, 0, 2560, 1600)));
    }
}
