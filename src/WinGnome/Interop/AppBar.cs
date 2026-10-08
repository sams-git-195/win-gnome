using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WinGnome.Core.Geometry;
using WinGnome.Infrastructure;

namespace WinGnome.Interop;

internal enum AppBarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

/// <summary>
/// Registers a WPF window as a shell application desktop toolbar (AppBar) so the system work area
/// excludes it and maximised windows do not cover it. All geometry is in physical pixels.
/// </summary>
internal sealed partial class AppBar : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    private const uint ABM_NEW = 0x00;
    private const uint ABM_REMOVE = 0x01;
    private const uint ABM_QUERYPOS = 0x02;
    private const uint ABM_SETPOS = 0x03;
    private const uint ABM_WINDOWPOSCHANGED = 0x09;
    private const int ABN_POSCHANGED = 0x01;
    private const int ABN_FULLSCREENAPP = 0x02;

    [LibraryImport("shell32.dll")]
    private static partial nuint SHAppBarMessage(uint message, ref APPBARDATA data);

    private readonly nint _hwnd;
    private readonly uint _callbackMessage;
    private readonly HwndSource? _source;
    private AppBarEdge _edge;
    private int _thickness;
    private PixelRect _monitor;
    private bool _registered;

    /// <param name="window">Window that must already have a handle.</param>
    public AppBar(Window window)
    {
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _callbackMessage = NativeMethods.RegisterWindowMessage("WinGnome.AppBar." + Guid.NewGuid().ToString("N"));
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
    }

    /// <summary>Raised when a full-screen app opens (true) or closes (false) on this bar's monitor.</summary>
    public event EventHandler<bool>? FullScreenChanged;

    /// <summary>The rectangle the shell granted, in physical pixels.</summary>
    public PixelRect Bounds { get; private set; }

    public bool IsRegistered => _registered;

    /// <summary>Registers (if needed) and docks the bar on <paramref name="edge"/> of <paramref name="monitor"/>.</summary>
    public PixelRect Dock(AppBarEdge edge, int thicknessPx, PixelRect monitor)
    {
        _edge = edge;
        _thickness = Math.Max(1, thicknessPx);
        _monitor = monitor;

        if (!_registered)
        {
            var data = NewData();
            data.uCallbackMessage = _callbackMessage;
            _registered = SHAppBarMessage(ABM_NEW, ref data) != 0;
            if (!_registered)
            {
                Log.Warn("ABM_NEW failed; bar will float without reserving space");
            }
        }

        Reposition();
        return Bounds;
    }

    /// <summary>Unregisters the bar, giving the reserved space back to the work area.</summary>
    public void Undock()
    {
        if (!_registered)
        {
            return;
        }

        var data = NewData();
        SHAppBarMessage(ABM_REMOVE, ref data);
        _registered = false;
    }

    private void Reposition()
    {
        var rect = ProposedRect();
        if (_registered)
        {
            var data = NewData();
            data.uEdge = (uint)_edge;
            data.rc = RECT.From(rect);
            SHAppBarMessage(ABM_QUERYPOS, ref data);

            // The shell may have moved the edge we are not anchored to; re-apply our thickness.
            var granted = data.rc.ToPixelRect();
            rect = _edge switch
            {
                AppBarEdge.Top => granted with { Bottom = granted.Top + _thickness },
                AppBarEdge.Bottom => granted with { Top = granted.Bottom - _thickness },
                AppBarEdge.Left => granted with { Right = granted.Left + _thickness },
                _ => granted with { Left = granted.Right - _thickness },
            };
            data.rc = RECT.From(rect);
            SHAppBarMessage(ABM_SETPOS, ref data);
            rect = data.rc.ToPixelRect();
        }

        Bounds = rect;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_NOACTIVATE);

        if (_registered)
        {
            var data = NewData();
            SHAppBarMessage(ABM_WINDOWPOSCHANGED, ref data);
        }
    }

    private PixelRect ProposedRect() => _edge switch
    {
        AppBarEdge.Top => _monitor with { Bottom = _monitor.Top + _thickness },
        AppBarEdge.Bottom => _monitor with { Top = _monitor.Bottom - _thickness },
        AppBarEdge.Left => _monitor with { Right = _monitor.Left + _thickness },
        _ => _monitor with { Left = _monitor.Right - _thickness },
    };

    private APPBARDATA NewData() => new()
    {
        cbSize = Marshal.SizeOf<APPBARDATA>(),
        hWnd = _hwnd,
    };

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != (int)_callbackMessage || !_registered)
        {
            return 0;
        }

        switch ((int)wParam)
        {
            case ABN_POSCHANGED:
                Reposition();
                break;
            case ABN_FULLSCREENAPP:
                FullScreenChanged?.Invoke(this, lParam != 0);
                break;
        }

        handled = true;
        return 0;
    }

    public void Dispose()
    {
        Undock();
        _source?.RemoveHook(WndProc);
    }
}
