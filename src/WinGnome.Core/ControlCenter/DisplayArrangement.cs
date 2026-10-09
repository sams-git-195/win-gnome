using WinGnome.Core.Geometry;

namespace WinGnome.Core.ControlCenter;

/// <summary>A display's place on the desktop, in physical pixels.</summary>
/// <param name="Id">Stable id of the display (its GDI device name).</param>
/// <param name="Bounds">Desktop rectangle; the primary display sits at (0, 0).</param>
public sealed record DisplayPlacement(string Id, PixelRect Bounds);

/// <summary>
/// Geometry of the Displays panel's arrangement: keeping displays edge to edge without gaps or overlaps (which
/// Windows rejects), moving the primary display to the origin, and scaling the arrangement into the preview.
/// </summary>
public static class DisplayArrangement
{
    /// <summary>Translates every display so <paramref name="primaryId"/> sits at (0, 0), as Windows requires of the primary.</summary>
    public static IReadOnlyList<DisplayPlacement> MakePrimary(IReadOnlyList<DisplayPlacement> displays, string primaryId)
    {
        var primary = Get(displays, primaryId);
        var dx = -primary.Bounds.Left;
        var dy = -primary.Bounds.Top;
        return displays.Select(d => d with { Bounds = d.Bounds.Offset(dx, dy) }).ToList();
    }

    /// <summary>
    /// Gives display <paramref name="id"/> a new size, keeping its top-left corner. Displays entirely to its right
    /// shift by the change in width and displays entirely below it by the change in height, so neighbours stay flush.
    /// The result is translated so <paramref name="primaryId"/> stays at the origin, as Windows requires.
    /// </summary>
    public static IReadOnlyList<DisplayPlacement> Resize(IReadOnlyList<DisplayPlacement> displays, string id, string primaryId, int width, int height)
    {
        var old = Get(displays, id).Bounds;
        var dx = width - old.Width;
        var dy = height - old.Height;
        var resized = displays.Select(d =>
        {
            if (d.Id == id)
            {
                return d with { Bounds = PixelRect.FromSize(old.Left, old.Top, width, height) };
            }

            var shiftX = d.Bounds.Left >= old.Right ? dx : 0;
            var shiftY = d.Bounds.Top >= old.Bottom ? dy : 0;
            return d with { Bounds = d.Bounds.Offset(shiftX, shiftY) };
        }).ToList();
        return MakePrimary(resized, primaryId);
    }

