namespace WinGnome.Core.ControlCenter;

/// <summary>What happened when a display change was applied.</summary>
public enum DisplayApplyResult
{
    /// <summary>The change is showing for this session and waits for "Keep changes?".</summary>
    Applied,

    /// <summary>Applied, but Windows adjusted it; the pending change records what is really showing.</summary>
    AppliedAdjusted,

    /// <summary>The target equals the current settings; nothing was done.</summary>
    NoChange,

    /// <summary>Displays were attached or removed since the panel read them; nothing was done.</summary>
    DisplaysChanged,

    /// <summary>Windows rejected the configuration when validating it; nothing was done.</summary>
    Rejected,

    /// <summary>The revert record couldn't be written, so the change wasn't applied.</summary>
    NotRecorded,

    /// <summary>Applying failed and the original settings are showing again.</summary>
    FailedAndRestored,

    /// <summary>Applying failed and the original couldn't be restored; the record stays for the next start.</summary>
    FailedNotRestored,
}

/// <summary>The result of <see cref="DisplayChangeFlow.Apply"/>, with the change to confirm when it was applied.</summary>
public sealed record DisplayApplyOutcome(DisplayApplyResult Result, DisplayRevert? Pending);

/// <summary>What start-up recovery did with a revert record.</summary>
public enum DisplayRecoveryResult
{
    NoRecord,

    /// <summary>The record couldn't be read and was deleted.</summary>
    Discarded,

    /// <summary>The unconfirmed change was no longer showing (reboot, sign-out, a newer change); the record was deleted.</summary>
    NothingToRevert,

    Reverted,

    /// <summary>The revert didn't take; the record stays for the next start.</summary>
    Failed,
}

/// <summary>
/// The order of operations behind the Displays panel's "Keep changes?" safety, with the system calls injected so it
/// can be tested: read and validate before changing, record before applying, apply for the session only, delete the
/// record before saving a kept change (so a crash in between can't revert it), and delete the record after a revert
/// only when the original is showing again.
/// </summary>
/// <param name="readCurrent">Current mode and position of every attached display.</param>
/// <param name="test">Validates a whole configuration without applying it.</param>
/// <param name="applyForSession">Applies a configuration without saving it (a reboot or sign-out drops it).</param>
/// <param name="revert">Puts displays back to the given settings; true only when they show them again.</param>
/// <param name="persist">Saves the showing configuration so it survives sign-out.</param>
/// <param name="writeRecord">Writes the revert record; false when it couldn't be written.</param>
/// <param name="deleteRecord">Deletes the revert record.</param>
public sealed class DisplayChangeFlow(
    Func<IReadOnlyList<DisplaySetting>> readCurrent,
    Func<IReadOnlyList<DisplaySetting>, bool> test,
    Func<IReadOnlyList<DisplaySetting>, bool> applyForSession,
    Func<IReadOnlyList<DisplaySetting>, bool> revert,
    Func<IReadOnlyList<DisplaySetting>, bool> persist,
    Func<DisplayRevert, bool> writeRecord,
    Action deleteRecord)
{
    /// <summary>Applies <paramref name="target"/> for the session, recording what to go back to first.</summary>
    public DisplayApplyOutcome Apply(IReadOnlyList<DisplaySetting> target)
    {
        var original = readCurrent();
        if (!SameDisplays(original, target))
        {
            return new(DisplayApplyResult.DisplaysChanged, null);
        }

        if (DisplayRevertRecord.IsStillApplied(new DisplayRevert(original, target), original))
        {
            return new(DisplayApplyResult.NoChange, null);
        }

        if (!test(target))
        {
            return new(DisplayApplyResult.Rejected, null);
        }

        var pending = new DisplayRevert(original, target);
        if (!writeRecord(pending))
        {
            return new(DisplayApplyResult.NotRecorded, null);
        }

        if (applyForSession(target))
        {
            return Confirm(pending);
        }

        return Revert(pending)
            ? new(DisplayApplyResult.FailedAndRestored, null)
            : new(DisplayApplyResult.FailedNotRestored, null);
    }

    /// <summary>
    /// After a successful apply: Windows may have adjusted the configuration (SDC_ALLOW_CHANGES), so what is showing is
    /// read back. When it differs from the target, the record is rewritten with what is showing, so Keep saves that and
    /// recovery recognises it; if the record can't be rewritten, the change is reverted.
    /// </summary>
    private DisplayApplyOutcome Confirm(DisplayRevert pending)
    {
        var showing = readCurrent();
        if (DisplayRevertRecord.IsStillApplied(pending, showing))
        {
            return new(DisplayApplyResult.Applied, pending);
        }

        var adjusted = pending with
        {
            Target = showing
                .Where(s => pending.Target.Any(t => string.Equals(t.DeviceName, s.DeviceName, StringComparison.OrdinalIgnoreCase)))
                .ToList(),
        };
        if (writeRecord(adjusted))
        {
            return new(DisplayApplyResult.AppliedAdjusted, adjusted);
        }

        return Revert(pending)
            ? new(DisplayApplyResult.NotRecorded, null)
            : new(DisplayApplyResult.FailedNotRestored, null);
    }

    /// <summary>
    /// First half of "Keep Changes": deletes the record (before anything is saved) and returns whether the change is
    /// still showing and should be saved with <see cref="Persist"/>. A change that already went away is not saved.
    /// </summary>
    public bool BeginKeep(DisplayRevert pending)
    {
        var stillShowing = DisplayRevertRecord.IsStillApplied(pending, readCurrent());
        deleteRecord();
        return stillShowing;
    }

    /// <summary>Second half of "Keep Changes": saves the target so it survives sign-out.</summary>
    public bool Persist(DisplayRevert pending) => persist(pending.Target);

    /// <summary>Goes back to the original settings; the record is deleted only when that worked.</summary>
    public bool Revert(DisplayRevert pending)
    {
        if (!revert(pending.Original))
        {
            return false;
        }

        deleteRecord();
        return true;
    }

    /// <summary>
    /// At start-up: reverts a change recorded by a previous run that stopped during the countdown, if it is still
    /// showing. There is no pre-test: validating the original against the changed layout can fail where the revert works.
    /// </summary>
    /// <param name="record">The parsed record, or null when there is none or it couldn't be read.</param>
    /// <param name="recordExists">True when a record file exists (an unreadable one is discarded).</param>
    public DisplayRecoveryResult Recover(DisplayRevert? record, bool recordExists)
    {
        if (record is null)
        {
            if (recordExists)
            {
                deleteRecord();
                return DisplayRecoveryResult.Discarded;
            }

            return DisplayRecoveryResult.NoRecord;
        }

        var current = readCurrent();
        if (!DisplayRevertRecord.IsStillApplied(record, current))
        {
            deleteRecord();
            return DisplayRecoveryResult.NothingToRevert;
        }

        var attached = record.Original
            .Where(o => current.Any(c => string.Equals(c.DeviceName, o.DeviceName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return Revert(record with { Original = attached }) ? DisplayRecoveryResult.Reverted : DisplayRecoveryResult.Failed;
    }

    private static bool SameDisplays(IReadOnlyList<DisplaySetting> current, IReadOnlyList<DisplaySetting> target) =>
        current.Count == target.Count
        && target.All(t => current.Any(c => string.Equals(c.DeviceName, t.DeviceName, StringComparison.OrdinalIgnoreCase)));
}
