using WinGnome.Core.Geometry;

namespace WinGnome.Core.Windows;

/// <summary>
/// Finds the caption buttons of a window that draws its own title bar by asking it what is under a row of
/// points (WM_NCHITTEST). Apps that support Snap Layouts answer HTMINBUTTON, HTMAXBUTTON and HTCLOSE over their
/// own buttons; anything else is treated as "unknown" and the window is left alone.
/// </summary>
public static class CaptionHitTestProbe
{
    public const int HtMinButton = 8;
    public const int HtMaxButton = 9;
    public const int HtClose = 20;

    /// <summary>How far left of the frame's right edge the row is probed, in DIPs (three 46 DIP buttons and margin).</summary>
    public const double RowLength = 200;

    /// <summary>How far down from the frame's top edge the column is probed, in DIPs.</summary>
    public const double ColumnLength = 64;

    /// <summary>Distance in DIPs below the frame top where the row is probed (inside every caption-button height).</summary>
    public const double RowOffset = 10;

    /// <summary>Distance in physical pixels between horizontal samples.</summary>
    public const int HorizontalStep = 2;

    /// <summary>Largest gap in DIPs allowed between the frame's right edge and the close button.</summary>
    public const double MaxEdgeGap = 8;

    /// <summary>Smallest believable width in DIPs of the whole group (three buttons).</summary>
    public const double MinGroupWidth = 3 * 30;

    /// <summary>
    /// Widest HTCAPTION gap in DIPs allowed between two buttons. Docker Desktop leaves a 2 px drag strip between
    /// its maximise and minimise buttons at 125 %.
    /// </summary>
    public const double MaxButtonGap = 2;

    /// <summary>Largest ratio between the widest and the narrowest button: the three are drawn the same size.</summary>
    public const double MaxButtonRatio = 1.25;

    /// <summary>
    /// Largest gap in DIPs between the frame top and the close button. Docker Desktop's close button starts 9 px
    /// (7 DIPs at 125 %) below the top.
    /// </summary>
    public const double MaxTopGap = 8;

    private const int HtCaption = 2;

    /// <summary>
    /// The screen x of every sample along the probed row, right to left, starting one pixel inside the frame.
    /// </summary>
    public static int[] RowSamples(PixelRect frame, double dpiScale)
    {
        var scale = ValidScale(dpiScale);
        var count = Math.Max(1, (int)Math.Ceiling(RowLength * scale / HorizontalStep));
        var xs = new int[count];
        for (var i = 0; i < count; i++)
        {
            xs[i] = frame.Right - 1 - (i * HorizontalStep);
        }

        return xs;
    }

    /// <summary>The screen y of the probed row.</summary>
    public static int RowY(PixelRect frame, double dpiScale) =>
        frame.Top + (int)Math.Round(RowOffset * ValidScale(dpiScale), MidpointRounding.AwayFromZero);

    /// <summary>Rows above the close button allowed in the probed column (<see cref="MaxTopGap"/> in pixels).</summary>
    public static int MaxTopGapPixels(double dpiScale) =>
        (int)Math.Round(MaxTopGap * ValidScale(dpiScale), MidpointRounding.AwayFromZero);

    /// <summary>Number of one-pixel samples down the column (starting at the frame top).</summary>
    public static int ColumnSampleCount(double dpiScale) => Math.Max(1, (int)Math.Ceiling(ColumnLength * ValidScale(dpiScale)));

