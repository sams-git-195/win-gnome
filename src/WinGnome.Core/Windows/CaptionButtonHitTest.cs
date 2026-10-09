namespace WinGnome.Core.Windows;

/// <summary>Maps a pointer position inside the traffic-light overlay to the circle it targets.</summary>
public static class CaptionButtonHitTest
{
    /// <summary>
    /// Finds the button under <paramref name="x"/> (DIPs from the overlay's left edge). Each circle owns the
    /// full overlay height and half of the gap to its neighbours on both sides, so the targets are much larger
    /// than the circles themselves and adjacent targets meet without overlapping.
    /// </summary>
    /// <returns>The button, or null when the pointer is over the padding outside the group.</returns>
    public static CaptionButtonKind? Find(CaptionOverlayLayout layout, double x)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var buttons = layout.Buttons;
        if (buttons.Count == 0 || !double.IsFinite(x))
        {
            return null;
        }

        var pitch = buttons.Count > 1 ? Math.Abs(buttons[1].CenterX - buttons[0].CenterX) : layout.Diameter;
        var halfTarget = Math.Max(layout.Diameter, pitch) / 2;
        foreach (var slot in buttons)
        {
            if (x >= slot.CenterX - halfTarget && x < slot.CenterX + halfTarget)
            {
                return slot.Kind;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the button under a pointer in a header bar laid out by <see cref="CaptionButtonLayout.ComputeForHeaderBar"/>.
    /// Used to answer WM_NCHITTEST, which reports physical pixels while the layout is in DIPs.
    /// </summary>
    /// <param name="layout">The header bar layout, in DIPs from the bar's top-left corner.</param>
    /// <param name="clientX">Pointer x in physical pixels from the window's client-area left edge.</param>
    /// <param name="clientY">Pointer y in physical pixels from the window's client-area top edge.</param>
    /// <param name="dpiScale">Physical pixels per DIP of the window.</param>
    /// <param name="barLeftDip">The bar's left edge in DIPs from the client-area left edge (the maximised margin).</param>
    /// <param name="barTopDip">The bar's top edge in DIPs from the client-area top edge.</param>
    /// <returns>The button, or null when the pointer is outside the button group or the scale is invalid.</returns>
    public static CaptionButtonKind? FindInHeaderBar(
        CaptionOverlayLayout layout, int clientX, int clientY, double dpiScale, double barLeftDip, double barTopDip)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!double.IsFinite(dpiScale) || dpiScale <= 0)
        {
            return null;
        }

        var x = (clientX / dpiScale) - barLeftDip;
        var y = (clientY / dpiScale) - barTopDip;
        var bounds = layout.Bounds;
        if (x < bounds.Left || x >= bounds.Right || y < bounds.Top || y >= bounds.Bottom)
        {
            return null;
        }

        return Find(layout, x - bounds.Left);
    }
}
