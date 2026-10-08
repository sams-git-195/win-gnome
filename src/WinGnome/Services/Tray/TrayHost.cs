using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinGnome.Core.Geometry;
using WinGnome.Core.Tray;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Services.Tray;

/// <summary>
/// Receives the notification-area icons of every running app while Explorer keeps showing them too.
/// </summary>
/// <remarks>
/// <para>
/// Apps add tray icons with Shell_NotifyIcon, which sends WM_COPYDATA to the first top-level window of class
/// "Shell_TrayWnd" (FindWindow order = z-order). The host registers its own hidden window of that class in front of
/// Explorer's, records each call, and forwards <em>every</em> message to Explorer's real tray window synchronously,
/// so Explorer keeps its icons and SHAppBarMessage (sent the same way) keeps working for every AppBar, ours included.
/// Because Explorer saw every call, nothing is lost if WinGnome dies, even under taskkill /f.
/// </para>
/// <para>
/// The window lives on its own thread with a plain Win32 message loop. Every Shell_NotifyIcon and SHAppBarMessage
/// call in the session waits for this window, so it must never wait for WPF layout, a modal dialog or the
/// dispatcher; the thread only parses, forwards and posts results to the UI thread.
/// </para>
/// <para>
/// At start the host broadcasts "TaskbarCreated", which makes running apps add their icons again (to us; Explorer
/// receives them through forwarding and ignores the duplicates). The broadcast carries <see cref="OwnBroadcastMarker"/>
/// in wParam (Explorer sends 0) so WinGnome's own listeners know Explorer did not actually restart.
/// </para>
/// <para>
/// Only one WinGnome process hosts the tray at a time (a session-wide mutex); a second instance waits and takes over
/// when the first one stops.
/// </para>
/// </remarks>
internal sealed class TrayHost : IDisposable
{
    /// <summary>wParam of the TaskbarCreated broadcasts WinGnome sends ('WGNM'); Explorer's own broadcast uses 0.</summary>
    public const nint OwnBroadcastMarker = 0x57474E4D;

    private const string WindowClass = "Shell_TrayWnd";
    private const string MutexName = @"Local\WinGnome-TrayHost";

    /// <summary>Window property that marks a Shell_TrayWnd as a WinGnome tray host (readable from other processes).</summary>
    private const string HostProperty = "WinGnome.TrayHost";

    /// <summary>Same order of magnitude as the shell's own timeouts; Explorer normally answers in well under 1 ms.</summary>
    private const uint ForwardTimeoutMs = 4000;

    private const nint ZOrderTimer = 1;
    private const nint RebroadcastTimer = 2;

    /// <summary>
    /// Explorer raises its taskbar to the top of the topmost band when it is clicked or slides in; calls made while it
    /// is in front of us reach Explorer only. A quick, allocation-free check puts us back in front.
    /// </summary>
    private const uint ZOrderCheckMs = 250;

    /// <summary>
    /// After a TaskbarCreated broadcast, apps re-register in a burst (NIM_ADD then often NIM_SETVERSION) while windows
    /// come and go, which makes Explorer raise its taskbar: a call missed then (a lost NIM_SETVERSION switches the
    /// icon to the wrong callback format) is not repeated. So for a few seconds the check runs as often as user32
    /// timers allow.
    /// </summary>
    private const uint FastZOrderCheckMs = 15;

    private static readonly TimeSpan FastZOrderPeriod = TimeSpan.FromSeconds(4);

    /// <summary>Icons whose owner window is gone are dropped every 5 s, as Explorer does.</summary>
    private static readonly TimeSpan PruneInterval = TimeSpan.FromSeconds(5);

    /// <summary>After Explorer restarts, apps re-add their icons straight to its new (frontmost) tray; ask again.</summary>
    private const uint RebroadcastDelayMs = 2000;

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly Dispatcher _dispatcher;
    private readonly Action<TrayChange, bool, IconHandle?> _onChange;
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _thread;
    private readonly NativeMethods.WndProc _wndProc;
    private readonly uint _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");

    // Shared with the UI thread.
    private readonly object _gate = new();
    private readonly Dictionary<TrayIconId, PixelRect> _iconBounds = [];
    private PixelRect _barBounds;
    private volatile uint _threadId;
    private volatile nint _hwnd;

    // Tray thread only.
    private readonly TrayIconRegistry _registry = new();
    private nint _instance;
    private nint _explorerTray;
    private long _lastPruneMs;
    private long _fastChecksUntilMs;
    private bool _yieldLogged;

