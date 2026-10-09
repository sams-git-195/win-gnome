using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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
/// icons grow into headroom beyond the strip). Explorer restarts are handled by the dock coordinator in one pass
/// (<see cref="Redock"/>); the crash path is <see cref="AppBar.UndockAll"/>.
/// </remarks>
internal sealed class DockReservation : IDisposable
{
    private Window? _window;
    private AppBar? _appBar;
    private (AppBarEdge Edge, int Thickness, PixelRect Monitor)? _reserved;

    /// <summary>The AppBar undocked itself because its monitor went away or changed.</summary>
    public event EventHandler? Detached;

    /// <summary>True after the AppBar detached itself and before the next <see cref="Reserve"/>.</summary>
    public bool IsDetached { get; private set; }

    /// <summary>Reserves <paramref name="thicknessPx"/> physical pixels along <paramref name="edge"/> of <paramref name="monitor"/>.</summary>
    public void Reserve(AppBarEdge edge, int thicknessPx, PixelRect monitor)
    {
        if (_appBar is null)
        {
            _appBar = new AppBar(CreateWindow());
            _appBar.Detached += OnDetached;
        }

        IsDetached = false;
        _reserved = (edge, thicknessPx, monitor);
        _appBar.Dock(edge, thicknessPx, monitor);
    }

    /// <summary>Gives the strip back to the work area.</summary>
    public void Release()
    {
        _reserved = null;
        _appBar?.Undock();
    }

    /// <summary>After Explorer restarted: registers the current reservation again (no-op when nothing is reserved).</summary>
    public void Redock()
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

    /// <summary>Registers the strip again if the work area no longer leaves it out (see <see cref="AppBar.EnsureReserved"/>).</summary>
    public bool EnsureReserved() => _reserved is not null && _appBar is not null && _appBar.EnsureReserved();

    /// <summary>Undocks without forgetting the reservation, so <see cref="Redock"/> can register it again.</summary>
    public void Undock() => _appBar?.Undock();

    private void OnDetached(object? sender, EventArgs e)
    {
        _reserved = null;
        IsDetached = true;
        Detached?.Invoke(this, EventArgs.Empty);
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
        return _window;
    }

    public void Dispose()
    {
        _reserved = null;
        if (_appBar is not null)
        {
            _appBar.Detached -= OnDetached;
            _appBar.Dispose();
            _appBar = null;
        }

        _window?.Close();
        _window = null;
    }
}
