using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;

namespace WinGnome.Core.Dock;

/// <summary>The size in pixels of the window a thumbnail would show (DWM's source size).</summary>
public readonly record struct DockPreviewSourceSize(int Width, int Height);

/// <summary>
/// Where one window's item sits in the popover. Every rectangle is relative to the popover's client area, in physical
/// pixels.
/// </summary>
/// <param name="Item">The whole item: caption row above the thumbnail box, with padding.</param>
/// <param name="Caption">The caption row: the title on the left and <paramref name="Close"/> on the right.</param>
/// <param name="Close">The close button, inside <paramref name="Caption"/> at its right edge.</param>
/// <param name="Box">The thumbnail box the thumbnail is fitted into.</param>
/// <param name="Thumbnail">
/// The largest rectangle of the source's aspect ratio centred in <paramref name="Box"/>; empty for a placeholder.
/// </param>
/// <param name="IsPlaceholder">
/// True when the window has no usable picture (unknown or under 32 px in either dimension): show the app icon centred
/// in <paramref name="Box"/> instead of a thumbnail.
/// </param>
public sealed record DockPreviewItemLayout(
    PixelRect Item,
    PixelRect Caption,
    PixelRect Close,
    PixelRect Box,
    PixelRect Thumbnail,
    bool IsPlaceholder);

/// <summary>The popover for one dock icon.</summary>
/// <param name="Popover">The popover window's rectangle in screen pixels; empty when there are no windows.</param>
/// <param name="Items">
/// One entry per window shown, in input order (the first <c>Items.Count</c> windows). When some windows do not fit the
/// rest are summarised by <paramref name="OverflowItem"/>.
/// </param>
/// <param name="OverflowCount">How many windows the "+N more" item stands for; 0 when all fit.</param>
/// <param name="OverflowItem">The "+N more" item, relative to the popover; empty when <paramref name="OverflowCount"/> is 0.</param>
public sealed record DockPreviewPlan(
    PixelRect Popover,
    IReadOnlyList<DockPreviewItemLayout> Items,
    int OverflowCount,
    PixelRect OverflowItem);

/// <summary>
/// Pure geometry of the window preview popover (spec 0024), physical pixels in and out. Sizes are laid out in
/// device-independent pixels and converted per edge with the monitor's scale, rounding half away from zero.
/// </summary>
public static class DockPreviewLayout
{
    /// <summary>Largest thumbnail box width.</summary>
    public const double MaxBoxWidthDip = 224;

    /// <summary>Smallest thumbnail box width before windows overflow into "+N more".</summary>
    public const double MinBoxWidthDip = 112;

    /// <summary>Thumbnail box height as a fraction of its width.</summary>
    public const double BoxAspect = 0.625;

    /// <summary>Padding around the box inside an item.</summary>
    public const double ItemPaddingDip = 8;

    /// <summary>Height of the caption row.</summary>
    public const double CaptionHeightDip = 28;

    /// <summary>Side of the square close button.</summary>
    public const double CloseSizeDip = 24;

    /// <summary>Space between items.</summary>
    public const double ItemGapDip = 4;

    /// <summary>Padding between the popover edge and its items.</summary>
    public const double PopoverPaddingDip = 8;

    /// <summary>Space between the dock window's inner edge and the popover.</summary>
    public const double DockGapDip = 8;

    /// <summary>Space kept free at the edges of the monitor's usable area.</summary>
    public const double ScreenMarginDip = 8;

    /// <summary>A source smaller than this in either dimension has no usable picture.</summary>
    public const int MinUsableSourcePx = 32;

    // Absorbs floating point noise in the "does it fit exactly" comparisons at non-integer scales.
    private const double Epsilon = 1e-6;

    /// <summary>
    /// Lays out the popover beside the dock: above a bottom dock, right of a left dock, left of a right dock.
    /// </summary>
    /// <param name="position">Which edge the dock hugs.</param>
    /// <param name="dockWindow">The dock window's rectangle (<see cref="DockFrame.Window"/>), screen pixels.</param>
    /// <param name="anchor">The icon's centre along the dock (x for a bottom dock, y for a side dock), screen pixels.</param>
    /// <param name="monitorBounds">The dock's monitor.</param>
    /// <param name="workArea">The monitor's work area; the monitor bounds are used when it is empty.</param>
    /// <param name="scale">The monitor's DPI scale (1 = 100 %); 1 when not a positive number.</param>
    /// <param name="sources">One entry per window, in display order; null when the source size is unknown.</param>
    public static DockPreviewPlan Compute(DockPosition position, PixelRect dockWindow, int anchor, PixelRect monitorBounds,
        PixelRect workArea, double scale, IReadOnlyList<DockPreviewSourceSize?> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            return new DockPreviewPlan(default, [], 0, default);
        }