    /// <summary>A call could not be passed on to Explorer, so Explorer may lack an icon we have.</summary>
    private bool _forwardFailed;
    private bool _disposed;

    /// <param name="dispatcher">UI dispatcher that <paramref name="onChange"/> runs on.</param>
    /// <param name="onChange">
    /// Called on the UI thread, in order, for every change of the icon list: the change, whether the icon image changed,
    /// and the new image (null = no image). The icon handle is only valid during the call.
    /// </param>
    /// <param name="barBounds">Where the bar is, in physical pixels: the host window claims that strip, as a taskbar would.</param>
    public TrayHost(Dispatcher dispatcher, Action<TrayChange, bool, IconHandle?> onChange, PixelRect barBounds)
    {
        _dispatcher = dispatcher;
        _onChange = onChange;
        _barBounds = barBounds;
        _wndProc = WndProc;
        _thread = new Thread(Run) { Name = "WinGnome tray host", IsBackground = true };
        _thread.Start();
    }

    /// <summary>True for the TaskbarCreated broadcasts a WinGnome tray host sends (Explorer did not restart).</summary>
    public static bool IsOwnBroadcast(nint wParam) => wParam == OwnBroadcastMarker;

    /// <summary>True for the tray host window of any WinGnome process: a Shell_TrayWnd that is not a taskbar.</summary>
    public static bool IsHostWindow(nint hwnd) => hwnd != 0 && NativeMethods.GetProp(hwnd, HostProperty) != 0;

    /// <summary>Moves the (invisible) host window onto the bar's strip.</summary>
    public void SetBarBounds(PixelRect bounds)
    {
        lock (_gate)
        {
            _barBounds = bounds;
        }

        var hwnd = _hwnd;
        if (hwnd != 0)
        {
            // Asynchronous: never block the UI thread on the tray thread.
            NativeMethods.SetWindowPos(hwnd, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_ASYNCWINDOWPOS);
        }
    }

    /// <summary>Records where an icon is on screen, for apps that position flyouts with Shell_NotifyIconGetRect.</summary>
    public void SetIconBounds(TrayIconId icon, PixelRect bounds)
    {
        lock (_gate)
        {
            // A GUID icon that moved to a new owner window has a new ID: drop the entry under its old one.
            if (icon.ItemGuid != Guid.Empty)
            {
                foreach (var stale in _iconBounds.Keys.Where(key => key.ItemGuid == icon.ItemGuid && key != icon).ToList())
                {
                    _iconBounds.Remove(stale);
                }
            }

            _iconBounds[icon] = bounds;
        }
    }

