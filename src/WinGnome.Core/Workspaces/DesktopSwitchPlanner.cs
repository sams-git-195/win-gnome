namespace WinGnome.Core.Workspaces;

/// <summary>
/// Turns "go to desktop N" requests into a number of Ctrl+Win+arrow presses. Explorer only updates its registry
/// state after a switch has happened, so a request made while an earlier switch is still on its way (a double-click
/// on a dot, two dots clicked quickly) is counted from the desktop being switched to, not the stale registry index;
/// otherwise the extra presses would overshoot.
/// </summary>
public sealed class DesktopSwitchPlanner
{
    /// <summary>How long a requested switch is trusted over the registry before falling back to it (e.g. the switch was refused).</summary>
    public static readonly TimeSpan PendingTimeout = TimeSpan.FromSeconds(1.5);

    private int? _pendingTarget;
    private DateTime _pendingSince;

    /// <summary>The desktop new requests are counted from: a switch still on its way, or else the current one.</summary>
    public int EffectiveIndex(VirtualDesktopState state, DateTime now) =>
        _pendingTarget is { } target && target < state.Count && now - _pendingSince < PendingTimeout
            ? target
            : state.CurrentIndex;

    /// <summary>Signed number of switches (positive = right) to reach <paramref name="index"/> (clamped to the existing desktops).</summary>
    public int PlanSwitchTo(VirtualDesktopState state, int index, DateTime now)
    {
        var from = EffectiveIndex(state, now);
        var target = Math.Clamp(index, 0, Math.Max(0, state.Count - 1));
        var steps = VirtualDesktopState.StepsTo(from, target);
        if (steps != 0)
        {
            _pendingTarget = target;
            _pendingSince = now;
        }

        return steps;
    }

    /// <summary>Signed number of switches to move <paramref name="delta"/> desktops from the effective one (stops at the ends).</summary>
    public int PlanSwitchBy(VirtualDesktopState state, int delta, DateTime now) =>
        PlanSwitchTo(state, EffectiveIndex(state, now) + delta, now);

    /// <summary>Feeds a fresh registry state; the pending switch is forgotten once Explorer reports its target.</summary>
    public void Observe(VirtualDesktopState state)
    {
        if (_pendingTarget is { } target && (target == state.CurrentIndex || target >= state.Count))
        {
            _pendingTarget = null;
        }
    }
}
