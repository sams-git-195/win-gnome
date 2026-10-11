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
    /// True when <paramref name="workArea"/> leaves out more than <paramref name="strip"/> on <paramref name="edge"/>:
    /// Explorer was seen to reserve a top bar's strip twice (work area 0,80 for a 40 px bar, KI-114). Another AppBar
    /// stacked on the same edge looks the same, so this only says the edge is worth re-checking, not that it is wrong.
    /// An empty strip is never over-reserved.
    /// </summary>
    public static bool IsOverReserved(AppBarEdge edge, PixelRect strip, PixelRect workArea)
    {
        if (strip.IsEmpty)
        {
            return false;
        }

        return edge switch
        {
            AppBarEdge.Top => workArea.Top > strip.Bottom,
            AppBarEdge.Bottom => workArea.Bottom < strip.Top,
            AppBarEdge.Left => workArea.Left > strip.Right,
            _ => workArea.Right < strip.Left,
        };
    }

    /// <summary>
    /// The band an over-reserved work area leaves out beyond <paramref name="strip"/> on <paramref name="edge"/>,
    /// across the work area's width (or height): where another AppBar stacked on that edge would sit.
    /// </summary>
    public static PixelRect OverReservedGap(AppBarEdge edge, PixelRect strip, PixelRect workArea) => edge switch
    {
        AppBarEdge.Top => new PixelRect(workArea.Left, strip.Bottom, workArea.Right, workArea.Top),
        AppBarEdge.Bottom => new PixelRect(workArea.Left, workArea.Bottom, workArea.Right, strip.Top),
        AppBarEdge.Left => new PixelRect(strip.Right, workArea.Top, workArea.Left, workArea.Bottom),
        _ => new PixelRect(workArea.Right, workArea.Top, strip.Left, workArea.Bottom),
    };

    /// <summary>
    /// Whether <paramref name="window"/> looks like another AppBar holding <paramref name="band"/>: it covers the whole
    /// band and is about as thick as it (within <see cref="MaximisedWindowRefit.MaxOverhang"/> either side), not an app
    /// window that merely reaches over it, such as one maximised before the work area shrank.
    /// </summary>
    public static bool IsBandOccupant(AppBarEdge edge, PixelRect band, PixelRect window)
    {
        if (window.Left > band.Left || window.Top > band.Top || window.Right < band.Right || window.Bottom < band.Bottom)
        {
            return false;
        }

        const int Slack = MaximisedWindowRefit.MaxOverhang;
        return edge is AppBarEdge.Top or AppBarEdge.Bottom
            ? window.Top >= band.Top - Slack && window.Bottom <= band.Bottom + Slack
            : window.Left >= band.Left - Slack && window.Right <= band.Right + Slack;
    }

    /// <summary>
    /// <paramref name="workArea"/> with only <paramref name="edge"/> moved back to <paramref name="strip"/>'s inner
    /// edge: the value to correct an over-reserved work area to.
    /// </summary>
    public static PixelRect FittedToStrip(AppBarEdge edge, PixelRect strip, PixelRect workArea) => edge switch
    {
        AppBarEdge.Top => workArea with { Top = strip.Bottom },
        AppBarEdge.Bottom => workArea with { Bottom = strip.Top },
        AppBarEdge.Left => workArea with { Left = strip.Right },
        _ => workArea with { Right = strip.Left },
    };

    /// <summary>
    /// <paramref name="workArea"/> with only <paramref name="edge"/> moved out to <paramref name="strip"/>'s outer edge:
    /// the work area as it would be without the bar, recorded as the value to give back on exit. The strip's outer
    /// edge, not the monitor's: a visible taskbar or another AppBar beyond the bar keeps its strip.
    /// </summary>
    public static PixelRect WithoutStrip(AppBarEdge edge, PixelRect strip, PixelRect workArea) => edge switch
    {
        AppBarEdge.Top => workArea with { Top = strip.Top },
        AppBarEdge.Bottom => workArea with { Bottom = strip.Bottom },
        AppBarEdge.Left => workArea with { Left = strip.Left },
        _ => workArea with { Right = strip.Right },
    };

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

    /// <summary>
    /// <paramref name="strip"/> one pixel thicker on the side away from <paramref name="edge"/>: claimed just before the
    /// strip itself, so Explorer sees its rectangle change and recomputes the work area (it ignores a SETPOS of the
    /// rectangle it already holds).
    /// </summary>
    public static PixelRect OnePixelThicker(AppBarEdge edge, PixelRect strip) => edge switch
    {
        AppBarEdge.Top => strip with { Bottom = strip.Bottom + 1 },
        AppBarEdge.Bottom => strip with { Top = strip.Top - 1 },
        AppBarEdge.Left => strip with { Right = strip.Right + 1 },
        _ => strip with { Left = strip.Left - 1 },
    };
}