        var s = double.IsFinite(scale) && scale > 0 ? scale : 1;
        var vertical = position != DockPosition.Bottom;
        var area = UsableArea(monitorBounds, workArea);
        var gapPx = Px(DockGapDip, s);
        var marginPx = Px(ScreenMarginDip, s);

        var alongSpace = ((vertical ? area.Height : area.Width) / s) - (2 * ScreenMarginDip);
        var acrossSpace = AcrossSpacePx(position, dockWindow, area, gapPx, marginPx) / s;

        var (boxWidth, visible, overflow) = FitBoxes(sources.Count, vertical, alongSpace, acrossSpace);
        var slots = visible + (overflow > 0 ? 1 : 0);
        var along = (slots * AlongCell(boxWidth, vertical)) + ((slots - 1) * ItemGapDip) + (2 * PopoverPaddingDip);
        var across = AcrossCell(boxWidth, vertical) + (2 * PopoverPaddingDip);
        var width = Px(vertical ? across : along, s);
        var height = Px(vertical ? along : across, s);

        var popover = Place(position, dockWindow, anchor, area, gapPx, marginPx, width, height);

        var items = new List<DockPreviewItemLayout>(visible);
        for (var i = 0; i < visible; i++)
        {
            items.Add(ItemAt(i, boxWidth, vertical, s, sources[i]));
        }

