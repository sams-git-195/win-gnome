namespace WinGnome.Core.Geometry;

/// <summary>
/// An integer screen rectangle in physical pixels. Left/Top are inclusive and Right/Bottom are
/// exclusive, matching the Win32 RECT convention.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Creates a rectangle from an origin and a size.</summary>
    public static PixelRect FromSize(int x, int y, int width, int height) => new(x, y, x + width, y + height);

    /// <summary>Width, never negative.</summary>
    public int Width => Math.Max(0, Right - Left);

    /// <summary>Height, never negative.</summary>
    public int Height => Math.Max(0, Bottom - Top);

    /// <summary>True when the rectangle covers no pixels.</summary>
    public bool IsEmpty => Right <= Left || Bottom <= Top;

    /// <summary>Horizontal centre (rounded down).</summary>
    public int CenterX => Left + (Width / 2);

    /// <summary>Vertical centre (rounded down).</summary>
    public int CenterY => Top + (Height / 2);

    /// <summary>True when the pixel lies inside; the right and bottom edges are exclusive.</summary>
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    /// <summary>True when both rectangles are non-empty and share at least one pixel.</summary>
    public bool Intersects(PixelRect other) =>
        !IsEmpty && !other.IsEmpty && Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

    /// <summary>The overlapping area, or an empty rectangle when there is none.</summary>
    public PixelRect Intersect(PixelRect other)
    {
        if (!Intersects(other))
        {
            return default;
        }

        return new PixelRect(
            Math.Max(Left, other.Left),
            Math.Max(Top, other.Top),
            Math.Min(Right, other.Right),
            Math.Min(Bottom, other.Bottom));
    }

    /// <summary>Moves the rectangle by the given deltas.</summary>
    public PixelRect Offset(int dx, int dy) => new(Left + dx, Top + dy, Right + dx, Bottom + dy);

    /// <summary>Grows (or shrinks, when negative) each side by the given amounts.</summary>
    public PixelRect Inflate(int dx, int dy) => new(Left - dx, Top - dy, Right + dx, Bottom + dy);

    /// <summary>
    /// A rectangle of the same size centred in <paramref name="area"/>. When this rectangle is larger
    /// than the area on an axis, its top-left is pinned to the area's top-left on that axis so the
    /// title bar stays reachable.
    /// </summary>
    public PixelRect CenteredIn(PixelRect area)
    {
        var x = Math.Max(area.Left, area.Left + ((area.Width - Width) / 2));
        var y = Math.Max(area.Top, area.Top + ((area.Height - Height) / 2));
        return FromSize(x, y, Width, Height);
    }
}
