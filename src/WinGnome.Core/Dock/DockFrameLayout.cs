using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;

namespace WinGnome.Core.Dock;

/// <summary>Physical-pixel rectangles of the dock on one monitor.</summary>
/// <param name="Geometry">The dock body when shown and hidden, and the reveal strip.</param>
/// <param name="Window">
/// Bounds of the dock window: the body, the gap between the body and the screen edge, and transparent
/// headroom that magnified icons grow into.
/// </param>
/// <param name="ReservedThickness">Distance from the screen edge to the body's inner edge (the AppBar strip).</param>
public sealed record DockFrame(DockGeometry Geometry, PixelRect Window, int ReservedThickness);

/// <summary>Sizes the dock window around the <see cref="DockLayout"/> body.</summary>
public static class DockFrameLayout
{
    /// <summary>Smallest icon size (DIP) the dock shrinks to when too many items are open.</summary>
    public const double MinIconSizeDip = 16;

    /// <summary>
    /// Builds the frame for a body computed by <see cref="DockLayout.Compute(PixelRect, DockPosition, int, double, double, bool, DockLayoutOptions)"/>.
    /// </summary>
    /// <param name="monitor">Monitor bounds.</param>
    /// <param name="position">Which edge the dock hugs.</param>
    /// <param name="geometry">The body geometry.</param>
    /// <param name="cellLengthPx">Length of one icon cell (icon plus padding) in physical pixels.</param>
    /// <param name="iconSizePx">Icon size in physical pixels.</param>
    /// <param name="magnification">Maximum hover magnification (1 = none, so no headroom).</param>
    /// <param name="extendToEdges">Panel mode: the body already spans the whole edge.</param>
    public static DockFrame Compute(PixelRect monitor, DockPosition position, DockGeometry geometry, double cellLengthPx,
        double iconSizePx, double magnification, bool extendToEdges)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var growth = double.IsFinite(magnification) && magnification > 1 ? magnification - 1 : 0;
        var cell = double.IsFinite(cellLengthPx) && cellLengthPx > 0 ? cellLengthPx : 0;
        var icon = double.IsFinite(iconSizePx) && iconSizePx > 0 ? iconSizePx : 0;

        // A magnified icon grows away from the edge by at most cell * (magnification - 1).
        var across = (int)Math.Ceiling(cell * growth);

        // Neighbouring icons widen too. With the cos^2 fall-off of DockLayout.MagnificationScale the summed
        // growth is about growth * range (range = 2.5 icons), plus at most one cell of discretisation error.
        // The body stays centred, so half of that lands on each side.
        var along = extendToEdges ? 0 : (int)Math.Ceiling(growth * ((2.5 * icon) + cell) / 2);

        var body = geometry.Bounds;
        PixelRect window;
        int reserved;
        switch (position)
        {
            case DockPosition.Left:
                window = new PixelRect(monitor.Left, body.Top - along, body.Right + across, body.Bottom + along);
                reserved = body.Right - monitor.Left;
                break;

            case DockPosition.Right:
                window = new PixelRect(body.Left - across, body.Top - along, monitor.Right, body.Bottom + along);
                reserved = monitor.Right - body.Left;
                break;

            default:
                window = new PixelRect(body.Left - along, body.Top - across, body.Right + along, monitor.Bottom);
                reserved = monitor.Bottom - body.Top;
                break;
        }

        return new DockFrame(geometry, ClampToMonitor(window, monitor), Math.Max(0, reserved));
    }

    /// <summary>
    /// The icon size (DIP) that lets <paramref name="cellCount"/> icons plus <paramref name="fixedLengthDip"/> of
    /// separators fit along a monitor edge of <paramref name="monitorLengthDip"/>. Never larger than
    /// <paramref name="iconSizeDip"/> and never smaller than <see cref="MinIconSizeDip"/> (unless the requested size is).
    /// </summary>
    public static double FitIconSize(double monitorLengthDip, int cellCount, double fixedLengthDip, double iconSizeDip,
        double iconPaddingDip, double endPaddingDip)
    {
        if (cellCount <= 0 || !double.IsFinite(monitorLengthDip) || !double.IsFinite(iconSizeDip))
        {
            return iconSizeDip;
        }

        // Two end paddings inside the dock plus the same distance kept from each end of the monitor.
        var available = monitorLengthDip - (4 * Math.Max(0, endPaddingDip)) - Math.Max(0, fixedLengthDip);
        var fitting = (available / cellCount) - (2 * Math.Max(0, iconPaddingDip));
        return fitting >= iconSizeDip ? iconSizeDip : Math.Min(iconSizeDip, Math.Max(MinIconSizeDip, fitting));
    }

    private static PixelRect ClampToMonitor(PixelRect window, PixelRect monitor) => new(
        Math.Max(window.Left, monitor.Left),
        Math.Max(window.Top, monitor.Top),
        Math.Min(window.Right, monitor.Right),
        Math.Min(window.Bottom, monitor.Bottom));
}
