using System.Windows.Threading;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services;

namespace WinGnome.Features.Taskbar;

/// <summary>
/// Manages the native Windows taskbar while WinGnome runs: hidden ("WinGnome dock" mode,
/// GeneralSettings.HideWindowsTaskbar), switched to auto-hide ("native taskbar" mode with
/// GeneralSettings.NativeTaskbarAutoHide), or left alone. Keeps a hidden taskbar hidden when Explorer re-shows or
/// recreates it, and lets the top bar "peek" at a hidden taskbar to reach the system tray.
/// </summary>
[FeatureOrder(10)]
internal sealed class TaskbarFeature : IFeature, IEmergencyRestore
{
    /// <summary>Delay before re-hiding a taskbar Explorer just showed, so we never fight Explorer in a tight loop.</summary>
    private static readonly TimeSpan RehideDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// If Explorer re-shows the taskbar more than <see cref="RehideBurstLimit"/> times within
    /// <see cref="RehideBurstWindow"/>, re-hiding slows down to <see cref="RehideBackoffDelay"/> so the two never
    /// flicker the taskbar back and forth several times a second.
    /// </summary>
    private const int RehideBurstLimit = 5;
    private static readonly TimeSpan RehideBurstWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RehideBackoffDelay = TimeSpan.FromSeconds(5);

    /// <summary>Delay after an Explorer restart; the new taskbar is still initialising when the broadcast arrives.</summary>
    private static readonly TimeSpan RecreatedDelay = TimeSpan.FromSeconds(1);

    /// <summary>Foreground changes right after a peek (the menu that requested it closing) must not end it.</summary>
    private static readonly TimeSpan PeekGrace = TimeSpan.FromMilliseconds(600);

    private static readonly TimeSpan PeekTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PeekRecheck = TimeSpan.FromSeconds(1);

    private readonly ShellContext _context;
    private readonly string _settingsDirectory;
    private readonly DispatcherTimer _rehideTimer;
    private readonly DispatcherTimer _peekTimer;
    private readonly Queue<DateTime> _recentRehides = new();
    private TaskbarCreatedListener? _listener;
    private bool _backoffLogged;

    /// <summary>
    /// What is currently in effect. Anything but <see cref="TaskbarMode.Untouched"/> is backed by the restore marker.
    /// Read by the crash handler, possibly from another thread.
    /// </summary>
    private volatile TaskbarMode _mode;
    private bool _recreated;
    private bool _peeking;
    private DateTime _peekStartedUtc;
    private bool _started;

    public TaskbarFeature(ShellContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _settingsDirectory = context.Settings.Directory;
        _rehideTimer = new DispatcherTimer(DispatcherPriority.Normal, context.Dispatcher);
        _rehideTimer.Tick += OnRehideTimer;
        _peekTimer = new DispatcherTimer(DispatcherPriority.Normal, context.Dispatcher);
        _peekTimer.Tick += OnPeekTimer;
    }

    private enum TaskbarMode
    {
        /// <summary>The taskbar is as the user left it (and no marker is owed).</summary>
        Untouched,
        /// <summary>Native taskbar mode: auto-hide on, windows visible.</summary>
        AutoHide,
        /// <summary>WinGnome dock mode: auto-hide on and the taskbar windows hidden.</summary>
        Hidden,
    }

    public string Name => "Taskbar";

    private bool IsHidden => _mode == TaskbarMode.Hidden;

    public void Start(AppSettings settings)
    {
        _started = true;
        _context.Windows.RawWindowEvent += OnRawWindowEvent;
        _context.Windows.ForegroundChanged += OnForegroundChanged;
        _context.Commands.TaskbarPeekRequested += OnPeekRequested;
        _listener = new TaskbarCreatedListener(OnTaskbarCreated);
        ApplySettings(settings);
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var general = settings.General;
        var desired = _context.IsSafeMode ? TaskbarMode.Untouched
            : general.HideWindowsTaskbar ? TaskbarMode.Hidden
            : general.NativeTaskbarAutoHide ? TaskbarMode.AutoHide
            : TaskbarMode.Untouched;
        if (desired != _mode)
        {
            SwitchTo(desired);
        }
    }

    /// <summary>
    /// Moves between modes. Hide and SetAutoHideOnly both keep the marker that records the user's original
    /// state (written the first time either runs), so any mode can follow any other and a final restore still
    /// returns the taskbar to what the user had.
    /// </summary>
    private void SwitchTo(TaskbarMode target)
    {
        EndPeek(rehide: false, "taskbar mode changed");
        _rehideTimer.Stop();
        _recreated = false;
        switch (target)
        {
            case TaskbarMode.Hidden:
                if (TaskbarController.Hide(_settingsDirectory))
                {
                    _mode = TaskbarMode.Hidden;
                    Log.Info("Windows taskbar hidden");
                }

                break;

            case TaskbarMode.AutoHide:
                if (TaskbarController.SetAutoHideOnly(_settingsDirectory))
                {
                    _mode = TaskbarMode.AutoHide;
                    Log.Info("Windows taskbar set to auto-hide");
                }

                break;

            default:
                _mode = TaskbarMode.Untouched;
                TaskbarController.RestoreFromMarker(_settingsDirectory);
                break;
        }
    }

