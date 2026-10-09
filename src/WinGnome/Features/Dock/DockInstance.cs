using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WinGnome.Core.Dock;
using WinGnome.Core.Geometry;
using WinGnome.Core.Monitors;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services;
using WinGnome.Services.Tray;

namespace WinGnome.Features.Dock;

/// <summary>
/// The dock on one monitor: its window, blur backdrop, actions, context menu, visibility and strip reservation,
/// laid out for that monitor. Pins, catalogue, theme, Super+N and display changes are coordinated by
/// <see cref="DockFeature"/>.
/// </summary>
internal sealed class DockInstance : IDisposable
{
    /// <summary>Padding at both ends of the dock (DIP).</summary>
    private const double EndPadding = 8;

    /// <summary>Largest icon bitmap requested from the shell.</summary>
    private const int MaxIconPixels = 256;

    // Broadcast by Explorer when it (re)starts; a restarted Explorer has forgotten every AppBar registration.
    private static readonly uint TaskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

    private readonly ShellContext _context;
    private readonly ExternalForeground _foreground;
    private readonly DockViewModel _viewModel;
    private readonly BlurBackdrop _backdrop;
    private readonly DockWindow _window;
    private readonly DockActions _actions;
    private readonly DockMenuPresenter _menu;
    private readonly DockVisibilityController _visibility;
    private readonly DockReservation _reservation;
    private readonly HwndSource? _source;
    private DockSettings _dock;
    private bool _relayoutQueued;
    private bool _menuOpen;
    private bool _dragging;
    private bool _disposed;
    private int _iconPixels;
    private (int Cells, int Separators) _layoutCounts = (-1, -1);
    private (string, PixelRect, DockPosition, DockVisibility, double)? _loggedPlacement;

    public DockInstance(ShellContext context, ExternalForeground foreground, DockEdgePoller poller, DockSettings dock, MonitorInfo monitor)
    {
        _context = context;
        _foreground = foreground;
        _dock = dock;
        Monitor = monitor;
        _viewModel = new DockViewModel(context.Icons, context.Apps);
        _backdrop = new BlurBackdrop("WinGnome Dock Backdrop");
        _window = new DockWindow(_viewModel, _backdrop);
        var window = _window;
        _actions = new DockActions(context, foreground, window.PlayLaunchFeedback, () => new WindowInteropHelper(window).Handle);
        _menu = new DockMenuPresenter(context.Dispatcher);
        _visibility = new DockVisibilityController(context, _window, foreground, poller);
        _reservation = new DockReservation();

        _window.EntryInvoked += OnEntryInvoked;
        _window.MenuRequested += OnMenuRequested;
        _window.PinDragStarted += OnPinDragStarted;
        _window.PinDragFinished += OnPinDragFinished;
        _window.FilesDropped += OnFilesDropped;
        _window.DpiChanged += OnWindowDpiChanged;
        _menu.OpenChanged += OnMenuOpenChanged;
        _reservation.Detached += OnReservationDetached;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
        _source?.AddHook(WndProc);
    }

    /// <summary>Explorer restarted (raised by every dock; the coordinator coalesces them into one pass).</summary>
    public event EventHandler? ExplorerRestarted;

    /// <summary>The strip's AppBar undocked itself because its monitor went away or changed.</summary>
    public event EventHandler? Detached;

    /// <summary>The monitor this dock is laid out for.</summary>
    public MonitorInfo Monitor { get; private set; }

    /// <summary>The model changed its cell count or icon size and wants a fresh model.</summary>
    public event EventHandler? RefreshRequested;

    public SurfaceState State => new(Monitor.Key, Monitor.Bounds, Monitor.Dpi, _reservation.IsDetached);

    /// <summary>Pinned launch ids in the order shown (after a drag preview).</summary>
    public IReadOnlyList<DockAppEntry> AppEntries => _viewModel.AppEntries;

    public void ApplySettings(DockSettings dock) => _dock = dock;