    /// <summary>
    /// Derives the left edge of the button group from the hit-test codes along the row (sampled at
    /// <paramref name="xs"/>, right to left). The codes must read, from the right: optional non-button codes
    /// (the resize border), then HTCLOSE, HTMAXBUTTON and HTMINBUTTON runs, then non-button codes only. Two
    /// runs may be separated by an HTCAPTION strip up to <see cref="MaxButtonGap"/> wide; the runs must be about
    /// the same width (<see cref="MaxButtonRatio"/>) and together at least <see cref="MinGroupWidth"/> wide.
    /// </summary>
    /// <returns>The screen x of the group's left edge, or null when the row does not show exactly that trio.</returns>
    public static int? FindGroupLeft(IReadOnlyList<int> xs, IReadOnlyList<int> codes, double dpiScale)
    {
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(codes);
        if (xs.Count != codes.Count || xs.Count == 0)
        {
            return null;
        }

        var scale = ValidScale(dpiScale);
        var i = 0;
        while (i < codes.Count && !IsButton(codes[i]))
        {
            i++;
        }

        // Only the resize border may sit between the frame edge and the close button.
        if (i == codes.Count || xs[0] - xs[i] > MaxEdgeGap * scale)
        {
            return null;
        }

        var groupStart = i;
        int[] expected = [HtClose, HtMaxButton, HtMinButton];
        Span<int> widths = stackalloc int[expected.Length];
        for (var button = 0; button < expected.Length; button++)
        {
            if (button > 0 && !SkipGap(xs, codes, ref i, HtCaption, MaxButtonGap * scale))
            {
                return null;
            }

            var start = i;
            while (i < codes.Count && codes[i] == expected[button])
            {
                i++;
            }

            if (i == start || i == codes.Count)
            {
                // Missing zone, or the run reaches the end of the row so its left edge is unknown.
                return null;
            }

            widths[button] = xs[start] - xs[i - 1] + 1;
        }

        if (!RoughlyEqual(widths) || xs[groupStart] - xs[i - 1] + 1 < (MinGroupWidth * scale) - HorizontalStep)
        {
            return null;
        }

        for (var rest = i; rest < codes.Count; rest++)
        {
            if (IsButton(codes[rest]))
            {
                return null;
            }
        }

        // The edge lies between the last minimise sample and the next one; cover the whole gap.
        return xs[i] + 1;
    }

    /// <summary>
    /// Steps over a run of <paramref name="gapCode"/> (possibly empty) between two buttons. False when the run is
    /// wider than <paramref name="maxWidth"/> pixels or reaches the end of the row.
    /// </summary>
    internal static bool SkipGap(IReadOnlyList<int> xs, IReadOnlyList<int> codes, ref int i, int gapCode, double maxWidth)
    {
        var start = i;
        while (i < codes.Count && codes[i] == gapCode)
        {
            i++;
        }

        return i < codes.Count && (i == start || xs[start] - xs[i - 1] + 1 <= maxWidth);
    }

    /// <summary>True when the widest of <paramref name="widths"/> is at most <see cref="MaxButtonRatio"/> times the narrowest.</summary>
    internal static bool RoughlyEqual(ReadOnlySpan<int> widths)
    {
        int min = int.MaxValue, max = 0;
        foreach (var width in widths)
        {
            min = Math.Min(min, width);
            max = Math.Max(max, width);
        }

        return min > 0 && max <= min * MaxButtonRatio;
    }

    /// <summary>
    /// Derives the vertical extent of the buttons from one-pixel samples down a column through the close button
    /// (index 0 is the frame top). The close zone must start within the top <paramref name="maxTopGap"/> samples
    /// and end before the column does.
    /// </summary>
    /// <returns>(top, bottom) offsets from the frame top, bottom exclusive, or null.</returns>
    public static (int Top, int Bottom)? FindCloseExtent(IReadOnlyList<int> codes, int maxTopGap)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var top = 0;
        while (top < codes.Count && codes[top] != HtClose)
        {
            top++;
        }

        if (top >= codes.Count || top > maxTopGap)
        {
            return null;
        }

        var bottom = top;
        while (bottom < codes.Count && codes[bottom] == HtClose)
        {
            bottom++;
        }

        return bottom == codes.Count ? null : (top, bottom);
    }

    /// <summary>
    /// Where the overlay must go: from the group's left edge to the frame's right edge, over the probed rows.
    /// </summary>
    public static PixelRect ButtonsRect(PixelRect frame, int groupLeft, (int Top, int Bottom) extent) =>
        new(groupLeft, frame.Top + extent.Top, frame.Right, frame.Top + extent.Bottom);

    private static bool IsButton(int code) => code is HtMinButton or HtMaxButton or HtClose;

    internal static double ValidScale(double dpiScale) => double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1;
}
