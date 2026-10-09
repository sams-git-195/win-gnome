using WinGnome.Core.Geometry;

namespace WinGnome.Core.Shell;

/// <summary>Whether a monitor's work area still leaves a docked AppBar's strip out.</summary>
public static class AppBarReservation
{
    /// <summary>
    /// True when <paramref name="workArea"/> stops short of <paramref name="strip"/> on <paramref name="edge"/>, so
    /// maximised windows stay clear of it. Explorer resets work areas on some display changes (a monitor unplugged)
    /// while keeping the registration, and then no ABN_POSCHANGED arrives: this is how a bar notices. A bar stacked
    /// behind another AppBar on the same edge still passes (the work area ends beyond both). An empty strip reserves
    /// nothing and always passes.
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
}