public enum StripRecoveryKind
{
    /// <summary>The strip is reserved (again); nothing to do.</summary>
    None,
    /// <summary>The strip is missing; check again at <see cref="StripRecoveryStep.DueMs"/>.</summary>
    Wait,
    /// <summary>
    /// The strip has just gone missing: claim the slot the bar holds again (<c>ABM_SETPOS</c>), then check again at
    /// <see cref="StripRecoveryStep.DueMs"/>.
    /// </summary>
    Claim,
    /// <summary>Set the monitor's work area directly now, then check again at <see cref="StripRecoveryStep.DueMs"/>.</summary>
    Shrink,
    /// <summary>
    /// The work area still leaves out more than the strip after a claim: set its edge back to the strip's directly
    /// (KI-114), unless another AppBar's window occupies the extra band.
    /// </summary>
    Correct,
    /// <summary>Register the AppBar again now, then check again at <see cref="StripRecoveryStep.DueMs"/>.</summary>
    Reregister,
    /// <summary>
    /// The attempts are used up; the bar stops acting until the cool-down (<see cref="StripRecovery.ReArmDelayMs"/>)
    /// has passed, after which the next check — the bar's own one-shot timer, a shell notification or a display
    /// pass — starts a fresh episode; an undock, a re-dock or the strip being reserved ends it sooner.
    /// <see cref="StripRecoveryStep.DueMs"/> carries the cool-down's deadline.
    /// </summary>
    GiveUp,
}

public readonly record struct StripRecoveryStep(StripRecoveryKind Kind, long DueMs = 0);

/// <summary>
/// What one bar does while its strip is missing from the work area. Explorer can lose the rectangle a still-registered
/// AppBar claimed (seen on build 26200 with the taskbar hidden: ABM_NEW said the bar was registered, yet a test AppBar
/// on another edge got a work area without our strip, and one ABM_SETPOS of the unchanged rectangle had Explorer apply
/// it within 0.5 s), so an episode starts by claiming the slot again. That is at most one SETPOS per episode and per
/// <see cref="ClaimIntervalMs"/> across episodes: a SETPOS makes Explorer notify every AppBar and recompute the work
/// areas, so two bars (or a bar and an Explorer that keeps dropping the strip, KI-102) must not claim in turn for
/// ever. If the strip is still missing <see cref="FirstActionMs"/> later the bar acts, at most <see cref="MaxAttempts"/> times with a growing
/// gap, then stops acting for a <see cref="ReArmDelayMs"/> cool-down, after which the next
/// check starts a fresh bounded episode (an undock, a re-dock or the strip being reserved ends the cool-down
/// sooner). The action is to set the work area directly (<see cref="WorkAreaFallback"/>) when that is allowed, and
/// to register the AppBar again when it is not (safe mode, which changes no system state); the cool-down applies to
/// both. Re-registering is no longer the normal action: the third live run showed it did not help while Explorer
/// deferred its recompute, and may have restarted that deferral. Pure and clock-free: callers pass a monotonic time
/// in milliseconds.
/// </summary>
public sealed class StripRecovery
{
    /// <summary>How long after claiming the slot again the bar acts on a strip that is still missing.</summary>
    public const long FirstActionMs = 1_500;

    /// <summary>
    /// How long after docking the bar checks its strip. Explorer applies a granted strip within about 0.3 s in the steady
    /// state; when it defers (start-up, ~35 s) the check claims early, which costs one SETPOS within the claim interval.
    /// </summary>
    public const long AfterDockCheckMs = 500;

    /// <summary>The shortest gap between two claims, across episodes; an episode starting sooner only waits.</summary>
    public const long ClaimIntervalMs = 30_000;

    /// <summary>Gap after the first action.</summary>
    public const long SecondActionMs = 5_000;

    /// <summary>Gap after the second action; the third is the last.</summary>
    public const long ThirdActionMs = 20_000;

    public const int MaxAttempts = 3;

    /// <summary>How long an exhausted bar stops acting before a check starts a fresh episode.</summary>
    public const long ReArmDelayMs = 300_000;

    private long? _missingSinceMs;
    private int _attempts;
    private long _nextAttemptMs;
    private long? _reArmAtMs;
    private long? _lastClaimMs; // Survives Reset: it bounds claims across episodes.
    private PixelRect? _overFor;
    private long _overSinceMs;
    private bool _overCorrected;

