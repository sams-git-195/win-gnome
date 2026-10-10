using System.Windows;
using System.Windows.Media;
using WinGnome.Core.TopBar;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar.Controls;

/// <summary>
/// Pixel-snapping of a 16-unit icon geometry, shared by <see cref="SymbolicIcon"/> and <see cref="LogoGlyph"/> so both
/// keep straight edges sharp at every DPI. Callers cache the returned (frozen) shape; this only builds it.
/// </summary>
internal static class GeometrySnap
{
    /// <summary>
    /// Scales a 16-unit icon to <paramref name="px"/> device pixels, moving every on-curve point to the nearest pixel
    /// corner (curve control points and arc radii only scale, which keeps curves smooth). Returned in DIPs, frozen.
    /// </summary>
    public static Geometry Snap(Geometry source, int px, double scale)
    {
        var unitToDip = px / (double)BarMetrics.IconGridUnits / scale;
        Point Corner(Point p) => new(BarMetrics.SnapIconUnit(p.X, px) / scale, BarMetrics.SnapIconUnit(p.Y, px) / scale);
        Point Control(Point p) => new(p.X * unitToDip, p.Y * unitToDip);

        var path = PathGeometry.CreateFromGeometry(source);
        var result = new PathGeometry { FillRule = path.FillRule };
        foreach (var figure in path.Figures)
        {
            var snapped = new PathFigure { StartPoint = Corner(figure.StartPoint), IsClosed = figure.IsClosed, IsFilled = figure.IsFilled };
            foreach (var segment in figure.Segments)
            {
                PathSegment? copy = segment switch
                {
                    LineSegment line => new LineSegment(Corner(line.Point), line.IsStroked),
                    PolyLineSegment poly => new PolyLineSegment(poly.Points.Select(Corner), poly.IsStroked),
                    ArcSegment arc => new ArcSegment(
                        Corner(arc.Point), new Size(arc.Size.Width * unitToDip, arc.Size.Height * unitToDip),
                        arc.RotationAngle, arc.IsLargeArc, arc.SweepDirection, arc.IsStroked),
                    BezierSegment bezier => new BezierSegment(Control(bezier.Point1), Control(bezier.Point2), Corner(bezier.Point3), bezier.IsStroked),
                    QuadraticBezierSegment quad => new QuadraticBezierSegment(Control(quad.Point1), Corner(quad.Point2), quad.IsStroked),
                    _ => null,
                };
                if (copy is null)
                {
                    // Not used by SymbolicIcons.xaml or LogoMarks.xaml; draw the icon scaled but unsnapped rather than
                    // not at all.
                    Log.Warn($"Icon geometry has an unsupported {segment.GetType().Name}; drawing it unsnapped");
                    var scaled = source.Clone();
                    scaled.Transform = new ScaleTransform(unitToDip, unitToDip);
                    scaled.Freeze();
                    return scaled;
                }

                snapped.Segments.Add(copy);
            }

            result.Figures.Add(snapped);
        }

        result.Freeze();
        return result;
    }
}
