using System.Text;
using System.Windows.Threading;
using Microsoft.Win32;
using WinGnome.Core.Geometry;
using WinGnome.Core.Monitors;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Services;

/// <summary>A display change: the layout before and after, and what differs.</summary>
internal sealed class DisplayLayoutChangedEventArgs(MonitorLayout previous, MonitorLayout current, MonitorLayoutDiff diff) : EventArgs
{
    public MonitorLayout Previous { get; } = previous;

    public MonitorLayout Current { get; } = current;

    public MonitorLayoutDiff Diff { get; } = diff;
}

/// <summary>
/// The one place that reads the monitors. Display events (resolution, arrangement, hot-plug, resume, unlock, DPI
/// changes reported by our windows, and AppBars that detached themselves) restart one 250 ms one-shot timer; on
/// its tick the layout is read, diffed and <see cref="LayoutChanged"/> raised when something changed. A pass that
/// found changes is followed by one more 1.5 s later, always raised, because Windows settles some layouts late
/// (hot-plug) and Explorer may reset work areas after the first pass. No timer runs at idle.
/// </summary>
internal sealed class DisplayLayoutService : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FollowUpDelay = TimeSpan.FromMilliseconds(1500);

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private Dictionary<nint, string> _keys;
    private bool _forced;
    private bool _nextIsFollowUp;
    private bool _disposed;

    public DisplayLayoutService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _timer.Tick += OnTick;
        Current = Read(out _keys);
        Log.Info($"Displays: {Describe(Current)}");

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    /// <summary>Raised on the dispatcher after a display change, or after a forced <see cref="Invalidate"/>.</summary>
    public event EventHandler<DisplayLayoutChangedEventArgs>? LayoutChanged;

    /// <summary>The layout as of the last pass.</summary>
    public MonitorLayout Current { get; private set; }

    /// <summary>
    /// Schedules a re-read (any thread). With <paramref name="force"/> the pass raises <see cref="LayoutChanged"/>
    /// even when the layout looks unchanged, so owners of detached AppBars always get a reconcile pass.
    /// </summary>
    public void Invalidate(bool force = false)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => Invalidate(force));
            return;
        }

        if (_disposed)
        {
            return;
        }

        _forced |= force;
        _nextIsFollowUp = false;
        Restart(SettleDelay);
    }

    /// <summary>
    /// Schedules forced passes 1.5 s and 10 s from now (then nothing). Call once after every feature has started:
    /// hiding the Windows taskbar makes Explorer recompute work areas asynchronously after our bars docked, and it
    /// does not always notify them, so the owners re-check their strips at those points.
    /// </summary>
    public void VerifyAfterStart()
    {
        var delays = new Queue<TimeSpan>([TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(8.5)]);
        var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = delays.Dequeue() };
        timer.Tick += (_, _) =>
        {
            Invalidate(force: true);
            if (delays.Count > 0 && !_disposed)
            {
                timer.Interval = delays.Dequeue();
            }
            else
            {
                timer.Stop();
            }
        };
        timer.Start();
    }

    /// <summary>
    /// The key of the monitor nearest to <paramref name="hwnd"/>, or null when even a direct read fails (callers treat
    /// that as the primary). HMONITORs are not stable across display changes, so a handle missing from the last read
    /// is read directly and triggers a new pass.
    /// </summary>
    public string? MonitorKeyOf(nint hwnd)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (_keys.TryGetValue(monitor, out var key))
        {
            return key;
        }

        Invalidate();
        return NativeMethods.GetMonitorInfoEx(monitor)?.DeviceName;
    }

    /// <summary>Reads every monitor now (a few microseconds; no blocking calls).</summary>
    public static MonitorLayout Read() => Read(out _);

    /// <summary>One line per monitor for the log: key, bounds, work area and DPI.</summary>
    public static string Describe(MonitorLayout layout)
    {
        if (layout.Monitors.Count == 0)
        {
            return "no monitors";
        }

        var text = new StringBuilder();
        foreach (var m in layout.Monitors)
        {
            text.Append(text.Length == 0 ? "" : "; ")
                .Append(m.Key).Append(m.IsPrimary ? " (primary)" : "")
                .Append(" bounds ").Append(Format(m.Bounds))
                .Append(" work ").Append(Format(m.WorkArea))
                .Append(" dpi ").Append(m.Dpi);
        }

        return text.ToString();
    }

    private static string Format(PixelRect r) => $"{r.Left},{r.Top},{r.Right},{r.Bottom}";

    private static MonitorLayout Read(out Dictionary<nint, string> keys)
    {
        var handles = new List<nint>();
        if (!NativeMethods.EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
            {
                handles.Add(monitor);
                return true;
            }, 0))
        {
            Log.Warn("EnumDisplayMonitors failed");
        }

        keys = [];
        var monitors = new List<MonitorInfo>();
        foreach (var handle in handles)
        {
            if (NativeMethods.GetMonitorInfoEx(handle) is not { } info)
            {
                continue;
            }

            var dpi = NativeMethods.GetDpiForMonitor(handle, 0, out var dpiX, out _) == 0 && dpiX > 0 ? (int)dpiX : 96;
            var name = info.DeviceName;
            keys[handle] = name;
            monitors.Add(new MonitorInfo(name, info.rcMonitor.ToPixelRect(), info.rcWork.ToPixelRect(), dpi,
                (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));
        }

        return MonitorLayout.Create(monitors);
    }

    private void Restart(TimeSpan delay)
    {
        _timer.Stop();
        _timer.Interval = delay;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        var followUp = _nextIsFollowUp;
        _nextIsFollowUp = false;

        var now = Read(out var keys);
        if (now.Monitors.Count == 0)
        {
            // A read between unplugging and the next monitor appearing, or while the session is switching: keep the
            // last layout (destroying every bar would be worse) and look again.
            Log.Warn("Displays: no monitors read; keeping the previous layout");
            if (!followUp)
            {
                _nextIsFollowUp = true;
                Restart(FollowUpDelay);
            }

            return;
        }

        var previous = Current;
        var diff = MonitorLayoutDiff.Compute(previous, now);
        Current = now;
        _keys = keys;
        var forced = _forced;
        _forced = false;
        if (diff.IsEmpty && !forced)
        {
            return;
        }

        Log.Info($"Displays {(diff.IsEmpty ? "re-checked" : "changed")}{(followUp ? " (follow-up)" : "")}: removed {diff.Removed.Count}, changed {diff.Changed.Count}, added {diff.Added.Count}. {Describe(now)}");
        LayoutChanged?.Invoke(this, new DisplayLayoutChangedEventArgs(previous, now, diff));
        if (!diff.IsEmpty && !followUp && !_timer.IsEnabled)
        {
            // Forced: even when the layout has settled by then, owners re-check that their strips are still reserved
            // (Explorer recomputes work areas after a topology change on its own schedule).
            _nextIsFollowUp = true;
            _forced = true;
            Restart(FollowUpDelay);
        }
    }

    // SystemEvents raise on their own thread; Invalidate hops to the dispatcher.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Invalidate();

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        // Monitors may have been rearranged (docking station) while the machine slept.
        if (e.Mode == PowerModes.Resume)
        {
            Invalidate();
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
        {
            Invalidate();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // SystemEvents is static: forgotten handlers would keep this service alive and keep firing.
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
