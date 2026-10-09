using System.Diagnostics;
using System.Windows.Threading;
using WinGnome.Core.Monitors;
using WinGnome.Core.Settings;
using WinGnome.Core.Windows;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services;

namespace WinGnome.Features.TopBar;

/// <summary>
/// The GNOME top bar on every monitor (or only the primary, <see cref="TopBarSettings.Monitors"/>): one full-width
/// AppBar per monitor with Activities, workspace dots, that monitor's focused app, clock/calendar, tray icons and the
/// system status menu. This coordinator owns what the bars share (<see cref="TopBarServices"/>) and one
/// <see cref="TopBarInstance"/> per monitor key, and applies <see cref="SurfacePlan"/> steps on display changes,
/// detached AppBars, Explorer restarts and settings changes. Disabling the bar tears everything down (windows,
/// AppBar reservations, timers, audio and registry callbacks); enabling it builds it again.
/// </summary>
[FeatureOrder(20)]
internal sealed class TopBarFeature : IFeature, IEmergencyRestore
{
    /// <summary>Window moves (Win+Shift+Arrow, drags) are coalesced before the focused apps are re-evaluated.</summary>
    private static readonly TimeSpan LocationSettleDelay = TimeSpan.FromMilliseconds(150);

    private readonly ShellContext _context;
    private readonly List<TopBarInstance> _bars = [];
    private readonly MonitorFocusTracker _focus = new();
    private readonly HashSet<nint> _trackedWindows = [];
    private readonly DispatcherTimer _locationTimer;
    private TopBarSettings _settings = new();
    private TopBarServices? _services;
    private string? _desktopKey;
    private bool _explorerPassQueued;

    public TopBarFeature(ShellContext context)
    {
        _context = context;
        _locationTimer = new DispatcherTimer(DispatcherPriority.Background, context.Dispatcher) { Interval = LocationSettleDelay };
        _locationTimer.Tick += OnLocationTimer;
    }

    public string Name => "Top bar";

    public void Start(AppSettings settings) => ApplySettings(settings);

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.TopBar;
        if (!_settings.Enabled)
        {
            TearDown();
            return;
        }

        if (_services is null)
        {
            _services = new TopBarServices(_context, _settings);
            _context.Displays.LayoutChanged += OnLayoutChanged;
            _context.Windows.ForegroundChanged += OnForegroundChanged;
            _context.Windows.WindowsChanged += OnWindowsChanged;
            _context.Windows.RawWindowEvent += OnRawWindowEvent;
            Reconcile("start");
            UpdateFocusTable();
            OnForegroundChanged(this, _context.Windows.Foreground);
            return;
        }

        _services.ApplySettings(_settings);
        Reconcile("settings");

