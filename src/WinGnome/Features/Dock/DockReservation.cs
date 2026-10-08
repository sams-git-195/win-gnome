using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using WinGnome.Core.Geometry;
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
    private Window? _window;
    private AppBar? _appBar;

    /// <summary>Reserves <paramref name="thicknessPx"/> physical pixels along <paramref name="edge"/> of <paramref name="monitor"/>.</summary>
    public void Reserve(AppBarEdge edge, int thicknessPx, PixelRect monitor)
    {
        _appBar ??= new AppBar(CreateWindow());
        _appBar.Dock(edge, thicknessPx, monitor);
    }

    /// <summary>Gives the strip back to the work area.</summary>
    public void Release() => _appBar?.Undock();

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
        return _window;
    }

    public void Dispose()
    {
        _appBar?.Dispose();
        _appBar = null;
        _window?.Close();
        _window = null;
    }
}
