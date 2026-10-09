namespace WinGnome.Core.ControlCenter;

/// <summary>Where a display change is in its "Keep changes?" confirmation.</summary>
public enum KeepChangesState
{
    /// <summary>Nothing is waiting for confirmation.</summary>
    Idle,

    /// <summary>A change was applied and reverts unless the user keeps it before the deadline.</summary>
    Waiting,

    /// <summary>The user kept the change.</summary>
    Kept,

    /// <summary>The change was reverted, by the user or because the deadline passed.</summary>
    Reverted,
}

/// <summary>
/// GNOME's display confirmation: after a change is applied the user has a fixed time to keep it, otherwise it is
/// reverted, so a mode the screen can't show never sticks. The caller supplies the time and drives <see cref="Tick"/>
/// from a timer; the state machine itself never reads the clock.
/// </summary>
public sealed class KeepChangesCountdown
{
    /// <summary>GNOME's confirmation time.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly TimeSpan _timeout;
    private DateTime _deadline;

    public KeepChangesCountdown()
        : this(DefaultTimeout)
    {
    }

    public KeepChangesCountdown(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _timeout = timeout;
    }

    public KeepChangesState State { get; private set; } = KeepChangesState.Idle;

    /// <summary>Starts waiting for confirmation of a change just applied. A new change restarts the full timeout.</summary>
    public void Start(DateTime now)
    {
        _deadline = now + _timeout;
        State = KeepChangesState.Waiting;
    }

    /// <summary>Whole seconds left before the change reverts, rounded up; 0 when not waiting or past the deadline.</summary>
    public int SecondsLeft(DateTime now)
    {
        if (State != KeepChangesState.Waiting)
        {
            return 0;
        }

        var left = (_deadline - now).TotalSeconds;
        return left <= 0 ? 0 : (int)Math.Ceiling(left);
    }

    /// <summary>
    /// Advances the countdown. Returns true exactly once, when the deadline passes while waiting: the caller must then
    /// revert the change.
    /// </summary>
    public bool Tick(DateTime now)
    {
        if (State != KeepChangesState.Waiting || now < _deadline)
        {
            return false;
        }

        State = KeepChangesState.Reverted;
        return true;
    }

    /// <summary>The user keeps the change. Returns false (and does nothing) unless a change was waiting.</summary>
    public bool Keep() => Finish(KeepChangesState.Kept);

    /// <summary>The user reverts the change. Returns false (and does nothing) unless a change was waiting.</summary>
    public bool Revert() => Finish(KeepChangesState.Reverted);

    private bool Finish(KeepChangesState outcome)
    {
        if (State != KeepChangesState.Waiting)
        {
            return false;
        }

        State = outcome;
        return true;
    }
}
