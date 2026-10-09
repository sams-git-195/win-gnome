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
/// <para>
/// Each slot has a <see cref="ThumbnailTrack"/> between its window's real position and its grid slot. The
/// overview's <see cref="OverviewAnimator"/> moves every thumbnail along its track with
/// <see cref="SetProgress"/>; once settled, tracks are still at the slot.
/// </para>
/// </remarks>
internal sealed class ThumbnailLayer
{
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
    private bool _moving;
    private double _progress = 1;

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

    /// <summary>True while the grid is on screen (not replaced by search results).</summary>
    public bool IsVisible => _visible;

    /// <summary>
    /// Lays out the grid for <paramref name="windows"/> with their icons in place of live thumbnails (no DWM
    /// registration): used to warm WPF up before the first real open. <see cref="Clear"/> removes it again.
    /// </summary>
    public void ShowPlaceholders(PixelRect hostBounds, double scale, LayoutRect area, IReadOnlyList<WindowInfo> windows)
    {
        Clear();
        _hostBounds = hostBounds;
        _scale = scale;
        _area = area;
        _visible = true;
        foreach (var window in windows.DistinctBy(w => w.Handle))
        {
            AddSlot(window, null);
        }

        Arrange();
        Settle(fadeCaptions: false);
    }

    /// <summary>
    /// Replaces all thumbnails with <paramref name="windows"/>. With <paramref name="fromWindows"/> each one is
    /// put where its window really is, ready to glide into the grid through <see cref="SetProgress"/>.
    /// </summary>
    /// <param name="host">The overview window that DWM draws into.</param>
    /// <param name="hostBounds">The host's screen rectangle in physical pixels.</param>
    /// <param name="scale">The host's DPI scale.</param>
    /// <param name="area">Area (DIPs, host-relative) to arrange the windows in.</param>
    /// <param name="windows">Windows to show, in display order (z-order, topmost first).</param>
    /// <param name="fromWindows">Start at the windows' real positions instead of in the grid.</param>
    public void Show(nint host, PixelRect hostBounds, double scale, LayoutRect area, IReadOnlyList<WindowInfo> windows, bool fromWindows)
    {
        Clear();
        _host = host;
        _hostBounds = hostBounds;
        _scale = scale;
        _area = area;
        _visible = true;

        // DWM stacks thumbnails in registration order (the last on top), so registering bottom-up makes the
        // first frame, with every thumbnail over its window, look exactly like the desktop.
        var thumbnails = new Dictionary<nint, DwmThumbnail?>();
        try
        {
            for (var i = windows.Count - 1; i >= 0; i--)
            {
                if (!thumbnails.ContainsKey(windows[i].Handle))
                {
                    thumbnails[windows[i].Handle] = DwmThumbnail.TryRegister(_host, windows[i].Handle);
                }
            }

            // Each handle gets one slot; a slot owns its thumbnail from here on (Clear releases it).
            foreach (var window in windows)
            {
                if (thumbnails.Remove(window.Handle, out var thumbnail))
                {
                    AddSlot(window, thumbnail);
                }
            }
        }
        finally
        {
            // Only left over if AddSlot threw: release what no slot took.
            foreach (var thumbnail in thumbnails.Values)
            {
                thumbnail?.Dispose();
            }
        }

        Arrange();
        if (!fromWindows)
        {
            Settle(fadeCaptions: false);
            return;
        }

        foreach (var slot in _slots)
        {
            // Minimised windows have no on-screen position to grow from, so they fade in at their slot.
            slot.Track = OnScreenRect(slot) is { } onScreen
                ? new ThumbnailTrack(onScreen, slot.TargetPx, 255, 255)
                : new ThumbnailTrack(slot.TargetPx, slot.TargetPx, 0, 255);
        }

        BeginMoving();
    }

    /// <summary>Puts every thumbnail at <paramref name="eased"/> progress (0..1) along its track.</summary>
    public void SetProgress(double eased)
    {
        _progress = eased;
        if (!_visible)
        {
            return;
        }

        foreach (var slot in _slots)
        {
            var frame = slot.Track.At(eased);
            slot.Thumbnail?.Show(frame.Rect, frame.Opacity);
        }
    }

    /// <summary>
    /// Ends a glide into the grid: thumbnails rest in their slots and the captions fade in (shown straight away
    /// when nothing was moving, e.g. with animations off or after a mode switch).
    /// </summary>
    public void CompleteOpening() => Settle(fadeCaptions: _moving);

    /// <summary>
    /// Points every thumbnail back at its window, starting from where it is now. Windows that are minimised
    /// (or gone) fade out where they are. Captions hide for the glide.
    /// </summary>
    /// <param name="raised">The window being brought to the front (0: none); its thumbnail lands on top.</param>
    public void BeginClosing(nint raised)
    {
        RaiseThumbnail(raised);
        foreach (var slot in _slots)
        {
            var current = slot.Track.At(_progress);
            slot.Track = OnScreenRect(slot) is { } onScreen
                ? OverviewTransition.Retarget(slot.Track, _progress, onScreen, 255)
                : OverviewTransition.Retarget(slot.Track, _progress, current.Rect, 0);
        }

        BeginMoving();
    }

