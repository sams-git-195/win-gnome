using System.Windows.Interop;
using WinGnome.Core.Geometry;
using WinGnome.Core.Monitors;
using WinGnome.Core.Settings;
using WinGnome.Core.TopBar;
using WinGnome.Features.TopBar.ViewModels;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services;
using WinGnome.Services.Tray;

namespace WinGnome.Features.TopBar;

/// <summary>
/// The top bar on one monitor: its window, blur backdrop, AppBar on the monitor's top edge, focused app and tray
/// icons. Display changes, Explorer restarts and settings are coordinated by <see cref="TopBarFeature"/>.
/// </summary>
internal sealed class TopBarInstance : IDisposable
{
    // Broadcast by Explorer when it (re)starts; a restarted Explorer has forgotten every AppBar registration.
    private static readonly uint TaskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

    private readonly ShellContext _context;
    private readonly TopBarViewModel _viewModel;
    private readonly BlurBackdrop _backdrop;
    private readonly TopBarWindow _window;
    private readonly AppBar _appBar;
    private readonly HwndSource? _source;
    private TopBarSettings _settings;
    private bool _fullScreen;
    private bool _disposed;

    public TopBarInstance(ShellContext context, TopBarServices services, TopBarSettings settings, MonitorInfo monitor)
    {
        _context = context;
        _settings = settings;
        Monitor = monitor;
        _viewModel = new TopBarViewModel(context, services, settings);
        _backdrop = new BlurBackdrop("WinGnome Top Bar Backdrop");
        _window = new TopBarWindow(context, _viewModel, settings, services.Popups, _backdrop, services.Logo);

        // Creates the handle: the bar must be a no-activate tool window before it is ever shown.
        ShellSurface.MakeNonActivating(_window, topmost: true);

        // Onto the target monitor in physical pixels before the first Show, so WPF gets WM_DPICHANGED now and lays the
        // bar out at that monitor's DPI from its first frame (WPF never positions it in DIPs of the wrong monitor).
        var hwnd = new WindowInteropHelper(_window).Handle;
        NativeMethods.SetWindowPos(hwnd, 0, monitor.Bounds.Left, monitor.Bounds.Top, monitor.Bounds.Width, 1,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        _appBar = new AppBar(_window);
        _appBar.FullScreenChanged += OnFullScreenChanged;
        _appBar.Moved += OnAppBarMoved;
        _appBar.Detached += OnAppBarDetached;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);

        try
        {
            _window.ApplySettings(settings);
            DockOn(monitor);
            _window.Show();
        }
        catch
        {
            // The coordinator never gets this instance, so nothing else would give the strip back before exit.
            Dispose();
            throw;
        }
    }

    /// <summary>Explorer restarted (raised for every bar; the coordinator coalesces them into one pass).</summary>
    public event EventHandler? ExplorerRestarted;

    /// <summary>The AppBar undocked itself because its monitor went away or changed.</summary>
    public event EventHandler? Detached;

    /// <summary>The bar's strip changed (docked, re-docked, or restacked by the shell).</summary>
    public event EventHandler? BoundsChanged;

    /// <summary>The monitor this bar is laid out and docked for.</summary>
    public MonitorInfo Monitor { get; private set; }

    public bool IsDetached { get; private set; }

    /// <summary>The bar's strip in physical pixels.</summary>
    public PixelRect Bounds => _appBar.Bounds;

    public SurfaceState State => new(Monitor.Key, Monitor.Bounds, Monitor.Dpi, IsDetached);

    /// <summary>Registers (if needed) and docks the AppBar on <paramref name="monitor"/>'s top edge, laid out for its DPI.</summary>
    public void DockOn(MonitorInfo monitor)
    {
        Monitor = monitor;
        IsDetached = false;
        var scale = monitor.Scale;
        var geometry = TopBarGeometry.Compute(_settings.Height, _settings.Margin, _settings.CornerRadius, scale);
        var granted = _appBar.Dock(AppBarEdge.Top, geometry.ThicknessPx, monitor.Bounds);
        _window.ApplyGeometry(geometry, scale);

        // Docking raised the bar alone; bring its blur backdrop back directly beneath it.
        _window.RaiseToTop();
        _viewModel.Tray.BarBounds = granted;
        BoundsChanged?.Invoke(this, EventArgs.Empty);
        Log.Info($"Top bar docked on {monitor.Key} at {granted.Left},{granted.Top} {granted.Width}x{granted.Height} px (DPI scale {scale:0.##})");
    }

