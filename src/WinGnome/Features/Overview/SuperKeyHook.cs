using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinGnome.Core.Input;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Overview;

/// <summary>
/// Low-level keyboard hook for GNOME's Super-key shortcuts: tapping the Windows key alone toggles the
/// overview instead of opening Start, and Super+1..9 activates the n-th dock item.
/// </summary>
/// <remarks>
/// <para>
/// The hook runs on its own thread with its own message loop. Windows silently removes low-level hooks
/// whose callbacks are slow, and every keystroke on the system waits for the callback, so it must never
/// be stuck behind UI work. The callback only feeds <see cref="SuperKeyStateMachine"/>, decides whether to
/// swallow the key, and posts the actual work to the UI thread.
/// </para>
/// <para>
/// Only digit keys pressed together with the Windows key (when the dock option is on) are ever swallowed.
/// Windows-key releases always pass through; Start is kept closed by tapping an unassigned "mask" key first.
/// Injected input (ours and other programs') is ignored entirely.
/// </para>
/// </remarks>
internal sealed class SuperKeyHook : IDisposable
{
    private const int FirstDigitVk = 0x31; // '1'
    private const int LastDigitVk = 0x39;  // '9'

    private readonly Dispatcher _ui;
    private readonly Thread _thread;

    // Hook-thread state: only touched inside the callback.
    private readonly SuperKeyStateMachine _stateMachine = new();
    private readonly bool[] _swallowedDigits = new bool[LastDigitVk - FirstDigitVk + 1];
    private bool _startMasked;

    // Written by the UI thread, read by the hook thread.
    private volatile bool _superOpensOverview;
    private volatile bool _superNumberActivatesDock;

    // Kept in a field so the GC cannot collect the delegate while user32 still calls it.
    private NativeMethods.LowLevelKeyboardProc? _callback;
    private nint _hook;

    // Hand-over between Dispose (UI thread) and Run (hook thread), guarded by _lifetimeLock.
    private readonly object _lifetimeLock = new();
    private Dispatcher? _hookDispatcher;
    private bool _stopRequested;

    private SuperKeyHook(Dispatcher ui)
    {
        _ui = ui;
        _thread = new Thread(Run)
        {
            Name = "WinGnome keyboard hook",
            IsBackground = true,
        };
    }

    /// <summary>Raised on the UI thread when the Windows key was tapped on its own.</summary>
    public event EventHandler? OverviewToggleRequested;

    /// <summary>Raised on the UI thread with the zero-based dock index for Super+1..9.</summary>
    public event EventHandler<int>? DockItemRequested;

    /// <summary>Installs the hook on a new thread. Returns null (and logs) when Windows refuses the hook.</summary>
    public static SuperKeyHook? TryStart(Dispatcher ui)
    {
        var hook = new SuperKeyHook(ui);

        // Deliberately not disposed: after a timeout the hook thread may still call Set() on it.
        var installed = new ManualResetEventSlim();
        hook._thread.Start(installed);
        if (!installed.Wait(TimeSpan.FromSeconds(5)) || hook._hook == 0)
        {
            Log.Warn("The keyboard hook could not be installed; Super-key shortcuts are unavailable");
            hook.Dispose();
            return null;
        }

        return hook;
    }

    /// <summary>Chooses which shortcuts are active. Safe to call at any time from the UI thread.</summary>
    public void Configure(bool superOpensOverview, bool superNumberActivatesDock)
    {
        _superOpensOverview = superOpensOverview;
        _superNumberActivatesDock = superNumberActivatesDock;
    }

    private void Run(object? state)
    {
        var installed = (ManualResetEventSlim)state!;
        _callback = HookProc;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _callback, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == 0)
        {
            Log.Warn($"SetWindowsHookEx(WH_KEYBOARD_LL) failed (error {Marshal.GetLastPInvokeError()})");
            installed.Set();
            return;
        }

        bool stopRequested;
        lock (_lifetimeLock)
        {
            _hookDispatcher = Dispatcher.CurrentDispatcher;
            stopRequested = _stopRequested;
        }

        installed.Set();

        // Low-level hook callbacks are delivered through this thread's message loop. Skipped when Dispose
        // already ran (TryStart gave up waiting): the hook must not outlive its owner.
        if (!stopRequested)
        {
            Dispatcher.Run();
        }

        if (!NativeMethods.UnhookWindowsHookEx(_hook))
        {
            Log.Warn($"UnhookWindowsHookEx failed (error {Marshal.GetLastPInvokeError()})");
        }

