using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using WinGnome.Core.Settings;
using WinGnome.Core.TopBar;
using WinGnome.Features.TopBar.Popups;
using WinGnome.Features.TopBar.ViewModels;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services.Tray;

namespace WinGnome.Features.TopBar;

/// <summary>
/// The GNOME top bar: a full-width AppBar on the primary monitor's top edge with Activities, workspace dots,
/// focused app, clock/calendar and the system status menu. Disabling the bar tears everything down (window,
/// AppBar reservation, timers, audio and registry callbacks); enabling it builds it again.
/// </summary>
[FeatureOrder(20)]
internal sealed class TopBarFeature : IFeature, IEmergencyRestore
{
    private readonly ShellContext _context;

    // Broadcast by Explorer when it (re)starts; a restarted Explorer has forgotten every AppBar registration.
    private readonly uint _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

    private TopBarSettings _settings = new();
    private TopBarViewModel? _viewModel;
    private PopupHost? _popups;
    private BlurBackdrop? _backdrop;
    private TopBarWindow? _window;
    private AppBar? _appBar;
    private HwndSource? _source;
    private bool _fullScreen;

    public TopBarFeature(ShellContext context)
    {
        _context = context;
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

        if (_window is null)
        {
            Build();
            return;
        }

        _viewModel!.ApplySettings(_settings);
        _window.ApplySettings(_settings);
        Dock();
    }

    private void Build()
    {
        _viewModel = new TopBarViewModel(_context, _settings);
        _popups = new PopupHost(_context.Windows);
        _backdrop = new BlurBackdrop("WinGnome Top Bar Backdrop");
        _window = new TopBarWindow(_context, _viewModel, _settings, _popups, _backdrop);

        // Creates the handle: the bar must be a no-activate tool window before it is ever shown.
        ShellSurface.MakeNonActivating(_window, topmost: true);
        _appBar = new AppBar(_window);
        _appBar.FullScreenChanged += OnFullScreenChanged;
        _appBar.Moved += OnAppBarMoved;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
        _source?.AddHook(WndProc);

        _window.ApplySettings(_settings);
        Dock();
        _window.Show();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>(Re)registers the AppBar on the primary monitor and lays the bar out for that monitor's DPI.</summary>
    private void Dock()
    {
        if (_window is null || _appBar is null)
        {
            return;
        }

        var monitor = NativeMethods.MonitorFromPoint(default, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var (bounds, _) = NativeMethods.GetMonitorRects(monitor);
        if (bounds.IsEmpty)
        {
            Log.Warn("Could not read the primary monitor's bounds; the top bar was not docked");
            return;
        }

        var scale = NativeMethods.GetMonitorScale(monitor);
        var geometry = TopBarGeometry.Compute(_settings.Height, _settings.Margin, _settings.CornerRadius, scale);
        var granted = _appBar.Dock(AppBarEdge.Top, geometry.ThicknessPx, bounds);
        _window.ApplyGeometry(geometry, scale);

        // Docking raised the bar alone; bring its blur backdrop back directly beneath it.
        _window.RaiseToTop();
        _viewModel?.Tray.SetBarBounds(granted);
        Log.Info($"Top bar docked at {granted.Left},{granted.Top} {granted.Width}x{granted.Height} px (DPI scale {scale:0.##})");
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_DPICHANGED)
        {
            // WPF rescales the window to Windows' suggested rectangle first; then restore our exact strip.
            _context.Dispatcher.BeginInvoke(Dock, DispatcherPriority.Background);
        }
        else if (_taskbarCreatedMessage != 0 && msg == (int)_taskbarCreatedMessage && !TrayHost.IsOwnBroadcast(wParam))
        {
            _context.Dispatcher.BeginInvoke(() =>
            {
                _appBar?.Undock();
                Dock();

                // The new Explorer never reports the end of a full-screen app that the old one saw start, so a bar
                // hidden for it would stay hidden for good; show it again unless that app still covers the monitor.
                if (_fullScreen && !ForegroundCoversPrimaryMonitor())
                {
                    OnFullScreenChanged(this, false);
                }
            }, DispatcherPriority.Background);
        }

        return 0;
    }

    /// <summary>The shell restacked the bar (another top AppBar came or went): the tray host follows it.</summary>
    private void OnAppBarMoved(object? sender, EventArgs e)
    {
        if (_appBar is not null)
        {
            _viewModel?.Tray.SetBarBounds(_appBar.Bounds);
        }
    }

    private void OnFullScreenChanged(object? sender, bool fullScreen)
    {
        if (_window is null)
        {
            return;
        }

        // Some Explorer builds report the desktop itself as a full-screen app when it gets focus; the bar must stay.
        if (fullScreen && NativeMethods.GetClassName(NativeMethods.GetForegroundWindow()) is "Progman" or "WorkerW")
        {
            return;
        }

        if (fullScreen == _fullScreen)
        {
            return;
        }

        _fullScreen = fullScreen;
        if (fullScreen)
        {
            _window.ClosePopups();
            _window.Hide();
        }
        else
        {
            // The bar's blur backdrop hides and shows with the bar (TopBarWindow follows its visibility).
            _window.Show();

            // The full-screen app may have pushed itself above us in the topmost band.
            _window.RaiseToTop();
        }
    }

    private static bool ForegroundCoversPrimaryMonitor()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (NativeMethods.GetClassName(foreground) is "Progman" or "WorkerW")
        {
            return false;
        }

        var (monitor, _) = NativeMethods.GetPrimaryMonitorRects();
        var window = NativeMethods.GetWindowBounds(foreground);
        return !monitor.IsEmpty && window.Left <= monitor.Left && window.Top <= monitor.Top
            && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
    }

    // SystemEvents may raise on its own thread; always hop to the dispatcher.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        _context.Dispatcher.BeginInvoke(Dock, DispatcherPriority.Background);

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        // Monitors may have been rearranged (docking station) while the machine slept.
        if (e.Mode == PowerModes.Resume)
        {
            _context.Dispatcher.BeginInvoke(Dock, DispatcherPriority.Background);
        }
    }

    private void TearDown()
    {
        if (_window is null)
        {
            return;
        }

        // SystemEvents is static: forgotten handlers would keep the whole bar alive and keep firing.
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _source?.RemoveHook(WndProc);
        _source = null;

        if (_appBar is not null)
        {
            _appBar.FullScreenChanged -= OnFullScreenChanged;
            _appBar.Moved -= OnAppBarMoved;
            _appBar.Dispose();
            _appBar = null;
        }

        _window.Shutdown();
        _window = null;

        // The bar is owned by its backdrop; it was closed first so WPF tears it down rather than Win32.
        _backdrop?.Dispose();
        _backdrop = null;
        _popups?.Dispose();
        _popups = null;
        _viewModel?.Dispose();
        _viewModel = null;
        _fullScreen = false;
    }

    public void Dispose() => TearDown();

    /// <summary>
    /// Crash path: give the reserved strip back to the work area (ABM_REMOVE is a plain Win32 call) and ask the tray
    /// host to step aside. Explorer already has every tray icon, because the host forwarded each call to it.
    /// </summary>
    public void EmergencyRestore()
    {
        _appBar?.Undock();
        _viewModel?.Tray.EmergencyRestore();
    }
}