    /// <summary>Turns a closing glide around: every thumbnail heads back to its slot from where it is now.</summary>
    public void BeginReopening()
    {
        foreach (var slot in _slots)
        {
            slot.Track = OverviewTransition.Retarget(slot.Track, _progress, slot.TargetPx, 255);
        }

        BeginMoving();
    }

    /// <summary>
    /// Brings the grid in line with the current window list: closed windows disappear, new ones are added
    /// at the end and titles are refreshed. Surviving windows keep their order so the grid does not shuffle.
    /// </summary>
    /// <remarks>Not for use during a glide: the overview applies window changes once it has settled.</remarks>
    public void Update(IReadOnlyList<WindowInfo> windows)
    {
        if (_host == 0)
        {
            return;
        }

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
            AddSlot(window, DwmThumbnail.TryRegister(_host, window.Handle));
        }

        _selected = _slots.FindIndex(s => s.Window.Handle == selectedWindow);
        Arrange();
        Settle(fadeCaptions: false);
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
        UpdateCanvasVisibility();
        if (visible)
        {
            SetProgress(_progress);
            return;
        }

        foreach (var slot in _slots)
        {
            slot.Thumbnail?.Hide();
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
        foreach (var slot in _slots)
        {
            slot.Thumbnail?.Dispose();
        }

        _slots.Clear();
        _canvas.Children.Clear();
        _selected = -1;
        _host = 0;
        _moving = false;
        _progress = 1;
    }

    private void AddSlot(WindowInfo window, DwmThumbnail? thumbnail)
    {
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

    /// <summary>Rests every thumbnail in its slot and shows the captions, optionally fading them in.</summary>
    private void Settle(bool fadeCaptions)
    {
        foreach (var slot in _slots)
        {
            slot.Track = ThumbnailTrack.Still(slot.TargetPx);
        }

        _moving = false;
        SetProgress(1);
        UpdateCanvasVisibility();
        _canvas.BeginAnimation(UIElement.OpacityProperty, fadeCaptions && _visible ? new DoubleAnimation(0, 1, CaptionFadeDuration) : null);
    }

    /// <summary>Starts a glide: thumbnails go to the start of their tracks and the captions hide.</summary>
    private void BeginMoving()
    {
        // Captions and highlight rings are ordinary WPF content. They stay hidden while the thumbnails glide and
        // fade in once they have landed (as in GNOME): fading the full-screen canvas during the glide made WPF
        // re-render an overview-sized layer every frame, which halved the glide's frame rate (measured on a
        // 2560x1440 screen: 8 instead of 16 frames per glide, 5 with software rendering).
        _moving = true;
        _canvas.BeginAnimation(UIElement.OpacityProperty, null);
        UpdateCanvasVisibility();
        SetProgress(0);
    }

    private void UpdateCanvasVisibility() =>
        _canvas.Visibility = !_visible ? Visibility.Collapsed : _moving ? Visibility.Hidden : Visibility.Visible;

    /// <summary>
    /// Where the slot's window is on screen, relative to the host, as the whole window rectangle: DWM thumbnails
    /// draw the invisible resize borders too, so the visible frame bounds would make the first frame jump.
    /// Null for minimised or closed windows.
    /// </summary>
    private PixelRect? OnScreenRect(WindowSlot slot)
    {
        var hwnd = slot.Window.Handle;
        if (!NativeMethods.IsWindow(hwnd) || NativeMethods.IsIconic(hwnd))
        {
            return null;
        }

        var bounds = NativeMethods.GetWindowBounds(hwnd);
        return bounds.IsEmpty ? null : bounds.Offset(-_hostBounds.Left, -_hostBounds.Top);
    }

    /// <summary>Re-registers a window's thumbnail so DWM draws it above the others; keeps the old one if that fails.</summary>
    private void RaiseThumbnail(nint hwnd)
    {
        if (_slots.Find(s => s.Window.Handle == hwnd) is not { Thumbnail: { } old } slot
            || DwmThumbnail.TryRegister(_host, hwnd) is not { } raised)
        {
            return;
        }

        slot.Thumbnail = raised;
        old.Dispose();
    }

    private void RefreshSelection()
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            _slots[i].View.IsSelected = i == _selected;
        }
    }

    private sealed class WindowSlot(WindowInfo window, DwmThumbnail? thumbnail, WindowSlotView view, ImageSource? icon, LayoutSize naturalSize)
    {
        public WindowInfo Window { get; set; } = window;

        public DwmThumbnail? Thumbnail { get; set; } = thumbnail;

        public WindowSlotView View { get; } = view;

        public ImageSource? Icon { get; } = icon;

        public LayoutSize NaturalSize { get; } = naturalSize;

        public LayoutRect Target { get; set; }

        public PixelRect TargetPx { get; set; }

        public ThumbnailTrack Track { get; set; }
    }
}
