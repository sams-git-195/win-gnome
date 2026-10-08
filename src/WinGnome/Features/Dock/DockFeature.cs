using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using WinGnome.Core.Dock;
using WinGnome.Core.Geometry;
using WinGnome.Core.Settings;
using WinGnome.Core.Theming;
using WinGnome.Core.Windows;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Dock;

/// <summary>
/// GNOME Dash-to-Dock / macOS style dock on the primary monitor: pinned and running apps, magnification,
/// visibility modes, context menus, drag and drop, and Super+N activation.
/// </summary>
[FeatureOrder(30)]
internal sealed class DockFeature : IFeature
{
    /// <summary>Padding at both ends of the dock (DIP).</summary>
    private const double EndPadding = 8;

    /// <summary>Largest icon bitmap requested from the shell.</summary>
    private const int MaxIconPixels = 256;

    private readonly ShellContext _context;
    private AppSettings _settings;
    private bool _started;
    private bool _enabled;
    private bool _refreshQueued;
    private bool _relayoutQueued;
    private bool _menuOpen;
    private bool _dragging;
    private int _iconPixels;
    private (int Cells, int Separators) _layoutCounts = (-1, -1);

    private ExternalForeground? _foreground;
    private DockViewModel? _viewModel;
    private DockBackdropWindow? _backdrop;
    private DockWindow? _window;
    private DockActions? _actions;
    private DockMenuPresenter? _menu;
    private DockVisibilityController? _visibility;
    private DockReservation? _reservation;

    public DockFeature(ShellContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _settings = context.Settings.Current;
    }

    public string Name => "Dock";

    public void Start(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _started = true;
        _context.Windows.WindowsChanged += OnModelInputChanged;
        _context.Apps.Changed += OnModelInputChanged;
        _context.Theme.ThemeChanged += OnThemeChanged;
        _context.Commands.DockItemActivationRequested += OnDockItemActivationRequested;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Apply();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Apply();
    }

    private DockSettings Dock => _settings.Dock;

    private void Apply()
    {
        if (!Dock.Enabled)
        {
            Disable();
            return;
        }

        EnsureCreated();
        _enabled = true;
        ApplyStyle();
        RefreshNow();
        Relayout();
        _visibility!.SetEnabled(true);
    }

    private void Disable()
    {
        if (!_enabled)
        {
            return;
        }

        _enabled = false;
        _menu?.Close();
        _visibility?.SetEnabled(false);
        _reservation?.Release();
    }

    private void EnsureCreated()
    {
        if (_window is not null)
        {
            return;
        }

        _foreground = new ExternalForeground(_context.Windows);
        _foreground.Changed += OnModelInputChanged;
        _viewModel = new DockViewModel(_context.Icons, _context.Apps);
        _backdrop = new DockBackdropWindow();
        _window = new DockWindow(_viewModel, _backdrop);
        _actions = new DockActions(_context, _foreground, _window.PlayLaunchFeedback);
        _menu = new DockMenuPresenter(_context.Dispatcher);
        _visibility = new DockVisibilityController(_context, _window, _foreground);
        _reservation = new DockReservation();

        _window.EntryInvoked += OnEntryInvoked;
        _window.MenuRequested += OnMenuRequested;
        _window.PinDragStarted += OnPinDragStarted;
        _window.PinDragFinished += OnPinDragFinished;
        _window.FilesDropped += OnFilesDropped;
        _window.DpiChanged += OnWindowDpiChanged;
        _menu.OpenChanged += OnMenuOpenChanged;
    }

    // ---- Model ------------------------------------------------------------------------------

    private void OnModelInputChanged(object? sender, EventArgs e)
    {
        // Close a menu when the user switches to another app (in case the menu itself missed the click).
        if (sender is ExternalForeground)
        {
            _menu?.Close();
        }

        QueueRefresh();
    }

    /// <summary>Coalesces bursts of tracker/catalogue events into one refresh per dispatcher pass.</summary>
    private void QueueRefresh()
    {
        if (_enabled && !_refreshQueued)
        {
            _refreshQueued = true;
            _context.Dispatcher.BeginInvoke(DispatcherPriority.Background, RefreshNow);
        }
    }

