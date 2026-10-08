using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinGnome.Core.Geometry;
using WinGnome.Core.Overview;
using WinGnome.Core.Windows;
using WinGnome.Interop;
using WinGnome.Services.Apps;

namespace WinGnome.Features.Overview;

/// <summary>
/// The overview's window grid: one live DWM thumbnail per window, arranged with
/// <see cref="OverviewLayout"/>, with a <see cref="WindowSlotView"/> on a WPF canvas at the same place for
/// hit-testing, highlight and caption.
/// </summary>
/// <remarks>
/// Layout happens in DIPs; DWM wants physical pixels relative to the host window's client area, which for
/// the borderless full-screen overview is the window itself. Every thumbnail is unregistered in
/// <see cref="Clear"/>, which the overview calls whenever it hides.
/// </remarks>
internal sealed class ThumbnailLayer
{
    /// <summary>Duration of the "zoom out" from the real window positions into the grid.</summary>
    private static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(220);

    /// <summary>Fade-in of captions and highlight rings once the thumbnails have landed.</summary>
    private static readonly TimeSpan CaptionFadeDuration = TimeSpan.FromMilliseconds(120);

    /// <summary>Gap between slots. Rows need room for the caption under each thumbnail plus the highlight rings.</summary>
    private const double Spacing = WindowSlotView.CaptionHeight + (2 * WindowSlotView.FramePadding) + 16;

    /// <summary>Smaller DWM source sizes mean DWM has no usable picture (e.g. some minimised windows).</summary>
    private const int MinUsableSourcePx = 32;

    private const double PlaceholderIconDip = 64;

    private readonly Canvas _canvas;
    private readonly IIconProvider _icons;
    private readonly List<WindowSlot> _slots = [];
    private nint _host;
    private PixelRect _hostBounds;
    private double _scale = 1;
    private LayoutRect _area;
    private int _selected = -1;
    private bool _visible = true;
    private Stopwatch? _animationClock;

    public ThumbnailLayer(Canvas canvas, IIconProvider icons)
    {
        _canvas = canvas;
        _icons = icons;
    }

    /// <summary>A thumbnail was clicked.</summary>
    public event EventHandler<nint>? WindowActivated;

    /// <summary>A thumbnail's close button (or middle click) was used.</summary>
    public event EventHandler<nint>? WindowCloseRequested;

    /// <summary>The keyboard-selected window, or 0.</summary>
    public nint SelectedWindow => _selected >= 0 && _selected < _slots.Count ? _slots[_selected].Window.Handle : 0;

    /// <summary>Replaces all thumbnails with <paramref name="windows"/>, optionally animating them into place.</summary>
    /// <param name="host">The overview window that DWM draws into.</param>
    /// <param name="hostBounds">The host's screen rectangle in physical pixels.</param>
    /// <param name="scale">The host's DPI scale.</param>
    /// <param name="area">Area (DIPs, host-relative) to arrange the windows in.</param>
    /// <param name="windows">Windows to show, in display order.</param>
    /// <param name="animate">Glide thumbnails from their real positions into the grid.</param>
    public void Show(nint host, PixelRect hostBounds, double scale, LayoutRect area, IReadOnlyList<WindowInfo> windows, bool animate)
    {
        Clear();
        _host = host;
        _hostBounds = hostBounds;
        _scale = scale;
        _area = area;
        _visible = true;
        _canvas.Visibility = Visibility.Visible;

        foreach (var window in windows)
        {
            AddSlot(window);
        }

        Arrange();
        if (animate && _slots.Count > 0)
        {
            StartAnimation();
        }
        else
        {
            ApplyTargets();
        }
    }