        // The height, margin or corner radius may have changed: lay every bar out again on its own monitor.
        foreach (var bar in _bars)
        {
            bar.ApplySettings(_settings);
            bar.DockOn(bar.Monitor);
        }
    }

    // ---- Reconcile ---------------------------------------------------------------------------

    private void OnLayoutChanged(object? sender, DisplayLayoutChangedEventArgs e)
    {
        _focus.OnLayoutChanged(e.Current);
        Reconcile("display change");
        UpdateFocusTable();
    }

    /// <summary>
    /// Brings the bars in line with the current layout and settings, in <see cref="SurfacePlan"/> order (removals,
    /// releases, docks, additions), and logs the pass with its count of SHAppBarMessage calls (storm diagnostics).
    /// </summary>
    private void Reconcile(string reason)
    {
        if (_services is null)
        {
            return;
        }

        var timer = Stopwatch.StartNew();
        var callsBefore = AppBar.MessageCount;
        var steps = SurfacePlan.Reconcile(_bars.Select(b => b.State), _context.Displays.Current, _settings.Monitors);
        foreach (var step in steps)
        {
            try
            {
                Apply(step);
            }
            catch (Exception ex)
            {
                Log.Warn($"Top bar: reconcile step {step.Kind} {step.Key} failed", ex);
            }
        }

        UpdateTrayHostBounds();
        RefreshFocusedApps();
        if (steps.Count > 0 || reason != "settings")
        {
            var summary = string.Join(", ", steps.Select(s => $"{s.Kind} {s.Key}{(s.Monitor is { } m && m.Key != s.Key ? "->" + m.Key : "")}"));
            Log.Info($"Top bar: reconcile ({reason}): [{summary}] in {timer.ElapsedMilliseconds} ms, {AppBar.MessageCount - callsBefore} SHAppBarMessage calls, {_bars.Count} bars");
        }
    }

    private void Apply(SurfaceStep step)
    {
        var bar = Find(step.Key);
        switch (step.Kind)
        {
            case SurfaceStepKind.Remove when bar is not null:
                _bars.Remove(bar);
                Destroy(bar);
                break;
            case SurfaceStepKind.Release:
                bar?.Undock();
                break;
            case SurfaceStepKind.Dock when bar is not null:
                bar.DockOn(step.Monitor!);
                break;
            case SurfaceStepKind.Add:
                _bars.Add(Create(step.Monitor!));
                break;
        }
    }

    private TopBarInstance? Find(string key) =>
        _bars.FirstOrDefault(b => string.Equals(b.Monitor.Key, key, StringComparison.OrdinalIgnoreCase));

    private TopBarInstance Create(MonitorInfo monitor)
    {
        var bar = new TopBarInstance(_context, _services!, _settings, monitor);
        bar.ExplorerRestarted += OnExplorerRestarted;
        bar.Detached += OnBarDetached;
        bar.BoundsChanged += OnBarBoundsChanged;
        return bar;
    }

    private void Destroy(TopBarInstance bar)
    {
        bar.ExplorerRestarted -= OnExplorerRestarted;
        bar.Detached -= OnBarDetached;
        bar.BoundsChanged -= OnBarBoundsChanged;
        bar.Dispose();
    }

    /// <summary>A bar's monitor went away or changed under it: re-read the layout; the pass re-docks or removes it.</summary>
    private void OnBarDetached(object? sender, EventArgs e) => _context.Displays.Invalidate(force: true);

    private void OnBarBoundsChanged(object? sender, EventArgs e)
    {
        if (sender is TopBarInstance { Monitor.IsPrimary: true })
        {
            UpdateTrayHostBounds();
        }
    }

    /// <summary>
    /// The hidden tray host window sits on the primary bar's strip, as a taskbar's would. Without a bar on the primary
    /// (being rebuilt, or bars on other monitors only for a moment) it keeps its last strip.
    /// </summary>
    private void UpdateTrayHostBounds()
    {
        if (_bars.FirstOrDefault(b => b.Monitor.IsPrimary && !b.IsDetached) is { } primary)
        {
            _services?.Tray.SetHostBounds(primary.Bounds);
        }
    }

    /// <summary>
    /// Every bar window receives Explorer's TaskbarCreated broadcast; one pass handles them all: undock every bar,
    /// re-dock them in plan order, then re-check full-screen state. A restarted Explorer has forgotten every AppBar.
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
            var timer = Stopwatch.StartNew();
            var callsBefore = AppBar.MessageCount;
            for (var i = _bars.Count - 1; i >= 0; i--)
            {
                _bars[i].Undock();
            }

            var layout = _context.Displays.Current;
            foreach (var bar in _bars.OrderBy(b => IndexIn(layout, b.Monitor.Key)))
            {
                bar.DockOn(layout.Find(bar.Monitor.Key) ?? bar.Monitor);
            }

            foreach (var bar in _bars)
            {
                bar.RecheckFullScreen();
            }

            UpdateTrayHostBounds();
            Log.Info($"Top bar: Explorer restarted; re-docked {_bars.Count} bars in {timer.ElapsedMilliseconds} ms, {AppBar.MessageCount - callsBefore} SHAppBarMessage calls");

            // The layout may have changed while Explorer was gone.
            _context.Displays.Invalidate(force: true);
        });
    }

    private static int IndexIn(MonitorLayout layout, string key)
    {
        for (var i = 0; i < layout.Monitors.Count; i++)
        {
            if (string.Equals(layout.Monitors[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    // ---- Focused app per monitor -------------------------------------------------------------

    private string KeyOf(nint hwnd) =>
        _context.Displays.MonitorKeyOf(hwnd) ?? _context.Displays.Current.Primary?.Key ?? "";

    private void OnForegroundChanged(object? sender, nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        // GNOME shows no app while the desktop is focused: on the monitor where the desktop was clicked.
        if (NativeMethods.GetClassName(hwnd) is "Progman" or "WorkerW")
        {
            _desktopKey = NativeMethods.GetCursorPos(out var cursor) && _context.Displays.Current.At(cursor.X, cursor.Y) is { } monitor
                ? monitor.Key
                : _context.Displays.Current.Primary?.Key;
            RefreshFocusedApps();
            return;
        }

        // WinGnome's own windows (Inspect returns null), the taskbar, Start, and an app's owned dialogs are not
        // task-switcher windows: every bar keeps showing the app that was focused before them.
        if (_context.Windows.Inspect(hwnd) is not { } info || !WindowFilter.IsTaskSwitcherWindow(info))
        {
            return;
        }

        _desktopKey = null;
        _focus.OnForeground(hwnd, KeyOf(hwnd));
        RefreshFocusedApps();
    }

    private void OnWindowsChanged(object? sender, EventArgs e)
    {
        UpdateFocusTable();
        RefreshFocusedApps();
    }

    /// <summary>
    /// With more than one monitor, a window moved to another monitor (Win+Shift+Arrow, a drag) changes two bars.
    /// Only a deadline is set here (WinEvent callback); the window table is rebuilt when it expires.
    /// </summary>
    private void OnRawWindowEvent(uint eventType, nint hwnd)
    {
        if (eventType is (WinEventHook.EVENT_SYSTEM_MOVESIZEEND or WinEventHook.EVENT_OBJECT_LOCATIONCHANGE)
            && _context.Displays.Current.Monitors.Count > 1 && _trackedWindows.Contains(hwnd) && !_locationTimer.IsEnabled)
        {
            _locationTimer.Start();
        }
    }

    private void OnLocationTimer(object? sender, EventArgs e)
    {
        _locationTimer.Stop();
        UpdateFocusTable();
        RefreshFocusedApps();
    }

    private void UpdateFocusTable()
    {
        var table = new Dictionary<nint, TrackedWindow>();
        _trackedWindows.Clear();
        foreach (var window in _context.Windows.Windows)
        {
            table[window.Handle] = new TrackedWindow(KeyOf(window.Handle), window.IsMinimized);
            _trackedWindows.Add(window.Handle);
        }

        _focus.OnWindowsChanged(table);
    }

    private void RefreshFocusedApps()
    {
        foreach (var bar in _bars)
        {
            var key = bar.Monitor.Key;
            bar.ShowFocusedApp(string.Equals(key, _desktopKey, StringComparison.OrdinalIgnoreCase) ? 0 : _focus.Current(key));
        }
    }

    // ---- Teardown ----------------------------------------------------------------------------

    private void TearDown()
    {
        if (_services is null)
        {
            return;
        }

        _context.Displays.LayoutChanged -= OnLayoutChanged;
        _context.Windows.ForegroundChanged -= OnForegroundChanged;
        _context.Windows.WindowsChanged -= OnWindowsChanged;
        _context.Windows.RawWindowEvent -= OnRawWindowEvent;
        _locationTimer.Stop();

        // Reverse creation order: each bar undocks before its window closes.
        for (var i = _bars.Count - 1; i >= 0; i--)
        {
            Destroy(_bars[i]);
        }

        _bars.Clear();
        _services.Dispose();
        _services = null;
        _desktopKey = null;
    }

    public void Dispose()
    {
        TearDown();
        _locationTimer.Tick -= OnLocationTimer;
    }

    /// <summary>
    /// Crash path: the AppBars were already removed by <see cref="AppBar.UndockAll"/>; ask the tray host to step aside.
    /// Explorer already has every tray icon, because the host forwarded each call to it.
    /// </summary>
    public void EmergencyRestore() => _services?.Tray.EmergencyRestore();
}
