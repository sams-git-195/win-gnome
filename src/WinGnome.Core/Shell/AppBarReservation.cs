using WinGnome.Core.Geometry;

namespace WinGnome.Core.Shell;

/// <summary>Whether a monitor's work area still leaves a docked AppBar's strip out.</summary>
public static class AppBarReservation
{
    /// <summary>
    /// True when <paramref name="workArea"/> stops short of <paramref name="strip"/> on <paramref name="edge"/>, so
    /// maximised windows stay clear of it. Explorer applies AppBar strips to work areas on its own schedule (right
    /// after the taskbar was switched to auto-hide it was seen to take ~35 s) and can recompute them without our strip
    /// (a monitor unplugged); this is how a bar notices. A bar stacked behind another AppBar on the same edge still
    /// passes (the work area ends beyond both). An empty strip reserves nothing and always passes.
    /// </summary>
    public static bool IsReserved(AppBarEdge edge, PixelRect strip, PixelRect workArea)
    {
        if (strip.IsEmpty)
        {
            return true;
        }

        return edge switch
        {
            AppBarEdge.Top => workArea.Top >= strip.Bottom,
            AppBarEdge.Bottom => workArea.Bottom <= strip.Top,
            AppBarEdge.Left => workArea.Left >= strip.Right,
            _ => workArea.Right <= strip.Left,
        };
    }

    /// <summary>
    /// Whether a shell notification (ABN_POSCHANGED, ABN_STATECHANGE) moves the bar: only when the shell offers a
    /// different slot than the one the bar holds or last asked for. Answering every notification with a SETPOS lets N
    /// bars on one edge notify each other forever. A missing strip is <see cref="StripRecovery"/>'s business.
    /// </summary>
    /// <param name="queried">The QUERYPOS answer with the bar's thickness applied.</param>
    /// <param name="bounds">The rectangle the bar holds now.</param>
    /// <param name="requested">The rectangle the bar last asked for (the shell may have adjusted it into <paramref name="bounds"/>).</param>
    public static bool ShouldMove(PixelRect queried, PixelRect bounds, PixelRect requested) =>
        queried != bounds && queried != requested;
}

public enum StripRecoveryKind
{
    /// <summary>The strip is reserved (again); nothing to do.</summary>
    None,
    /// <summary>The strip is missing; check again at <see cref="StripRecoveryStep.DueMs"/>.</summary>
    Wait,
    /// <summary>Register the AppBar again now, then check again at <see cref="StripRecoveryStep.DueMs"/>.</summary>
    Reregister,
    /// <summary>The attempts are used up; leave it (the next display change or Explorer restart starts afresh).</summary>
    GiveUp,
}

public readonly record struct StripRecoveryStep(StripRecoveryKind Kind, long DueMs = 0);

/// <summary>
/// What one bar does while its strip is missing from the work area. Explorer applies strips late on its own (seen
/// ~35 s after the taskbar went auto-hide), and registering again during that time did not help and may restart its
/// delay, so a bar first waits <see cref="GraceMs"/>, then registers again at most <see cref="MaxAttempts"/> times with
/// a doubling back-off. Pure and clock-free: callers pass a monotonic time in milliseconds.
/// </summary>
public sealed class StripRecovery
{
    /// <summary>How long a missing strip is left to Explorer before the bar registers again.</summary>
    public const long GraceMs = 45_000;

    /// <summary>Gap after the first re-registration; it doubles after each further one.</summary>
    public const long FirstBackoffMs = 60_000;

    public const int MaxAttempts = 3;

    private long? _missingSinceMs;
    private int _attempts;
    private long _nextAttemptMs;

    /// <summary>True between a missing strip being seen and the strip being reserved again.</summary>
    public bool IsMissing => _missingSinceMs is not null;

    /// <summary>Feeds a fresh check of the work area.</summary>
    public StripRecoveryStep Update(bool reserved, long nowMs)
    {
        if (reserved)
        {
            Reset();
            return new StripRecoveryStep(StripRecoveryKind.None);
        }

        if (_missingSinceMs is null)
        {
            _missingSinceMs = nowMs;
            _nextAttemptMs = nowMs + GraceMs;
            return new StripRecoveryStep(StripRecoveryKind.Wait, _nextAttemptMs);
        }

        if (_attempts >= MaxAttempts)
        {
            return new StripRecoveryStep(StripRecoveryKind.GiveUp);
        }

        if (nowMs < _nextAttemptMs)
        {
            return new StripRecoveryStep(StripRecoveryKind.Wait, _nextAttemptMs);
        }

        _attempts++;
        _nextAttemptMs = nowMs + (FirstBackoffMs << (_attempts - 1));
        return new StripRecoveryStep(StripRecoveryKind.Reregister, _nextAttemptMs);
    }

    /// <summary>Forgets the missing strip (it is reserved again, or the bar was undocked).</summary>
    public void Reset()
    {
        _missingSinceMs = null;
        _attempts = 0;
        _nextAttemptMs = 0;
    }
}