    /// <summary>Shows the dock (after <see cref="DockOn"/>) or hides it and gives its strip back.</summary>
    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            _menu.Close();
            _reservation.Release();
        }

        _visibility.SetEnabled(enabled);
    }

    /// <summary>Lays the dock out for <paramref name="monitor"/> and reserves its strip there in always-visible mode.</summary>
    public void DockOn(MonitorInfo monitor)
    {
        Monitor = monitor;
        Relayout();
    }

    /// <summary>Gives the strip back, keeping the dock, so a later <see cref="DockOn"/> can reserve it again.</summary>
    public void Release() => _reservation.Release();

    /// <summary>Explorer pass, step one: undock without forgetting the reservation.</summary>
    public void UndockForExplorer() => _reservation.Undock();

    /// <summary>Explorer pass, step two: register the reservation with the new Explorer.</summary>
    public void RedockForExplorer() => _reservation.Redock();

    public void CloseMenu() => _menu.Close();

    public void ApplyStyle()
    {
        try
        {
            _window.ApplyStyle(new DockStyle(ParseOptional(_dock.BackgroundColor), _dock.Opacity, _dock.Blur, ParseOptional(_dock.IndicatorColor)));
        }
        catch (Exception ex)
        {
            Log.Warn("Dock: could not apply the appearance", ex);
        }
    }

    /// <summary>Rebuilds the items from <paramref name="running"/> (already filtered for this monitor when isolating).</summary>
    public void Refresh(IReadOnlyList<RunningWindow> running)
    {
        // A drag preview reorders entries locally; a refresh would undo it mid-drag.
        if (_dragging)
        {
            return;
        }

        try
        {
            var apps = DockModelBuilder.Build(_dock.PinnedApps, running.ToList(), _foreground.Handle, _dock.ShowRunningApps, _context.Apps.ResolvePath);
            _viewModel.Update(apps, _dock, _iconPixels);
            if ((_viewModel.CellCount, _viewModel.SeparatorCount) != _layoutCounts)
            {
                Relayout();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Dock: refresh on {Monitor.Key} failed", ex);
        }
    }

    /// <summary>Super+N: the <paramref name="index"/>-th app entry (pinned first), if there is one.</summary>
    public void ActivateItem(int index)
    {
        if (index >= 0 && index < _viewModel.AppEntries.Count)
        {
            Run(() => _actions.Click(_viewModel.AppEntries[index]));
        }
    }

    // ---- Geometry ---------------------------------------------------------------------------

    /// <summary>Computes the dock geometry for its monitor and pushes it to the windows and the AppBar.</summary>
    private void Relayout()
    {
        try
        {
            var monitor = Monitor.Bounds;
            var scale = Monitor.Scale;
            var cells = _viewModel.CellCount;
            var separators = _viewModel.SeparatorCount;
            var separatorLength = separators * DockAppearance.SeparatorLength;
            var monitorLength = (_dock.Position == DockPosition.Bottom ? monitor.Width : monitor.Height) / scale;

            var iconSize = DockFrameLayout.FitIconSize(monitorLength, Math.Max(1, cells), separatorLength, _dock.IconSize, _dock.IconSpacing, EndPadding);
            var cellSize = iconSize + (2 * _dock.IconSpacing);
            var options = new DockLayoutOptions(_dock.IconSpacing, _dock.EdgeMargin, EndPadding, separatorLength);
            var geometry = DockLayout.Compute(monitor, _dock.Position, cells, iconSize * scale, scale, _dock.ExtendToEdges, options);
            var frame = DockFrameLayout.Compute(monitor, _dock.Position, geometry, cellSize * scale, iconSize * scale, _dock.Magnification, _dock.ExtendToEdges);

            _layoutCounts = (cells, separators);
            _viewModel.Appearance.Apply(_dock, iconSize, _dock.CornerRadius);
            _window.ApplyLayout(new DockViewLayout(
                frame.Window,
                _dock.Position,
                _dock.ExtendToEdges,
                EdgeGap: _dock.ExtendToEdges ? 0 : _dock.EdgeMargin,
                BodyThickness: cellSize + (2 * DockWindow.BodyPadding),
                EndPadding,
                _dock.CornerRadius,
                _dock.Magnification,
                iconSize));

            if (_dock.Visibility == DockVisibility.AlwaysVisible)
            {
                _reservation.Reserve(ToAppBarEdge(_dock.Position), frame.ReservedThickness, monitor);
            }
            else
            {
                _reservation.Release();
            }

            _visibility.Configure(_dock.Visibility, frame, Monitor);

            // Inner edges (another monitor beyond the dock's edge) are not detected; reveal there is unreliable (KI-071).
            var placement = (Monitor.Key, monitor, _dock.Position, _dock.Visibility, scale);
            if (placement != _loggedPlacement)
            {
                _loggedPlacement = placement;
                Log.Info($"Dock laid out on {Monitor.Key} ({_dock.Position} edge, {_dock.Visibility}, DPI scale {scale:0.##})");
            }

            // Load icons at the size they are shown when fully magnified, so they stay crisp.
            var iconPixels = (int)Math.Min(MaxIconPixels, Math.Round(iconSize * scale * Math.Max(1, _dock.Magnification)));
            if (iconPixels != _iconPixels)
            {
                _iconPixels = iconPixels;
                RefreshRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Dock: layout on {Monitor.Key} failed", ex);
        }
    }

    private static AppBarEdge ToAppBarEdge(DockPosition position) => position switch
    {
        DockPosition.Left => AppBarEdge.Left,
        DockPosition.Right => AppBarEdge.Right,
        _ => AppBarEdge.Bottom,
    };

    private static HexColor? ParseOptional(string value) => HexColor.TryParse(value, out var color) ? color : null;

    /// <summary>
    /// WPF rescales the window to Windows' suggested rectangle on a DPI change; lay it out again afterwards. A scale
    /// change sends no WM_DISPLAYCHANGE, so the layout service also re-reads the monitors.
    /// </summary>
    private void OnWindowDpiChanged(object? sender, DpiChangedEventArgs e)
    {
        _context.Displays.Invalidate();
        if (_relayoutQueued)
        {
            return;
        }

        _relayoutQueued = true;
        _context.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _relayoutQueued = false;

            // Only on a monitor that still exists with the same bounds: Windows also sends WM_DPICHANGED when it moves
            // the window off a removed monitor, and the coordinator's pass handles that case.
            if (!_disposed && !_reservation.IsDetached
                && DisplayLayoutService.Read().Find(Monitor.Key) is { } monitor && monitor.Bounds == Monitor.Bounds)
            {
                DockOn(monitor);
            }
        });
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (TaskbarCreatedMessage != 0 && msg == (int)TaskbarCreatedMessage && !TrayHost.IsOwnBroadcast(wParam))
        {
            ExplorerRestarted?.Invoke(this, EventArgs.Empty);
        }

        return 0;
    }

    private void OnReservationDetached(object? sender, EventArgs e) => Detached?.Invoke(this, EventArgs.Empty);

    // ---- Input ------------------------------------------------------------------------------

    private void OnEntryInvoked(object? sender, DockEntryEventArgs e) => Run(() => _actions.Invoke(e.Entry, e.Button));

    private void OnMenuRequested(object? sender, DockMenuRequestEventArgs e) => Run(() =>
    {
        if (DockMenuBuilder.Build(e.Entry, _actions, _context.Windows) is { } menu)
        {
            _menu.Show(menu, e.Target, _dock.Position, _foreground.Handle);
        }
    });

    private void OnMenuOpenChanged(object? sender, bool open)
    {
        _menuOpen = open;
        _visibility.SetInteracting(_menuOpen || _dragging);
    }

    private void OnPinDragStarted(object? sender, EventArgs e)
    {
        _dragging = true;
        _visibility.SetInteracting(true);
    }

    private void OnPinDragFinished(object? sender, bool committed)
    {
        _dragging = false;
        _visibility.SetInteracting(_menuOpen);
        if (committed)
        {
            // The settings change triggers ApplySettings, which refreshes every dock in the new order.
            Run(() => _actions.ReorderPins(_viewModel.PinnedLaunchIds));
        }
        else
        {
            // Cancelled: undo the drag preview.
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnFilesDropped(object? sender, DockFilesDroppedEventArgs e) => Run(() => _actions.PinFiles(e.Paths, e.PinIndex));

    /// <summary>Input handlers call into Win32 and other apps; a failure must never take the dock down.</summary>
    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Warn("Dock: action failed", ex);
        }
    }

    /// <summary>Gives the strip back first, then closes the windows.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source?.RemoveHook(WndProc);
        _menu.Close();
        _visibility.Dispose();
        _reservation.Detached -= OnReservationDetached;
        _reservation.Dispose();
        _window.EntryInvoked -= OnEntryInvoked;
        _window.MenuRequested -= OnMenuRequested;
        _window.PinDragStarted -= OnPinDragStarted;
        _window.PinDragFinished -= OnPinDragFinished;
        _window.FilesDropped -= OnFilesDropped;
        _window.DpiChanged -= OnWindowDpiChanged;
        _menu.OpenChanged -= OnMenuOpenChanged;

        // The dock is owned by the backdrop; close it first so WPF tears it down rather than Win32.
        _window.Close();
        _backdrop.Dispose();
    }
}