    /// <summary>Crash path (any thread, plain Win32): ask the tray thread to remove the window and hand icons back.</summary>
    public void EmergencyRestore()
    {
        var threadId = _threadId;
        if (threadId != 0)
        {
            NativeMethods.PostThreadMessage(threadId, NativeMethods.WM_QUIT, 0, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Order matters: the thread checks _stop after creating its message queue, so either it sees the event or
        // our WM_QUIT lands in that queue.
        _stop.Set();
        EmergencyRestore();
        if (!_thread.Join(StopTimeout))
        {
            Log.Warn("The tray host did not stop in time");
            return;
        }

        _stop.Dispose();
    }

    private void Run()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        try
        {
            using var mutex = new Mutex(false, MutexName);
            if (!WaitForHostRole(mutex))
            {
                return;
            }

            try
            {
                if (CreateHostWindow() && !_stop.WaitOne(0))
                {
                    Log.Info("Tray host started");
                    while (NativeMethods.GetMessage(out var msg, 0, 0, 0) > 0)
                    {
                        NativeMethods.DispatchMessage(in msg);
                    }
                }
            }
            finally
            {
                DestroyHostWindow();
                mutex.ReleaseMutex();
            }
        }
        catch (Exception ex)
        {
            // A background thread's exception would end the process; the bar simply shows no tray icons instead.
            Log.Error("The tray host failed", ex);
        }
    }

    /// <summary>Waits until no other WinGnome process hosts the tray, or until we are stopped.</summary>
    private bool WaitForHostRole(Mutex mutex)
    {
        try
        {
            if (mutex.WaitOne(0))
            {
                return true;
            }

            Log.Info("Another WinGnome instance shows the tray icons; this one takes over when that one stops");
            return WaitHandle.WaitAny([_stop, mutex]) == 1;
        }
        catch (AbandonedMutexException)
        {
            // The previous host was killed; Explorer has all icons, so taking over is safe. We now own the mutex.
            return !_stop.WaitOne(0) || ReleaseAndRefuse(mutex);
        }
    }

    private static bool ReleaseAndRefuse(Mutex mutex)
    {
        mutex.ReleaseMutex();
        return false;
    }

    private bool CreateHostWindow()
    {
        _instance = NativeMethods.GetModuleHandle(null);
        var className = Marshal.StringToHGlobalUni(WindowClass);
        try
        {
            var windowClass = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = _instance,
                lpszClassName = className,
            };
            if (NativeMethods.RegisterClassEx(in windowClass) == 0)
            {
                Log.Warn($"Could not register the tray host window class (error {Marshal.GetLastPInvokeError()})");
                return false;
            }
        }
        finally
        {
            // RegisterClassEx copies the name into the atom table.
            Marshal.FreeHGlobal(className);
        }

        PixelRect bounds;
        lock (_gate)
        {
            bounds = _barBounds;
        }

        // A hidden, unowned topmost tool window: invisible, never activated, not in Alt+Tab, and a top-level window,
        // which FindWindow (and therefore shell32) requires. Topmost puts it in the band Explorer's taskbar lives in.
        var hwnd = NativeMethods.CreateWindowEx(
            (uint)(NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST), WindowClass, null, unchecked((uint)NativeMethods.WS_POPUP),
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0, 0, _instance, 0);
        if (hwnd == 0)
        {
            Log.Warn($"Could not create the tray host window (error {Marshal.GetLastPInvokeError()})");
            NativeMethods.UnregisterClass(WindowClass, _instance);
            return false;
        }

        _hwnd = hwnd;
        NativeMethods.SetProp(hwnd, HostProperty, 1);
        BringToFront();
        _lastPruneMs = Environment.TickCount64;
        AskAppsToRegister(hwnd);
        return true;
    }

    private void DestroyHostWindow()
    {
        var hwnd = _hwnd;
        if (hwnd == 0)
        {
            return;
        }

        _hwnd = 0;
        NativeMethods.KillTimer(hwnd, ZOrderTimer);
        NativeMethods.KillTimer(hwnd, RebroadcastTimer);
        NativeMethods.RemoveProp(hwnd, HostProperty);
        NativeMethods.DestroyWindow(hwnd);
        NativeMethods.UnregisterClass(WindowClass, _instance);

        // Explorer received every call through forwarding, so it already has every icon: normally nothing to hand back.
        // Asking all apps to re-register anyway is not free: it races with the start broadcast of a WinGnome instance
        // taking over from us, and apps that ignore a second TaskbarCreated in quick succession then end up only in
        // Explorer's tray. So only when a forward failed (Explorer hung or restarting) do we ask apps to re-register,
        // and with our window gone, they re-register with Explorer.
        if (_forwardFailed)
        {
            BroadcastTaskbarCreated();
        }

        Log.Info("Tray host stopped");
    }

    /// <summary>Broadcasts TaskbarCreated and guards the front closely while apps answer it.</summary>
    private void AskAppsToRegister(nint hwnd)
    {
        _fastChecksUntilMs = Environment.TickCount64 + (long)FastZOrderPeriod.TotalMilliseconds;
        NativeMethods.SetTimer(hwnd, ZOrderTimer, FastZOrderCheckMs, 0);
        BroadcastTaskbarCreated();
    }