        var overflowItem = overflow > 0 ? ItemRect(visible, boxWidth, vertical, s) : default;
        return new DockPreviewPlan(popover, items, overflow, overflowItem);
    }

    /// <summary>The work area clipped to the monitor, or the whole monitor when that leaves nothing.</summary>
    private static PixelRect UsableArea(PixelRect monitorBounds, PixelRect workArea)
    {
        var area = workArea.Intersect(monitorBounds);
        return area.IsEmpty ? monitorBounds : area;
    }

    /// <summary>Pixels between the dock window (plus the gap) and the far margin of the usable area.</summary>
    private static double AcrossSpacePx(DockPosition position, PixelRect dockWindow, PixelRect area, int gapPx, int marginPx) =>
        position switch
        {
            DockPosition.Left => (area.Right - marginPx) - (dockWindow.Right + gapPx),
            DockPosition.Right => (dockWindow.Left - gapPx) - (area.Left + marginPx),
            _ => (dockWindow.Top - gapPx) - (area.Top + marginPx),
        };

    /// <summary>
    /// The thumbnail box width (DIP) and how many windows get an item. Every item shares one box width: the largest
    /// that fits all windows along the dock and fits across, never above the maximum or below the minimum. When even
    /// the minimum does not fit all windows, the last slot becomes the "+N more" item.
    /// </summary>
    private static (double BoxWidth, int Visible, int Overflow) FitBoxes(int count, bool vertical, double alongSpace, double acrossSpace)
    {
        var contentAlong = alongSpace - (2 * PopoverPaddingDip);
        var fitAlong = BoxForAlongCell((contentAlong - ((count - 1) * ItemGapDip)) / count, vertical);
        if (fitAlong >= MinBoxWidthDip - Epsilon)
        {
            var fitAcross = BoxForAcrossCell(acrossSpace - (2 * PopoverPaddingDip), vertical);
            return (Math.Clamp(Math.Min(fitAlong, fitAcross), MinBoxWidthDip, MaxBoxWidthDip), count, 0);
        }

        var slots = (int)Math.Floor(((contentAlong + ItemGapDip) / (AlongCell(MinBoxWidthDip, vertical) + ItemGapDip)) + Epsilon);

        // One slot is "+N more"; keep at least one window next to it even on a monitor too small for two slots.
        slots = Math.Max(slots, 2);
        return slots >= count ? (MinBoxWidthDip, count, 0) : (MinBoxWidthDip, slots - 1, count - (slots - 1));
    }

    /// <summary>Size of one item along the dock: its width above a bottom dock, its height beside a side dock.</summary>
    private static double AlongCell(double boxWidth, bool vertical) =>
        vertical ? ItemHeight(boxWidth) : ItemWidth(boxWidth);

    private static double AcrossCell(double boxWidth, bool vertical) =>
        vertical ? ItemWidth(boxWidth) : ItemHeight(boxWidth);

    private static double BoxForAlongCell(double cell, bool vertical) =>
        vertical ? BoxForItemHeight(cell) : cell - (2 * ItemPaddingDip);

    private static double BoxForAcrossCell(double cell, bool vertical) =>
        vertical ? cell - (2 * ItemPaddingDip) : BoxForItemHeight(cell);

    private static double ItemWidth(double boxWidth) => boxWidth + (2 * ItemPaddingDip);

    private static double ItemHeight(double boxWidth) => (BoxAspect * boxWidth) + (2 * ItemPaddingDip) + CaptionHeightDip;

    private static double BoxForItemHeight(double height) => (height - (2 * ItemPaddingDip) - CaptionHeightDip) / BoxAspect;

    /// <summary>The popover rectangle: against the dock window, centred on the anchor and kept inside the usable area.</summary>
    private static PixelRect Place(DockPosition position, PixelRect dockWindow, int anchor, PixelRect area, int gapPx, int marginPx,
        int width, int height)
    {
        if (position == DockPosition.Bottom)
        {
            var x = ClampStart(anchor - (width / 2), width, area.Left + marginPx, area.Right - marginPx);
            return PixelRect.FromSize(x, dockWindow.Top - gapPx - height, width, height);
        }

        var y = ClampStart(anchor - (height / 2), height, area.Top + marginPx, area.Bottom - marginPx);
        var left = position == DockPosition.Left ? dockWindow.Right + gapPx : dockWindow.Left - gapPx - width;
        return PixelRect.FromSize(left, y, width, height);
    }

    /// <summary>Moves a span inside [min, max]; when it is longer than the range its start is pinned to min.</summary>
    private static int ClampStart(int start, int length, int min, int max) => Math.Max(min, Math.Min(max - length, start));

    private static DockPreviewItemLayout ItemAt(int index, double boxWidth, bool vertical, double scale, DockPreviewSourceSize? source)
    {
        var (itemX, itemY) = ItemOrigin(index, boxWidth, vertical);
        var captionTop = itemY + ItemPaddingDip;
        var boxLeft = itemX + ItemPaddingDip;

        var box = Rect(boxLeft, captionTop + CaptionHeightDip, boxWidth, BoxAspect * boxWidth, scale);
        var closeTop = captionTop + ((CaptionHeightDip - CloseSizeDip) / 2);
        var placeholder = source is not { Width: >= MinUsableSourcePx, Height: >= MinUsableSourcePx };
        return new DockPreviewItemLayout(
            ItemRect(index, boxWidth, vertical, scale),
            Rect(boxLeft, captionTop, boxWidth, CaptionHeightDip, scale),
            Rect(boxLeft + boxWidth - CloseSizeDip, closeTop, CloseSizeDip, CloseSizeDip, scale),
            box,
            placeholder ? default : Fit(box, source!.Value),
            placeholder);
    }

    private static PixelRect ItemRect(int index, double boxWidth, bool vertical, double scale)
    {
        var (x, y) = ItemOrigin(index, boxWidth, vertical);
        return Rect(x, y, ItemWidth(boxWidth), ItemHeight(boxWidth), scale);
    }

    /// <summary>Top-left of item <paramref name="index"/> in DIP, relative to the popover.</summary>
    private static (double X, double Y) ItemOrigin(int index, double boxWidth, bool vertical)
    {
        var offset = PopoverPaddingDip + (index * (AlongCell(boxWidth, vertical) + ItemGapDip));
        return vertical ? (PopoverPaddingDip, offset) : (offset, PopoverPaddingDip);
    }

    /// <summary>The largest rectangle of the source's aspect ratio centred in the box.</summary>
    private static PixelRect Fit(PixelRect box, DockPreviewSourceSize source)
    {
        int width;
        int height;
        if ((long)source.Width * box.Height >= (long)source.Height * box.Width)
        {
            width = box.Width;
            height = Math.Clamp(RoundAway((double)box.Width * source.Height / source.Width), 1, box.Height);
        }
        else
        {
            height = box.Height;
            width = Math.Clamp(RoundAway((double)box.Height * source.Width / source.Height), 1, box.Width);
        }

        return PixelRect.FromSize(box.Left + ((box.Width - width) / 2), box.Top + ((box.Height - height) / 2), width, height);
    }

    /// <summary>Converts a DIP rectangle to pixels, rounding each edge so neighbours stay flush.</summary>
    private static PixelRect Rect(double x, double y, double width, double height, double scale) =>
        new(Px(x, scale), Px(y, scale), Px(x + width, scale), Px(y + height, scale));

    private static int Px(double dip, double scale) => RoundAway(dip * scale);

    private static int RoundAway(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
