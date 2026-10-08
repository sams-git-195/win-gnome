using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinGnome.Core.Dock;
using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Dock;

/// <summary>
/// The dock surface: a topmost, never-activated, layered window holding the dock body. It renders the items,
/// magnifies them under the pointer, slides in and out, and turns mouse input into high-level events for
/// <see cref="DockFeature"/>.
/// </summary>
internal sealed partial class DockWindow : Window
{
    /// <summary>Padding across the body around the icon cells. Matches the fixed value in <see cref="DockLayout"/>.</summary>
    public const double BodyPadding = 4;

    private const string PinDragFormat = "WinGnome.DockPin";
    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(200));
    private static readonly Duration EngageDuration = new(TimeSpan.FromMilliseconds(120));
    private static readonly Duration ReleaseDuration = new(TimeSpan.FromMilliseconds(180));
    private static readonly Color DefaultDockColor = Color.FromRgb(0x24, 0x24, 0x24);

    private readonly DockViewModel _viewModel;
    private readonly DockBackdropWindow _backdrop;
    private DockItemsPanel? _panel;
    private DockViewLayout? _layout;
    private double _bodyRadius;
    private bool _blurActive;
    private bool _revealed;
    private bool _hasRevealState;
    private int _slideGeneration;
    private bool _trackingSlide;
    private bool _magnifying;
    private PressState? _press;
    private DockAppEntry? _dragEntry;
    private bool _dropCommitted;

    public DockWindow(DockViewModel viewModel, DockBackdropWindow backdrop)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _backdrop = backdrop ?? throw new ArgumentNullException(nameof(backdrop));
        InitializeComponent();
        DataContext = viewModel;

        // Owned windows always stay above their owner, which keeps the dock above its blur backdrop.
        new WindowInteropHelper(this).Owner = backdrop.Handle;
        SourceInitialized += (_, _) => ShellSurface.MakeNonActivating(this, topmost: true);
        new WindowInteropHelper(this).EnsureHandle();

        SlideHost.MouseEnter += (_, _) => PointerEntered?.Invoke(this, EventArgs.Empty);
        SlideHost.MouseMove += OnPointerMove;
        SlideHost.MouseLeave += (_, _) => ReleaseMagnification();
        SlideHost.DragEnter += OnDragEnter;
        SlideHost.DragOver += OnDragOver;
        SlideHost.Drop += OnDrop;
        Items.PreviewMouseDown += OnItemMouseDown;
        Items.PreviewMouseMove += OnItemMouseMove;
        Items.PreviewMouseUp += OnItemMouseUp;
        LayoutUpdated += (_, _) => SyncBackdrop();
    }

    /// <summary>Left or middle click on an entry.</summary>
    public event EventHandler<DockEntryEventArgs>? EntryInvoked;

    /// <summary>Right click on an entry.</summary>
    public event EventHandler<DockMenuRequestEventArgs>? MenuRequested;

    /// <summary>The pointer (or a drag from another app) entered the dock.</summary>
    public event EventHandler? PointerEntered;

    /// <summary>A pinned icon started being dragged to a new position.</summary>
    public event EventHandler? PinDragStarted;

    /// <summary>The reordering drag ended; true when it was dropped on the dock (commit), false when cancelled.</summary>
    public event EventHandler<bool>? PinDragFinished;

    /// <summary>.exe or .lnk files were dropped on the dock.</summary>
    public event EventHandler<DockFilesDroppedEventArgs>? FilesDropped;

    /// <summary>True while the pointer is over the dock's hit-testable area.</summary>
    public bool IsPointerOver => SlideHost.IsMouseOver;

    private bool IsVertical => _layout is not null && _layout.Position != DockPosition.Bottom;

    /// <summary>Positions the window (physical pixels) and lays out the body inside it (DIPs).</summary>
    public void ApplyLayout(DockViewLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        var vertical = layout.Position != DockPosition.Bottom;
        var along = layout.ExtendToEdges ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        var alongVertical = layout.ExtendToEdges ? VerticalAlignment.Stretch : VerticalAlignment.Center;

        switch (layout.Position)
        {
            case DockPosition.Left:
                SlideHost.HorizontalAlignment = HorizontalAlignment.Left;
                SlideHost.VerticalAlignment = alongVertical;
                Body.Margin = new Thickness(layout.EdgeGap, 0, 0, 0);
                Items.HorizontalAlignment = HorizontalAlignment.Left;
                Items.VerticalAlignment = VerticalAlignment.Center;
                break;

            case DockPosition.Right:
                SlideHost.HorizontalAlignment = HorizontalAlignment.Right;
                SlideHost.VerticalAlignment = alongVertical;
                Body.Margin = new Thickness(0, 0, layout.EdgeGap, 0);
                Items.HorizontalAlignment = HorizontalAlignment.Right;
                Items.VerticalAlignment = VerticalAlignment.Center;
                break;

            default:
                SlideHost.HorizontalAlignment = along;
                SlideHost.VerticalAlignment = VerticalAlignment.Bottom;
                Body.Margin = new Thickness(0, 0, 0, layout.EdgeGap);
                Items.HorizontalAlignment = HorizontalAlignment.Center;
                Items.VerticalAlignment = VerticalAlignment.Bottom;
                break;
        }

        Body.Width = vertical ? layout.BodyThickness : double.NaN;
        Body.Height = vertical ? double.NaN : layout.BodyThickness;
        Items.Margin = vertical
            ? new Thickness(BodyPadding, layout.EndPadding, BodyPadding, layout.EndPadding)
            : new Thickness(layout.EndPadding, BodyPadding, layout.EndPadding, BodyPadding);

        _bodyRadius = Math.Clamp(layout.CornerRadius, 0, layout.BodyThickness / 2);
        var radius = BodyCornerRadius(layout, _bodyRadius);
        BodyBackground.CornerRadius = radius;
        BodyOutline.CornerRadius = radius;

        ConfigurePanel();
        ResetSlideTransform();

        RaiseToTop();
        SyncBackdrop();
    }

    /// <summary>Applies colours and the blur material. Call again when the theme changes.</summary>
    public void ApplyStyle(DockStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var tint = style.Background ?? ToHex(TryFindResource("DockBackgroundColor") as Color? ?? DefaultDockColor);

        // The body is always tinted here rather than through the accent colour: DWM's blur cannot follow the
        // body's corner radius exactly (see DockBackdropWindow), and the WPF tint keeps the edge seamless. The
        // backdrop only adds the blur underneath, so the same opacity looks the same with or without it.
        _blurActive = style.Blur != BlurEffect.None && _backdrop.ApplyEffect(style.Blur, tint, 0);
        if (!_blurActive)
        {
            _backdrop.ClearEffect();
        }

        if (style.Background is { } color)
        {
            BodyBackground.Background = Frozen(color);
        }
        else
        {
            BodyBackground.SetResourceReference(Border.BackgroundProperty, "DockBackgroundBrush");
        }

        BodyBackground.Opacity = style.Opacity;
        Resources["DockIndicatorBrush"] = style.Indicator is { } indicator
            ? Frozen(indicator)
            : TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
        SyncBackdrop();
    }

    /// <summary>Slides the dock on or off screen. The window is hidden entirely once it is off screen.</summary>
    public void SetRevealed(bool shown, bool animate)
    {
        if (_hasRevealState && shown == _revealed)
        {
            return;
        }

        _hasRevealState = true;
        _revealed = shown;
        SlideHost.IsHitTestVisible = shown;
        if (!shown)
        {
            ResetMagnification();
        }
        else if (!IsVisible)
        {
            Show();

            // Other topmost windows may have been raised above us while we were hidden.
            RaiseToTop();
        }

        var generation = ++_slideGeneration;
        var target = shown ? 0 : HiddenOffset();
        if (!animate || !IsVisible)
        {
            Slide.BeginAnimation(SlideProperty(), null);
            Slide.SetValue(SlideProperty(), target);
            FinishSlide();
            return;
        }

        var animation = new DoubleAnimation(target, SlideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = shown ? EasingMode.EaseOut : EasingMode.EaseIn },
        };
        animation.Completed += (_, _) =>
        {
            // A newer slide replaced this one; it will finish the job.
            if (generation == _slideGeneration)
            {
                FinishSlide();
            }
        };
        TrackSlide(true);
        Slide.BeginAnimation(SlideProperty(), animation);
    }

    /// <summary>Collapses any magnification immediately (before menus open, when hiding, while dragging).</summary>
    public void ResetMagnification()
    {
        _magnifying = false;
        if (_panel is not null)
        {
            _panel.BeginAnimation(DockItemsPanel.EngagementProperty, null);
            _panel.Engagement = 0;
            _panel.PointerOffset = double.NaN;
        }
    }

    /// <summary>Quick "launching" hop of an entry's icon, away from the screen edge.</summary>
    public void PlayLaunchFeedback(DockEntry entry)
    {
        if (_layout is null
            || Items.ItemContainerGenerator.ContainerFromItem(entry) is not DependencyObject container
            || FindDescendant(container, "BounceHost") is not FrameworkElement host)
        {
            return;
        }

        // Stay inside the window: the hop may use the padding around the icon plus any magnification headroom.
        var appearance = _viewModel.Appearance;
        var headroom = appearance.CellSize * Math.Max(0, _layout.Magnification - 1);
        var room = ((appearance.CellSize - appearance.IconSize) / 2) + BodyPadding + headroom - 1;
        var hop = Math.Max(3, Math.Min(appearance.IconSize * 0.3, room));
        var direction = _layout.Position == DockPosition.Left ? 1 : -1;

        var bounce = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromMilliseconds(640)) };
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(direction * hop, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(340)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(direction * hop * 0.4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(490)), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        bounce.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(640)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));

        var transform = new TranslateTransform();
        host.RenderTransform = transform;
        transform.BeginAnimation(_layout.Position == DockPosition.Bottom ? TranslateTransform.YProperty : TranslateTransform.XProperty, bounce);
    }

    protected override void OnClosed(EventArgs e)
    {
        TrackSlide(false);
        base.OnClosed(e);
    }

    private void OnItemsPanelLoaded(object sender, RoutedEventArgs e)
    {
        _panel = sender as DockItemsPanel;
        ConfigurePanel();
    }

    private void ConfigurePanel()
    {
        if (_panel is null || _layout is null)
        {
            return;
        }

        _panel.Position = _layout.Position;
        _panel.IconSize = _layout.IconSize;
        _panel.MaxScale = _layout.Magnification;
        ResetMagnification();
    }

    // ---- Magnification ----------------------------------------------------------------------

    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (_panel is null || _layout is null || _layout.Magnification <= 1 || !_revealed || _dragEntry is not null)
        {
            return;
        }

        // Relative to the panel centre, which stays put however much the items grow (the dock is centred).
        var point = e.GetPosition(_panel);
        _panel.PointerOffset = IsVertical ? point.Y - (_panel.ActualHeight / 2) : point.X - (_panel.ActualWidth / 2);
        if (!_magnifying)
        {
            _magnifying = true;
            _panel.BeginAnimation(DockItemsPanel.EngagementProperty, new DoubleAnimation(1, EngageDuration));
        }
    }

    private void ReleaseMagnification()
    {
        if (_magnifying && _panel is not null)
        {
            _magnifying = false;
            _panel.BeginAnimation(DockItemsPanel.EngagementProperty, new DoubleAnimation(0, ReleaseDuration));
        }
    }

    // ---- Clicks -----------------------------------------------------------------------------

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        var container = ContainerOf(e.OriginalSource as DependencyObject);
        _press = container?.DataContext is DockEntry entry
            ? new PressState(entry, container, e.ChangedButton, e.GetPosition(this))
            : null;
    }

    private void OnItemMouseMove(object sender, MouseEventArgs e)
    {
        if (_press is not { Button: MouseButton.Left, Entry: DockAppEntry { IsPinned: true } entry } press
            || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(this) - press.Origin;
        if (Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance)
        {
            _press = null;
            StartPinDrag(entry, press.Container);
        }
    }

    private void OnItemMouseUp(object sender, MouseButtonEventArgs e)
    {
        var press = _press;
        _press = null;

        // A click only counts when it is released on the item it was pressed on.
        if (press is null || press.Button != e.ChangedButton || ContainerOf(e.OriginalSource as DependencyObject) != press.Container)
        {
            return;
        }

        e.Handled = true;
        if (e.ChangedButton == MouseButton.Right)
        {
            // Menus are placed against the item, so it must be back at its normal size first.
            ResetMagnification();
            MenuRequested?.Invoke(this, new DockMenuRequestEventArgs(press.Entry, press.Container));
        }
        else if (e.ChangedButton is MouseButton.Left or MouseButton.Middle)
        {
            EntryInvoked?.Invoke(this, new DockEntryEventArgs(press.Entry, e.ChangedButton));
        }
    }

    private FrameworkElement? ContainerOf(DependencyObject? element) =>
        element is null ? null : ItemsControl.ContainerFromElement(Items, element) as FrameworkElement;

    // ---- Drag and drop ----------------------------------------------------------------------

    private void StartPinDrag(DockAppEntry entry, FrameworkElement container)
    {
        ResetMagnification();
        _dragEntry = entry;
        _dropCommitted = false;
        PinDragStarted?.Invoke(this, EventArgs.Empty);
        try
        {
            DragDrop.DoDragDrop(container, new DataObject(PinDragFormat, entry.Key), DragDropEffects.Move);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Log.Warn("Dock: reordering drag failed", ex);
        }
        finally
        {
            var committed = _dropCommitted;
            _dragEntry = null;
            PinDragFinished?.Invoke(this, committed);
        }
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (_dragEntry is null)
        {
            // OLE drags do not raise mouse events, so tell the visibility logic the pointer has arrived.
            PointerEntered?.Invoke(this, EventArgs.Empty);
        }

        OnDragOver(sender, e);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (_dragEntry is not null && e.Data.GetDataPresent(PinDragFormat))
        {
            if (EntryAt(e.GetPosition(Items)) is DockAppEntry { IsPinned: true } target && target != _dragEntry)
            {
                _viewModel.PreviewMove(_dragEntry, target);
            }

            e.Effects = DragDropEffects.Move;
        }
        else if (PinnableFiles(e.Data).Count > 0)
        {
            e.Effects = DragDropEffects.Link;
        }

        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_dragEntry is not null)
        {
            _dropCommitted = e.Data.GetDataPresent(PinDragFormat);
        }
        else if (PinnableFiles(e.Data) is { Count: > 0 } files)
        {
            FilesDropped?.Invoke(this, new DockFilesDroppedEventArgs(files, PinIndexAt(e.GetPosition(Items))));
        }

        e.Handled = true;
    }

    private static List<string> PinnableFiles(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths.Where(DockPins.IsPinnableFile).ToList()
            : [];

    private FrameworkElement? ContainerAt(Point point) =>
        VisualTreeHelper.HitTest(Items, point)?.VisualHit is { } hit ? ContainerOf(hit) : null;

    private DockEntry? EntryAt(Point point) => ContainerAt(point)?.DataContext as DockEntry;

    /// <summary>Index in the pinned list where a drop at <paramref name="point"/> (Items coordinates) inserts.</summary>
    private int PinIndexAt(Point point)
    {
        var pinned = _viewModel.Entries.OfType<DockAppEntry>().Where(e => e.IsPinned).ToList();
        if (ContainerAt(point) is not { DataContext: DockAppEntry { IsPinned: true } target } container)
        {
            return pinned.Count;
        }

        var local = Items.TranslatePoint(point, container);
        var after = IsVertical ? local.Y > container.ActualHeight / 2 : local.X > container.ActualWidth / 2;
        return pinned.IndexOf(target) + (after ? 1 : 0);
    }

    // ---- Sliding and backdrop ---------------------------------------------------------------

    private DependencyProperty SlideProperty() =>
        _layout?.Position is null or DockPosition.Bottom ? TranslateTransform.YProperty : TranslateTransform.XProperty;

    /// <summary>Distance that moves the body (and its gap) completely past the screen edge.</summary>
    private double HiddenOffset()
    {
        if (_layout is null)
        {
            return 0;
        }

        var distance = _layout.EdgeGap + _layout.BodyThickness + 2;
        return _layout.Position == DockPosition.Left ? -distance : distance;
    }

    private void ResetSlideTransform()
    {
        Slide.BeginAnimation(TranslateTransform.XProperty, null);
        Slide.BeginAnimation(TranslateTransform.YProperty, null);
        Slide.X = 0;
        Slide.Y = 0;
        if (_hasRevealState && !_revealed)
        {
            Slide.SetValue(SlideProperty(), HiddenOffset());
        }
    }

    private void FinishSlide()
    {
        TrackSlide(false);
        if (!_revealed && IsVisible)
        {
            // Off screen: hide the windows so no overlay sits above full-screen apps.
            Hide();
            _backdrop.Hide();
        }

        SyncBackdrop();
    }

    /// <summary>Render transforms do not cause layout passes, so the backdrop follows the slide frame by frame.</summary>
    private void TrackSlide(bool track)
    {
        if (track == _trackingSlide)
        {
            return;
        }

        _trackingSlide = track;
        if (track)
        {
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e) => SyncBackdrop();

    /// <summary>Shows/hides the blur backdrop and clips it to the body's current on-screen rectangle.</summary>
    private void SyncBackdrop()
    {
        if (!_blurActive || !IsVisible || _layout is null || Body.RenderSize.IsEmpty)
        {
            if (_backdrop.IsVisible)
            {
                _backdrop.Hide();
            }

            return;
        }

        // Body bounds within the window (including the slide transform), converted to screen pixels.
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var bounds = Body.TransformToAncestor(this).TransformBounds(new Rect(Body.RenderSize));
        var origin = _layout.WindowBounds;
        var rect = new PixelRect(
            origin.Left + Px(bounds.Left * scale),
            origin.Top + Px(bounds.Top * scale),
            origin.Left + Px(bounds.Right * scale),
            origin.Top + Px(bounds.Bottom * scale));
        if (_layout.ExtendToEdges)
        {
            // DWM rounds every corner. Pushing the screen-edge side off screen hides those corners, leaving them
            // square as in the WPF body.
            var overhang = Px(2 * Math.Max(_bodyRadius, 8) * scale);
            rect = _layout.Position switch
            {
                DockPosition.Left => rect with { Left = rect.Left - overhang },
                DockPosition.Right => rect with { Right = rect.Right + overhang },
                _ => rect with { Bottom = rect.Bottom + overhang },
            };
        }

        _backdrop.PlaceOver(rect, _bodyRadius, scale);
        if (!_backdrop.IsVisible)
        {
            _backdrop.Show();
        }
    }

    /// <summary>
    /// Puts the backdrop and then the dock at the top of the topmost band. The dock is owned by the backdrop, so
    /// raising the owner brings the dock along above it.
    /// </summary>
    private void RaiseToTop()
    {
        NativeMethods.SetWindowPos(_backdrop.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        if (_layout is not null)
        {
            ShellSurface.SetBounds(this, _layout.WindowBounds, topmost: true);
        }
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private static CornerRadius BodyCornerRadius(DockViewLayout layout, double r)
    {
        if (!layout.ExtendToEdges)
        {
            return new CornerRadius(r);
        }

        // Panel mode: square on the screen-edge side.
        return layout.Position switch
        {
            DockPosition.Left => new CornerRadius(0, r, r, 0),
            DockPosition.Right => new CornerRadius(r, 0, 0, r),
            _ => new CornerRadius(r, r, 0, 0),
        };
    }

    private static DependencyObject? FindDescendant(DependencyObject root, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Name: var childName } && childName == name)
            {
                return child;
            }

            if (FindDescendant(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static SolidColorBrush Frozen(HexColor color)
    {
        var brush = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static HexColor ToHex(Color color) => new(color.A, color.R, color.G, color.B);

    private static int Px(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private sealed record PressState(DockEntry Entry, FrameworkElement Container, MouseButton Button, Point Origin);
}