    private void BroadcastTaskbarCreated()
    {
        if (_taskbarCreated != 0)
        {
            NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, _taskbarCreated, OwnBroadcastMarker, 0);
        }
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        // Exceptions must never unwind into user32.
        try
        {
            switch (msg)
            {
                case NativeMethods.WM_COPYDATA:
                    return OnCopyData(hwnd, wParam, lParam);

                case NativeMethods.WM_TIMER:
                    OnTimer(hwnd, wParam);
                    return 0;

                case NativeMethods.WM_WINDOWPOSCHANGING:
                    KeepHidden(lParam);
                    break;

                case (uint)NativeMethods.WM_CLOSE:
                    // Someone closing "the taskbar". Default handling would destroy the host behind our back.
                    return 0;

                case NativeMethods.WM_COMMAND:
                    return Forward(msg, wParam, lParam);
            }

            if (_taskbarCreated != 0 && msg == _taskbarCreated)
            {
                OnTaskbarCreated(hwnd, wParam);
                return 0;
            }

            // Private messages apps send to "the taskbar" belong to Explorer. Registered messages (0xC000+) are
            // broadcasts every top-level window receives, Explorer included, so they are not forwarded.
            if (msg is >= NativeMethods.WM_USER and < NativeMethods.RegisteredMessageFirst)
            {
                return Forward(msg, wParam, lParam);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Tray host: message 0x{msg:X} failed", ex);
            return 0;
        }

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private unsafe nint OnCopyData(nint hwnd, nint wParam, nint lParam)
    {
        if (lParam == 0)
        {
            return 0;
        }

        var copy = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
        var data = copy.lpData != 0 && copy.cbData > 0 ? new ReadOnlySpan<byte>((void*)copy.lpData, copy.cbData) : default;
        switch ((TrayCopyDataKind)copy.dwData)
        {
            case TrayCopyDataKind.NotifyIcon:
                return OnNotifyIcon(data, wParam, lParam);

            case TrayCopyDataKind.IconRect:
                var query = ShellTrayData.ParseIconRectQuery(data);
                return query is { } q && TryGetIconBounds(q.Icon, out var bounds) ? q.Answer(bounds) : Forward(NativeMethods.WM_COPYDATA, wParam, lParam);

            default:
                // AppBar messages (and anything unknown) are Explorer's business: pass them through untouched. The
                // block carries the caller's process ID and shared-memory handle, so Explorer answers the caller directly.
                var result = Forward(NativeMethods.WM_COPYDATA, wParam, lParam);

                // Explorer raises its taskbar when AppBars change (an AppBar comes, goes or moves); until we are back
                // in front, Shell_NotifyIcon calls would reach Explorer only. Do not wait for the next check.
                KeepInFront(hwnd);
                return result;
        }
    }

    private nint OnNotifyIcon(ReadOnlySpan<byte> data, nint wParam, nint lParam)
    {
        var command = ShellTrayData.ParseNotifyIcon(data);

        // The caller may destroy its HICON as soon as Shell_NotifyIcon returns: copy it now, while the call is blocked.
        var icon = command is not null && command.Has(NotifyIconFields.Icon) && command.Icon != 0 ? IconHandle.Copy(command.Icon) : null;
        try
        {
            var shellAccepted = Forward(NativeMethods.WM_COPYDATA, wParam, lParam) != 0;
            if (command is null)
            {
                return shellAccepted ? 1 : 0;
            }

            var change = _registry.Apply(command, shellAccepted);
            if (change.Kind != TrayChangeKind.None)
            {
                // A new icon image counts only if the copy worked (a stale handle keeps the previous image).
                var imageChanged = command.Has(NotifyIconFields.Icon) && (command.Icon == 0 || icon is not null);
                Publish(change, imageChanged, icon);
                icon = null;
            }

            if (change.Kind == TrayChangeKind.Removed)
            {
                lock (_gate)
                {
                    _iconBounds.Remove(change.Icon!.Id);
                }
            }

            // Success if either of us knows the icon: an app must not retry or give up on an icon that exists.
            return change.Accepted || shellAccepted ? 1 : 0;
        }
        finally
        {
            icon?.Dispose();
        }
    }

    private void Publish(TrayChange change, bool imageChanged, IconHandle? icon)
    {
        _dispatcher.BeginInvoke(() =>
        {
            try
            {
                _onChange(change, imageChanged, icon);
            }
            finally
            {
                icon?.Dispose();
            }
        });
    }

    private bool TryGetIconBounds(TrayIconId query, out PixelRect bounds)
    {
        lock (_gate)
        {
            foreach (var (icon, iconBounds) in _iconBounds)
            {
                if (icon.IsIdentifiedBy(query))
                {
                    bounds = iconBounds;
                    return true;
                }
            }
        }

        bounds = default;
        return false;
    }

    /// <summary>Passes a message on to Explorer's tray window the way it reached us (sent or posted).</summary>
    private nint Forward(uint msg, nint wParam, nint lParam)
    {
        var target = ExplorerTray();
        if (target == 0)
        {
            return 0;
        }

        if (!NativeMethods.InSendMessage())
        {
            NativeMethods.PostMessage(target, (int)msg, wParam, lParam);
            return 0;
        }

        // Not SMTO_BLOCK: while Explorer works, calls sent to us are still served (and forwarded in turn, nested).
        // When dozens of apps answer a TaskbarCreated broadcast at once they queue up like that; and Explorer itself
        // may broadcast (WM_SETTINGCHANGE after a work-area change) or notify the AppBar whose call we are forwarding
        // while we wait, which must not stall. Only Explorer is ever a target, and it never forwards, so this cannot loop.
        if (NativeMethods.SendMessageTimeout(target, (int)msg, wParam, lParam, NativeMethods.SMTO_ABORTIFHUNG, ForwardTimeoutMs, out var result) != 0)
        {
            return result;
        }

        if (!_forwardFailed)
        {
            _forwardFailed = true;
            Log.Warn($"Tray host: Explorer did not answer message 0x{msg:X} (error {Marshal.GetLastPInvokeError()}); apps will be asked to re-register their icons when the tray host stops");
        }

        return 0;
    }

    /// <summary>
    /// Explorer's tray window, or 0. Anything else (another shell's tray host could forward back to us) is never a
    /// target: with no Explorer, calls simply fail as they would without a shell.
    /// </summary>
    private nint ExplorerTray()
    {
        if (_explorerTray == 0 || !NativeMethods.IsWindow(_explorerTray))
        {
            var tray = TaskbarController.FindExplorerTray();
            var path = tray == 0 ? null : NativeMethods.GetProcessPath(NativeMethods.GetProcessId(tray));
            _explorerTray = string.Equals(Path.GetFileName(path), "explorer.exe", StringComparison.OrdinalIgnoreCase) ? tray : 0;
        }

        return _explorerTray;
    }

    private void OnTimer(nint hwnd, nint timer)
    {
        if (timer == RebroadcastTimer)
        {
            NativeMethods.KillTimer(hwnd, RebroadcastTimer);
            BringToFront();
            AskAppsToRegister(hwnd);
            return;
        }

        KeepInFront(hwnd);
        var now = Environment.TickCount64;
        if (_fastChecksUntilMs != 0 && now >= _fastChecksUntilMs)
        {
            _fastChecksUntilMs = 0;
            NativeMethods.SetTimer(hwnd, ZOrderTimer, ZOrderCheckMs, 0);
        }

        if (now - _lastPruneMs >= (long)PruneInterval.TotalMilliseconds)
        {
            _lastPruneMs = now;
            PruneDeadIcons();
        }
    }

    private void KeepInFront(nint hwnd)
    {
        var front = NativeMethods.FindWindow(WindowClass, null);
        if (front == 0 || front == hwnd)
        {
            return;
        }

        if (front == ExplorerTray() || IsHostWindow(front))
        {
            BringToFront();
        }
        else if (!_yieldLogged)
        {
            // Another program's tray host (RetroBar, for instance). Fighting it for the front would make both miss
            // icons, so it keeps the front; we still see calls whenever we are in front of it.
            _yieldLogged = true;
            Log.Info("Another program hosts tray icons in front of WinGnome's tray host; not competing with it");
        }
    }

    private void PruneDeadIcons()
    {
        foreach (var change in _registry.RemoveWhere(icon => !NativeMethods.IsWindow(icon.Id.Owner)))
        {
            lock (_gate)
            {
                _iconBounds.Remove(change.Icon!.Id);
            }

            Publish(change, false, null);
        }
    }

    private void OnTaskbarCreated(nint hwnd, nint wParam)
    {
        if (IsOwnBroadcast(wParam))
        {
            return;
        }

        // Explorer restarted: its new tray window was created in front of ours, so apps are re-adding their icons
        // directly to it right now. Get back in front, and ask them once more when they are done.
        _explorerTray = 0;
        BringToFront();
        NativeMethods.SetTimer(hwnd, RebroadcastTimer, RebroadcastDelayMs, 0);
    }

    private void BringToFront()
    {
        var hwnd = _hwnd;
        if (hwnd != 0)
        {
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    /// <summary>The host must never become visible, even if something "shows the taskbar" by class name.</summary>
    private static void KeepHidden(nint lParam)
    {
        if (lParam == 0)
        {
            return;
        }

        var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        if ((pos.flags & NativeMethods.SWP_SHOWWINDOW) != 0)
        {
            pos.flags &= ~NativeMethods.SWP_SHOWWINDOW;
            Marshal.StructureToPtr(pos, lParam, false);
        }
    }
}
