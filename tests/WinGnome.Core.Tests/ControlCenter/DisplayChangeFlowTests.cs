using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class DisplayChangeFlowTests
{
    private static readonly DisplaySetting A = new(@"\\.\DISPLAY1", 1920, 1080, 60, 0, 0, IsPrimary: true);
    private static readonly DisplaySetting B = new(@"\\.\DISPLAY2", 2560, 1440, 144, 1920, 0, IsPrimary: false);
    private static readonly DisplaySetting ASmaller = A with { Width = 1280, Height = 720 };
    private static readonly DisplaySetting BMoved = B with { X = 1280 };

    private static readonly IReadOnlyList<DisplaySetting> Original = [A, B];
    private static readonly IReadOnlyList<DisplaySetting> Target = [ASmaller, BMoved];

    /// <summary>A scripted display system that logs every call in order.</summary>
    private sealed class FakeSystem
    {
        public List<string> Calls { get; } = [];

        public IReadOnlyList<DisplaySetting> Current { get; set; } = Original;

        public bool TestResult { get; set; } = true;

        public bool ApplyResult { get; set; } = true;

        public bool RevertResult { get; set; } = true;

        public bool PersistResult { get; set; } = true;

        public bool WriteResult { get; set; } = true;

        /// <summary>What a successful apply really shows; the target when null.</summary>
        public IReadOnlyList<DisplaySetting>? Shows { get; set; }

        public DisplayRevert? Written { get; private set; }

        public DisplayChangeFlow Flow() => new(
            () => { Calls.Add("read"); return Current; },
            target => { Calls.Add("test"); return TestResult; },
            target =>
            {
                Calls.Add("apply");
                if (ApplyResult)
                {
                    Current = Shows ?? target;
                }

                return ApplyResult;
            },
            original =>
            {
                Calls.Add("revert");
                if (RevertResult)
                {
                    Current = original;
                }

                return RevertResult;
            },
            target => { Calls.Add("persist"); return PersistResult; },
            revert => { Calls.Add("write"); Written = revert; return WriteResult; },
            () => Calls.Add("delete"));
    }

    [Fact]
    public void Apply_Success_RecordsBeforeApplyingAndReturnsThePendingChange()
    {
        var system = new FakeSystem();

        var outcome = system.Flow().Apply(Target);

        Assert.Equal(DisplayApplyResult.Applied, outcome.Result);
        Assert.Equal(new DisplayRevert(Original, Target), outcome.Pending);
        Assert.Equal(["read", "test", "write", "apply", "read"], system.Calls);
    }

    [Fact]
    public void Apply_WindowsShowsSomethingElse_RecordsWhatIsShowingAsTheTarget()
    {
        // Windows may adjust a supplied configuration (SDC_ALLOW_CHANGES); Keep must then save what is really showing.
        var shown = new[] { ASmaller with { RefreshHz = 50 }, BMoved };
        var system = new FakeSystem { Shows = shown };

        var outcome = system.Flow().Apply(Target);

        Assert.Equal(DisplayApplyResult.AppliedAdjusted, outcome.Result);
        Assert.NotNull(outcome.Pending);
        Assert.Equal(Original, outcome.Pending.Original);
        Assert.Equal(shown, outcome.Pending.Target);
        Assert.Same(outcome.Pending, system.Written);
        Assert.Equal(["read", "test", "write", "apply", "read", "write"], system.Calls);
    }

    [Fact]
    public void Apply_AdjustedButTheRecordCannotBeUpdated_Reverts()
    {
        var system = new FakeSystem { Shows = [ASmaller with { RefreshHz = 50 }, BMoved] };
        var writes = 0;
        var flow = new DisplayChangeFlow(
            () => system.Current,
            _ => true,
            target => { system.Current = system.Shows!; return true; },
            original => { system.Current = original; return true; },
            _ => true,
            _ => ++writes == 1,
            () => system.Calls.Add("delete"));

        var outcome = flow.Apply(Target);

        Assert.Equal(DisplayApplyResult.NotRecorded, outcome.Result);
        Assert.Equal(Original, system.Current);
    }

    [Fact]
    public void Apply_DisplaysChangedSinceTheEdit_DoesNothing()
    {
        var system = new FakeSystem { Current = [A] };

        var outcome = system.Flow().Apply(Target);

        Assert.Equal(DisplayApplyResult.DisplaysChanged, outcome.Result);
        Assert.Null(outcome.Pending);
        Assert.Equal(["read"], system.Calls);
    }

    [Fact]
    public void Apply_SameAsCurrent_DoesNothing()
    {
        var system = new FakeSystem();

        Assert.Equal(DisplayApplyResult.NoChange, system.Flow().Apply(Original).Result);
        Assert.Equal(["read"], system.Calls);
    }

    [Fact]
    public void Apply_RejectedByValidation_WritesNothing()
    {
        var system = new FakeSystem { TestResult = false };

        Assert.Equal(DisplayApplyResult.Rejected, system.Flow().Apply(Target).Result);
        Assert.Equal(["read", "test"], system.Calls);
    }

    [Fact]
    public void Apply_RecordCannotBeWritten_DoesNotApply()
    {
        var system = new FakeSystem { WriteResult = false };

        Assert.Equal(DisplayApplyResult.NotRecorded, system.Flow().Apply(Target).Result);
        Assert.Equal(["read", "test", "write"], system.Calls);
    }

    [Fact]
    public void Apply_ApplyFails_RevertsAndDeletesTheRecord()
    {
        var system = new FakeSystem { ApplyResult = false };

        var outcome = system.Flow().Apply(Target);

        Assert.Equal(DisplayApplyResult.FailedAndRestored, outcome.Result);
        Assert.Equal(["read", "test", "write", "apply", "revert", "delete"], system.Calls);
    }

    [Fact]
    public void Apply_ApplyAndRevertFail_KeepsTheRecordForTheNextStart()
    {
        var system = new FakeSystem { ApplyResult = false, RevertResult = false };

        var outcome = system.Flow().Apply(Target);

        Assert.Equal(DisplayApplyResult.FailedNotRestored, outcome.Result);
        Assert.DoesNotContain("delete", system.Calls);
    }

    [Fact]
    public void BeginKeep_StillShowing_DeletesTheRecordBeforeAnyPersist()
    {
        var system = new FakeSystem { Current = Target };
        var flow = system.Flow();

        var persist = flow.BeginKeep(new DisplayRevert(Original, Target));

        Assert.True(persist);
        Assert.Equal(["read", "delete"], system.Calls);
    }

    [Fact]
    public void Keep_PersistsOnlyAfterTheRecordIsGone()
    {
        var system = new FakeSystem { Current = Target };
        var flow = system.Flow();
        var pending = new DisplayRevert(Original, Target);

        if (flow.BeginKeep(pending))
        {
            flow.Persist(pending);
        }

        Assert.Equal(["read", "delete", "persist"], system.Calls);
    }

    [Fact]
    public void BeginKeep_ChangeNoLongerShowing_DeletesTheRecordAndSkipsPersist()
    {
        var system = new FakeSystem { Current = Original };

        Assert.False(system.Flow().BeginKeep(new DisplayRevert(Original, Target)));
        Assert.Equal(["read", "delete"], system.Calls);
    }

    [Fact]
    public void Persist_ReportsFailure()
    {
        var system = new FakeSystem { PersistResult = false, Current = Target };

        Assert.False(system.Flow().Persist(new DisplayRevert(Original, Target)));
    }

    [Fact]
    public void Revert_Success_DeletesTheRecord()
    {
        var system = new FakeSystem { Current = Target };

        Assert.True(system.Flow().Revert(new DisplayRevert(Original, Target)));
        Assert.Equal(["revert", "delete"], system.Calls);
    }

    [Fact]
    public void Revert_Failure_KeepsTheRecord()
    {
        var system = new FakeSystem { Current = Target, RevertResult = false };

        Assert.False(system.Flow().Revert(new DisplayRevert(Original, Target)));
        Assert.Equal(["revert"], system.Calls);
    }

    [Fact]
    public void Recover_NoRecord_DoesNothing()
    {
        var system = new FakeSystem();

        Assert.Equal(DisplayRecoveryResult.NoRecord, system.Flow().Recover(null, recordExists: false));
        Assert.Empty(system.Calls);
    }

    [Fact]
    public void Recover_UnreadableRecord_IsDeleted()
    {
        var system = new FakeSystem();

        Assert.Equal(DisplayRecoveryResult.Discarded, system.Flow().Recover(null, recordExists: true));
        Assert.Equal(["delete"], system.Calls);
    }

    [Fact]
    public void Recover_ChangeNoLongerShowing_DeletesWithoutReverting()
    {
        var system = new FakeSystem { Current = Original };

        Assert.Equal(DisplayRecoveryResult.NothingToRevert, system.Flow().Recover(new DisplayRevert(Original, Target), recordExists: true));
        Assert.Equal(["read", "delete"], system.Calls);
    }

    [Fact]
    public void Recover_StillShowing_RevertsWithoutAPreTestAndDeletes()
    {
        var system = new FakeSystem { Current = Target };

        Assert.Equal(DisplayRecoveryResult.Reverted, system.Flow().Recover(new DisplayRevert(Original, Target), recordExists: true));
        Assert.Equal(["read", "revert", "delete"], system.Calls);
    }

    [Fact]
    public void Recover_RevertFails_KeepsTheRecordForNextTime()
    {
        var system = new FakeSystem { Current = Target, RevertResult = false };

        Assert.Equal(DisplayRecoveryResult.Failed, system.Flow().Recover(new DisplayRevert(Original, Target), recordExists: true));
        Assert.Equal(["read", "revert"], system.Calls);
    }

    [Fact]
    public void Recover_OnlyRevertsDisplaysStillAttached()
    {
        IReadOnlyList<DisplaySetting>? reverted = null;
        var flow = new DisplayChangeFlow(
            () => [ASmaller],
            _ => true,
            _ => true,
            original => { reverted = original; return true; },
            _ => true,
            _ => true,
            () => { });

        flow.Recover(new DisplayRevert(Original, [ASmaller]), recordExists: true);

        Assert.Equal([A], reverted);
    }
}
