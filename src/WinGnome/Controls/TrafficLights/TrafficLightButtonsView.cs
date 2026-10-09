using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Core.Windows;

namespace WinGnome.Controls.TrafficLights;

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
    /// <summary>Gap in target DIPs between a circle and its keyboard focus ring.</summary>
    private const double FocusRingGap = 1.5;

    /// <summary>Thickness of the keyboard focus ring in target DIPs.</summary>
    private const double FocusRingThickness = 2;

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
    private CaptionButtonKind? _nonClientHovered;
    private bool _nonClientPressed;
    private Pen? _focusPen;
    private int _keyboardIndex = -1;

    public TrafficLightButtonsView()
    {
        Focusable = false;
        FocusVisualStyle = null;
        SnapsToDevicePixels = false;
    }

    /// <summary>
    /// Raised when a circle is clicked (pressed and released over the same, available circle), or invoked from the
    /// keyboard or UI Automation.
    /// </summary>
    public event Action<CaptionButtonKind>? ButtonClicked;

    /// <summary>The target window is maximised, so the maximise circle is announced as "Restore".</summary>
    public bool IsTargetMaximized { get; set; }

    /// <summary>
    /// Lets the circles take keyboard focus: Tab reaches the group, Left and Right move between circles, and Enter
    /// or Space invokes one. Only for windows WinGnome owns; overlays on other apps' windows never take focus.
    /// </summary>
    public bool IsKeyboardNavigable
    {
        get => Focusable;
        set => Focusable = value;
    }

    /// <summary>The current layout (target DIPs), for the automation peers.</summary>
    internal CaptionOverlayLayout? Layout => _layout;

    /// <summary>The circle with keyboard focus inside the group, or null.</summary>
    internal CaptionButtonKind? KeyboardButton =>
        IsKeyboardFocused && _layout is { } layout && _keyboardIndex >= 0 && _keyboardIndex < layout.Buttons.Count
            ? layout.Buttons[_keyboardIndex].Kind
            : null;

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
        _focusPen = null;
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

    /// <summary>
    /// Pointer state over a circle that the host window reports as non-client area. A header bar answers
    /// HTMAXBUTTON over its maximise circle so Windows shows Snap Layouts, and WPF then never sees the mouse there,
    /// so the host relays hover and press from its WM_NCMOUSEMOVE and WM_NCLBUTTONDOWN handling instead.
    /// </summary>
    /// <param name="hovered">The circle under the pointer, or null when the pointer left it.</param>
    /// <param name="pressed">The left button went down on <paramref name="hovered"/> and hasn't been released.</param>
    public void SetNonClientPointer(CaptionButtonKind? hovered, bool pressed)
    {
        pressed &= hovered is not null;
        if (_nonClientHovered == hovered && _nonClientPressed == pressed)
        {
            return;
        }

        _nonClientHovered = hovered;
        _nonClientPressed = pressed;
        InvalidateVisual();
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

        var focused = KeyboardButton;
        var groupHovered = _isGroupHovered || _nonClientHovered is not null || focused is not null;
        var showGlyphs = TrafficLightAppearance.ShowGlyphs(_colors, _settings, groupHovered);
        var radius = _layout.Diameter / 2;

        drawingContext.PushTransform(_contentTransform);
        for (var i = 0; i < _layout.Buttons.Count; i++)
        {
            var slot = _layout.Buttons[i];
            var enabled = IsAvailable(slot.Kind);
            var fill = TrafficLightAppearance.Fill(_colors, slot.Kind, enabled, _isWindowActive, groupHovered,
                _settings.DimInactiveWindows, InteractionOf(slot.Kind));

            // Inset by half the ring's thickness so the ring sits inside the circle's footprint.
            var ringRadius = radius - (_borderPen!.Thickness / 2);
            drawingContext.DrawEllipse(BrushFor(fill), _borderPen, new Point(slot.CenterX, slot.CenterY), ringRadius, ringRadius);
            if (showGlyphs && enabled)
            {
                drawingContext.DrawGeometry(null, _glyphPen, _glyphs[i]);
            }

            if (focused == slot.Kind)
            {
                _focusPen ??= CreateFocusPen();
                var focusRadius = radius + FocusRingGap + (_focusPen.Thickness / 2);
                drawingContext.DrawEllipse(null, _focusPen, new Point(slot.CenterX, slot.CenterY), focusRadius, focusRadius);
            }
        }

        drawingContext.Pop();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TrafficLightButtonsAutomationPeer(this);

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (KeyboardButton is null)
        {
            _keyboardIndex = CaptionButtonAccessibility.MoveFocus(Availability(), -1, 1);
        }

        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Left or Key.Right:
                _keyboardIndex = CaptionButtonAccessibility.MoveFocus(Availability(), _keyboardIndex, e.Key == Key.Left ? -1 : 1);
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Enter or Key.Space when KeyboardButton is { } kind:
                Invoke(kind);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Runs a circle's action, as a click would. Unavailable circles do nothing.</summary>
    internal void Invoke(CaptionButtonKind kind)
    {
        if (IsAvailable(kind))
        {
            ButtonClicked?.Invoke(kind);
        }
    }

    /// <summary>Moves keyboard focus to <paramref name="kind"/>'s circle (UI Automation SetFocus).</summary>
    internal void FocusButton(CaptionButtonKind kind)
    {
        if (_layout is null || !Focusable)
        {
            return;
        }

        _keyboardIndex = _layout.Buttons.ToList().FindIndex(slot => slot.Kind == kind);
        Focus();
        InvalidateVisual();
    }

    /// <summary>A circle's bounds in physical screen pixels, or empty when the view isn't on screen.</summary>
    internal Rect ScreenBoundsOf(CaptionButtonSlot slot)
    {
        if (_layout is null || PresentationSource.FromVisual(this) is null)
        {
            return Rect.Empty;
        }

        // The circles are drawn under the content transform (target DIPs to overlay DIPs).
        var radius = _layout.Diameter / 2;
        var scale = _contentTransform.ScaleX;
        var topLeft = PointToScreen(new Point((slot.CenterX - radius) * scale, (slot.CenterY - radius) * scale));
        var bottomRight = PointToScreen(new Point((slot.CenterX + radius) * scale, (slot.CenterY + radius) * scale));
        return new Rect(topLeft, bottomRight);
    }

    internal bool IsAvailable(CaptionButtonKind kind) => kind switch
    {
        CaptionButtonKind.Minimize => _canMinimize,
        CaptionButtonKind.Maximize => _canMaximize,
        _ => true,
    };

    private bool[] Availability() => _layout?.Buttons.Select(slot => IsAvailable(slot.Kind)).ToArray() ?? [];

    private Pen CreateFocusPen()
    {
        // The theme's accent, like the focus rings on the settings controls (Adwaita blue if there is none).
        var brush = TryFindResource("AccentBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x35, 0x84, 0xE4));
        var pen = new Pen(brush, FocusRingThickness);
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }
        return pen;
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
        if (_nonClientHovered == kind)
        {
            return _nonClientPressed ? CaptionButtonInteraction.Pressed : CaptionButtonInteraction.Hovered;
        }

        if (_pressed is { } pressed)
        {
            return pressed == kind && _hovered == kind ? CaptionButtonInteraction.Pressed : CaptionButtonInteraction.None;
        }

        return _hovered == kind ? CaptionButtonInteraction.Hovered : CaptionButtonInteraction.None;
    }

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
