namespace WinGnome.Core.Windows;

/// <summary>
/// Finds the caption buttons of a Windows App SDK app that reports only its maximise button (for Snap Layouts):
/// Dia answers HTMAXBUTTON over maximise but HTCLIENT over close and minimise. Client area beside one button zone
/// could be anything, so this is only used with the web-buttons setting on and for windows that have Windows App
/// SDK caption controls (spec 0009), and the click guard also re-checks the maximise zone on every click.
/// </summary>
public static class CaptionMaxAnchorProbe
{
    private const int HtClient = 1;
    private const int HtCaption = 2;

    /// <summary>Smallest believable width in DIPs of the maximise zone.</summary>
    public const double MinButtonWidth = 30;

    /// <summary>
    /// Derives the group from the hit-test codes along the row (sampled at <paramref name="xs"/>, right to left).
    /// The codes must read, from the right: optional resize-border codes (at most
    /// <see cref="CaptionHitTestProbe.MaxEdgeGap"/>), an HTCLIENT close zone, immediately an HTMAXBUTTON zone at
    /// least <see cref="MinButtonWidth"/> wide, then immediately at least as much HTCLIENT again (the minimise
    /// zone, taken to be as wide as maximise). The close zone (with the border over it) and the maximise zone
    /// must be about the same width (<see cref="CaptionHitTestProbe.MaxButtonRatio"/>), and no other button
    /// code may appear in the row.
    /// </summary>
    /// <returns>The screen x of the group's left edge and of the maximise zone's middle, or null.</returns>
    public static (int GroupLeft, int MaximiseMiddle)? FindGroup(IReadOnlyList<int> xs, IReadOnlyList<int> codes, double dpiScale)
    {
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(codes);
        if (xs.Count != codes.Count || xs.Count == 0 || HasOtherButtonCodes(codes))
        {
            return null;
        }

        var scale = CaptionHitTestProbe.ValidScale(dpiScale);
        var i = 0;
        while (i < codes.Count && codes[i] is not (HtClient or HtCaption or CaptionHitTestProbe.HtMaxButton))
        {
            i++;
        }

        var edge = i < codes.Count ? xs[0] - xs[i] : 0;
        if (i == codes.Count || codes[i] != HtClient || edge > CaptionHitTestProbe.MaxEdgeGap * scale)
        {
            return null;
        }

        var closeStart = i;
        Skip(codes, ref i, HtClient);
        if (i == codes.Count || codes[i] != CaptionHitTestProbe.HtMaxButton)
        {
            return null;
        }

        var closeWidth = xs[closeStart] - xs[i - 1] + 1 + edge;
        var maxStart = i;
        Skip(codes, ref i, CaptionHitTestProbe.HtMaxButton);
        if (i == codes.Count || codes[i] != HtClient)
        {
            return null;
        }

        var maxWidth = xs[maxStart] - xs[i - 1] + 1;
        ReadOnlySpan<int> widths = [closeWidth, maxWidth];
        if (maxWidth < (MinButtonWidth * scale) - CaptionHitTestProbe.HorizontalStep || !CaptionHitTestProbe.RoughlyEqual(widths))
        {
            return null;
        }

        // The minimise zone: client area for at least the maximise zone's width.
        var maxLeft = xs[i] + 1;
        var minStart = i;
        Skip(codes, ref i, HtClient);
        var minRun = i == codes.Count ? xs[minStart] - xs[i - 1] + 1 : xs[minStart] - xs[i];
        if (minRun < maxWidth - CaptionHitTestProbe.HorizontalStep)
        {
            return null;
        }

        for (var rest = i; rest < codes.Count; rest++)
        {
            if (codes[rest] == CaptionHitTestProbe.HtMaxButton)
            {
                return null;
            }
        }

        return (maxLeft - maxWidth, (xs[maxStart] + xs[minStart - 1]) / 2);
    }

    private static void Skip(IReadOnlyList<int> codes, ref int i, int code)
    {
        while (i < codes.Count && codes[i] == code)
        {
            i++;
        }
    }

    private static bool HasOtherButtonCodes(IReadOnlyList<int> codes)
    {
        foreach (var code in codes)
        {
            if (code is CaptionHitTestProbe.HtMinButton or CaptionHitTestProbe.HtClose)
            {
                return true;
            }
        }

        return false;
    }
}