    private void OnRawWindowEvent(uint eventType, nint hwnd)
    {
        if (eventType == WinEventHook.EVENT_OBJECT_SHOW && IsHidden && !_peeking && TaskbarController.IsTaskbarWindow(hwnd))
        {
            ScheduleRehide(NextRehideDelay(), hwnd);
        }
    }

    private TimeSpan NextRehideDelay()
    {
        var now = DateTime.UtcNow;
        while (_recentRehides.Count > 0 && now - _recentRehides.Peek() > RehideBurstWindow)
        {
            _recentRehides.Dequeue();
        }

        if (_recentRehides.Count < RehideBurstLimit)
        {
            if (_backoffLogged)
            {
                Log.Info("Explorer stopped re-showing the taskbar; re-hiding at the normal rate again");
            }

            _backoffLogged = false;
            return RehideDelay;
        }

        if (!_backoffLogged)
        {
            _backoffLogged = true;
            Log.Warn("Explorer keeps showing the taskbar again; re-hiding it less often");
        }

        return RehideBackoffDelay;
    }

    /// <summary>
    /// The taskbar may only stay hidden or auto-hidden while its restore marker exists. When something else restored
    /// it (the settings page's "Restore taskbar" button, <c>--restore-taskbar</c>), the marker is gone: stop managing
    /// the taskbar until the taskbar mode is applied again, rather than hiding it with nothing able to undo that.
    /// </summary>
    private bool StillOwnsTaskbar()
    {
        if (_mode == TaskbarMode.Untouched)
        {
            return false;
        }

        if (TaskbarController.HasMarker(_settingsDirectory))
        {
            return true;
        }

        Log.Info("The taskbar was restored outside the taskbar feature; leaving it alone");
        _mode = TaskbarMode.Untouched;
        return false;
    }

    private void OnTaskbarCreated()
    {
        if (_mode == TaskbarMode.Untouched)
        {
            return;
        }

        Log.Info("Explorer recreated the taskbar; applying the taskbar mode again");
        EndPeek(rehide: false, "Explorer recreated the taskbar");
        _recreated = true;
        _rehideTimer.Stop();
        ScheduleRehide(RecreatedDelay);
    }

    /// <summary>
    /// Starts the re-hide timer if it is idle. <paramref name="hwnd"/> is the taskbar window Explorer showed, or 0
    /// when the trigger was not a show event; only the show path logs, throttled because a burst re-runs this every
    /// 250 ms and the suppressed count preserves the correlation.
    /// </summary>
    private void ScheduleRehide(TimeSpan delay, nint hwnd = 0)
    {
        // Start-if-idle (not restart): repeated show events must not postpone the re-hide indefinitely.
        if (!_rehideTimer.IsEnabled)
        {
            _rehideTimer.Interval = delay;
            _rehideTimer.Start();
            if (hwnd != 0)
            {
                ThrottledLog.Info("taskbar-rehide-scheduled", $"Explorer showed taskbar window 0x{hwnd:X}; re-hiding in {delay.TotalMilliseconds:0} ms");
            }
        }
    }

