namespace WinGnome.Core.Shell;

/// <summary>Where a shell-mode trial stands (spec 0013).</summary>
public enum ShellTrialState
{
    /// <summary>No trial record: shell mode is either confirmed or was never turned on.</summary>
    None,

    /// <summary>Turned on for one sign-in; the user hasn't confirmed yet.</summary>
    TrialPending,

    /// <summary>The user confirmed "Keep WinGnome as my shell".</summary>
    Confirmed,
}

public enum ShellStartAction
{
    StartWinGnome,
    StartExplorer,

    /// <summary>The session is ending: no restart, no Explorer, nothing recorded.</summary>
    DoNothing,

    /// <summary>Nothing left to try: Explorer failed to start too.</summary>
    GiveUp,
}

/// <summary>Why the bootstrap chose Explorer (or gave up).</summary>
public enum ExplorerReason
{
    None,
    RestoreRequested,
    ShiftHeld,
    SafeMode,
    DisabledFlag,
    CrashLoop,
    RestartsExhausted,
    WinGnomeMissing,

    /// <summary>An input couldn't be determined (unreadable flag, history or Safe Mode state): fail closed.</summary>
    UnknownState,

    /// <summary>A previous Explorer start in this session failed, so the bootstrap is retrying it.</summary>
    ExplorerRetry,
}

/// <summary>
/// Everything the bootstrap knows when it decides. Nullable members are probes that can fail;
/// <c>null</c> means "couldn't tell" and makes the decision fail closed (Explorer, value removed).
/// </summary>
/// <param name="ShiftHeld">Shift is down at sign-in.</param>
/// <param name="SafeMode">Windows is in Safe Mode (clean boot).</param>
/// <param name="DisabledFlagPresent">The <c>shell-disabled</c> flag file exists.</param>
/// <param name="Crashes">Recorded shell crashes before this decision.</param>
/// <param name="ReadyTimedOut">WinGnome didn't signal ready within <see cref="ShellStartDecision.ReadyTimeout"/> of starting; counts as one more crash.</param>
/// <param name="RestartsSoFar">Restarts already done in this session.</param>
/// <param name="Now">The current time (passed in, never read from the clock).</param>
/// <param name="Trial">Trial state.</param>
/// <param name="ExplorerFallbackAttempts">How many times this session the bootstrap already tried to start Explorer.</param>
/// <param name="WinGnomeExeExists">WinGnome.exe is present at the expected path.</param>
/// <param name="IsSessionEnding">Logoff or shutdown is in progress.</param>
/// <param name="RestoreRequested">The user asked to restore Explorer (<c>--restore-shell</c>).</param>
public sealed record ShellStartInputs(
    bool ShiftHeld,
    bool? SafeMode,
    bool? DisabledFlagPresent,
    CrashHistory? Crashes,
    bool ReadyTimedOut,
    int RestartsSoFar,
    DateTimeOffset Now,
    ShellTrialState Trial,
    int ExplorerFallbackAttempts,
    bool WinGnomeExeExists,
    bool IsSessionEnding,
    bool RestoreRequested);

/// <param name="Action">What to do.</param>
/// <param name="Reason">Why Explorer (or nothing) was chosen; <see cref="ExplorerReason.None"/> otherwise.</param>
/// <param name="RemoveShellValue">Remove the per-user Winlogon Shell value now.</param>
/// <param name="KeepTrial">Retain a pending trial record (it ends otherwise).</param>
public sealed record ShellStartOutcome(ShellStartAction Action, ExplorerReason Reason, bool RemoveShellValue, bool KeepTrial);

/// <summary>
/// The bootstrap's decision, with no I/O so it can be tested exhaustively. Recoveries that the user
/// or a loop trigger remove the Shell value so the next sign-in is Explorer too; Safe Mode is
/// transient and leaves it alone. The bootstrap never writes the value back: only the app does, on
/// confirmation (<see cref="TrialConfirmation"/>).
/// </summary>
public static class ShellStartDecision
{
    /// <summary>Explorer starts allowed in one session before the bootstrap gives up.</summary>
    public const int MaxExplorerAttempts = 3;

    /// <summary>How long WinGnome has, from its process start, to signal ready.</summary>
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(90);

    public static ShellStartOutcome Decide(ShellStartInputs i)
    {
        var pending = i.Trial == ShellTrialState.TrialPending;

        if (i.IsSessionEnding)
        {
            return new ShellStartOutcome(ShellStartAction.DoNothing, ExplorerReason.None, RemoveShellValue: false, KeepTrial: pending);
        }

        if (i.ExplorerFallbackAttempts >= MaxExplorerAttempts)
        {
            return new ShellStartOutcome(ShellStartAction.GiveUp, ExplorerReason.ExplorerRetry, RemoveShellValue: true, KeepTrial: false);
        }

        if (i.ExplorerFallbackAttempts > 0)
        {
            return Explorer(ExplorerReason.ExplorerRetry, remove: true, keepTrial: false);
        }

        if (i.RestoreRequested)
        {
            return Explorer(ExplorerReason.RestoreRequested, remove: true, keepTrial: false);
        }

        if (i.ShiftHeld)
        {
            return Explorer(ExplorerReason.ShiftHeld, remove: true, keepTrial: false);
        }

        if (i.SafeMode == true)
        {
            return Explorer(ExplorerReason.SafeMode, remove: false, keepTrial: pending);
        }

        if (i.DisabledFlagPresent == true)
        {
            return Explorer(ExplorerReason.DisabledFlag, remove: true, keepTrial: false);
        }

        if (i.SafeMode is null || i.DisabledFlagPresent is null || i.Crashes is null)
        {
            return Explorer(ExplorerReason.UnknownState, remove: true, keepTrial: false);
        }

        if (!i.WinGnomeExeExists)
        {
            return Explorer(ExplorerReason.WinGnomeMissing, remove: true, keepTrial: false);
        }

        var crashes = i.ReadyTimedOut ? i.Crashes.Record(i.Now, i.Now) : i.Crashes;
        if (crashes.IsCrashLoop(i.Now))
        {
            return Explorer(ExplorerReason.CrashLoop, remove: true, keepTrial: false);
        }

        if (i.RestartsSoFar >= RestartBackoff.MaxRestarts)
        {
            return Explorer(ExplorerReason.RestartsExhausted, remove: true, keepTrial: false);
        }

        // A pending trial removes the value as it starts, so a reboot without confirming falls back.
        return new ShellStartOutcome(ShellStartAction.StartWinGnome, ExplorerReason.None, RemoveShellValue: pending, KeepTrial: pending);
    }

    /// <summary>
    /// Records a shell exit. An exit while the session is ending (logoff kills the process) is not a crash.
    /// </summary>
    public static CrashHistory RecordExit(CrashHistory history, DateTimeOffset now, bool isSessionEnding) =>
        isSessionEnding ? history : history.Record(now, now);

    private static ShellStartOutcome Explorer(ExplorerReason reason, bool remove, bool keepTrial) =>
        new(ShellStartAction.StartExplorer, reason, remove, keepTrial);
}

/// <summary>When "Keep WinGnome as my shell" may take effect.</summary>
public static class TrialConfirmation
{
    /// <summary>The session must have been up this long, so a shell that limps along for a moment can't be confirmed.</summary>
    public static readonly TimeSpan MinimumUptime = TimeSpan.FromMinutes(5);

    /// <summary>Needs the user's explicit confirmation and at least <see cref="MinimumUptime"/> (inclusive) of uptime.</summary>
    public static bool CanConfirm(bool userConfirmed, TimeSpan sessionUptime) =>
        userConfirmed && sessionUptime >= MinimumUptime;
}
