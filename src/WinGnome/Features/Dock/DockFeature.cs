using System.Diagnostics;
using System.Windows.Threading;
using WinGnome.Core.Dock;
using WinGnome.Core.Monitors;
using WinGnome.Core.Settings;
using WinGnome.Core.Windows;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services;

namespace WinGnome.Features.Dock;

/// <summary>
/// GNOME Dash-to-Dock / macOS style dock: pinned and running apps, magnification, visibility modes, context menus,
/// drag and drop, and Super+N activation. On the primary monitor by default, or on every monitor
/// (<see cref="DockSettings.Monitors"/>), optionally each showing only its own monitor's windows. This coordinator
/// owns the pins, catalogue, theme, external foreground, Super+N and the shared edge poll, and one
/// <see cref="DockInstance"/> per monitor key; display changes and detached strips run <see cref="SurfacePlan"/> steps.
/// The crash path needs nothing here: every reservation is an <see cref="AppBar"/>, removed by <see cref="AppBar.UndockAll"/>.
/// </summary>
[FeatureOrder(30)]
internal sealed class DockFeature : IFeature
{
    /// <summary>Window moves are coalesced before isolated docks re-filter their windows.</summary>
    private static readonly TimeSpan LocationSettleDelay = TimeSpan.FromMilliseconds(150);

    private readonly ShellContext _context;
    private readonly List<DockInstance> _docks = [];
    private readonly DispatcherTimer _locationTimer;
    private readonly HashSet<nint> _trackedWindows = [];
    private AppSettings _settings;
    private bool _started;
    private bool _enabled;
    private bool _refreshQueued;
    private bool _explorerPassQueued;
    private ExternalForeground? _foreground;
    private DockEdgePoller? _poller;

    public DockFeature(ShellContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _settings = context.Settings.Current;
        _locationTimer = new DispatcherTimer(DispatcherPriority.Background, context.Dispatcher) { Interval = LocationSettleDelay };
        _locationTimer.Tick += OnLocationTimer;
    }

    public string Name => "Dock";

    private DockSettings Dock => _settings.Dock;

    /// <summary>Docks on other monitors show only their own windows (only with a dock on every monitor).</summary>
    private bool Isolating => Dock.Monitors == BarMonitors.All && Dock.IsolateMonitors;

    public void Start(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _started = true;
        _context.Windows.WindowsChanged += OnModelInputChanged;
        _context.Windows.RawWindowEvent += OnRawWindowEvent;
        _context.Apps.Changed += OnModelInputChanged;
        _context.Theme.ThemeChanged += OnThemeChanged;
        _context.Commands.DockItemActivationRequested += OnDockItemActivationRequested;
        _context.Displays.LayoutChanged += OnLayoutChanged;
        Apply();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Apply();
    }

    private void Apply()
    {
        if (!Dock.Enabled)
        {
            Disable();
            return;
        }

        if (_foreground is null)
        {
            _foreground = new ExternalForeground(_context.Windows);
            _foreground.Changed += OnForegroundChanged;
            _poller = new DockEdgePoller(_context.Dispatcher);
        }

        _enabled = true;
        Reconcile("settings");
        foreach (var dock in _docks)
        {
            dock.ApplySettings(Dock);
            dock.ApplyStyle();
        }

        RefreshNow();
        foreach (var dock in _docks)
        {
            dock.RelayoutIfMonitorUnchanged();
            dock.SetEnabled(true);
        }
    }

    /// <summary>Destroys every dock (their strips go back first); pins and settings stay.</summary>
    private void Disable()
    {
        if (!_enabled)
        {
            return;
        }

        _enabled = false;
        _locationTimer.Stop();
        for (var i = _docks.Count - 1; i >= 0; i--)
        {
            Destroy(_docks[i]);
        }

        _docks.Clear();
    }

    // ---- Reconcile ---------------------------------------------------------------------------

    private void OnLayoutChanged(object? sender, DisplayLayoutChangedEventArgs e)
    {
        if (_enabled)
        {
            Reconcile(e.Diff.IsEmpty ? "re-check" : "display change", recheckStrips: true);
            if (!e.Diff.IsEmpty)
            {
                QueueRefresh();
            }
        }
    }