    /// <summary>
    /// Moves display <paramref name="id"/> to the free spot nearest the proposed top-left corner that lies flush against
    /// another display's edge (sharing at least one pixel of it) without overlapping any display. Within
    /// <paramref name="snapDistance"/> pixels it also lines up with that neighbour's top or bottom (left or right) edge.
    /// Returns the arrangement unchanged when there is no other display or no free spot.
    /// </summary>
    public static IReadOnlyList<DisplayPlacement> Move(IReadOnlyList<DisplayPlacement> displays, string id, int proposedLeft, int proposedTop, int snapDistance)
    {
        var moving = Get(displays, id).Bounds;
        var others = displays.Where(d => d.Id != id).Select(d => d.Bounds).ToList();
        var width = moving.Width;
        var height = moving.Height;

        PixelRect? best = null;
        long bestDistance = long.MaxValue;
        foreach (var other in others)
        {
            foreach (var candidate in FlushSpots(other, width, height, proposedLeft, proposedTop, snapDistance))
            {
                if (others.Any(o => o.Intersects(candidate)))
                {
                    continue;
                }

                long ex = candidate.Left - proposedLeft;
                long ey = candidate.Top - proposedTop;
                var distance = (ex * ex) + (ey * ey);
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
        }

        return best is { } placed
            ? displays.Select(d => d.Id == id ? d with { Bounds = placed } : d).ToList()
            : displays;
    }

    /// <summary>
    /// True when no two displays overlap and every display is joined to the others through shared edges (touching only
    /// at a corner does not count), which is what Windows accepts.
    /// </summary>
    public static bool IsValid(IReadOnlyList<DisplayPlacement> displays)
    {
        for (var i = 0; i < displays.Count; i++)
        {
            for (var j = i + 1; j < displays.Count; j++)
            {
                if (displays[i].Bounds.Intersects(displays[j].Bounds))
                {
                    return false;
                }
            }
        }

        if (displays.Count < 2)
        {
            return true;
        }

        var reached = new HashSet<int> { 0 };
        var pending = new Stack<int>([0]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            for (var next = 0; next < displays.Count; next++)
            {
                if (!reached.Contains(next) && ShareAnEdge(displays[current].Bounds, displays[next].Bounds))
                {
                    reached.Add(next);
                    pending.Push(next);
                }
            }
        }

        return reached.Count == displays.Count;
    }

    /// <summary>
    /// Scales the arrangement uniformly to fit a <paramref name="width"/> × <paramref name="height"/> preview with
    /// <paramref name="padding"/> on every side, centred. Returns one rectangle per display, in input order.
    /// </summary>
    public static IReadOnlyList<LayoutRect> Fit(IReadOnlyList<DisplayPlacement> displays, double width, double height, double padding)
    {
        if (displays.Count == 0)
        {
            return [];
        }

        var left = displays.Min(d => d.Bounds.Left);
        var top = displays.Min(d => d.Bounds.Top);
        var right = displays.Max(d => d.Bounds.Right);
        var bottom = displays.Max(d => d.Bounds.Bottom);
        var spanX = Math.Max(1, right - left);
        var spanY = Math.Max(1, bottom - top);

        var scale = Math.Max(0, Math.Min((width - (2 * padding)) / spanX, (height - (2 * padding)) / spanY));
        var offsetX = (width - (spanX * scale)) / 2;
        var offsetY = (height - (spanY * scale)) / 2;
        return displays
            .Select(d => new LayoutRect(
                offsetX + ((d.Bounds.Left - left) * scale),
                offsetY + ((d.Bounds.Top - top) * scale),
                d.Bounds.Width * scale,
                d.Bounds.Height * scale))
            .ToList();
    }

    /// <summary>The four flush positions around <paramref name="other"/> nearest the proposed corner, edge-aligned when close.</summary>
    private static IEnumerable<PixelRect> FlushSpots(PixelRect other, int width, int height, int left, int top, int snap)
    {
        // Left and right of the neighbour: slide vertically, keeping at least one pixel of shared edge.
        var y = Math.Clamp(top, other.Top - height + 1, other.Bottom - 1);
        y = Align(y, height, other.Top, other.Bottom, snap);
        yield return PixelRect.FromSize(other.Left - width, y, width, height);
        yield return PixelRect.FromSize(other.Right, y, width, height);

        // Above and below: slide horizontally.
        var x = Math.Clamp(left, other.Left - width + 1, other.Right - 1);
        x = Align(x, width, other.Left, other.Right, snap);
        yield return PixelRect.FromSize(x, other.Top - height, width, height);
        yield return PixelRect.FromSize(x, other.Bottom, width, height);
    }

    /// <summary>Snaps a position to the neighbour's start or end edge when either is within <paramref name="snap"/>; the closer one wins.</summary>
    private static int Align(int position, int length, int start, int end, int snap)
    {
        var toStart = Math.Abs(position - start);
        var toEnd = Math.Abs(position + length - end);
        if (toStart <= snap && toStart <= toEnd)
        {
            return start;
        }

        return toEnd <= snap ? end - length : position;
    }

    private static bool ShareAnEdge(PixelRect a, PixelRect b)
    {
        var verticalOverlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        var horizontalOverlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        var sideBySide = (a.Right == b.Left || b.Right == a.Left) && verticalOverlap > 0;
        var stacked = (a.Bottom == b.Top || b.Bottom == a.Top) && horizontalOverlap > 0;
        return sideBySide || stacked;
    }

    private static DisplayPlacement Get(IReadOnlyList<DisplayPlacement> displays, string id) =>
        displays.FirstOrDefault(d => d.Id == id) ?? throw new ArgumentException($"No display with id \"{id}\".", nameof(id));
}
