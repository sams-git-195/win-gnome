using System.Windows;
using System.Windows.Media;
using WinGnome.Core.Windows;

namespace WinGnome.Features.WindowButtons;

/// <summary>
/// Vector glyphs (×, −, +) drawn inside the circles. Built from line segments rather than font characters so
/// they stay centred and crisp at every diameter and DPI.
/// </summary>
internal static class GlyphGeometry
{
    /// <summary>Half the length of the − and + strokes, relative to the circle radius.</summary>
    private const double BarExtent = 0.5;

    /// <summary>Half the extent of each × stroke along both axes, relative to the circle radius.</summary>
    private const double CrossExtent = 0.38;

    /// <summary>Stroke thickness relative to the circle diameter.</summary>
    public const double StrokeRatio = 0.1;

    /// <summary>Creates the frozen glyph for <paramref name="kind"/> centred on <paramref name="center"/>.</summary>
    public static Geometry Create(CaptionButtonKind kind, Point center, double diameter)
    {
        var radius = diameter / 2;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            switch (kind)
            {
                case CaptionButtonKind.Close:
                    var cross = radius * CrossExtent;
                    Line(context, center, -cross, -cross, cross, cross);
                    Line(context, center, -cross, cross, cross, -cross);
                    break;
                case CaptionButtonKind.Minimize:
                    Line(context, center, -radius * BarExtent, 0, radius * BarExtent, 0);
                    break;
                default:
                    Line(context, center, -radius * BarExtent, 0, radius * BarExtent, 0);
                    Line(context, center, 0, -radius * BarExtent, 0, radius * BarExtent);
                    break;
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static void Line(StreamGeometryContext context, Point center, double x1, double y1, double x2, double y2)
    {
        context.BeginFigure(new Point(center.X + x1, center.Y + y1), isFilled: false, isClosed: false);
        context.LineTo(new Point(center.X + x2, center.Y + y2), isStroked: true, isSmoothJoin: false);
    }
}