    /// <summary>
    /// Brings the docks in line with the layout and <see cref="DockSettings.Monitors"/>, in <see cref="SurfacePlan"/>
    /// order. A primary swap with a dock on the primary only moves the one dock. On display passes
    /// (<paramref name="recheckStrips"/>) every reserving dock then checks that the work area still leaves its strip
    /// out (Explorer resets work areas on some topology changes). Logs the pass with its count of SHAppBarMessage calls.
    /// </summary>
    private void Reconcile(string reason, bool recheckStrips = false)
    {
        var timer = Stopwatch.StartNew();
        var callsBefore = AppBar.MessageCount;
        var steps = SurfacePlan.Reconcile(_docks.Select(d => d.State), _context.Displays.Current, Dock.Monitors);
        foreach (var step in steps)
        {
            try
            {
                Apply(step);
            }
            catch (Exception ex)
            {
                Log.Warn($"Dock: reconcile step {step.Kind} {step.Key} failed", ex);
            }
        }

        var reregistered = recheckStrips ? _docks.Count(EnsureReserved) : 0;
        if (steps.Count > 0 || reregistered > 0 || reason == "display change")
        {
            var summary = string.Join(", ", steps.Select(s => $"{s.Kind} {s.Key}{(s.Monitor is { } m && m.Key != s.Key ? "->" + m.Key : "")}"));
            Log.Info($"Dock: reconcile ({reason}): [{summary}] in {timer.ElapsedMilliseconds} ms, {AppBar.MessageCount - callsBefore} SHAppBarMessage calls, {_docks.Count} docks, {reregistered} strips registered again, edge poll {(_poller?.IsRunning == true ? "running" : "idle")}");
        }
    }

    private static bool EnsureReserved(DockInstance dock)
    {
        try
        {
            return dock.EnsureReserved();
        }
        catch (Exception ex)
        {
            Log.Warn($"Dock: could not re-check the strip on {dock.Monitor.Key}", ex);
            return false;
        }
    }

    private void Apply(SurfaceStep step)
    {
        var dock = Find(step.Key);
        switch (step.Kind)
        {
            case SurfaceStepKind.Remove when dock is not null:
                _docks.Remove(dock);
                Destroy(dock);
                break;
            case SurfaceStepKind.Release:
                dock?.Release();
                break;
            case SurfaceStepKind.Dock when dock is not null:
                dock.DockOn(step.Monitor!);
                break;
            case SurfaceStepKind.Add:
                var created = Create(step.Monitor!);
                _docks.Add(created);
                created.Refresh(RunningFor(created.Monitor.Key));
                created.DockOn(step.Monitor!);
                created.SetEnabled(true);
                break;
        }
    }

    private DockInstance? Find(string key) =>
        _docks.FirstOrDefault(d => string.Equals(d.Monitor.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Super+1..9 act on the primary monitor's dock.</summary>
    private DockInstance? PrimaryDock =>
        _docks.FirstOrDefault(d => d.Monitor.IsPrimary) ?? _docks.FirstOrDefault();

    private DockInstance Create(MonitorInfo monitor)
    {
        var dock = new DockInstance(_context, _foreground!, _poller!, Dock, monitor);
        dock.ApplyStyle();
        dock.ExplorerRestarted += OnExplorerRestarted;
        dock.Detached += OnDockDetached;
        dock.RefreshRequested += OnRefreshRequested;
        return dock;
    }

    private void Destroy(DockInstance dock)
    {
        dock.ExplorerRestarted -= OnExplorerRestarted;
        dock.Detached -= OnDockDetached;
        dock.RefreshRequested -= OnRefreshRequested;
        dock.Dispose();
    }

    /// <summary>A dock's strip lost its monitor: re-read the layout; the pass re-docks or removes the dock.</summary>
    private void OnDockDetached(object? sender, EventArgs e) => _context.Displays.Invalidate(force: true);

    /// <summary>
    /// Every dock window receives Explorer's TaskbarCreated broadcast (WinGnome's own tray broadcasts are filtered);
    /// one pass re-registers every reserved strip: all undocked first, then all docked again.
    /// </summary>
    private void OnExplorerRestarted(object? sender, EventArgs e)
    {
        if (_explorerPassQueued)
        {
            return;
        }

        _explorerPassQueued = true;
        _context.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _explorerPassQueued = false;
            foreach (var dock in _docks)
            {
                dock.UndockForExplorer();
            }

            foreach (var dock in _docks)
            {
                dock.RedockForExplorer();
            }

            Log.Info($"Dock: Explorer restarted; re-registered {_docks.Count} docks");
            _context.Displays.Invalidate(force: true);
        });
    }

