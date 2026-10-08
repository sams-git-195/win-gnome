using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Core.Windows;

namespace WinGnome.Features.WindowButtons;

/// <summary>
/// Draws the three traffic-light circles and turns clicks into <see cref="ButtonClicked"/>. Everything is drawn
/// in <see cref="OnRender"/> with cached brushes, pens and geometries, so the view costs nothing while the
/// window is merely moved, and re-renders only when hover, press, focus or settings change.
/// </summary>
/// <remarks>
/// The layout is computed in the target window's DIPs. The overlay normally has the same DPI, but while a window
/// straddles two monitors the small overlay can sit on a monitor with a different DPI; a scale transform maps
/// the target's DIPs onto the overlay's DIPs so the circles always cover the same physical pixels.
/// </remarks>
internal sealed class TrafficLightButtonsView : FrameworkElement
{
    private readonly ScaleTransform _contentTransform = new(1, 1);
    private readonly Dictionary<HexColor, SolidColorBrush> _brushes = [];
    private CaptionOverlayLayout? _layout;
    private Geometry[] _glyphs = [];
    private TrafficLightColors? _colors;
    private WindowButtonSettings? _settings;
    private Pen? _borderPen;
    private Pen? _glyphPen;
    private double _targetScale = 1;
    private double _surfaceScale = 1;
    private bool _canMinimize = true;
    private bool _canMaximize = true;
    private bool _isWindowActive;
    private bool _isGroupHovered;
    private CaptionButtonKind? _hovered;
    private CaptionButtonKind? _pressed;

    public TrafficLightButtonsView()
    {
        Focusable = false;
        SnapsToDevicePixels = false;
    }

    /// <summary>Raised when a circle is clicked (pressed and released over the same, available circle).</summary>
    public event Action<CaptionButtonKind>? ButtonClicked;

    /// <summary>Sets the circle geometry and which buttons the target window supports.</summary>
    /// <param name="layout">Layout in the target window's DIPs.</param>
    /// <param name="targetScale">Physical pixels per DIP of the target window.</param>
    /// <param name="canMinimize">The target has WS_MINIMIZEBOX.</param>
    /// <param name="canMaximize">The target has WS_MAXIMIZEBOX.</param>
    public void SetLayout(CaptionOverlayLayout layout, double targetScale, bool canMinimize, bool canMaximize)
    {
        _layout = layout;
        _targetScale = targetScale;
        _canMinimize = canMinimize;
        _canMaximize = canMaximize;
        _glyphs = layout.Buttons
            .Select(slot => GlyphGeometry.Create(slot.Kind, new Point(slot.CenterX, slot.CenterY), layout.Diameter))
            .ToArray();
        UpdateContentScale();
        RebuildPens();
        InvalidateVisual();
    }

    /// <summary>Sets the colours and glyph behaviour.</summary>
    public void SetAppearance(TrafficLightColors colors, WindowButtonSettings settings)
    {
        _colors = colors;
        _settings = settings;
        _brushes.Clear();
        RebuildPens();
        InvalidateVisual();
    }

    /// <summary>Whether the target window has keyboard focus (inactive windows are dimmed).</summary>
    public bool IsWindowActive
    {
        get => _isWindowActive;
        set
        {
            if (_isWindowActive != value)
            {
                _isWindowActive = value;
                InvalidateVisual();
            }
        }
    }

    /// <summary>Physical pixels per DIP of the overlay window itself (changes when it moves between monitors).</summary>
    public void SetSurfaceScale(double surfaceScale)
    {
        _surfaceScale = surfaceScale > 0 ? surfaceScale : 1;
        UpdateContentScale();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        // A transparent backdrop makes the whole overlay hit-testable, including the gaps between circles.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_layout is null || _colors is null || _settings is null)
        {
            return;
        }

        var showGlyphs = TrafficLightAppearance.ShowGlyphs(_colors, _settings, _isGroupHovered);
        var radius = _layout.Diameter / 2;

        drawingContext.PushTransform(_contentTransform);
        for (var i = 0; i < _layout.Buttons.Count; i++)
        {
            var slot = _layout.Buttons[i];
            var enabled = IsAvailable(slot.Kind);
            var fill = TrafficLightAppearance.Fill(_colors, slot.Kind, enabled, _isWindowActive, _isGroupHovered,
                _settings.DimInactiveWindows, InteractionOf(slot.Kind));

            // Inset by half the ring's thickness so the ring sits inside the circle's footprint.
            var ringRadius = radius - (_borderPen!.Thickness / 2);
            drawingContext.DrawEllipse(BrushFor(fill), _borderPen, new Point(slot.CenterX, slot.CenterY), ringRadius, ringRadius);
            if (showGlyphs && enabled)
            {
                drawingContext.DrawGeometry(null, _glyphPen, _glyphs[i]);
            }
        }

        drawingContext.Pop();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetPointerState(groupHovered: true, HitTest(e));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!IsMouseCaptured)
        {
            SetPointerState(groupHovered: false, hovered: null);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        e.Handled = true;
        var kind = HitTest(e);
        if (kind is { } pressed && IsAvailable(pressed))
        {
            _pressed = pressed;
            CaptureMouse();
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        e.Handled = true;
        var pressed = _pressed;
        var released = HitTest(e);
        ReleaseMouseCapture();
        if (pressed is { } kind && released == kind && IsMouseOver)
        {
            ButtonClicked?.Invoke(kind);
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _pressed = null;
        SetPointerState(IsMouseOver, IsMouseOver ? HitTest(e) : null);
        InvalidateVisual();
    }

    private void SetPointerState(bool groupHovered, CaptionButtonKind? hovered)
    {
        if (_isGroupHovered == groupHovered && _hovered == hovered)
        {
            return;
        }

        _isGroupHovered = groupHovered;
        _hovered = hovered;
        InvalidateVisual();
    }

    private CaptionButtonKind? HitTest(MouseEventArgs e)
    {
        if (_layout is null)
        {
            return null;
        }

        // Pointer positions are in overlay DIPs; the layout is in target DIPs.
        var x = e.GetPosition(this).X / _contentTransform.ScaleX;
        return CaptionButtonHitTest.Find(_layout, x);
    }

    private CaptionButtonInteraction InteractionOf(CaptionButtonKind kind)
    {
        if (_pressed is { } pressed)
        {
            return pressed == kind && _hovered == kind ? CaptionButtonInteraction.Pressed : CaptionButtonInteraction.None;
        }

        return _hovered == kind ? CaptionButtonInteraction.Hovered : CaptionButtonInteraction.None;
    }

    private bool IsAvailable(CaptionButtonKind kind) => kind switch
    {
        CaptionButtonKind.Minimize => _canMinimize,
        CaptionButtonKind.Maximize => _canMaximize,
        _ => true,
    };

    private void UpdateContentScale()
    {
        var scale = _targetScale / _surfaceScale;
        _contentTransform.ScaleX = scale;
        _contentTransform.ScaleY = scale;
    }

    private void RebuildPens()
    {
        if (_layout is null || _colors is null)
        {
            return;
        }

        // One physical pixel, expressed in the target's DIPs (the space the circles are drawn in).
        _borderPen = new Pen(BrushFor(_colors.Border), 1 / _targetScale);
        _borderPen.Freeze();
        _glyphPen = new Pen(BrushFor(_colors.Glyph), _layout.Diameter * GlyphGeometry.StrokeRatio)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        _glyphPen.Freeze();
    }

    private SolidColorBrush BrushFor(HexColor color)
    {
        if (!_brushes.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
            brush.Freeze();
            _brushes[color] = brush;
        }

        return brush;
    }
}