    /// <summary>
    /// Brings the grid in line with the current window list: closed windows disappear, new ones are added
    /// at the end and titles are refreshed. Surviving windows keep their order so the grid does not shuffle.
    /// </summary>
    public void Update(IReadOnlyList<WindowInfo> windows)
    {
        if (_host == 0)
        {
            return;
        }

        StopAnimation();
        var selectedWindow = SelectedWindow;
        var current = windows.ToDictionary(w => w.Handle);
        for (var i = _slots.Count - 1; i >= 0; i--)
        {
            var slot = _slots[i];
            if (current.Remove(slot.Window.Handle, out var info))
            {
                // The new title is shown by Arrange below.
                slot.Window = info;
            }
            else
            {
                RemoveSlot(i);
            }
        }

        foreach (var window in windows.Where(w => current.ContainsKey(w.Handle)))
        {
            AddSlot(window);
        }

        _selected = _slots.FindIndex(s => s.Window.Handle == selectedWindow);
        Arrange();
        ApplyTargets();
        RefreshSelection();
    }

    /// <summary>Hides the grid while search results are shown, and brings it back afterwards.</summary>
    public void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            return;
        }

        _visible = visible;
        StopAnimation();
        _canvas.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        foreach (var slot in _slots)
        {
            if (visible)
            {
                slot.Thumbnail?.Show(slot.TargetPx);
            }
            else
            {
                slot.Thumbnail?.Hide();
            }
        }
    }

    /// <summary>Moves the keyboard selection to the nearest thumbnail in <paramref name="direction"/>.</summary>
    public void MoveSelection(NavigationDirection direction)
    {
        _selected = SelectionNavigator.FindNeighbor(_slots.Select(s => s.Target).ToList(), _selected, direction);
        RefreshSelection();
    }

    /// <summary>Unregisters every thumbnail and removes every slot.</summary>
    public void Clear()
    {
        StopAnimation();
        foreach (var slot in _slots)
        {
            slot.Thumbnail?.Dispose();
        }

        _slots.Clear();
        _canvas.Children.Clear();
        _selected = -1;
        _host = 0;
    }

    private void AddSlot(WindowInfo window)
    {
        var thumbnail = DwmThumbnail.TryRegister(_host, window.Handle);
        var sourceSize = thumbnail?.QuerySourceSize();
        if (thumbnail is not null && sourceSize is not { Width: >= MinUsableSourcePx, Height: >= MinUsableSourcePx })
        {
            // DWM has no usable picture; the slot shows the app icon instead.
            thumbnail.Dispose();
            thumbnail = null;
            sourceSize = null;
        }

        var natural = sourceSize
            ?? (window.IsMinimized ? NativeMethods.GetRestoredSize(window.Handle) : null)
            ?? (window.Bounds.IsEmpty ? (800, 600) : (window.Bounds.Width, window.Bounds.Height));

        var icon = _icons.GetWindowIcon(window.Handle, window.ProcessPath, (int)Math.Round(PlaceholderIconDip * _scale));
        var view = new WindowSlotView();
        var slot = new WindowSlot(window, thumbnail, view, icon, new LayoutSize(natural.Width / _scale, natural.Height / _scale));
        view.Activated += (_, _) => WindowActivated?.Invoke(this, slot.Window.Handle);
        view.CloseRequested += (_, _) => WindowCloseRequested?.Invoke(this, slot.Window.Handle);
        view.Hovered += (_, _) =>
        {
            _selected = _slots.IndexOf(slot);
            RefreshSelection();
        };

        _slots.Add(slot);
        _canvas.Children.Add(view);
    }

    private void RemoveSlot(int index)
    {
        var slot = _slots[index];
        slot.Thumbnail?.Dispose();
        _canvas.Children.Remove(slot.View);
        _slots.RemoveAt(index);
    }

    private void Arrange()
    {
        // Leave room around the thumbnails for the highlight ring, and below the last row for its caption.
        var padding = WindowSlotView.FramePadding;
        var area = new LayoutRect(
            _area.X + padding,
            _area.Y + padding,
            Math.Max(1, _area.Width - (2 * padding)),
            Math.Max(1, _area.Height - (2 * padding) - WindowSlotView.CaptionHeight));
        var targets = OverviewLayout.Arrange(_slots.Select(s => s.NaturalSize).ToList(), area, Spacing);

        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            var target = targets[i];
            slot.Target = target;
            slot.TargetPx = new PixelRect(
                (int)Math.Round(target.X * _scale),
                (int)Math.Round(target.Y * _scale),
                (int)Math.Round(target.Right * _scale),
                (int)Math.Round(target.Bottom * _scale));

            var view = slot.View;
            Canvas.SetLeft(view, target.X - padding);
            Canvas.SetTop(view, target.Y - padding);
            view.Width = target.Width + (2 * padding);
            view.Height = target.Height + (2 * padding) + WindowSlotView.CaptionHeight;

            // The caption row holds a 28 DIP column either side of the title (icon and close button).
            view.SetContent(slot.Window.Title, slot.Icon, view.Width - 56 - 26);
        }
    }

    private void ApplyTargets()
    {
        if (!_visible)
        {
            return;
        }

        foreach (var slot in _slots)
        {
            slot.Thumbnail?.Show(slot.TargetPx);
        }
    }

    private void RefreshSelection()
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            _slots[i].View.IsSelected = i == _selected;
        }
    }

    private void StartAnimation()
    {
        foreach (var slot in _slots)
        {
            // Minimised windows have no on-screen position to grow from, so they fade in at their slot.
            var onScreen = slot.Window.IsMinimized || slot.Window.Bounds.IsEmpty
                ? (PixelRect?)null
                : slot.Window.Bounds.Offset(-_hostBounds.Left, -_hostBounds.Top);
            slot.StartPx = onScreen ?? slot.TargetPx;
            slot.StartOpacity = onScreen is null ? (byte)0 : (byte)255;
            slot.Thumbnail?.Show(slot.StartPx, slot.StartOpacity);
        }

        // Captions and highlight rings are ordinary WPF content. They stay hidden while the thumbnails glide and
        // fade in once they have landed (as in GNOME): fading the full-screen canvas during the glide made WPF
        // re-render an overview-sized layer every frame, which halved the glide's frame rate (measured on a
        // 2560x1440 screen: 8 instead of 16 frames per glide, 5 with software rendering).
        _canvas.BeginAnimation(UIElement.OpacityProperty, null);
        _canvas.Visibility = Visibility.Hidden;
        _animationClock = Stopwatch.StartNew();
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_animationClock is null)
        {
            return;
        }

        var progress = _animationClock.Elapsed.TotalMilliseconds / OpenDuration.TotalMilliseconds;
        if (progress >= 1)
        {
            StopAnimation();
            ApplyTargets();
            if (_visible)
            {
                _canvas.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, CaptionFadeDuration));
            }

            return;
        }

        var eased = ThumbnailTransition.EaseOut(progress);
        foreach (var slot in _slots)
        {
            slot.Thumbnail?.Show(
                ThumbnailTransition.Interpolate(slot.StartPx, slot.TargetPx, eased),
                ThumbnailTransition.FadeIn(slot.StartOpacity, eased));
        }
    }

    /// <summary>
    /// Ends a running opening animation immediately (thumbnails are left where they were) and shows the captions.
    /// </summary>
    private void StopAnimation()
    {
        if (_animationClock is null)
        {
            return;
        }

        _animationClock = null;
        CompositionTarget.Rendering -= OnRendering;
        _canvas.Visibility = _visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed class WindowSlot(WindowInfo window, DwmThumbnail? thumbnail, WindowSlotView view, ImageSource? icon, LayoutSize naturalSize)
    {
        public WindowInfo Window { get; set; } = window;

        public DwmThumbnail? Thumbnail { get; } = thumbnail;

        public WindowSlotView View { get; } = view;

        public ImageSource? Icon { get; } = icon;

        public LayoutSize NaturalSize { get; } = naturalSize;

        public LayoutRect Target { get; set; }

        public PixelRect TargetPx { get; set; }

        public PixelRect StartPx { get; set; }

        public byte StartOpacity { get; set; }
    }
}
