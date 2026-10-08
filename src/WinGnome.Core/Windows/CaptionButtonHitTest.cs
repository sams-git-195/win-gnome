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
}
