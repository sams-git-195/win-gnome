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
    private TaskbarCreatedListener? _listener;

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
        EndPeek(rehide: false);
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
            ScheduleRehide(RehideDelay);
        }
    }

    private void OnTaskbarCreated()
    {
        if (_mode == TaskbarMode.Untouched)
        {
            return;
        }

        Log.Info("Explorer recreated the taskbar; applying the taskbar mode again");
        EndPeek(rehide: false);
        _recreated = true;
        _rehideTimer.Stop();
        ScheduleRehide(RecreatedDelay);
    }

    private void ScheduleRehide(TimeSpan delay)
    {
        // Start-if-idle (not restart): repeated show events must not postpone the re-hide indefinitely.
        if (!_rehideTimer.IsEnabled)
        {
            _rehideTimer.Interval = delay;
            _rehideTimer.Start();
        }
    }

    private void OnRehideTimer(object? sender, EventArgs e)
    {
        _rehideTimer.Stop();
        if (_mode == TaskbarMode.Untouched || _peeking)
        {
            return;
        }

        try
        {
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
                TaskbarController.HideWindows();
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
            return;
        }

        try
        {
            _rehideTimer.Stop();
            _peeking = true;
            _peekStartedUtc = DateTime.UtcNow;
            TaskbarController.ShowWindows();

            var primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (primary != 0)
            {
                NativeMethods.SetWindowPos(primary, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

                // The taskbar is in auto-hide mode, so showing its window leaves it parked off-screen. Giving it the
                // focus (what Win+T does) is the reliable way to make an auto-hide taskbar slide in.
                WindowActivator.Activate(primary);
            }

            _peekTimer.Interval = PeekTimeout;
            _peekTimer.Start();
        }
        catch (Exception ex)
        {
            Log.Warn("Could not reveal the taskbar", ex);
            EndPeek(rehide: true);
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
                EndPeek(rehide: true);
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
                _peekTimer.Interval = PeekRecheck;
                return;
            }

            EndPeek(rehide: true);
        }
        catch (Exception ex)
        {
            Log.Warn("Taskbar peek: timeout check failed", ex);
            EndPeek(rehide: true);
        }
    }

    private void EndPeek(bool rehide)
    {
        _peekTimer.Stop();
        if (!_peeking)
        {
            return;
        }

        _peeking = false;
        if (rehide && IsHidden)
        {
            TaskbarController.HideWindows();
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

        var tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
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
