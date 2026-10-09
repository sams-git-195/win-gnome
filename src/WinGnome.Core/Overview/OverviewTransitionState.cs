namespace WinGnome.Core.Overview;

/// <summary>One step of a transition: progress within the current segment and the overall position.</summary>
/// <param name="SegmentEased">Eased progress (0..1) of the current segment, for thumbnail tracks retargeted at its start.</param>
/// <param name="Position">Where the overview is between the windows (0) and the grid (1).</param>
/// <param name="Finished">True on the last frame of the segment.</param>
public readonly record struct TransitionFrame(double SegmentEased, double Position, bool Finished);

/// <summary>
/// The overview's open/close state machine: one position between the windows (0) and the grid (1) that
/// opening, closing and any number of reversals move along, so a reversal always takes the share of the
/// full duration that it has to travel back.
/// </summary>
/// <remarks>
/// Time is passed in (the app uses each frame's rendering time). The clock of a segment starts at its first
/// frame, not at <see cref="Begin"/>: the first frame can take several refreshes to appear.
/// </remarks>
public sealed class OverviewTransitionState
{
    /// <summary>How long after its expected end a segment that stopped getting frames is forced to finish.</summary>
    public static readonly TimeSpan WatchdogMargin = TimeSpan.FromMilliseconds(300);

    private double _from;
    private double _target;
    private TimeSpan? _start;
    private bool _windowsChanged;

    /// <summary>Where the overview is: 0 at the windows (closed), 1 in the grid (open).</summary>
    public double Position { get; private set; }

    /// <summary>True from <see cref="Begin"/> until the segment finishes.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>True when the current or last segment heads for the grid.</summary>
    public bool IsOpening => _target == 1;

    /// <summary>Length of the current segment.</summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>After this long without finishing, the app calls <see cref="Finish"/> (frames have stopped).</summary>
    public TimeSpan WatchdogDelay => Duration + WatchdogMargin;

    /// <summary>Starts moving towards the grid or the windows from the current position.</summary>
    public void Begin(bool opening, bool animationsEnabled)
    {
        // Window events aren't watched while closing, so a reopened grid must catch up once it settles.
        if (opening && IsMoving && !IsOpening)
        {
            _windowsChanged = true;
        }

        _from = Position;
        _target = opening ? 1 : 0;
        _start = null;
        Duration = OverviewTransition.DurationFor(animationsEnabled, opening) * Math.Abs(_target - _from);
        IsMoving = true;
    }

    /// <summary>The frame drawn at <paramref name="now"/>; the first call starts the segment's clock.</summary>
    public TransitionFrame Advance(TimeSpan now)
    {
        _start ??= now;
        var progress = OverviewTransition.Progress(now - _start.Value, Duration);
        if (progress >= 1)
        {
            return Finish();
        }

        var eased = OverviewTransition.EaseOutQuad(progress);
        Position = _from + ((_target - _from) * eased);
        return new TransitionFrame(eased, Position, false);
    }

    /// <summary>Ends the segment at its target (also the watchdog's way out when frames stop coming).</summary>
    public TransitionFrame Finish()
    {
        Position = _target;
        IsMoving = false;
        return new TransitionFrame(1, Position, true);
    }

    /// <summary>Records a window-list change. True when it can be applied now; false while moving (deferred).</summary>
    public bool NoteWindowsChanged()
    {
        if (!IsMoving)
        {
            return true;
        }

        _windowsChanged = true;
        return false;
    }

    /// <summary>True (once) when window-list changes were deferred and must be applied now.</summary>
    public bool TakeWindowChanges()
    {
        var changed = _windowsChanged;
        _windowsChanged = false;
        return changed;
    }

    /// <summary>Back to closed and still (the overview has hidden).</summary>
    public void Reset()
    {
        Position = 0;
        _from = 0;
        _target = 0;
        _start = null;
        Duration = TimeSpan.Zero;
        IsMoving = false;
        _windowsChanged = false;
    }
}
