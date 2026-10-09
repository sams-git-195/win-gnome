using WinGnome.Core.Geometry;
using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class WorkAreaLedgerTests
{
    private const string Display1 = @"\\.\DISPLAY1";
    private const string Display2 = @"\\.\DISPLAY2";

    private static readonly PixelRect Full = new(0, 0, 2560, 1600);

    // The primary after the top bar's shrink, and after the bottom dock's shrink stacked on it.
    private static readonly PixelRect BarApplied = new(0, 40, 2560, 1600);
    private static readonly PixelRect DockApplied = new(0, 40, 2560, 1504);

    // A fresh read in which the bar's strip was lost but the dock's survived, and the bar's re-shrink from it.
    private static readonly PixelRect BarMissing = new(0, 0, 2560, 1504);
    private static readonly PixelRect BarReshrunk = new(0, 40, 2560, 1504);

    private static WorkAreaRecord Rec(long owner, string key, PixelRect original, PixelRect applied) =>
        new(owner, key, Full, original, applied);

    [Fact]
    public void Add_EmptyList_AppendsTheRecord()
    {
        var bar = Rec(1, Display1, Full, BarApplied);

        Assert.Equal([bar], WorkAreaLedger.Add([], bar));
    }

    [Fact]
    public void Add_NoRecordForThePair_AppendsAtTheEnd()
    {
        var primary = Rec(1, Display1, Full, BarApplied);
        var upper = Rec(2, Display2, Full, BarApplied);

        Assert.Equal([primary, upper], WorkAreaLedger.Add([primary], upper));
    }

    [Fact]
    public void Add_SameOwnerAndMonitor_ReplacesTheOldRecordAndMovesToTheEnd()
    {
        var old = Rec(1, Display1, Full, BarApplied);
        var other = Rec(2, Display2, Full, BarApplied);
        var reshrunk = Rec(1, Display1, BarMissing, BarReshrunk);

        Assert.Equal([other, reshrunk], WorkAreaLedger.Add([old, other], reshrunk));
    }

    [Fact]
    public void Add_TwoIdenticalRecordsForThePair_OnlyTheNewOneSurvives()
    {
        // A marker written by a build before the ledger rule can be recovered holding duplicates; one Add must
        // clear every record of the pair, not just the first match, or the marker never stops accumulating.
        var old = Rec(1, Display1, Full, BarApplied);
        var reshrunk = Rec(1, Display1, BarMissing, BarReshrunk);

        Assert.Equal([reshrunk], WorkAreaLedger.Add([old, old], reshrunk));
    }

    [Fact]
    public void Add_SameOwnerOnAnotherMonitor_KeepsBoth()
    {
        // A bar re-docked onto a different monitor after a display change: one record per monitor, not per bar.
        var onPrimary = Rec(1, Display1, Full, BarApplied);
        var onUpper = Rec(1, Display2, Full, BarApplied);

        Assert.Equal([onPrimary, onUpper], WorkAreaLedger.Add([onPrimary], onUpper));
    }

    [Fact]
    public void Add_TwoBarsOnOneMonitor_KeepsBoth()
    {
        // The top bar's and the dock's strips stack; both records must survive to unwind.
        var bar = Rec(1, Display1, Full, BarApplied);
        var dock = Rec(2, Display1, BarApplied, DockApplied);

        Assert.Equal([bar, dock], WorkAreaLedger.Add([bar], dock));
    }

    [Fact]
    public void Add_MonitorKeyCaseDiffers_StillReplacesAndMovesToTheEnd()
    {
        var lower = Rec(1, Display1.ToLowerInvariant(), Full, BarApplied);
        var dock = Rec(2, Display1, BarApplied, DockApplied);
        var upper = Rec(1, Display1, BarMissing, BarReshrunk);

        Assert.Equal([dock, upper], WorkAreaLedger.Add([lower, dock], upper));
    }

    [Fact]
    public void Add_BarReshrinksAfterTheDock_TheDockStaysFirstAndTheBarMovesToTheEnd()
    {
        // The unwind order this exists for: the bar shrank first, the dock stacked on it, then the bar's strip was
        // lost and it shrank again from a read that still left the dock's strip out. The list must end [dock, bar],
        // so releasing both restores the bar first (to a work area that still holds the dock's strip) and the dock
        // after; replacing in place would leave [bar, dock] and the bar's record would no longer match the live
        // value, stranding its strip until Explorer recomputed.
        var bar = Rec(1, Display1, Full, BarApplied);
        var dock = Rec(2, Display1, BarApplied, DockApplied);
        var reshrunk = Rec(1, Display1, BarMissing, BarReshrunk);

        Assert.Equal([dock, reshrunk], WorkAreaLedger.Add([bar, dock], reshrunk));
    }

    [Fact]
    public void Add_LeavesTheInputListAlone()
    {
        // The controller rolls a failed shrink back to the list it passed in; Add must not have touched it.
        var bar = Rec(1, Display1, Full, BarApplied);
        var input = new List<WorkAreaRecord> { bar };

        WorkAreaLedger.Add(input, Rec(1, Display1, BarMissing, BarReshrunk));

        Assert.Equal([bar], input);
    }
}
