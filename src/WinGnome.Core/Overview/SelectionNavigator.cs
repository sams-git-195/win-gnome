using WinGnome.Core.Geometry;

namespace WinGnome.Core.Overview;

/// <summary>An arrow-key direction.</summary>
public enum NavigationDirection
{
    Left,
    Right,
    Up,
    Down,
}

/// <summary>Keyboard selection movement for the overview's thumbnail layout and application grid.</summary>
public static class SelectionNavigator
{
    /// <summary>Weight of the off-axis distance: neighbours in line with the current item win over diagonal ones.</summary>
    private const double OffAxisWeight = 2;

    /// <summary>
    /// Index of the item nearest to <paramref name="current"/> in <paramref name="direction"/>, judged by
    /// rectangle centres. Returns <paramref name="current"/> when nothing lies that way, 0 when nothing is
    /// selected yet, and -1 when there are no items.
    /// </summary>
    public static int FindNeighbor(IReadOnlyList<LayoutRect> items, int current, NavigationDirection direction)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return -1;
        }

        if (current < 0 || current >= items.Count)
        {
            return 0;
        }

        var origin = items[current];
        var best = current;
        var bestScore = double.PositiveInfinity;
        for (var i = 0; i < items.Count; i++)
        {
            if (i == current)
            {
                continue;
            }

            var dx = items[i].CenterX - origin.CenterX;
            var dy = items[i].CenterY - origin.CenterY;
            var (along, across) = direction switch
            {
                NavigationDirection.Left => (-dx, dy),
                NavigationDirection.Right => (dx, dy),
                NavigationDirection.Up => (-dy, dx),
                _ => (dy, dx),
            };

            if (along <= 0.5)
            {
                continue;
            }

            var score = along + (OffAxisWeight * Math.Abs(across));
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Moves through a row-major grid of <paramref name="count"/> items with <paramref name="columns"/>
    /// columns. Left/Right step by one (crossing row ends); Up/Down step by a row, and Down from a row
    /// above a shorter last row lands on the last item. Stays put at the edges; returns 0 when nothing
    /// is selected yet and -1 when there are no items.
    /// </summary>
    public static int MoveInGrid(int current, int count, int columns, NavigationDirection direction)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (current < 0 || current >= count)
        {
            return 0;
        }

        columns = Math.Max(1, columns);
        return direction switch
        {
            NavigationDirection.Left => Math.Max(0, current - 1),
            NavigationDirection.Right => Math.Min(count - 1, current + 1),
            NavigationDirection.Up => current - columns >= 0 ? current - columns : current,
            _ => MoveDown(current, count, columns),
        };
    }

    private static int MoveDown(int current, int count, int columns)
    {
        if (current + columns < count)
        {
            return current + columns;
        }

        var lastRow = (count - 1) / columns;
        return current / columns < lastRow ? count - 1 : current;
    }
}