    private void RefreshNow()
    {
        _refreshQueued = false;

        // A drag preview reorders entries locally; a refresh would undo it mid-drag.
        if (!_enabled || _dragging || _viewModel is null)
        {
            return;
        }

        try
        {
            var running = _context.Windows.Windows.Select(ToRunningWindow).ToList();
            var apps = DockModelBuilder.Build(Dock.PinnedApps, running, _foreground!.Handle, Dock.ShowRunningApps, _context.Apps.ResolvePath);
            _viewModel.Update(apps, Dock, _iconPixels);
            if ((_viewModel.CellCount, _viewModel.SeparatorCount) != _layoutCounts)
            {
                Relayout();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Dock: refresh failed", ex);
        }
    }

    private RunningWindow ToRunningWindow(WindowInfo window) => new(
        window.Handle,
        AppIdentity.ForWindow(window.AppUserModelId, window.ProcessPath, window.ProcessId),
        window.Title,
        _context.Apps.FindForWindow(window.AppUserModelId, window.ProcessPath)?.Name ?? _context.Windows.GetAppName(window),
        window.ProcessPath,
        window.AppUserModelId);

    // ---- Geometry ---------------------------------------------------------------------------

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => QueueRelayout();

    private void OnWindowDpiChanged(object? sender, DpiChangedEventArgs e) => QueueRelayout();

    /// <summary>SystemEvents may call on its own thread, and DPI changes arrive mid-resize; relayout afterwards on the UI thread.</summary>
    private void QueueRelayout()
    {
        if (_relayoutQueued)
        {
            return;
        }

        _relayoutQueued = true;
        _context.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _relayoutQueued = false;
            Relayout();
        });
    }

    /// <summary>Recomputes the dock geometry for the primary monitor and pushes it to the windows and the AppBar.</summary>
    private void Relayout()
    {
        if (!_enabled || _window is null || _viewModel is null)
        {
            return;
        }

        try
        {
            var monitorHandle = NativeMethods.MonitorFromPoint(default, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
            var (monitor, _) = NativeMethods.GetMonitorRects(monitorHandle);
            if (monitor.IsEmpty)
            {
                Log.Warn("Dock: could not read the primary monitor's bounds");
                return;
            }

            var scale = NativeMethods.GetMonitorScale(monitorHandle);
            var cells = _viewModel.CellCount;
            var separators = _viewModel.SeparatorCount;
            var separatorLength = separators * DockAppearance.SeparatorLength;
            var monitorLength = (Dock.Position == DockPosition.Bottom ? monitor.Width : monitor.Height) / scale;

            var iconSize = DockFrameLayout.FitIconSize(monitorLength, Math.Max(1, cells), separatorLength, Dock.IconSize, Dock.IconSpacing, EndPadding);
            var cellSize = iconSize + (2 * Dock.IconSpacing);
            var options = new DockLayoutOptions(Dock.IconSpacing, Dock.EdgeMargin, EndPadding, separatorLength);
            var geometry = DockLayout.Compute(monitor, Dock.Position, cells, iconSize * scale, scale, Dock.ExtendToEdges, options);
            var frame = DockFrameLayout.Compute(monitor, Dock.Position, geometry, cellSize * scale, iconSize * scale, Dock.Magnification, Dock.ExtendToEdges);

            _layoutCounts = (cells, separators);
            _viewModel.Appearance.Apply(Dock, iconSize, Dock.CornerRadius);
            _window.ApplyLayout(new DockViewLayout(
                frame.Window,
                Dock.Position,
                Dock.ExtendToEdges,
                EdgeGap: Dock.ExtendToEdges ? 0 : Dock.EdgeMargin,
                BodyThickness: cellSize + (2 * DockWindow.BodyPadding),
                EndPadding,
                Dock.CornerRadius,
                Dock.Magnification,
                iconSize));

            if (Dock.Visibility == DockVisibility.AlwaysVisible)
            {
                _reservation!.Reserve(ToAppBarEdge(Dock.Position), frame.ReservedThickness, monitor);
            }
            else
            {
                _reservation!.Release();
            }

            _visibility!.Configure(Dock.Visibility, frame, monitor, monitorHandle);

            // Load icons at the size they are shown when fully magnified, so they stay crisp.
            var iconPixels = (int)Math.Min(MaxIconPixels, Math.Round(iconSize * scale * Math.Max(1, Dock.Magnification)));
            if (iconPixels != _iconPixels)
            {
                _iconPixels = iconPixels;
                QueueRefresh();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Dock: layout failed", ex);
        }
    }

    private static AppBarEdge ToAppBarEdge(DockPosition position) => position switch
    {
        DockPosition.Left => AppBarEdge.Left,
        DockPosition.Right => AppBarEdge.Right,
        _ => AppBarEdge.Bottom,
    };

    // ---- Appearance -------------------------------------------------------------------------

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_enabled)
        {
            ApplyStyle();
        }
    }

    private void ApplyStyle()
    {
        try
        {
            _window!.ApplyStyle(new DockStyle(
                ParseOptional(Dock.BackgroundColor),
                Dock.Opacity,
                Dock.Blur,
                ParseOptional(Dock.IndicatorColor)));
        }
        catch (Exception ex)
        {
            Log.Warn("Dock: could not apply the appearance", ex);
        }
    }

    private static HexColor? ParseOptional(string value) => HexColor.TryParse(value, out var color) ? color : null;

    // ---- Input ------------------------------------------------------------------------------

    private void OnEntryInvoked(object? sender, DockEntryEventArgs e) => Run(() => _actions!.Invoke(e.Entry, e.Button));

    private void OnDockItemActivationRequested(object? sender, int index)
    {
        if (_enabled && _viewModel is not null && index >= 0 && index < _viewModel.AppEntries.Count)
        {
            Run(() => _actions!.Click(_viewModel.AppEntries[index]));
        }
    }

    private void OnMenuRequested(object? sender, DockMenuRequestEventArgs e) => Run(() =>
    {
        if (DockMenuBuilder.Build(e.Entry, _actions!, _context.Windows) is { } menu)
        {
            _menu!.Show(menu, e.Target, Dock.Position, _foreground!.Handle);
        }
    });

    private void OnMenuOpenChanged(object? sender, bool open)
    {
        _menuOpen = open;
        _visibility?.SetInteracting(_menuOpen || _dragging);
    }

    private void OnPinDragStarted(object? sender, EventArgs e)
    {
        _dragging = true;
        _visibility?.SetInteracting(true);
    }

    private void OnPinDragFinished(object? sender, bool committed)
    {
        _dragging = false;
        _visibility?.SetInteracting(_menuOpen);
        if (committed)
        {
            // The settings change triggers ApplySettings, which refreshes the dock in the new order.
            Run(() => _actions!.ReorderPins(_viewModel!.PinnedLaunchIds));
        }
        else
        {
            // Cancelled: undo the drag preview.
            QueueRefresh();
        }
    }

    private void OnFilesDropped(object? sender, DockFilesDroppedEventArgs e) => Run(() => _actions!.PinFiles(e.Paths, e.PinIndex));

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

    public void Dispose()
    {
        if (_started)
        {
            _context.Windows.WindowsChanged -= OnModelInputChanged;
            _context.Apps.Changed -= OnModelInputChanged;
            _context.Theme.ThemeChanged -= OnThemeChanged;
            _context.Commands.DockItemActivationRequested -= OnDockItemActivationRequested;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _started = false;
        }

        _enabled = false;
        _menu?.Close();
        _visibility?.Dispose();
        _reservation?.Dispose();
        if (_foreground is not null)
        {
            _foreground.Changed -= OnModelInputChanged;
            _foreground.Dispose();
        }

        // The dock is owned by the backdrop; close it first so WPF tears it down rather than Win32.
        _window?.Close();
        _backdrop?.Close();
        _window = null;
        _backdrop = null;
    }
}
