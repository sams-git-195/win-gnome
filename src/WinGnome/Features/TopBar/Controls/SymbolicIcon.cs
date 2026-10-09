using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using WinGnome.Core.TopBar;
using WinGnome.Infrastructure;

namespace WinGnome.Features.TopBar.Controls;

/// <summary>
/// One of the symbolic icons in <c>Theme/SymbolicIcons.xaml</c>, looked up by key and drawn in the inherited text
/// colour. Like <see cref="WindowsLogo"/> it works in device pixels: the icon is a whole number of pixels wide and
/// every corner of its 16-unit grid lands on a pixel edge, so straight edges stay sharp at 100, 125, 150 % and up
/// (a stretched path or font glyph puts them on fractions of a pixel and blurs them).
/// </summary>
internal sealed class SymbolicIcon : FrameworkElement
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(SymbolicIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(SymbolicIcon),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The text colour, inherited like a TextBlock's, so icons follow hover, accent and theme changes.</summary>
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(SymbolicIcon),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private Geometry? _source;
    private int _snappedPx;
    private Geometry? _snapped;

    /// <summary>Resource key of the icon's geometry, e.g. <c>NetworkWireless</c>.</summary>
    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Edge length in DIPs; rounded to whole device pixels when drawn.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Icon is not { } key || TryFindResource(key) is not Geometry source)
        {
            return;
        }

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var px = (int)Math.Round(BarMetrics.SnapToDevice(Size, scale) * scale);
        if (px <= 0)
        {
            return;
        }

        // Icons only change with the status they show; keep the last snapped shape for repaints (hover, colour).
        if (!ReferenceEquals(source, _source) || px != _snappedPx)
        {
            _source = source;
            _snappedPx = px;
            _snapped = Snap(source, px, scale);
        }

        drawingContext.DrawGeometry(Foreground, null, _snapped);
    }

    /// <summary>
    /// Scales a 16-unit icon to <paramref name="px"/> device pixels, moving every on-curve point to the nearest pixel
    /// corner (curve control points and arc radii only scale, which keeps curves smooth). Returned in DIPs.
    /// </summary>
    private static Geometry Snap(Geometry source, int px, double scale)
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
                    // Not used by SymbolicIcons.xaml; draw the icon scaled but unsnapped rather than not at all.
                    Log.Warn($"Symbolic icon has an unsupported {segment.GetType().Name}; drawing it unsnapped");
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
