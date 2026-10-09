using WinGnome.Core.Shell;

namespace WinGnome.Core.Tests.Shell;

public class ShellStartDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly ShellStartInputs Healthy = new(
        ShiftHeld: false,
        SafeMode: false,
        DisabledFlagPresent: false,
        Crashes: CrashHistory.Empty,
        ReadyTimedOut: false,
        RestartsSoFar: 0,
        Now: Now,
        Trial: ShellTrialState.Confirmed,
        ExplorerFallbackAttempts: 0,
        WinGnomeExeExists: true,
        IsSessionEnding: false,
        RestoreRequested: false);

    private static CrashHistory CrashesAgo(params int[] secondsAgo) =>
        CrashHistory.From(secondsAgo.Select(s => Now.AddSeconds(-s)));

    [Fact]
    public void Decide_Healthy_StartsWinGnomeAndLeavesTheValue()
    {
        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.StartWinGnome, ExplorerReason.None, RemoveShellValue: false, KeepTrial: false),
            ShellStartDecision.Decide(Healthy));
    }

    [Fact]
    public void Decide_ShiftHeld_StartsExplorerAndRemovesTheValue()
    {
        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.ShiftHeld, true, false),
            ShellStartDecision.Decide(Healthy with { ShiftHeld = true }));
    }

    [Fact]
    public void Decide_SafeMode_StartsExplorerButKeepsTheValue()
    {
        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.SafeMode, false, false),
            ShellStartDecision.Decide(Healthy with { SafeMode = true }));
    }

    [Fact]
    public void Decide_SafeModeDuringTrial_KeepsThePendingTrial()
    {
        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.SafeMode, false, true),
            ShellStartDecision.Decide(Healthy with { SafeMode = true, Trial = ShellTrialState.TrialPending }));
    }

    [Fact]
    public void Decide_DisabledFlag_StartsExplorerAndRemovesTheValue()
    {
        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.DisabledFlag, true, false),
            ShellStartDecision.Decide(Healthy with { DisabledFlagPresent = true }));
    }

    [Fact]
    public void Decide_RestoreRequested_StartsExplorerAndRemovesTheValue()
    {
        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.RestoreRequested, true, false),
            ShellStartDecision.Decide(Healthy with { RestoreRequested = true, Trial = ShellTrialState.TrialPending }));
    }

    [Fact]
    public void Decide_WinGnomeExeMissing_FailsClosedToExplorer()
    {
        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.WinGnomeMissing, true, false),
            ShellStartDecision.Decide(Healthy with { WinGnomeExeExists = false }));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Decide_AnyUnknownProbe_FailsClosedToExplorer(bool safeModeUnknown, bool flagUnknown, bool historyUnknown)
    {
        var inputs = Healthy with
        {
            SafeMode = safeModeUnknown ? null : false,
            DisabledFlagPresent = flagUnknown ? null : false,
            Crashes = historyUnknown ? null : CrashHistory.Empty,
        };
        var expected = new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.UnknownState, true, false);

        Assert.Equal(expected, ShellStartDecision.Decide(inputs));
    }

    [Fact]
    public void Decide_UnknownFlagButShiftHeld_ReportsShift()
    {
        var outcome = ShellStartDecision.Decide(Healthy with { ShiftHeld = true, DisabledFlagPresent = null });

        Assert.Equal(ExplorerReason.ShiftHeld, outcome.Reason);
    }

    [Fact]
    public void Decide_ThreeCrashesWithinTwoMinutes_StartsExplorerAndRemovesTheValue()
    {
        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.CrashLoop, true, false),
            ShellStartDecision.Decide(Healthy with { Crashes = CrashesAgo(10, 60, 119) }));
    }

    [Fact]
    public void Decide_TwoCrashesWithinTwoMinutes_StillStartsWinGnome()
    {
        var outcome = ShellStartDecision.Decide(Healthy with { Crashes = CrashesAgo(10, 60) });

        Assert.Equal(ShellStartAction.StartWinGnome, outcome.Action);
    }

    [Fact]
    public void Decide_ThirdCrashJustOutsideTwoMinutes_StillStartsWinGnome()
    {
        var outcome = ShellStartDecision.Decide(Healthy with { Crashes = CrashesAgo(10, 60, 121) });

        Assert.Equal(ShellStartAction.StartWinGnome, outcome.Action);
    }

    [Fact]
    public void Decide_FiveCrashesWithinThirtyMinutes_StartsExplorerAsSlowLoop()
    {
        var crashes = CrashesAgo(300, 600, 900, 1200, 1799);

        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.CrashLoop, true, false),
            ShellStartDecision.Decide(Healthy with { Crashes = crashes }));
    }

    [Fact]
    public void Decide_FourCrashesWithinThirtyMinutes_StillStartsWinGnome()
    {
        var outcome = ShellStartDecision.Decide(Healthy with { Crashes = CrashesAgo(300, 600, 900, 1200) });

        Assert.Equal(ShellStartAction.StartWinGnome, outcome.Action);
    }

    [Fact]
    public void Decide_ReadyTimedOut_StartsExplorerAndRemovesTheValue()
    {
        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.ReadyTimeout, true, false),
            ShellStartDecision.Decide(Healthy with { ReadyTimedOut = true }));
    }

    [Fact]
    public void Decide_ReadyTimedOutDuringTrial_EndsTheTrial()
    {
        var outcome = ShellStartDecision.Decide(Healthy with { ReadyTimedOut = true, Trial = ShellTrialState.TrialPending });

        Assert.Equal(new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.ReadyTimeout, true, false), outcome);
    }

    [Fact]
    public void Decide_UnreadableTrialState_FailsClosedToExplorer()
    {
        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.UnknownState, true, false),
            ShellStartDecision.Decide(Healthy with { Trial = null }));
    }

    [Fact]
    public void Decide_UndefinedTrialValue_FailsClosedToExplorer()
    {
        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.UnknownState, true, false),
            ShellStartDecision.Decide(Healthy with { Trial = (ShellTrialState)42 }));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void Decide_NegativeCounters_FailClosedToExplorer(int restarts, int explorerAttempts)
    {
        var outcome = ShellStartDecision.Decide(Healthy with { RestartsSoFar = restarts, ExplorerFallbackAttempts = explorerAttempts });

        Assert.Equal(new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.UnknownState, true, false), outcome);
    }

    [Fact]
    public void ReadyTimeout_IsNinetySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(90), ShellStartDecision.ReadyTimeout);
    }

    [Theory]
    [InlineData(2, ShellStartAction.StartWinGnome, ExplorerReason.None)]
    [InlineData(3, ShellStartAction.StartExplorer, ExplorerReason.RestartsExhausted)]
    public void Decide_RestartsSoFar_GivesUpAfterTheSchedule(int restarts, ShellStartAction action, ExplorerReason reason)
    {
        var outcome = ShellStartDecision.Decide(Healthy with { RestartsSoFar = restarts });

        Assert.Equal(action, outcome.Action);
        Assert.Equal(reason, outcome.Reason);
    }

    [Fact]
    public void Decide_TrialPending_StartsWinGnomeRemovesTheValueAndKeepsTheTrial()
    {
        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.StartWinGnome, ExplorerReason.None, true, true),
            ShellStartDecision.Decide(Healthy with { Trial = ShellTrialState.TrialPending }));
    }

    [Fact]
    public void Decide_NoTrialRecord_LeavesTheValueAndTrial()
    {
        var outcome = ShellStartDecision.Decide(Healthy with { Trial = ShellTrialState.None });

        Assert.False(outcome.RemoveShellValue);
        Assert.False(outcome.KeepTrial);
    }

    [Fact]
    public void Decide_TrialPendingAndShiftHeld_EndsTheTrial()
    {
        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.ShiftHeld, true, false),
            ShellStartDecision.Decide(Healthy with { ShiftHeld = true, Trial = ShellTrialState.TrialPending }));
    }

    [Fact]
    public void Decide_SessionEnding_DoesNothingWhateverElseIsWrong()
    {
        var inputs = Healthy with { IsSessionEnding = true, Crashes = CrashesAgo(1, 2, 3), ShiftHeld = true, ReadyTimedOut = true, Trial = null };

        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.DoNothing, ExplorerReason.None, false, false),
            ShellStartDecision.Decide(inputs));
    }

    [Fact]
    public void Decide_SessionEndingDuringTrial_KeepsTheTrialAndTouchesNothing()
    {
        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.DoNothing, ExplorerReason.None, false, true),
            ShellStartDecision.Decide(Healthy with { IsSessionEnding = true, Trial = ShellTrialState.TrialPending }));
    }

    [Fact]
    public void Decide_ExplorerFailedOnce_RetriesExplorer()
    {
        Assert.Equal(
            new ShellStartOutcome(ShellStartAction.StartExplorer, ExplorerReason.ExplorerRetry, true, false),
            ShellStartDecision.Decide(Healthy with { ExplorerFallbackAttempts = 1 }));
    }

    [Theory]
    [InlineData(2, ShellStartAction.StartExplorer)]
    [InlineData(3, ShellStartAction.GiveUp)]
    [InlineData(4, ShellStartAction.GiveUp)]
    public void Decide_ExplorerFallbackAttempts_GivesUpAtThree(int attempts, ShellStartAction action)
    {
        var outcome = ShellStartDecision.Decide(Healthy with { ExplorerFallbackAttempts = attempts });

        Assert.Equal(action, outcome.Action);
        Assert.True(outcome.RemoveShellValue);
    }

    [Fact]
    public void RecordExit_NormalExit_AddsACrash()
    {
        var history = ShellStartDecision.RecordExit(CrashHistory.Empty, Now, isSessionEnding: false, cleanExit: false);

        Assert.Equal([Now], history.Crashes);
    }

    [Fact]
    public void RecordExit_DuringSessionEnd_IsNotACrash()
    {
        var history = ShellStartDecision.RecordExit(CrashHistory.Empty, Now, isSessionEnding: true, cleanExit: false);

        Assert.Empty(history.Crashes);
    }

    [Fact]
    public void RecordExit_DeliberateCleanQuit_IsNotACrash()
    {
        var history = ShellStartDecision.RecordExit(CrashHistory.Empty, Now, isSessionEnding: false, cleanExit: true);

        Assert.Empty(history.Crashes);
    }

    [Theory]
    [InlineData(true, 300, true)]
    [InlineData(true, 301, true)]
    [InlineData(true, 299, false)]
    [InlineData(false, 3600, false)]
    [InlineData(true, 0, false)]
    public void CanConfirm_NeedsExplicitConfirmAndFiveMinutesUptime(bool confirmed, int uptimeSeconds, bool expected)
    {
        Assert.Equal(expected, TrialConfirmation.CanConfirm(confirmed, TimeSpan.FromSeconds(uptimeSeconds)));
    }
}