    /// <summary>
    /// Acts if the monitor's work area no longer leaves the strip out: it is set directly, or the AppBar is
    /// registered again where that isn't allowed (see <see cref="AppBar.EnsureReserved"/>). Returns true when it did.
    /// </summary>
    public bool EnsureReserved()
    {
        if (_disposed || IsDetached || !_appBar.EnsureReserved())
        {
            return false;
        }

        _window.RaiseToTop();
        _viewModel.Tray.BarBounds = _appBar.Bounds;
        BoundsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Gives the strip back to the work area, keeping the window for a later <see cref="DockOn"/>.</summary>
    public void Undock() => _appBar.Undock();

    public void ApplySettings(TopBarSettings settings)
    {
        _settings = settings;
        _viewModel.ApplySettings(settings);
        _window.ApplySettings(settings);
    }

    /// <summary>Shows the app of <paramref name="hwnd"/> (0: none) as this monitor's focused app.</summary>
    public void ShowFocusedApp(nint hwnd) => _viewModel.FocusedApp.Show(hwnd);

    /// <summary>
    /// After Explorer restarted: the new Explorer never reports the end of a full-screen app that the old one saw
    /// start, so a bar hidden for it would stay hidden for good; show it again unless that app still covers the
    /// monitor.
    /// </summary>
    public void RecheckFullScreen()
    {
        if (_fullScreen && !ForegroundCoversMonitor())
        {
            OnFullScreenChanged(this, false);
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_DPICHANGED)
        {
            // WPF rescales the window to Windows' suggested rectangle first; then restore our exact strip. A scale
            // change sends no WM_DISPLAYCHANGE, so the layout service also re-reads the monitors (the coordinator
            // re-docks the bar if the monitor changed).
            _context.Displays.Invalidate();
            _context.Dispatcher.BeginInvoke(RestoreStrip, System.Windows.Threading.DispatcherPriority.Background);
        }
        else if (msg == NativeMethods.WM_SETTINGCHANGE && wParam == NativeMethods.SPI_SETWORKAREA)
        {
            // A work area changed (ours or another AppBar's, or Explorer recomputing them): one debounced pass re-checks
            // that every strip is still reserved. Acting only on a missing strip keeps this from feeding itself.
            _context.Displays.Invalidate(force: true);
        }
        else if (TaskbarCreatedMessage != 0 && msg == (int)TaskbarCreatedMessage && !TrayHost.IsOwnBroadcast(wParam))
        {
            ExplorerRestarted?.Invoke(this, EventArgs.Empty);
        }

        return 0;
    }

    /// <summary>
    /// Re-docks on the same monitor, read fresh, but only while it still exists with the same bounds (after a DPI
    /// change or a settings change). Windows also sends WM_DPICHANGED when it moves the window off a removed monitor,
    /// and a settings change can arrive between a display change and the coordinator's pass: docking on a stale
    /// rectangle could reserve a second strip elsewhere, so that case is left to the pass.
    /// </summary>
    public void RestoreStrip()
    {
        if (!_disposed && !IsDetached && _appBar.IsRegistered
            && DisplayLayoutService.Read().Find(Monitor.Key) is { } monitor && monitor.Bounds == Monitor.Bounds)
        {
            DockOn(monitor);
        }
    }

    /// <summary>The shell restacked the bar (another top AppBar came or went).</summary>
    private void OnAppBarMoved(object? sender, EventArgs e)
    {
        _viewModel.Tray.BarBounds = _appBar.Bounds;
        BoundsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnAppBarDetached(object? sender, EventArgs e)
    {
        IsDetached = true;
        Detached?.Invoke(this, EventArgs.Empty);
    }

    private void OnFullScreenChanged(object? sender, bool fullScreen)
    {
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

    private bool ForegroundCoversMonitor()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (NativeMethods.GetClassName(foreground) is "Progman" or "WorkerW")
        {
            return false;
        }

        var monitor = Monitor.Bounds;
        var window = NativeMethods.GetWindowBounds(foreground);
        return window.Left <= monitor.Left && window.Top <= monitor.Top
            && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
    }

    /// <summary>Undocks first (the strip goes back before anything else), then closes the window and backdrop.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source?.RemoveHook(WndProc);
        _appBar.FullScreenChanged -= OnFullScreenChanged;
        _appBar.Moved -= OnAppBarMoved;
        _appBar.Detached -= OnAppBarDetached;
        _appBar.Dispose();
        _window.Shutdown();

        // The bar is owned by its backdrop; it was closed first so WPF tears it down rather than Win32.
        _backdrop.Dispose();
        _viewModel.Dispose();
    }
}