        _hook = 0;
    }

    private nint HookProc(int code, nint wParam, nint lParam)
    {
        try
        {
            if (code == NativeMethods.HC_ACTION && ShouldSwallow((int)wParam, lParam))
            {
                return 1;
            }
        }
        catch (Exception ex)
        {
            // An exception escaping into user32 would tear down the process; when in doubt, let the key through.
            Log.Error("Keyboard hook callback failed", ex);
        }

        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private unsafe bool ShouldSwallow(int message, nint lParam)
    {
        var data = (KBDLLHOOKSTRUCT*)lParam;
        if ((data->flags & NativeMethods.LLKHF_INJECTED) != 0 || data->dwExtraInfo == WindowActivator.InjectedMarker)
        {
            return false;
        }

        var vk = (int)data->vkCode;
        return message switch
        {
            NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN => OnKeyDown(vk),
            NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP => OnKeyUp(vk),
            _ => false,
        };
    }

    private bool OnKeyDown(int vk)
    {
        // GetAsyncKeyState still shows the state before this event, so "no Windows key down yet" means
        // this is a fresh press rather than auto-repeat. Starting clean also recovers from releases the
        // hook never saw (Win+L switches to the secure desktop before the key comes up).
        var winWasDown = IsWinKeyDown();
        if (IsWin(vk) && !winWasDown)
        {
            _stateMachine.Reset();
            _startMasked = false;
        }

        // Any non-Windows key while Windows is held makes this a combination (Win+E, Win+1, ...).
        _stateMachine.OnKeyDown(vk);

        if (vk is < FirstDigitVk or > LastDigitVk)
        {
            return false;
        }

        var slot = vk - FirstDigitVk;
        if (!_superNumberActivatesDock
            || !winWasDown
            || NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT)
            || NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL)
            || NativeMethods.IsKeyDown(NativeMethods.VK_MENU))
        {
            // This press reaches the system, so its release must too. Forgetting a stale "swallowed" mark
            // (its release was never seen, e.g. Win+L switched to the secure desktop first) keeps a later
            // plain digit from getting stuck down.
            _swallowedDigits[slot] = false;
            return false;
        }

        if (!_swallowedDigits[slot])
        {
            // Auto-repeat sends more key-downs while the digit is held; activate only once.
            _swallowedDigits[slot] = true;
            _ui.BeginInvoke(() => DockItemRequested?.Invoke(this, slot));
        }

        if (!_startMasked)
        {
            // The shell never sees the swallowed digit, so to it the Windows key looks like it was
            // pressed alone and Start would open on release. A tap of an unassigned key tells the
            // shell this was a combination.
            _startMasked = true;
            WindowActivator.TapKey(NativeMethods.VK_UNASSIGNED_MASK);
        }

        return true;
    }

    private bool OnKeyUp(int vk)
    {
        if (vk is >= FirstDigitVk and <= LastDigitVk && _swallowedDigits[vk - FirstDigitVk])
        {
            _swallowedDigits[vk - FirstDigitVk] = false;
            return true;
        }

        if (!IsWin(vk))
        {
            _stateMachine.OnKeyUp(vk);
            return false;
        }

        if (_stateMachine.OnKeyUp(vk) != SuperKeyAction.SuppressAndOpenOverview || !_superOpensOverview)
        {
            return false;
        }

        // Tap an unassigned key (marked as ours) so the shell treats this press as a combination and does
        // not open Start, then let the real release through. The release itself is never swallowed:
        // Windows would otherwise consider the Windows key still held.
        WindowActivator.TapKey(NativeMethods.VK_UNASSIGNED_MASK);

        _ui.BeginInvoke(() => OverviewToggleRequested?.Invoke(this, EventArgs.Empty));
        return false;
    }

    private static bool IsWin(int vk) => vk is SuperKeyStateMachine.VkLeftWin or SuperKeyStateMachine.VkRightWin;

    private static bool IsWinKeyDown() =>
        NativeMethods.IsKeyDown(SuperKeyStateMachine.VkLeftWin) || NativeMethods.IsKeyDown(SuperKeyStateMachine.VkRightWin);

    public void Dispose()
    {
        Dispatcher? dispatcher;
        lock (_lifetimeLock)
        {
            if (_stopRequested)
            {
                return;
            }

            // If the hook thread has not published its dispatcher yet, it sees this flag and unhooks at once.
            _stopRequested = true;
            dispatcher = _hookDispatcher;
        }

        // Asynchronous on purpose: a synchronous shutdown would block forever if the loop had already died.
        dispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        if (_thread.IsAlive && !_thread.Join(TimeSpan.FromSeconds(2)))
        {
            Log.Warn("The keyboard hook thread did not stop in time");
        }
    }
}
