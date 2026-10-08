using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinGnome.Core.Geometry;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Dock;

/// <summary>
/// Reserves the dock's strip of the screen ("always visible" mode) so maximised windows stop short of it.
/// </summary>
/// <remarks>
/// The AppBar is registered on an invisible, click-through window rather than on the dock window: the shell
/// positions an AppBar's window to exactly the reserved strip, while the dock window has to be larger (magnified
/// icons grow into headroom beyond the strip).
/// </remarks>
internal sealed class DockReservation : IDisposable
{
    // Broadcast by Explorer when it (re)starts; a restarted Explorer has forgotten every AppBar registration.
    private readonly uint _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

    private Window? _window;
    private HwndSource? _source;
    private AppBar? _appBar;
    private (AppBarEdge Edge, int Thickness, PixelRect Monitor)? _reserved;

    /// <summary>Reserves <paramref name="thicknessPx"/> physical pixels along <paramref name="edge"/> of <paramref name="monitor"/>.</summary>
    public void Reserve(AppBarEdge edge, int thicknessPx, PixelRect monitor)
    {
        _appBar ??= new AppBar(CreateWindow());
        _reserved = (edge, thicknessPx, monitor);
        _appBar.Dock(edge, thicknessPx, monitor);
    }

    /// <summary>
    /// Gives the strip back to the work area. Only plain Win32 calls, so it is also the crash-path restore
    /// (see <see cref="IEmergencyRestore"/>).
    /// </summary>
    public void Release()
    {
        _reserved = null;
        _appBar?.Undock();
    }

    private Window CreateWindow()
    {
        _window = new Window
        {
            Title = "WinGnome Dock Reservation",
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            Width = 1,
            Height = 1,
        };
        _window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(_window).Handle;
            var ex = NativeMethods.GetExStyle(hwnd)
                | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT
                | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)(ex & ~NativeMethods.WS_EX_APPWINDOW));
        };

        // Shown (fully transparent and click-through) so the shell treats it as a live bar.
        _window.Show();
        _source = HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
        _source?.AddHook(WndProc);
        return _window;
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (_taskbarCreatedMessage != 0 && msg == (int)_taskbarCreatedMessage)
        {
            // Register again with the new Explorer, after the broadcast has been delivered everywhere.
            _window?.Dispatcher.BeginInvoke(DispatcherPriority.Background, Reregister);
        }

        return 0;
    }

    private void Reregister()
    {
        if (_reserved is not { } reserved || _appBar is null)
        {
            return;
        }

        try
        {
            _appBar.Undock();
            _appBar.Dock(reserved.Edge, reserved.Thickness, reserved.Monitor);
        }
        catch (Exception ex)
        {
            Log.Warn("Dock: could not reserve the dock's strip again after Explorer restarted", ex);
        }
    }

    public void Dispose()
    {
        _reserved = null;
        _source?.RemoveHook(WndProc);
        _source = null;
        _appBar?.Dispose();
        _appBar = null;
        _window?.Close();
        _window = null;
    }
}
