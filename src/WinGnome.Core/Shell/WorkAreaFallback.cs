using WinGnome.Core.Geometry;

namespace WinGnome.Core.Shell;

/// <summary>
/// Shrinks a monitor's work area past a strip Explorer granted an AppBar but did not apply. Explorer applies strips
/// only inside its own taskbar layout pass, which it defers while its taskbar is auto-hidden (measured ~35 s) and can
/// skip entirely after a monitor was unplugged; this is the documented <c>SPI_SETWORKAREA</c> value it would have
/// written, computed here so the geometry is testable.
/// </summary>
public static class WorkAreaFallback
{
    /// <summary>
    /// The work area with only <paramref name="edge"/> moved past <paramref name="strip"/>, or null when there is
    /// nothing to do: the strip is empty, the work area already leaves it out (so the taskbar's and other AppBars'
    /// strips stay as they are), or the strip covers the whole monitor and shrinking would invert the work area.
    /// Starting from the caller's fresh read and moving one edge inward is what keeps every other reservation intact
    /// and makes two bars on one edge stack instead of overwriting each other.
    /// </summary>
    public static PixelRect? Shrink(AppBarEdge edge, PixelRect strip, PixelRect workArea)
    {
        if (AppBarReservation.IsReserved(edge, strip, workArea))
        {
            return null;
        }

        var shrunk = edge switch
        {
            AppBarEdge.Top => workArea with { Top = Math.Max(workArea.Top, strip.Bottom) },
            AppBarEdge.Bottom => workArea with { Bottom = Math.Min(workArea.Bottom, strip.Top) },
            AppBarEdge.Left => workArea with { Left = Math.Max(workArea.Left, strip.Right) },
            _ => workArea with { Right = Math.Min(workArea.Right, strip.Left) },
        };

        return shrunk.IsEmpty ? null : shrunk;
    }
}
