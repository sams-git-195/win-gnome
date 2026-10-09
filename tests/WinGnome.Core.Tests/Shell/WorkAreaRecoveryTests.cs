using WinGnome.Core.Geometry;
using WinGnome.Core.Monitors;
using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class WorkAreaRecoveryTests
{
    private const string Display1 = @"\\.\DISPLAY1";
    private const string Display2 = @"\\.\DISPLAY2";

    private static readonly PixelRect PrimaryBounds = new(0, 0, 2560, 1600);
    private static readonly PixelRect UpperBounds = new(-447, -1440, 2993, 0);

    // The primary with our top bar's strip applied, and without it.
    private static readonly PixelRect Shrunk = new(0, 40, 2560, 1600);
    private static readonly PixelRect Full = new(0, 0, 2560, 1600);

    private static MonitorInfo Mon(string key, PixelRect bounds, PixelRect work, bool primary = false) =>
        new(key, bounds, work, 120, primary);

    private static MonitorLayout Layout(params MonitorInfo[] monitors) => MonitorLayout.Create(monitors);

    private static HashSet<long> Released(params long[] owners) => [.. owners];

    [Fact]
    public void Plan_NoRecords_DoesNothing()
    {
        var plan = WorkAreaRecovery.Plan([], Layout(Mon(Display1, PrimaryBounds, Full, primary: true)), Released());

        Assert.Empty(plan.Restores);
        Assert.Empty(plan.Keep);
        Assert.False(plan.Nudge);
    }

    [Fact]
    public void Plan_ReleasedRecordStillApplied_RestoresTheOriginal()
    {
        var record = new WorkAreaRecord(1, Display1, PrimaryBounds, Full, Shrunk);

        var plan = WorkAreaRecovery.Plan([record], Layout(Mon(Display1, PrimaryBounds, Shrunk, primary: true)), Released(1));

        Assert.Equal([new WorkAreaRestore(Display1, Full)], plan.Restores);
        Assert.Empty(plan.Keep);
        Assert.False(plan.Nudge);
    }

    [Fact]
    public void Plan_RecordOfALiveBar_KeepsItAndWritesNothing()
    {
        var record = new WorkAreaRecord(1, Display1, PrimaryBounds, Full, Shrunk);

        var plan = WorkAreaRecovery.Plan([record], Layout(Mon(Display1, PrimaryBounds, Shrunk, primary: true)), Released());

        Assert.Empty(plan.Restores);
        Assert.Equal([record], plan.Keep);
        Assert.False(plan.Nudge);
    }

    [Fact]
    public void Plan_TwoStackedRecords_UnwindNewestFirst()
    {
        // A top bar shrank 0..40 away, then a bottom dock shrank the work area that was left.
        var bar = new WorkAreaRecord(1, Display1, PrimaryBounds, Full, Shrunk);
        var docked = new PixelRect(0, 40, 2560, 1504);
        var dock = new WorkAreaRecord(2, Display1, PrimaryBounds, Shrunk, docked);

        var plan = WorkAreaRecovery.Plan([bar, dock], Layout(Mon(Display1, PrimaryBounds, docked, primary: true)), Released(1, 2));

        Assert.Equal([new WorkAreaRestore(Display1, Shrunk), new WorkAreaRestore(Display1, Full)], plan.Restores);
        Assert.Empty(plan.Keep);
        Assert.False(plan.Nudge);
    }

    [Fact]
    public void Plan_OnlyTheOlderRecordReleased_KeepsBoth()
    {
        // The dock is still docked, so the bar's strip cannot be given back without taking the dock's with it.
        var bar = new WorkAreaRecord(1, Display1, PrimaryBounds, Full, Shrunk);
        var docked = new PixelRect(0, 40, 2560, 1504);
        var dock = new WorkAreaRecord(2, Display1, PrimaryBounds, Shrunk, docked);

        var plan = WorkAreaRecovery.Plan([bar, dock], Layout(Mon(Display1, PrimaryBounds, docked, primary: true)), Released(1));

        Assert.Empty(plan.Restores);
        Assert.Equal([bar, dock], plan.Keep);
        Assert.False(plan.Nudge);
    }

    [Fact]
    public void Plan_NewerRecordReleasedFirst_StillUnwindsBoth()
    {
        // Release order is not application order: the dock goes first, and the bar's record then matches.
        var bar = new WorkAreaRecord(1, Display1, PrimaryBounds, Full, Shrunk);
        var docked = new PixelRect(0, 40, 2560, 1504);
        var dock = new WorkAreaRecord(2, Display1, PrimaryBounds, Shrunk, docked);
        var afterDock = WorkAreaRecovery.Plan([bar, dock], Layout(Mon(Display1, PrimaryBounds, docked, primary: true)), Released(2));

        Assert.Equal([new WorkAreaRestore(Display1, Shrunk)], afterDock.Restores);
        Assert.Equal([bar], afterDock.Keep);

        var afterBar = WorkAreaRecovery.Plan(afterDock.Keep, Layout(Mon(Display1, PrimaryBounds, Shrunk, primary: true)), Released(1));

        Assert.Equal([new WorkAreaRestore(Display1, Full)], afterBar.Restores);
        Assert.Empty(afterBar.Keep);
    }

    [Fact]
    public void Plan_LiveWorkAreaIsSomeoneElses_DropsTheRecordAndNudges()
    {
        // Another instance stacked a second strip on the same edge: writing our original would take its strip away.
        var record = new WorkAreaRecord(1, Display1, PrimaryBounds, Full, Shrunk);
        var stacked = new PixelRect(0, 80, 2560, 1600);

        var plan = WorkAreaRecovery.Plan([record], Layout(Mon(Display1, PrimaryBounds, stacked, primary: true)), Released(1));

        Assert.Empty(plan.Restores);
        Assert.Empty(plan.Keep);
        Assert.True(plan.Nudge);
    }

    [Fact]
    public void Plan_LiveWorkAreaIsAlreadyTheOriginal_DropsTheRecordWithoutNudging()
    {
        // Explorer gave the strip back on its own after ABM_REMOVE.
        var record = new WorkAreaRecord(1, Display1, PrimaryBounds, Full, Shrunk);

        var plan = WorkAreaRecovery.Plan([record], Layout(Mon(Display1, PrimaryBounds, Full, primary: true)), Released(1));

        Assert.Empty(plan.Restores);
        Assert.Empty(plan.Keep);
        Assert.False(plan.Nudge);
    }

    [Fact]
    public void Plan_IdenticalDuplicateRecords_RestoresOnceAndDropsTheOlder()
    {
        // Markers written before the one-record-per-pair rule (WorkAreaLedger) could hold byte-identical
        // duplicates. Newest first: the newer restores to Original; the older then finds its Original equal to
        // the running value and leaves through the already-given-back branch — one restore, nothing kept.
        var record = new WorkAreaRecord(1, Display1, PrimaryBounds, Full, Shrunk);

        var plan = WorkAreaRecovery.Plan([record, record], Layout(Mon(Display1, PrimaryBounds, Shrunk, primary: true)), Released(1));

        Assert.Equal([new WorkAreaRestore(Display1, Full)], plan.Restores);
        Assert.Empty(plan.Keep);
        Assert.False(plan.Nudge);
    }

    [Fact]
    public void Plan_MonitorIsGone_DropsTheRecord()
    {
        var record = new WorkAreaRecord(1, Display2, UpperBounds, UpperBounds, new PixelRect(-447, -1408, 2993, 0));

        var plan = WorkAreaRecovery.Plan([record], Layout(Mon(Display1, PrimaryBounds, Full, primary: true)), Released(1));

        Assert.Empty(plan.Restores);
        Assert.Empty(plan.Keep);
        Assert.False(plan.Nudge);
    }

    [Fact]
    public void Plan_MonitorBoundsChanged_DropsTheRecordAsStale()
    {
        // A resolution change reset the work area, so the record describes a monitor that no longer exists.
        var record = new WorkAreaRecord(1, Display1, PrimaryBounds, Full, Shrunk);
        var smaller = new PixelRect(0, 0, 1920, 1080);

        var plan = WorkAreaRecovery.Plan([record], Layout(Mon(Display1, smaller, smaller, primary: true)), Released(1));

        Assert.Empty(plan.Restores);
        Assert.Empty(plan.Keep);
        Assert.False(plan.Nudge);
    }

    [Fact]
    public void Plan_NoMonitorsRead_KeepsEveryRecord()
    {
        // A failed read must not throw away the only record of a change we made.
        var record = new WorkAreaRecord(1, Display1, PrimaryBounds, Full, Shrunk);

        var plan = WorkAreaRecovery.Plan([record], MonitorLayout.Empty, Released(1));

        Assert.Empty(plan.Restores);
        Assert.Equal([record], plan.Keep);
        Assert.False(plan.Nudge);
    }

    [Fact]
    public void Plan_TwoMonitors_RestoresBoth()
    {
        var primary = new WorkAreaRecord(1, Display1, PrimaryBounds, Full, Shrunk);
        var upperWork = new PixelRect(-447, -1408, 2993, 0);
        var upper = new WorkAreaRecord(2, Display2, UpperBounds, UpperBounds, upperWork);

        var plan = WorkAreaRecovery.Plan(
            [primary, upper],
            Layout(Mon(Display1, PrimaryBounds, Shrunk, primary: true), Mon(Display2, UpperBounds, upperWork)),
            Released(1, 2));

        Assert.Equal([new WorkAreaRestore(Display1, Full), new WorkAreaRestore(Display2, UpperBounds)], plan.Restores);
        Assert.Empty(plan.Keep);
        Assert.False(plan.Nudge);
    }

    [Fact]
    public void Plan_KeyCaseDiffers_StillMatchesTheMonitor()
    {
        var record = new WorkAreaRecord(1, Display1.ToLowerInvariant(), PrimaryBounds, Full, Shrunk);

        var plan = WorkAreaRecovery.Plan([record], Layout(Mon(Display1, PrimaryBounds, Shrunk, primary: true)), Released(1));

        Assert.Equal([new WorkAreaRestore(Display1.ToLowerInvariant(), Full)], plan.Restores);
    }

    [Fact]
    public void Plan_BlockedChainOnOneMonitor_LeavesTheOtherAlone()
    {
        var bar = new WorkAreaRecord(1, Display1, PrimaryBounds, Full, Shrunk);
        var upperWork = new PixelRect(-447, -1408, 2993, 0);
        var upper = new WorkAreaRecord(2, Display2, UpperBounds, UpperBounds, upperWork);

        // The primary's bar is still live; the upper monitor's bar has gone.
        var plan = WorkAreaRecovery.Plan(
            [bar, upper],
            Layout(Mon(Display1, PrimaryBounds, Shrunk, primary: true), Mon(Display2, UpperBounds, upperWork)),
            Released(2));

        Assert.Equal([new WorkAreaRestore(Display2, UpperBounds)], plan.Restores);
        Assert.Equal([bar], plan.Keep);
        Assert.False(plan.Nudge);
    }

    [Fact]
    public void RepairAll_TaskbarHidden_ResetsEveryMonitorThatIsNotFull()
    {
        var repairs = WorkAreaRecovery.RepairAll(
            Layout(Mon(Display1, PrimaryBounds, Shrunk, primary: true), Mon(Display2, UpperBounds, UpperBounds)),
            taskbarHidden: true);

        // Only DISPLAY1: DISPLAY2's work area already is its full bounds.
        Assert.Equal([new WorkAreaRestore(Display1, PrimaryBounds)], repairs);
    }

    [Fact]
    public void RepairAll_TaskbarVisible_ResetsNothing()
    {
        // A visible taskbar's own strip must survive a repair that cannot know what it changed.
        var repairs = WorkAreaRecovery.RepairAll(
            Layout(Mon(Display1, PrimaryBounds, Shrunk, primary: true)),
            taskbarHidden: false);

        Assert.Empty(repairs);
    }

    [Fact]
    public void RepairAll_NoMonitors_ResetsNothing()
    {
        Assert.Empty(WorkAreaRecovery.RepairAll(MonitorLayout.Empty, taskbarHidden: true));
    }
}
