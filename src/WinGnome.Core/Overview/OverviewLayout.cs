using WinGnome.Core.Geometry;

namespace WinGnome.Core.Overview;

/// <summary>Arranges window thumbnails for the activities overview.</summary>
public static class OverviewLayout
{
    private const double MinScale = 0.01;

    /// <summary>
    /// Lays the windows out in rows that preserve each window's aspect ratio. The row count that gives the
    /// largest uniform scale wins (ties prefer fewer rows); windows are never scaled above 1.0. Rows are
    /// centred horizontally and the whole block is centred vertically in <paramref name="area"/>.
    /// The result is index-aligned with <paramref name="windows"/>. Sizes that are zero, negative or not finite count as 1x1.
    /// </summary>
    /// <param name="windows">Natural window sizes.</param>
    /// <param name="area">Available area.</param>
    /// <param name="spacing">Fixed gap between neighbours (not scaled).</param>
    public static IReadOnlyList<LayoutRect> Arrange(IReadOnlyList<LayoutSize> windows, LayoutRect area, double spacing)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var count = windows.Count;
        if (count == 0)
        {
            return [];
        }

        spacing = double.IsFinite(spacing) ? Math.Max(0, spacing) : 0;

        var widths = new double[count];
        var heights = new double[count];
        for (var i = 0; i < count; i++)
        {
            widths[i] = Sanitize(windows[i].Width);
            heights[i] = Sanitize(windows[i].Height);
        }

        var bestRows = 1;
        var bestScale = double.NegativeInfinity;
        for (var rows = 1; rows <= count; rows++)
        {
            var scale = ScaleFor(rows, count, widths, heights, area, spacing);
            if (scale > bestScale + 1e-12)
            {
                bestScale = scale;
                bestRows = rows;
            }
        }

        return Place(bestRows, bestScale, count, widths, heights, area, spacing);
    }

    private static double Sanitize(double value) => double.IsFinite(value) && value > 0 ? value : 1;

    private static (int Start, int Length) RowRange(int row, int rows, int count)
    {
        var baseCount = count / rows;
        var extra = count % rows;
        var start = (row * baseCount) + Math.Min(row, extra);
        var length = baseCount + (row < extra ? 1 : 0);
        return (start, length);
    }

    private static double ScaleFor(int rows, int count, double[] widths, double[] heights, LayoutRect area, double spacing)
    {
        var scale = 1.0;
        var heightSum = 0.0;
        for (var row = 0; row < rows; row++)
        {
            var (start, length) = RowRange(row, rows, count);
            var widthSum = 0.0;
            var rowHeight = 0.0;
            for (var i = start; i < start + length; i++)
            {
                widthSum += widths[i];
                rowHeight = Math.Max(rowHeight, heights[i]);
            }

            heightSum += rowHeight;
            scale = Math.Min(scale, (area.Width - ((length - 1) * spacing)) / widthSum);
        }

        scale = Math.Min(scale, (area.Height - ((rows - 1) * spacing)) / heightSum);
        return Math.Max(scale, MinScale);
    }

    private static LayoutRect[] Place(int rows, double scale, int count, double[] widths, double[] heights, LayoutRect area, double spacing)
    {
        var result = new LayoutRect[count];
        var rowHeights = new double[rows];
        for (var row = 0; row < rows; row++)
        {
            var (start, length) = RowRange(row, rows, count);
            for (var i = start; i < start + length; i++)
            {
                rowHeights[row] = Math.Max(rowHeights[row], heights[i] * scale);
            }
        }

        var blockHeight = rowHeights.Sum() + ((rows - 1) * spacing);
        var y = area.Y + ((area.Height - blockHeight) / 2);
        for (var row = 0; row < rows; row++)
        {
            var (start, length) = RowRange(row, rows, count);
            var rowWidth = ((length - 1) * spacing) + Enumerable.Range(start, length).Sum(i => widths[i] * scale);
            var x = area.X + ((area.Width - rowWidth) / 2);
            for (var i = start; i < start + length; i++)
            {
                var w = widths[i] * scale;
                var h = heights[i] * scale;
                result[i] = new LayoutRect(x, y + ((rowHeights[row] - h) / 2), w, h);
                x += w + spacing;
            }

            y += rowHeights[row] + spacing;
        }

        return result;
    }
}