    /// <summary>True between a missing strip being seen and the strip being reserved again.</summary>
    public bool IsMissing => _missingSinceMs is not null;

    /// <summary>Feeds a fresh check of the work area.</summary>
    /// <param name="reserved">Whether that check found the strip reserved.</param>
    /// <param name="shrinkAllowed">Whether this run may set a work area directly (false in safe mode).</param>
    /// <param name="nowMs">A monotonic time in milliseconds.</param>
    public StripRecoveryStep Update(bool reserved, bool shrinkAllowed, long nowMs)
    {
        if (reserved)
        {
            Reset();
            return new StripRecoveryStep(StripRecoveryKind.None);
        }

        if (_missingSinceMs is null)
        {
            _missingSinceMs = nowMs;
            return StartEpisode(nowMs);
        }

        if (_attempts >= MaxAttempts)
        {
            // The first give-up stamps the cool-down; later ones return the same deadline, so a forced-pass storm
            // can re-point the bar's timer but never push the cool-down later (KI-102).
            _reArmAtMs ??= nowMs + ReArmDelayMs;

            if (nowMs < _reArmAtMs)
            {
                return new StripRecoveryStep(StripRecoveryKind.GiveUp, _reArmAtMs.Value);
            }

            _attempts = 0;
            _reArmAtMs = null;
            return StartEpisode(nowMs);
        }

        if (nowMs < _nextAttemptMs)
        {
            return new StripRecoveryStep(StripRecoveryKind.Wait, _nextAttemptMs);
        }

        _attempts++;
        _nextAttemptMs = nowMs + (_attempts == 1 ? SecondActionMs : ThirdActionMs);
        return new StripRecoveryStep(shrinkAllowed ? StripRecoveryKind.Shrink : StripRecoveryKind.Reregister, _nextAttemptMs);
    }

    /// <summary>
    /// What the bar does about a work area that leaves out more than its strip (KI-114; pass the work area when
    /// <see cref="AppBarReservation.IsOverReserved"/> says so, null otherwise). Per distinct over-reserved value: claim
    /// once (within <see cref="ClaimIntervalMs"/> of any other claim it only waits), so Explorer can recompute from the
    /// AppBars it really holds; if the same value is still there <see cref="FirstActionMs"/> later, correct it, on every
    /// check until the caller <see cref="SettleOverReservation">settles</see> it. After that the value is left alone,
    /// so a legitimately stacked AppBar costs one claim and one look, not one every check.
    /// </summary>
    public StripRecoveryStep UpdateOverReservation(PixelRect? overReservedWorkArea, long nowMs)
    {
        if (overReservedWorkArea is not { } workArea)
        {
            _overFor = null;
            return new StripRecoveryStep(StripRecoveryKind.None);
        }

        if (workArea != _overFor)
        {
            _overFor = workArea;
            _overSinceMs = nowMs;
            _overCorrected = false;
            if (_lastClaimMs is { } last && nowMs - last < ClaimIntervalMs)
            {
                return new StripRecoveryStep(StripRecoveryKind.Wait, nowMs + FirstActionMs);
            }

            _lastClaimMs = nowMs;
            return new StripRecoveryStep(StripRecoveryKind.Claim, nowMs + FirstActionMs);
        }

        if (_overCorrected)
        {
            return new StripRecoveryStep(StripRecoveryKind.None);
        }

        if (nowMs - _overSinceMs < FirstActionMs)
        {
            return new StripRecoveryStep(StripRecoveryKind.Wait, _overSinceMs + FirstActionMs);
        }

        return new StripRecoveryStep(StripRecoveryKind.Correct);
    }

    /// <summary>
    /// The over-reserved value was dealt with (corrected, or found to be another AppBar's): no more looks at it. Not
    /// called when a correction was refused (budget) or failed, so the next check corrects again.
    /// </summary>
    public void SettleOverReservation() => _overCorrected = true;

    private StripRecoveryStep StartEpisode(long nowMs)
    {
        _nextAttemptMs = nowMs + FirstActionMs;
        if (_lastClaimMs is { } last && nowMs - last < ClaimIntervalMs)
        {
            return new StripRecoveryStep(StripRecoveryKind.Wait, _nextAttemptMs);
        }

        _lastClaimMs = nowMs;
        return new StripRecoveryStep(StripRecoveryKind.Claim, _nextAttemptMs);
    }

    /// <summary>Forgets the missing strip (it is reserved again, or the bar was undocked).</summary>
    public void Reset()
    {
        _missingSinceMs = null;
        _attempts = 0;
        _nextAttemptMs = 0;
        _reArmAtMs = null;
    }
}
