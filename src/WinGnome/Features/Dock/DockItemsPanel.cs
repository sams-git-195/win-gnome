using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinGnome.Core.Dock;
using WinGnome.Core.Settings;

namespace WinGnome.Features.Dock;

/// <summary>
/// Lays dock items out along the dock edge with macOS-style hover magnification.
/// </summary>
/// <remarks>
/// Each magnified item's slot widens along the dock (so neighbours make room), and the item itself is scaled
/// with a render transform anchored on the screen-edge side, so it grows away from the edge into the window's
/// transparent headroom. Scales are computed from the pointer's distance to the items' <em>unmagnified</em>
/// centres, measured from the panel's centre. The panel is always centred in the dock and grows symmetrically,
/// so that reference point never moves and the layout cannot feed back into itself.
/// </remarks>
internal sealed class DockItemsPanel : Panel
{
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(DockPosition), typeof(DockItemsPanel),
        new FrameworkPropertyMetadata(DockPosition.Bottom, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(DockItemsPanel),
        new FrameworkPropertyMetadata(48.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxScaleProperty = DependencyProperty.Register(
        nameof(MaxScale), typeof(double), typeof(DockItemsPanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Pointer position along the dock relative to the panel centre (DIPs), or NaN when not hovering.</summary>
    public static readonly DependencyProperty PointerOffsetProperty = DependencyProperty.Register(
        nameof(PointerOffset), typeof(double), typeof(DockItemsPanel),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>0..1 blend of the magnification, animated so it eases in and out instead of snapping.</summary>
    public static readonly DependencyProperty EngagementProperty = DependencyProperty.Register(
        nameof(Engagement), typeof(double), typeof(DockItemsPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private double[] _scales = [];

    public DockPosition Position
    {
        get => (DockPosition)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public double MaxScale
    {
        get => (double)GetValue(MaxScaleProperty);
        set => SetValue(MaxScaleProperty, value);
    }

    public double PointerOffset
    {
        get => (double)GetValue(PointerOffsetProperty);
        set => SetValue(PointerOffsetProperty, value);
    }

    public double Engagement
    {
        get => (double)GetValue(EngagementProperty);
        set => SetValue(EngagementProperty, value);
    }

    private bool IsVertical => Position != DockPosition.Bottom;

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren;
        var lengths = new double[children.Count];
        double thickness = 0;
        double total = 0;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            lengths[i] = Along(child.DesiredSize);
            thickness = Math.Max(thickness, Across(child.DesiredSize));
            total += lengths[i];
        }

        _scales = ComputeScales(children, lengths, total);

        double length = 0;
        for (var i = 0; i < lengths.Length; i++)
        {
            length += lengths[i] * _scales[i];
        }

        return IsVertical ? new Size(thickness, length) : new Size(length, thickness);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = InternalChildren;
        var thickness = IsVertical ? finalSize.Width : finalSize.Height;
        var origin = Position switch
        {
            DockPosition.Left => new Point(0, 0.5),
            DockPosition.Right => new Point(1, 0.5),
            _ => new Point(0.5, 1),
        };

        double cursor = 0;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var length = Along(child.DesiredSize);
            var scale = i < _scales.Length ? _scales[i] : 1;
            var slot = length * scale;

            // The child keeps its natural size, centred in its widened slot; the render transform does the growing.
            // Across the dock it hugs the screen-edge side.
            var start = cursor + ((slot - length) / 2);
            var across = Across(child.DesiredSize);
            var acrossStart = Position == DockPosition.Left ? 0 : thickness - across;
            child.Arrange(IsVertical
                ? new Rect(acrossStart, start, across, length)
                : new Rect(start, acrossStart, length, across));
            ApplyScale(child, scale, origin);
            cursor += slot;
        }

        return finalSize;
    }

    private double[] ComputeScales(UIElementCollection children, double[] lengths, double total)
    {
        var scales = new double[lengths.Length];
        var maxScale = 1 + ((Math.Max(1, MaxScale) - 1) * Math.Clamp(Engagement, 0, 1));
        var pointer = PointerOffset;
        double position = -total / 2;
        for (var i = 0; i < lengths.Length; i++)
        {
            var centre = position + (lengths[i] / 2);
            position += lengths[i];
            var magnifies = (children[i] as FrameworkElement)?.DataContext is not DockEntry entry || entry.Magnifies;
            scales[i] = magnifies && double.IsFinite(pointer)
                ? DockLayout.MagnificationScale(pointer - centre, IconSize, maxScale)
                : 1;
        }

        return scales;
    }

    private static void ApplyScale(UIElement child, double scale, Point origin)
    {
        child.RenderTransformOrigin = origin;
        if (child.RenderTransform is ScaleTransform { IsFrozen: false } transform)
        {
            transform.ScaleX = scale;
            transform.ScaleY = scale;
        }
        else
        {
            child.RenderTransform = new ScaleTransform(scale, scale);
        }
    }

    private double Along(Size size) => IsVertical ? size.Height : size.Width;

    private double Across(Size size) => IsVertical ? size.Width : size.Height;
}
