using WinGnome.Core.Geometry;

namespace WinGnome.Core.Shell;

/// <summary>What a docked bar does when the shell notifies it (ABN_POSCHANGED or ABN_STATECHANGE).</summary>
public enum AppBarRecheck
{
    /// <summary>Nothing changed for this bar: one QUERYPOS was all it cost.</summary>
    None,
    /// <summary>The shell offers another slot (a bar on the same edge came or went): SETPOS there and move.</summary>
    Move,
    /// <summary>
    /// Same slot, but the work area no longer leaves the strip out: register again in that slot (a SETPOS of the
    /// unchanged rectangle was seen not to bring the strip back).
    /// </summary>
    Reclaim,
}

/// <summary>Whether a monitor's work area still leaves a docked AppBar's strip out, and what to do when it doesn't.</summary>
public static class AppBarReservation
{
    /// <summary>
    /// Shortest gap between two reclaims of one bar from notifications. If the shell ever ignored a reclaim, two bars
    /// that reclaim on each other's notifications would otherwise notify each other forever; the debounced display
    /// passes catch anything left after the cool-down.
    /// </summary>
    public const long ReclaimCooldownMs = 2000;

    /// <summary>
    /// True when <paramref name="workArea"/> stops short of <paramref name="strip"/> on <paramref name="edge"/>, so
    /// maximised windows stay clear of it. Explorer recomputes work areas on its own (after the taskbar's auto-hide
    /// state changes, after a monitor is unplugged) and can leave a registered bar's strip out; this is how a bar
    /// notices. A bar stacked behind another AppBar on the same edge still passes (the work area ends beyond both).
    /// An empty strip reserves nothing and always passes.
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
    /// The response to a shell notification. Idempotence is "the shell's slot is ours and the work area already
    /// leaves the strip out", not "the slot is unchanged": Explorer can drop a strip from the work area without moving
    /// any bar, and then only registering again brings it back.
    /// </summary>
    /// <param name="queried">The QUERYPOS answer with the bar's thickness applied.</param>
    /// <param name="bounds">The rectangle the bar holds now.</param>
    /// <param name="requested">The rectangle the bar last asked for (the shell may have adjusted it into <paramref name="bounds"/>).</param>
    /// <param name="stripReserved"><see cref="IsReserved"/> for <paramref name="bounds"/> and a fresh work area.</param>
    /// <param name="msSinceLastReclaim">Time since this bar last reclaimed from a notification (long.MaxValue: never).</param>
    public static AppBarRecheck Decide(PixelRect queried, PixelRect bounds, PixelRect requested, bool stripReserved, long msSinceLastReclaim)
    {
        if (queried != bounds && queried != requested)
        {
            return AppBarRecheck.Move;
        }

        return !stripReserved && msSinceLastReclaim >= ReclaimCooldownMs ? AppBarRecheck.Reclaim : AppBarRecheck.None;
    }
}