    // ---- Model ------------------------------------------------------------------------------

    private void OnForegroundChanged(object? sender, EventArgs e)
    {
        // Close a menu when the user switches to another app (in case the menu itself missed the click).
        foreach (var dock in _docks)
        {
            dock.CloseMenu();
        }

        QueueRefresh();
    }

    private void OnModelInputChanged(object? sender, EventArgs e) => QueueRefresh();

    private void OnRefreshRequested(object? sender, EventArgs e) => QueueRefresh();

    /// <summary>
    /// Isolated docks show a window on its current monitor, and a move (Win+Shift+Arrow, a drag) raises only location
    /// events: set a deadline here (WinEvent callback), re-filter when it expires.
    /// </summary>
    private void OnRawWindowEvent(uint eventType, nint hwnd)
    {
        if (_enabled && Isolating && eventType is (WinEventHook.EVENT_SYSTEM_MOVESIZEEND or WinEventHook.EVENT_OBJECT_LOCATIONCHANGE)
            && _trackedWindows.Contains(hwnd) && !_locationTimer.IsEnabled)
        {
            _locationTimer.Start();
        }
    }

    private void OnLocationTimer(object? sender, EventArgs e)
    {
        _locationTimer.Stop();
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
        if (!_enabled)
        {
            return;
        }

        _trackedWindows.Clear();
        foreach (var window in _context.Windows.Windows)
        {
            _trackedWindows.Add(window.Handle);
        }

        foreach (var dock in _docks)
        {
            dock.Refresh(RunningFor(dock.Monitor.Key));
        }
    }

    /// <summary>The running windows a dock on monitor <paramref name="key"/> shows.</summary>
    private IReadOnlyList<RunningWindow> RunningFor(string key)
    {
        var running = _context.Windows.Windows.Select(ToRunningWindow).ToList();
        return DockWindowFilter.ForMonitor(running, KeyOf, key, Isolating);
    }

    private string KeyOf(nint hwnd) =>
        _context.Displays.MonitorKeyOf(hwnd) ?? _context.Displays.Current.Primary?.Key ?? "";

    private RunningWindow ToRunningWindow(WindowInfo window) => new(
        window.Handle,
        AppIdentity.ForWindow(window.AppUserModelId, window.ProcessPath, window.ProcessId),
        window.Title,
        _context.Apps.FindForWindow(window.AppUserModelId, window.ProcessPath)?.Name ?? _context.Windows.GetAppName(window),
        window.ProcessPath,
        window.AppUserModelId,
        window.IsMinimized);

    // ---- Appearance and input ----------------------------------------------------------------

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        foreach (var dock in _docks)
        {
            dock.ApplyStyle();
        }
    }

    private void OnDockItemActivationRequested(object? sender, int index)
    {
        if (_enabled)
        {
            PrimaryDock?.ActivateItem(index);
        }
    }

    public void Dispose()
    {
        if (_started)
        {
            _context.Windows.WindowsChanged -= OnModelInputChanged;
            _context.Windows.RawWindowEvent -= OnRawWindowEvent;
            _context.Apps.Changed -= OnModelInputChanged;
            _context.Theme.ThemeChanged -= OnThemeChanged;
            _context.Commands.DockItemActivationRequested -= OnDockItemActivationRequested;
            _context.Displays.LayoutChanged -= OnLayoutChanged;
            _started = false;
        }

        Disable();
        _locationTimer.Tick -= OnLocationTimer;
        _poller?.Dispose();
        if (_foreground is not null)
        {
            _foreground.Changed -= OnForegroundChanged;
            _foreground.Dispose();
            _foreground = null;
        }
    }
}
