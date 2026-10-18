using System.Windows;
using System.Windows.Threading;
using WinGnome.Core.Dock;
using WinGnome.Core.Geometry;
using WinGnome.Core.Monitors;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;
using WinGnome.Services;

namespace WinGnome.Features.Dock;

/// <summary>Where a dock's preview popover goes: the dock's edge, its window rectangle (screen px) and its monitor.</summary>
internal sealed record DockPreviewPlacement(DockPosition Position, PixelRect DockWindow, MonitorInfo Monitor);

/// <summary>
/// The window preview popover of one dock (spec 0024): feeds <see cref="DockPreviewHover"/> with the dock's hover
/// events, runs its one-shot timer, and shows, updates and hides the <see cref="DockPreviewWindow"/>.
/// </summary>
/// <remarks>
/// Nothing is created until the first hover that needs it: the timer when a show is first pending, the window when
/// it is first shown. The timer runs only while a show delay or leave grace is pending. Once created, the window is
/// kept hidden at 1 × 1 px between shows, without thumbnails.
/// </remarks>
internal sealed class DockPreviewPresenter : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly WindowTracker _windows;
    private readonly DockActions _actions;
    private readonly Func<DockPreviewPlacement?> _placement;
    private readonly DockPreviewHover _hover = new();
    private DispatcherTimer? _timer;
    private DockPreviewWindow? _window;
    private DockAppEntry? _hoveredEntry;
    private FrameworkElement? _hoveredContainer;
    private DockAppEntry? _shown;
    private IReadOnlyList<DockPreviewItem> _items = [];
    private int _anchor;
    private double _shownScale;
    private bool _enabled = true;
    private bool _disposed;

    /// <param name="dispatcher">The UI dispatcher.</param>
    /// <param name="windows">The window tracker: titles, minimised and elevated state, process ids.</param>
    /// <param name="actions">The dock's actions (focus, close, overview).</param>
    /// <param name="placement">The dock's current placement, or null when it has none yet.</param>
    public DockPreviewPresenter(Dispatcher dispatcher, WindowTracker windows, DockActions actions, Func<DockPreviewPlacement?> placement)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _placement = placement ?? throw new ArgumentNullException(nameof(placement));
    }

    /// <summary>Raised with true when the popover opens and false when it closes.</summary>
    public event EventHandler<bool>? OpenChanged;

    public bool IsOpen => _shown is not null;

    private static long Now => Environment.TickCount64;

    /// <summary><see cref="DockSettings.WindowPreviews"/>: off closes the popover and stops new ones.</summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            Dismiss();
        }
    }

    /// <summary>The pointer moved onto another dock item (<paramref name="entry"/> null for non-apps) or off them.</summary>
    public void PointerOverIcon(DockAppEntry? entry, FrameworkElement? container)
    {
        if (_disposed)
        {
            return;
        }

        _hoveredEntry = entry;
        _hoveredContainer = entry is null ? null : container;
        Apply(_hover.PointerOverIcon(entry?.Key, _enabled && entry is { ShowsPreview: true }, Now));
    }

    /// <summary>
    /// Closes the popover (a click, a drag, the dock hiding, a relayout, a settings change). The icon under the pointer
    /// does not open it again until the pointer leaves it.
    /// </summary>
    public void Dismiss()
    {
        if (!_disposed)
        {
            Apply(_hover.Dismiss());
        }
    }

    /// <summary>A context menu or a pin drag is up: no popover until it is over.</summary>
    public void SetSuppressed(bool suppressed)
    {
        if (!_disposed)
        {
            Apply(_hover.SetSuppressed(suppressed));
        }
    }

    /// <summary>
    /// The user's foreground window changed. Ignored while the pointer is on the popover: closing a window from it
    /// moves the foreground to another window, and the popover must stay up and update then.
    /// </summary>
    public void OnForegroundChanged()
    {
        if (IsOpen && _window is { IsMouseOver: false })
        {
            Dismiss();
        }
    }

    /// <summary>The dock was rebuilt: updates the open popover from its app's entry, closing it when the app is gone.</summary>
    public void Refresh(IReadOnlyList<DockAppEntry> entries)
    {
        if (_shown is not { } shown || _disposed)
        {
            return;
        }

        var entry = entries.FirstOrDefault(e => string.Equals(e.Key, shown.Key, StringComparison.OrdinalIgnoreCase));
        var items = entry is null ? [] : DockPreviewContent.Build(entry.App, _windows.Windows);
        if (entry is null || items.Count == 0)
        {
            Dismiss();
            return;
        }

        if (!DockPreviewContent.NeedsRebuild(_items, items))
        {
            _shown = entry;
            _items = items;
            _window?.UpdateTitles(items);
            return;
        }

        if (_placement() is { } placement)
        {
            Present(entry, items, placement, _anchor);
        }
        else
        {
            Dismiss();
        }
    }

    private void Apply(DockPreviewChange change)
    {
        switch (change)
        {
            case DockPreviewChange.Show or DockPreviewChange.Switch:
                ShowHovered();
                break;

            case DockPreviewChange.Hide:
                Conceal();
                break;
        }

        SyncTimer();
    }

    /// <summary>Opens (or switches) the popover for the hovered icon, which is the machine's key.</summary>
    private void ShowHovered()
    {
        if (_hoveredEntry is { } entry
            && string.Equals(entry.Key, _hover.Key, StringComparison.Ordinal)
            && _placement() is { } placement
            && AnchorOf(_hoveredContainer, placement.Position) is { } anchor
            && DockPreviewContent.Build(entry.App, _windows.Windows) is { Count: > 0 } items)
        {
            Present(entry, items, placement, anchor);
            return;
        }

        _hover.Dismiss();
        Conceal();
    }

    /// <summary>
    /// Builds the popover: thumbnails registered first (their source sizes feed the layout), then content, bounds, the
    /// thumbnails' rectangles, and finally the window shown without activation.
    /// </summary>
    private void Present(DockAppEntry entry, IReadOnlyList<DockPreviewItem> items, DockPreviewPlacement placement, int anchor)
    {
        try
        {
            var window = _window ??= CreateWindow();
            var monitor = placement.Monitor;
            var sizes = window.RegisterThumbnails(items);
            var plan = DockPreviewLayout.Compute(placement.Position, placement.DockWindow, anchor, monitor.Bounds, monitor.WorkArea, monitor.Scale, sizes);
            window.Render(entry.Name, entry.Icon, items, plan, monitor.Scale);
            window.Place(plan.Popover);
            if (!window.IsVisible)
            {
                // WPF may apply its own position and size when a window is first shown: place it again afterwards.
                window.Show();
                window.Place(plan.Popover);
            }

            var opened = _shown is null;
            _shown = entry;
            _items = items;
            _anchor = anchor;
            _shownScale = monitor.Scale;
            if (opened)
            {
                OpenChanged?.Invoke(this, true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Dock: could not show the window previews of '{entry.Name}'", ex);
            _hover.Dismiss();
            Conceal();
        }
    }

    private void Conceal()
    {
        var wasOpen = _shown is not null;
        _shown = null;
        _items = [];
        if (wasOpen)
        {
            _window?.Conceal();
            OpenChanged?.Invoke(this, false);
        }
    }

    /// <summary>The icon's centre along the dock in screen pixels (WPF's screen points are physical pixels).</summary>
    private static int? AnchorOf(FrameworkElement? container, DockPosition position)
    {
        if (container is null || PresentationSource.FromVisual(container) is null)
        {
            return null;
        }

        var centre = container.PointToScreen(new Point(container.ActualWidth / 2, container.ActualHeight / 2));
        return (int)Math.Round(position == DockPosition.Bottom ? centre.X : centre.Y, MidpointRounding.AwayFromZero);
    }

    // ---- Timer ------------------------------------------------------------------------------

    /// <summary>Arms the one-shot timer for the machine's deadline, or stops it when nothing is pending.</summary>
    private void SyncTimer()
    {
        if (_hover.Deadline is not { } due)
        {
            _timer?.Stop();
            return;
        }

        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher);
            _timer.Tick += OnTimerTick;
        }

        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, due - Now));
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        Guard(() => Apply(_hover.Tick(Now)));
    }

    // ---- Popover window ---------------------------------------------------------------------

    private DockPreviewWindow CreateWindow()
    {
        var window = new DockPreviewWindow();
        window.WindowChosen += OnWindowChosen;
        window.CloseRequested += OnCloseRequested;
        window.OverflowChosen += OnOverflowChosen;
        window.MouseEnter += OnPopoverMouseEnter;
        window.MouseLeave += OnPopoverMouseLeave;
        window.DpiChanged += OnPopoverDpiChanged;
        return window;
    }

    private void OnWindowChosen(object? sender, nint hwnd) => Guard(() =>
    {
        Dismiss();
        _actions.ActivateWindow(hwnd);
    });

    /// <summary>Posts a polite close; the popover stays and updates when the tracker sees the window go.</summary>
    private void OnCloseRequested(object? sender, DockPreviewItem item) => Guard(() => _actions.CloseWindow(item.Target));

    private void OnOverflowChosen(object? sender, EventArgs e) => Guard(() =>
    {
        var app = _shown?.App;
        Dismiss();
        if (app is not null)
        {
            _actions.ShowWindows(app);
        }
    });

    private void OnPopoverMouseEnter(object sender, System.Windows.Input.MouseEventArgs e) =>
        Guard(() => Apply(_hover.PointerOverPopover(true, Now)));

    private void OnPopoverMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        Guard(() => Apply(_hover.PointerOverPopover(false, Now)));

    /// <summary>
    /// Placing the popover on a monitor with another DPI changes its DPI to the one it was laid out for; only a change
    /// to any other DPI needs a new layout (deferred: this runs inside the move).
    /// </summary>
    private void OnPopoverDpiChanged(object sender, DpiChangedEventArgs e)
    {
        if (IsOpen && Math.Abs(e.NewDpi.DpiScaleX - _shownScale) > 0.001)
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Background, () => Guard(() =>
            {
                if (_shown is { } shown && _placement() is { } placement)
                {
                    Present(shown, _items, placement, _anchor);
                }
            }));
        }
    }

    /// <summary>Popover input and timer callbacks reach into other apps' windows; a failure must not take the dock down.</summary>
    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Warn("Dock: window preview action failed", ex);
        }
    }

    /// <summary>Releases the window (and with it every thumbnail), then the timer: reverse order of creation.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shown = null;
        _items = [];
        if (_window is { } window)
        {
            _window = null;
            window.WindowChosen -= OnWindowChosen;
            window.CloseRequested -= OnCloseRequested;
            window.OverflowChosen -= OnOverflowChosen;
            window.MouseEnter -= OnPopoverMouseEnter;
            window.MouseLeave -= OnPopoverMouseLeave;
            window.DpiChanged -= OnPopoverDpiChanged;
            window.Close();
        }

        if (_timer is { } timer)
        {
            _timer = null;
            timer.Stop();
            timer.Tick -= OnTimerTick;
        }

        _hoveredEntry = null;
        _hoveredContainer = null;
    }
}
