using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;

namespace WinGnome.Core.Windows;

/// <summary>Which caption action a traffic-light circle triggers.</summary>
public enum CaptionButtonKind { Close, Minimize, Maximize }

/// <summary>Centre of one circle, in device-independent pixels relative to the overlay's top-left corner.</summary>
public sealed record CaptionButtonSlot(CaptionButtonKind Kind, double CenterX, double CenterY);

/// <summary>Where the overlay window goes (physical pixels) and where each circle sits inside it (DIPs).</summary>
public sealed record CaptionOverlayLayout(PixelRect Bounds, double Diameter, IReadOnlyList<CaptionButtonSlot> Buttons);

/// <summary>Computes the geometry of the traffic-light overlay drawn over a window's title bar.</summary>
public static class CaptionButtonLayout
{
    /// <summary>Padding in DIPs between the circle group and the overlay's edge.</summary>
    public const double Padding = 12;

    /// <summary>Gap in DIPs between the window's left edge and an overlay placed on the left.</summary>
    public const double LeftInset = 8;

    /// <summary>Smallest circle diameter in DIPs the layout will shrink to.</summary>
    public const double MinDiameter = 8;

    /// <summary>Width in DIPs of the native minimise, maximise and close buttons of a standard Windows 11 caption.</summary>
    private const int HeaderBarNativeButtonsWidth = 138;

    /// <summary>
    /// Computes the overlay layout, or null when the native caption buttons rectangle is empty or the DPI scale is invalid.
    /// </summary>
    /// <param name="nativeButtonsScreen">Screen rectangle of the native min/max/close buttons.</param>
    /// <param name="windowScreen">Screen rectangle of the window.</param>
    /// <param name="dpiScale">Physical pixels per DIP (1.0 = 96 dpi).</param>
    /// <param name="settings">User settings (side, order, diameter, spacing).</param>
    public static CaptionOverlayLayout? Compute(PixelRect nativeButtonsScreen, PixelRect windowScreen, double dpiScale, WindowButtonSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        CaptionButtonKind[] order = settings.Order == ButtonOrder.CloseMinimizeMaximize
            ? [CaptionButtonKind.Close, CaptionButtonKind.Minimize, CaptionButtonKind.Maximize]
            : [CaptionButtonKind.Minimize, CaptionButtonKind.Maximize, CaptionButtonKind.Close];
        return Compute(nativeButtonsScreen, windowScreen, dpiScale, settings, order);
    }

    private static CaptionOverlayLayout? Compute(
        PixelRect nativeButtonsScreen, PixelRect windowScreen, double dpiScale, WindowButtonSettings settings, CaptionButtonKind[] order)
    {
        if (nativeButtonsScreen.IsEmpty || !double.IsFinite(dpiScale) || dpiScale <= 0)
        {
            return null;
        }

        var count = order.Length;

        var diameter = Math.Max(MinDiameter, double.IsFinite(settings.Diameter) ? settings.Diameter : MinDiameter);
        var spacing = Math.Max(0, double.IsFinite(settings.Spacing) ? settings.Spacing : 0);
        var naturalGroup = (count * diameter) + ((count - 1) * spacing);

        var isLeft = settings.Side == ButtonSide.Left;
        PixelRect bounds;
        if (isLeft)
        {
            var left = windowScreen.Left + RoundToInt(LeftInset * dpiScale);
            var width = (int)Math.Ceiling(((Padding * 2) + naturalGroup) * dpiScale);
            bounds = new PixelRect(left, nativeButtonsScreen.Top, left + width, nativeButtonsScreen.Bottom);
        }
        else
        {
            bounds = nativeButtonsScreen;
        }

        var widthDip = bounds.Width / dpiScale;
        var heightDip = bounds.Height / dpiScale;

        // Shrink to fit: spacing first, then the diameter itself (never below MinDiameter).
        var available = Math.Max(0, widthDip - (Padding * 2));
        if (naturalGroup > available + 1e-6)
        {
            if (count * diameter <= available)
            {
                spacing = (available - (count * diameter)) / (count - 1);
            }
            else
            {
                spacing = 0;
                diameter = Math.Max(MinDiameter, available / count);
            }
        }

        var group = (count * diameter) + ((count - 1) * spacing);
        var firstCenter = isLeft
            ? Padding + (diameter / 2)
            : widthDip - Padding - group + (diameter / 2);
        var centerY = heightDip / 2;

        var slots = new CaptionButtonSlot[count];
        for (var i = 0; i < count; i++)
        {
            slots[i] = new CaptionButtonSlot(order[i], firstCenter + (i * (diameter + spacing)), centerY);
        }

        return new CaptionOverlayLayout(bounds, diameter, slots);
    }

    /// <summary>
    /// Lays out the circles in a header bar that WinGnome draws itself (the settings window, the app picker), as if
    /// the bar were a <paramref name="widthDip"/> × <paramref name="heightDip"/> window at 100 % whose native buttons
    /// fill the standard 138 DIP block on the right. The result uses the same rules as the overlay, so the header bar
    /// and the settings preview match what other windows get.
    /// </summary>
    /// <returns>
    /// The layout in DIPs: <see cref="CaptionOverlayLayout.Bounds"/> is the button group's area inside the bar and the
    /// slot centres are relative to it. Null when the bar has no area.
    /// </returns>
    /// <param name="widthDip">Width of the bar in DIPs.</param>
    /// <param name="heightDip">Height of the bar in DIPs.</param>
    /// <param name="settings">User settings (side, order, diameter, spacing).</param>
    /// <param name="closeOnly">Lay out only the close circle (dialogs), at the end of the bar on the configured side.</param>
    public static CaptionOverlayLayout? ComputeForHeaderBar(double widthDip, double heightDip, WindowButtonSettings settings, bool closeOnly = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!double.IsFinite(widthDip) || !double.IsFinite(heightDip))
        {
            return null;
        }

        var bar = PixelRect.FromSize(0, 0, RoundToInt(widthDip), RoundToInt(heightDip));
        if (bar.IsEmpty)
        {
            return null;
        }

        var native = new PixelRect(Math.Max(0, bar.Right - HeaderBarNativeButtonsWidth), 0, bar.Right, bar.Bottom);
        return closeOnly
            ? Compute(native, bar, 1.0, settings, [CaptionButtonKind.Close])
            : Compute(native, bar, 1.0, settings);
    }

    private static int RoundToInt(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
