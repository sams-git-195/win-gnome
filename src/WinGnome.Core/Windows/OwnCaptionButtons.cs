using WinGnome.Core.Geometry;

namespace WinGnome.Core.Windows;

/// <summary>
/// Finds the caption buttons of a window that removes its title bar (its client area covers the caption) and
/// draws smaller buttons of its own where DWM still reports full-size native ones (Steam). Such apps answer
/// HTCLIENT over their buttons and HTCAPTION over the drag area beside them, so the HTCLIENT run at the frame's
/// right edge is where their buttons are. The overlay then only needs to cover that run, not DWM's rectangle.
/// </summary>
public static class OwnCaptionButtons
{
    /// <summary>Smallest believable width in DIPs of an app's own button group (three 16 DIP buttons).</summary>
    public const double MinGroupWidth = 3 * 16;

    /// <summary>
    /// Narrowest HTCAPTION run in DIPs left of the buttons that counts as their edge. Steam leaves 8 DIPs of drag
    /// area between its buttons and its other title-bar controls at 125 %.
    /// </summary>
    public const double MinCaptionRun = 4;

    private const int HtClient = 1;
    private const int HtCaption = 2;

    /// <summary>
    /// The left edge of the app's own buttons from the hit-test codes along the caption row (sampled at
    /// <paramref name="xs"/>, right to left from the frame edge, see <see cref="CaptionHitTestProbe.RowSamples"/>).
    /// The codes must read, from the right: optional resize-border codes, an HTCLIENT run at least
    /// <see cref="MinGroupWidth"/> wide, then an HTCAPTION run at least <see cref="MinCaptionRun"/> wide. The
    /// edge must fall strictly inside <paramref name="nativeButtons"/>, so the result only ever narrows DWM's
    /// rectangle.
    /// </summary>
    /// <returns>The screen x of the buttons' left edge, or null when the row does not show that shape.</returns>
    public static int? FindLeft(IReadOnlyList<int> xs, IReadOnlyList<int> codes, PixelRect nativeButtons, double dpiScale)
    {
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(codes);
        if (xs.Count != codes.Count || xs.Count == 0 || !double.IsFinite(dpiScale) || dpiScale <= 0)
        {
            return null;
        }

        // Only the resize border may sit between the frame edge and the buttons. Windows-drawn button codes
        // mean the native buttons are real, and a caption first means there are no buttons at the edge.
        var i = 0;
        while (i < codes.Count && codes[i] is not (HtClient or HtCaption))
        {
            if (IsButtonCode(codes[i]))
            {
                return null;
            }

            i++;
        }

        if (i == codes.Count || codes[i] != HtClient || xs[0] - xs[i] > CaptionHitTestProbe.MaxEdgeGap * dpiScale)
        {
            return null;
        }

        var clientStart = i;
        while (i < codes.Count && codes[i] == HtClient)
        {
            i++;
        }

        var captionStart = i;
        while (i < codes.Count && codes[i] == HtCaption)
        {
            i++;
        }

        // Each sample stands for the HorizontalStep pixels up to the next one.
        var clientWidth = (captionStart - clientStart) * CaptionHitTestProbe.HorizontalStep;
        var captionWidth = (i - captionStart) * CaptionHitTestProbe.HorizontalStep;
        if (captionStart == codes.Count || clientWidth < MinGroupWidth * dpiScale || captionWidth < MinCaptionRun * dpiScale)
        {
            return null;
        }

        // The edge lies between the last client sample and the first caption one; the buttons get the gap.
        var left = xs[captionStart] + 1;
        return left > nativeButtons.Left && left < nativeButtons.Right ? left : null;
    }

    private static bool IsButtonCode(int code) =>
        code is CaptionHitTestProbe.HtMinButton or CaptionHitTestProbe.HtMaxButton or CaptionHitTestProbe.HtClose;
}
