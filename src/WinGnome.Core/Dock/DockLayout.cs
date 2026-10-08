using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;

namespace WinGnome.Core.Dock;

/// <summary>Where the dock sits when shown, where it parks when hidden, and where the pointer reveals it.</summary>
public sealed record DockGeometry(PixelRect Bounds, PixelRect HiddenBounds, PixelRect TriggerZone);

/// <summary>Dock geometry and hover magnification maths.</summary>
public static class DockLayout
{
    /// <summary>Thickness in physical pixels of the reveal strip along the screen edge.</summary>
    public const int TriggerThickness = 2;

    /// <summary>
    /// Computes the dock rectangles on <paramref name="monitor"/> (physical pixels).
    /// </summary>
    /// <param name="monitor">Monitor bounds.</param>
    /// <param name="position">Which edge the dock hugs.</param>
    /// <param name="itemCount">Number of icons (at least one cell is always reserved).</param>
    /// <param name="iconSizePx">Icon size in physical pixels.</param>
    /// <param name="dpiScale">Physical pixels per DIP.</param>
    /// <param name="extendToEdges">Stretch along the whole edge with no gap instead of floating in the middle.</param>
    public static DockGeometry Compute(PixelRect monitor, DockPosition position, int itemCount, double iconSizePx, double dpiScale, bool extendToEdges)
    {
        var scale = double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1;
        var icon = double.IsFinite(iconSizePx) && iconSizePx > 0 ? iconSizePx : 0;

        var padding = Px(6 * scale);
        var cell = Px(icon + (2 * padding));
        var thickness = cell + (2 * Px(4 * scale));
        var edgeInset = Px(8 * scale);
        var gap = extendToEdges ? 0 : edgeInset;

        var horizontal = position == DockPosition.Bottom;
        var monitorLength = horizontal ? monitor.Width : monitor.Height;

        int length;
        if (extendToEdges)
        {
            length = monitorLength;
        }
        else
        {
            var desired = (Math.Max(1, itemCount) * cell) + (2 * edgeInset);
            length = Math.Max(1, Math.Min(desired, monitorLength - (2 * edgeInset)));
            length = Math.Min(length, monitorLength);
        }

        var lengthStart = (horizontal ? monitor.Left : monitor.Top) + ((monitorLength - length) / 2);

        PixelRect bounds;
        PixelRect hidden;
        PixelRect trigger;
        switch (position)
        {
            case DockPosition.Left:
                bounds = PixelRect.FromSize(monitor.Left + gap, lengthStart, thickness, length);
                hidden = PixelRect.FromSize(monitor.Left - thickness, lengthStart, thickness, length);
                trigger = PixelRect.FromSize(monitor.Left, bounds.Top, TriggerThickness, length);
                break;

            case DockPosition.Right:
                bounds = PixelRect.FromSize(monitor.Right - gap - thickness, lengthStart, thickness, length);
                hidden = PixelRect.FromSize(monitor.Right, lengthStart, thickness, length);
                trigger = PixelRect.FromSize(monitor.Right - TriggerThickness, bounds.Top, TriggerThickness, length);
                break;

            default:
                bounds = PixelRect.FromSize(lengthStart, monitor.Bottom - gap - thickness, length, thickness);
                hidden = PixelRect.FromSize(lengthStart, monitor.Bottom, length, thickness);
                trigger = PixelRect.FromSize(bounds.Left, monitor.Bottom - TriggerThickness, length, TriggerThickness);
                break;
        }

        return new DockGeometry(bounds, hidden, trigger);
    }

    /// <summary>
    /// Hover magnification factor for an icon whose centre is <paramref name="distancePx"/> from the pointer.
    /// Falls off with cos squared over a range of 2.5 icon sizes; returns 1 outside the range or when
    /// <paramref name="maxScale"/> is 1 or less.
    /// </summary>
    public static double MagnificationScale(double distancePx, double iconSizePx, double maxScale)
    {
        if (!double.IsFinite(distancePx) || !double.IsFinite(iconSizePx) || !double.IsFinite(maxScale) || maxScale <= 1)
        {
            return 1;
        }

        var range = 2.5 * iconSizePx;
        var distance = Math.Abs(distancePx);
        if (range <= 0 || distance >= range)
        {
            return 1;
        }

        var cos = Math.Cos(Math.PI / 2 * (distance / range));
        return 1 + ((maxScale - 1) * cos * cos);
    }

    private static int Px(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
