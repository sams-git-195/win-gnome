using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using WinGnome.Core.TopBar;

namespace WinGnome.Features.TopBar.Controls;

/// <summary>
/// One of the symbolic icons in <c>Theme/SymbolicIcons.xaml</c>, looked up by key and drawn in the inherited text
/// colour. Like <see cref="LogoGlyph"/> it works in device pixels: the icon is a whole number of pixels wide and
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
            _snapped = GeometrySnap.Snap(source, px, scale);
        }

        drawingContext.DrawGeometry(Foreground, null, _snapped);
    }
}
