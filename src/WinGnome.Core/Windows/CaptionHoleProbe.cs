using WinGnome.Core.Geometry;

namespace WinGnome.Core.Windows;

/// <summary>
/// Finds the caption buttons of an app that draws them in HTML (GitHub Desktop): it answers HTCLIENT over its
/// buttons, which are "no-drag" holes in an HTCAPTION drag region. Holes alone could be anything (a search box,
/// a tab), so this is only used for apps with a known button width (<see cref="CaptionDecorationRules.WebButtonWidth"/>)
/// and only accepts exactly three holes of that width at the right edge.
/// </summary>
public static class CaptionHoleProbe
{
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;

    /// <summary>Distance in physical pixels between horizontal samples: the drag strips between holes are 1 px wide.</summary>
    public const int HorizontalStep = 1;

    /// <summary>How far each hole may differ from the expected width, as a fraction of it (plus one sample).</summary>
    public const double WidthTolerance = 0.1;

    /// <summary>Smallest drag region in DIPs that must sit left of the holes.</summary>
    public const double MinCaptionLeft = 8;

    /// <summary>The screen x of every sample along the probed row, right to left, starting one pixel inside the frame.</summary>
    public static int[] RowSamples(PixelRect frame, double dpiScale)
    {
        var scale = CaptionHitTestProbe.ValidScale(dpiScale);
        var count = Math.Max(1, (int)Math.Ceiling(CaptionHitTestProbe.RowLength * scale / HorizontalStep));
        var xs = new int[count];
        for (var i = 0; i < count; i++)
        {
            xs[i] = frame.Right - 1 - (i * HorizontalStep);
        }

        return xs;
    }

    /// <summary>
    /// Derives the left edge of the button group from the hit-test codes along the row (sampled at
    /// <paramref name="xs"/>, right to left). The codes must read, from the right: optional resize-border codes
    /// (at most <see cref="CaptionHitTestProbe.MaxEdgeGap"/>), then three HTCLIENT holes each about
    /// <paramref name="expectedWidthDip"/> wide (the close hole counting the border over it), separated by
    /// HTCAPTION strips up to <see cref="CaptionHitTestProbe.MaxButtonGap"/> wide, then at least
    /// <see cref="MinCaptionLeft"/> of HTCAPTION.
    /// </summary>
    /// <returns>The screen x of the group's left edge, or null when the row does not show exactly that.</returns>
    public static int? FindClientHoles(IReadOnlyList<int> xs, IReadOnlyList<int> codes, double dpiScale, double expectedWidthDip)
    {
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(codes);
        if (xs.Count != codes.Count || xs.Count == 0)
        {
            return null;
        }

        var scale = CaptionHitTestProbe.ValidScale(dpiScale);
        var expected = expectedWidthDip * scale;
        var tolerance = (expected * WidthTolerance) + CaptionHitTestProbe.HorizontalStep;
        var i = 0;
        while (i < codes.Count && codes[i] is not (HtClient or HtCaption))
        {
            i++;
        }

        // The holes must start at the edge: only the resize border may cover the close button's right part.
        var edge = i < codes.Count ? xs[0] - xs[i] : 0;
        if (i == codes.Count || codes[i] != HtClient || edge > CaptionHitTestProbe.MaxEdgeGap * scale)
        {
            return null;
        }

        for (var hole = 0; hole < 3; hole++)
        {
            if (hole > 0 && !CaptionHitTestProbe.SkipGap(xs, codes, ref i, HtCaption, CaptionHitTestProbe.MaxButtonGap * scale))
            {
                return null;
            }

            var start = i;
            while (i < codes.Count && codes[i] == HtClient)
            {
                i++;
            }

            if (i == start || i == codes.Count)
            {
                return null;
            }

            var width = xs[start] - xs[i - 1] + 1 + (hole == 0 ? edge : 0);
            if (Math.Abs(width - expected) > tolerance)
            {
                return null;
            }
        }

        // A short strip followed by a fourth hole, or no drag region at all, is not a caption button group.
        var captionStart = i;
        while (i < codes.Count && codes[i] == HtCaption)
        {
            i++;
        }

        if (i == captionStart || xs[captionStart] - xs[i - 1] + 1 < MinCaptionLeft * scale)
        {
            return null;
        }

        // The edge lies between the last hole sample and the first caption sample.
        return xs[captionStart] + 1;
    }

    /// <summary>
    /// Derives the vertical extent of the holes from one-pixel samples down a column through the close hole
    /// (index 0 is the frame top). Only the top resize border may sit above the hole (at most
    /// <paramref name="maxTopGap"/> rows), and the hole must end at a non-client row before the column does.
    /// </summary>
    /// <returns>
    /// (top, bottom) offsets from the frame top, bottom exclusive, or null. The top is 0: the resize border is
    /// drawn over the button, which is painted from the frame top.
    /// </returns>
    public static (int Top, int Bottom)? FindHoleExtent(IReadOnlyList<int> codes, int maxTopGap)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var top = 0;
        while (top < codes.Count && codes[top] is HtTop or HtTopLeft or HtTopRight)
        {
            top++;
        }

        if (top > maxTopGap || top == codes.Count || codes[top] != HtClient)
        {
            return null;
        }

        var bottom = top;
        while (bottom < codes.Count && codes[bottom] == HtClient)
        {
            bottom++;
        }

        return bottom == codes.Count ? null : (0, bottom);
    }
}
