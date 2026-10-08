namespace WinGnome.Core.TopBar;

/// <summary>How a top-bar popup lines up with the item that opened it.</summary>
public enum PopupAlignment
{
    /// <summary>Card's left edge on the anchor's left edge.</summary>
    Start,
    /// <summary>Card centred under the anchor (the clock).</summary>
    Center,
    /// <summary>Card's right edge on the anchor's right edge (the system menu).</summary>
    End,
}

/// <summary>Positions a popup whose visible card is surrounded by a transparent shadow margin.</summary>
public static class PopupPlacement
{
    /// <summary>
    /// Horizontal offset, relative to the anchor's left edge, for a popup of <paramref name="popupWidth"/>
    /// (which includes <paramref name="shadowMargin"/> on both sides) so that the visible card is aligned.
    /// </summary>
    public static double HorizontalOffset(PopupAlignment alignment, double anchorWidth, double popupWidth, double shadowMargin) =>
        alignment switch
        {
            PopupAlignment.Start => -shadowMargin,
            PopupAlignment.End => anchorWidth - popupWidth + shadowMargin,
            _ => (anchorWidth - popupWidth) / 2,
        };
}
