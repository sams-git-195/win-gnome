using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WinGnome.Core.Geometry;
using WinGnome.Core.Shell;
using WinGnome.Infrastructure;
using WinGnome.Services;

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
    private const int ABN_STATECHANGE = 0x00;
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
    private string _recheckTrigger = string.Empty; // Read only via _recheckPending, which is always set with it.
    private readonly StripRecovery _recovery = new();
    private DispatcherTimer? _recoveryTimer;
    private bool _gaveUp;

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
    /// Crash path (any thread, plain Win32 only): sends ABM_REMOVE for every window that still holds a registration,
    /// then gives back every work area set directly for them. The instances are not told; a later
    /// <see cref="Undock"/> on one of them sends a harmless second ABM_REMOVE.
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

        // After the removals, so nothing can claim a strip while the work areas are put back.
        WorkAreaController.ReleaseAll();
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

    /// <summary>
    /// Checks that the monitor's work area still leaves the strip out (display passes call this; shell notifications
    /// and the bar's own one-shot timer use the same check). Explorer applies strips late on its own (seen ~35 s
    /// after the taskbar went auto-hide) and can recompute work areas without them (a monitor unplugged), so a missing
    /// strip is handled by <see cref="StripRecovery"/>: wait 1.5 s, then set the work area directly — or, where that
    /// isn't allowed, register the AppBar again (ABM_REMOVE, ABM_NEW and the docking sequence; a SETPOS of the
    /// unchanged rectangle does not bring a strip back) — at most three times, 5 s and 20 s apart, then a
    /// five-minute cool-down after which the checks resume. A one-shot timer runs only while a strip is missing.
    /// Returns true when it acted on a missing strip.
    /// </summary>
    public bool EnsureReserved() => CheckStrip("a display pass");

    private bool CheckStrip(string trigger)
    {
        if (!_registered || Bounds.IsEmpty)
        {
            return false;
        }

        var wasMissing = _recovery.IsMissing;
        var step = _recovery.Update(IsStripReserved(), WorkAreaController.CanShrink, Environment.TickCount64);
        switch (step.Kind)
        {
            case StripRecoveryKind.None:
                StopRecoveryTimer();
                _gaveUp = false;
                if (wasMissing)
                {
                    Log.Info($"AppBar 0x{_hwnd:X}: the strip {Format(Bounds)} is reserved again (seen after {trigger})");
                }

                return false;

            case StripRecoveryKind.Wait:
                if (_gaveUp)
                {
                    // Update only returns Wait after exhaustion once the cool-down has expired (or the episode is
                    // fresh, when _gaveUp is already false), so this fires exactly once per cool-down.
                    _gaveUp = false;
                    Log.Info($"AppBar 0x{_hwnd:X}: cool-down over; checking the strip {Format(Bounds)} again");
                }

                if (!wasMissing)
                {
                    Log.Info($"AppBar 0x{_hwnd:X}: after {trigger} the work area of monitor {Format(_monitor)} doesn't leave out the strip {Format(Bounds)}; leaving it to Explorer for {StripRecovery.FirstActionMs / 1000.0:0.#} s");
                }

                StartRecoveryTimer(step.DueMs);
                return false;

            case StripRecoveryKind.Shrink:
            {
                // WorkAreaController logs the change itself, with the monitor, both rectangles and the strip.
                var shrunk = WorkAreaController.TryShrink(_hwnd, _monitor, (Core.Shell.AppBarEdge)(int)_edge, Bounds);
                if (!shrunk)
                {
                    Log.Info($"AppBar 0x{_hwnd:X}: the strip {Format(Bounds)} is still missing from monitor {Format(_monitor)}'s work area (seen after {trigger}), which could not be set directly");
                }

                StartRecoveryTimer(step.DueMs);
                return shrunk;
            }

            case StripRecoveryKind.Reregister:
                Log.Info($"AppBar 0x{_hwnd:X}: the strip {Format(Bounds)} is still missing from monitor {Format(_monitor)}'s work area (seen after {trigger}); registering again");
                Reregister();
                StartRecoveryTimer(step.DueMs);
                return true;

            default:
                if (!_gaveUp)
                {
                    _gaveUp = true;
                    Log.Warn($"AppBar 0x{_hwnd:X}: Explorer still hasn't reserved the strip {Format(Bounds)} after {StripRecovery.MaxAttempts} attempts; not acting again for {StripRecovery.ReArmDelayMs / 60000} minutes (a re-dock or Explorer applying the strip ends the cool-down at once)");
                }

                // GiveUp: re-point the one-shot timer at the cool-down's deadline, forwarded verbatim — Core stamps
                // it once and returns the same deadline on every pre-expiry GiveUp, so a forced-pass storm can
                // shorten the remaining interval but never push the cool-down later (KI-102).
                StartRecoveryTimer(step.DueMs);
                return false;
        }
    }

    /// <summary>Registers again and re-docks in the same slot; see <see cref="EnsureReserved"/>.</summary>
    private void Reregister()
    {
        var before = Bounds;
        Guarded(() =>
        {
            Unregister();
            Register();
            Apply(_registered ? QueryRect() : ProposedRect());
        });

        if (Bounds != before)
        {
            Moved?.Invoke(this, EventArgs.Empty);
        }
    }

    private void StartRecoveryTimer(long dueMs)
    {
        if (_recoveryTimer is null)
        {
            _recoveryTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher);
            _recoveryTimer.Tick += OnRecoveryTimer;
        }

        _recoveryTimer.Stop();
        _recoveryTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, dueMs - Environment.TickCount64));
        _recoveryTimer.Start();
    }

    private void StopRecoveryTimer() => _recoveryTimer?.Stop();

    private void OnRecoveryTimer(object? sender, EventArgs e)
    {
        _recoveryTimer?.Stop();
        CheckStrip("the recovery timer");
    }

    private static string Format(PixelRect r) => $"{r.Left},{r.Top},{r.Right},{r.Bottom}";

    /// <summary>Unregisters the bar, giving the reserved space back to the work area.</summary>
    public void Undock()
    {
        _recovery.Reset();
        _gaveUp = false;
        StopRecoveryTimer();
        Unregister();

        // After ABM_REMOVE: give back a work area we set directly for this bar, newest first while it is still the
        // live value, so a dock still using the same monitor keeps its own strip.
        WorkAreaController.Release(_hwnd);
    }

    private void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        // Cleared before the call: the shell can dispatch an ABN_POSCHANGED to this window while ABM_REMOVE is in
        // progress, and a bar that still looks registered would answer it with QUERYPOS/SETPOS or detach twice.
        _registered = false;
        var data = NewData();
        Send(ABM_REMOVE, ref data);
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
    /// notify each other forever. <paramref name="trigger"/> is the notification's own name for the log lines: the
    /// KI-102 evidence cannot say which notification accompanied Explorer's recomputes, and the next occurrence
    /// should (spec 0010, B4).
    /// </summary>
    private void OnPositionChanged(string trigger)
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
            if (AppBarReservation.ShouldMove(rect, Bounds, _requested))
            {
                Apply(rect);
                moved = true;
            }
        });

        if (moved)
        {
            Moved?.Invoke(this, EventArgs.Empty);
        }

        // The slot may be unchanged while Explorer recomputed the work area without our strip (for example after the
        // taskbar's auto-hide state changed); StripRecovery decides when to act, so notifications can't loop.
        CheckStrip(trigger);
    }

    /// <summary>True when a fresh read of the monitor's work area leaves the strip out (or the monitor can't be read).</summary>
    private bool IsStripReserved()
    {
        var monitor = NativeMethods.MonitorFromRect(RECT.From(_monitor), NativeMethods.MONITOR_DEFAULTTONULL);
        if (monitor == 0)
        {
            return true;
        }

        var (bounds, workArea) = NativeMethods.GetMonitorRects(monitor);
        return bounds != _monitor || AppBarReservation.IsReserved((Core.Shell.AppBarEdge)(int)_edge, Bounds, workArea);
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
            var trigger = _recheckTrigger;
            _dispatcher.BeginInvoke(() => OnPositionChanged(trigger), DispatcherPriority.Background);
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
            // ABN_STATECHANGE: the taskbar's auto-hide or always-on-top state changed, after which Explorer recomputes
            // work areas; the same recheck applies (it acts only when our slot or our strip is really wrong).
            case ABN_POSCHANGED or ABN_STATECHANGE when _inCall:
                _recheckTrigger = NotificationName((int)wParam);
                _recheckPending = true;
                break;
            case ABN_POSCHANGED or ABN_STATECHANGE:
                OnPositionChanged(NotificationName((int)wParam));
                break;
            case ABN_FULLSCREENAPP:
                FullScreenChanged?.Invoke(this, lParam != 0);
                break;
        }

        handled = true;
        return 0;
    }

    private static string NotificationName(int notification) =>
        notification == ABN_POSCHANGED ? "ABN_POSCHANGED" : "ABN_STATECHANGE";

    public void Dispose()
    {
        Undock();
        if (_recoveryTimer is not null)
        {
            _recoveryTimer.Tick -= OnRecoveryTimer;
            _recoveryTimer = null;
        }

        _source?.RemoveHook(WndProc);
    }
}
