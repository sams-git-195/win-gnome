using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
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
/// <remarks>
/// Every window with a live <c>ABM_NEW</c> is also kept in a static registry, so the crash path
/// (<see cref="UndockAll"/>) can give every strip back without touching feature state.
/// </remarks>
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

    /// <summary>Windows that hold a live ABM_NEW registration, swapped lock-free so any thread can read it.</summary>
    private static ImmutableArray<nint> s_registered = ImmutableArray<nint>.Empty;

    private static long s_messageCount;

    private readonly nint _hwnd;
    private readonly uint _callbackMessage;
    private readonly HwndSource? _source;
    private readonly Dispatcher _dispatcher;
    private AppBarEdge _edge;
    private int _thickness;
    private PixelRect _monitor;
    private PixelRect _requested;
    private bool _registered;
    private bool _inCall;
    private bool _recheckPending;

    /// <param name="window">Window that must already have a handle.</param>
    public AppBar(Window window)
    {
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _dispatcher = window.Dispatcher;
        _callbackMessage = NativeMethods.RegisterWindowMessage("WinGnome.AppBar." + Guid.NewGuid().ToString("N"));
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
    }

    /// <summary>Raised when a full-screen app opens (true) or closes (false) on this bar's monitor.</summary>
    public event EventHandler<bool>? FullScreenChanged;

    /// <summary>Raised when the shell moved the bar on its own (ABN_POSCHANGED: another AppBar came or went).</summary>
    public event EventHandler? Moved;

    /// <summary>
    /// Raised after the bar undocked itself because the monitor it was docked on no longer exists with the same
    /// bounds (removed, moved or resized). The owner decides whether to dock it again or destroy it.
    /// </summary>
    public event EventHandler? Detached;

    /// <summary>Number of SHAppBarMessage calls made by every bar in this process (for storm diagnostics in the log).</summary>
    public static long MessageCount => Interlocked.Read(ref s_messageCount);

    /// <summary>The rectangle the shell granted, in physical pixels.</summary>
    public PixelRect Bounds { get; private set; }

    public bool IsRegistered => _registered;

    /// <summary>
    /// Crash path (any thread, plain Win32 only): sends ABM_REMOVE for every window that still holds a registration.
    /// The instances are not told; a later <see cref="Undock"/> on one of them sends a harmless second ABM_REMOVE.
    /// </summary>
    public static void UndockAll()
    {
        var windows = ImmutableInterlocked.InterlockedExchange(ref s_registered, ImmutableArray<nint>.Empty);
        foreach (var hwnd in windows)
        {
            try
            {
                var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
                Send(ABM_REMOVE, ref data);
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not remove the AppBar of window 0x{hwnd:X}", ex);
            }
        }
    }

    /// <summary>
    /// Registers <paramref name="hwnd"/> as an AppBar and removes it at once, without ever claiming space. Any AppBar
    /// traffic makes Explorer re-check its registrations (see <c>AppBarJanitor</c>). Returns false when ABM_NEW failed.
    /// </summary>
    public static bool RegisterAndRemove(nint hwnd)
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
        if (Send(ABM_NEW, ref data) == 0)
        {
            return false;
        }

        Send(ABM_REMOVE, ref data);
        return true;
    }

    /// <summary>
    /// Registers (if needed) and docks the bar on <paramref name="edge"/> of <paramref name="monitor"/>: the full
    /// QUERYPOS, SETPOS, move and WINDOWPOSCHANGED sequence, for first registration and explicit re-docks.
    /// </summary>
    public PixelRect Dock(AppBarEdge edge, int thicknessPx, PixelRect monitor)
    {
        _edge = edge;
        _thickness = Math.Max(1, thicknessPx);
        _monitor = monitor;
        Guarded(() =>
        {
            if (!_registered)
            {
                Register();
            }

            Apply(_registered ? QueryRect() : ProposedRect());
        });
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
        Send(ABM_REMOVE, ref data);
        _registered = false;
        ImmutableInterlocked.Update(ref s_registered, (list, hwnd) => list.Remove(hwnd), _hwnd);
    }

    private void Register()
    {
        var data = NewData();
        data.uCallbackMessage = _callbackMessage;
        _registered = Send(ABM_NEW, ref data) != 0;
        if (_registered)
        {
            ImmutableInterlocked.Update(ref s_registered, (list, hwnd) => list.Contains(hwnd) ? list : list.Add(hwnd), _hwnd);
        }
        else
        {
            Log.Warn("ABM_NEW failed; bar will float without reserving space");
        }
    }

    /// <summary>Asks the shell where the bar may go (QUERYPOS) and re-applies our thickness to the answer.</summary>
    private PixelRect QueryRect()
    {
        var data = NewData();
        data.uEdge = (uint)_edge;
        data.rc = RECT.From(ProposedRect());
        Send(ABM_QUERYPOS, ref data);

        // The shell may have moved the edge we are not anchored to; re-apply our thickness.
        var granted = data.rc.ToPixelRect();
        return _edge switch
        {
            AppBarEdge.Top => granted with { Bottom = granted.Top + _thickness },
            AppBarEdge.Bottom => granted with { Top = granted.Bottom - _thickness },
            AppBarEdge.Left => granted with { Right = granted.Left + _thickness },
            _ => granted with { Left = granted.Right - _thickness },
        };
    }

    /// <summary>Claims <paramref name="rect"/> (SETPOS when registered), moves the window there and tells the shell.</summary>
    private void Apply(PixelRect rect)
    {
        _requested = rect;
        if (_registered)
        {
            var data = NewData();
            data.uEdge = (uint)_edge;
            data.rc = RECT.From(rect);
            Send(ABM_SETPOS, ref data);
            rect = data.rc.ToPixelRect();
        }

        Bounds = rect;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_NOACTIVATE);

        if (_registered)
        {
            var data = NewData();
            Send(ABM_WINDOWPOSCHANGED, ref data);
        }
    }

    private PixelRect ProposedRect() => _edge switch
    {
        AppBarEdge.Top => _monitor with { Bottom = _monitor.Top + _thickness },
        AppBarEdge.Bottom => _monitor with { Top = _monitor.Bottom - _thickness },
        AppBarEdge.Left => _monitor with { Right = _monitor.Left + _thickness },
        _ => _monitor with { Left = _monitor.Right - _thickness },
    };

    /// <summary>
    /// ABN_POSCHANGED: another AppBar came, went or moved. Only a QUERYPOS unless our slot really changed, because
    /// every SETPOS makes the shell notify every other bar on the edge, and N bars that always answer with a SETPOS
    /// notify each other forever.
    /// </summary>
    private void OnPositionChanged()
    {
        if (!_registered)
        {
            return;
        }

        if (!MonitorStillMatches())
        {
            // Windows may already have moved this window to another monitor; docking there would reserve a second
            // strip on it. Step aside and let the owner re-read the layout.
            Log.Info($"AppBar 0x{_hwnd:X}: its monitor {_monitor} is gone or changed; undocking");
            Undock();
            Detached?.Invoke(this, EventArgs.Empty);
            return;
        }

        var moved = false;
        Guarded(() =>
        {
            var rect = QueryRect();
            if (rect != Bounds && rect != _requested)
            {
                Apply(rect);
                moved = true;
            }
        });

        if (moved)
        {
            Moved?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>True when the cached monitor rectangle is still exactly a live monitor's rectangle.</summary>
    private bool MonitorStillMatches()
    {
        var monitor = NativeMethods.MonitorFromRect(RECT.From(_monitor), NativeMethods.MONITOR_DEFAULTTONULL);
        return monitor != 0 && NativeMethods.GetMonitorRects(monitor).Monitor == _monitor;
    }

    /// <summary>
    /// Runs our own shell calls with the re-entrancy flag set. SHAppBarMessage is a synchronous cross-process send,
    /// so the shell's notification can be dispatched to this window while we are still inside one of our own calls;
    /// such a notification is only marked, and one recheck runs after the outer call returned.
    /// </summary>
    private void Guarded(Action action)
    {
        if (_inCall)
        {
            action();
            return;
        }

        _inCall = true;
        try
        {
            action();
        }
        finally
        {
            _inCall = false;
        }

        if (_recheckPending)
        {
            _recheckPending = false;
            _dispatcher.BeginInvoke(OnPositionChanged, DispatcherPriority.Background);
        }
    }

    private static nuint Send(uint message, ref APPBARDATA data)
    {
        Interlocked.Increment(ref s_messageCount);
        return SHAppBarMessage(message, ref data);
    }

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
            case ABN_POSCHANGED when _inCall:
                _recheckPending = true;
                break;
            case ABN_POSCHANGED:
                OnPositionChanged();
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