    private void OnRehideTimer(object? sender, EventArgs e)
    {
        _rehideTimer.Stop();
        if (_peeking)
        {
            return;
        }

        try
        {
            if (!StillOwnsTaskbar())
            {
                _recreated = false;
                return;
            }

            if (_recreated)
            {
                // A new Explorer may have reset the auto-hide state. The marker already exists, so these only re-apply.
                _recreated = false;
                var applied = IsHidden
                    ? TaskbarController.Hide(_settingsDirectory)
                    : TaskbarController.SetAutoHideOnly(_settingsDirectory);
                if (!applied)
                {
                    _mode = TaskbarMode.Untouched;
                }
            }
            else if (IsHidden)
            {
                _recentRehides.Enqueue(DateTime.UtcNow);
                var hidden = TaskbarController.HideWindows();
                ThrottledLog.Info("taskbar-rehide-executed", $"Re-hid {hidden} taskbar window(s) Explorer had shown");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not re-apply the taskbar mode", ex);
        }
    }

    private void OnPeekRequested(object? sender, EventArgs e)
    {
        if (!IsHidden)
        {
            Log.Info($"Taskbar peek ignored: the taskbar is not hidden (mode {_mode})");
            return;
        }

        if (!StillOwnsTaskbar())
        {
            return;
        }

        try
        {
            _rehideTimer.Stop();
            _peeking = true;
            _peekStartedUtc = DateTime.UtcNow;
            var shown = TaskbarController.ShowWindows();

            var primary = TaskbarController.FindExplorerTray();
            Log.Info($"Taskbar peek: showed {shown} taskbar window(s); Explorer's tray window is 0x{primary:X}");
            if (primary != 0)
            {
                NativeMethods.SetWindowPos(primary, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

                // The taskbar is in auto-hide mode, so showing its window leaves it parked off-screen. Giving it the
                // focus (what Win+T does) is the reliable way to make an auto-hide taskbar slide in.
                WindowActivator.Activate(primary);

                // After the activate, so the line says whether the slide-in trick actually got the taskbar the foreground.
                var foreground = NativeMethods.GetForegroundWindow();
                Log.Info($"Taskbar peek: foreground window is now 0x{foreground:X}{(foreground == primary ? " (the taskbar)" : string.Empty)}");
            }
            else
            {
                Log.Warn("Taskbar peek: Explorer's taskbar window was not found; the peek may stay invisible");
            }

            _peekTimer.Interval = PeekTimeout;
            _peekTimer.Start();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not reveal the taskbar", ex);
            EndPeek(rehide: true, "reveal failed");
        }
    }

    private void OnForegroundChanged(object? sender, nint hwnd)
    {
        if (!_peeking || DateTime.UtcNow - _peekStartedUtc < PeekGrace)
        {
            return;
        }

        try
        {
            if (!IsTaskbarRelated(hwnd))
            {
                EndPeek(rehide: true, $"foreground changed to 0x{hwnd:X}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Taskbar peek: foreground check failed", ex);
        }
    }

    private void OnPeekTimer(object? sender, EventArgs e)
    {
        try
        {
            if (IsCursorOverTaskbar())
            {
                // Still in use: look again shortly instead of pulling it away from under the pointer.
                ThrottledLog.Info("taskbar-peek-recheck", "Taskbar peek: the cursor is over the taskbar; re-checking in 1 s");
                _peekTimer.Interval = PeekRecheck;
                return;
            }

            EndPeek(rehide: true, "timeout");
        }
        catch (Exception ex)
        {
            Log.Warn("Taskbar peek: timeout check failed", ex);
            EndPeek(rehide: true, "timeout");
        }
    }

    private void EndPeek(bool rehide, string reason)
    {
        _peekTimer.Stop();
        if (!_peeking)
        {
            return;
        }

        _peeking = false;
        if (rehide && IsHidden && StillOwnsTaskbar())
        {
            var hidden = TaskbarController.HideWindows();
            Log.Info($"Taskbar peek ended ({reason}); re-hid {hidden} taskbar window(s)");
        }
        else
        {
            Log.Info($"Taskbar peek ended ({reason})");
        }
    }

    /// <summary>
    /// The taskbars themselves and Explorer's tray popups (the notification-area overflow lives in the taskbar's
    /// process). File Explorer windows share that process, so they are excluded by class.
    /// </summary>
    private static bool IsTaskbarRelated(nint hwnd)
    {
        if (hwnd == 0)
        {
            return false;
        }

        if (TaskbarController.IsTaskbarWindow(hwnd))
        {
            return true;
        }

        var tray = TaskbarController.FindExplorerTray();
        return tray != 0
            && NativeMethods.GetProcessId(hwnd) == NativeMethods.GetProcessId(tray)
            && NativeMethods.GetClassName(hwnd) is not ("CabinetWClass" or "ExploreWClass" or "Progman" or "WorkerW");
    }

    private static bool IsCursorOverTaskbar()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return false;
        }

        foreach (var hwnd in TaskbarController.FindTaskbarWindows())
        {
            if (NativeMethods.IsWindowVisible(hwnd) && NativeMethods.GetWindowBounds(hwnd).Contains(point.X, point.Y))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Crash path: plain Win32 and file I/O only (see <see cref="IEmergencyRestore"/>).</summary>
    public void EmergencyRestore()
    {
        if (_mode != TaskbarMode.Untouched)
        {
            _mode = TaskbarMode.Untouched;
            TaskbarController.RestoreFromMarker(_settingsDirectory);
        }
    }

    public void Dispose()
    {
        _rehideTimer.Stop();
        _peekTimer.Stop();
        if (_started)
        {
            _context.Windows.RawWindowEvent -= OnRawWindowEvent;
            _context.Windows.ForegroundChanged -= OnForegroundChanged;
            _context.Commands.TaskbarPeekRequested -= OnPeekRequested;
            _started = false;
        }

        _listener?.Dispose();
        _listener = null;
        if (_mode != TaskbarMode.Untouched)
        {
            SwitchTo(TaskbarMode.Untouched);
        }
    }
}
