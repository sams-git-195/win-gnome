namespace WinGnome.Core.Shell;

/// <summary>The window events the manager reacts to.</summary>
public enum TaskbarRegionEvent
{
    /// <summary>A window moved or resized (what Explorer's own region rewrite raises).</summary>
    LocationChange,

    /// <summary>A window was shown or created.</summary>
    ShowOrCreate,

    /// <summary>A window was destroyed; its handle may be reused.</summary>
    Destroy,
}

/// <summary>What a sweep removed, and the live Explorer taskbar windows whose empty region could not be removed.</summary>
public readonly record struct TaskbarRegionSweep(int Removed, IReadOnlyList<long> Remaining);

/// <summary>
/// Keeps Explorer's taskbar windows at an empty window region while the taskbar is hidden (spec 0023), over
/// <see cref="ITaskbarRegionHost"/>. The set of known windows is only a pre-filter for the busy location-change event:
/// every region change is preceded by a fresh <see cref="ITaskbarRegionHost.IsExplorerTaskbarWindow"/> check, because a
/// handle can be reused by another process once Explorer's window is gone. Windows are recorded in the restore marker
/// before they are emptied, once each. A <see cref="TaskbarRegionBreaker"/> ends the management for the session when
/// Explorer and we keep overwriting each other. UI thread only; <see cref="Sweep"/> is also safe for the crash path.
/// </summary>
public sealed class TaskbarRegionManager
{
    private readonly ITaskbarRegionHost _host;
    private readonly Func<DateTime> _clock;
    private readonly TaskbarRegionBreaker _breaker = new();
    private readonly HashSet<long> _known = [];
    private readonly HashSet<long> _recorded = [];
    private readonly HashSet<long> _hungLogged = [];
    private readonly Dictionary<long, int> _applied = [];
    private bool _suspended;

    public TaskbarRegionManager(ITaskbarRegionHost host, Func<DateTime> clock)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool Active { get; private set; }

    /// <summary>True once the breaker has ended the management for this session; <see cref="Start"/> does nothing then.</summary>
    public bool Tripped { get; private set; }

    /// <summary>Empties the region of every Explorer taskbar window and starts reacting to events.</summary>
    public void Start()
    {
        if (Active || Tripped)
        {
            return;
        }

        Active = true;
        _suspended = false;
        _recorded.Clear();
        foreach (var handle in _host.ReadRecords())
        {
            _recorded.Add(handle);
        }

        _host.Info($"Taskbar regions emptied on {Apply()} window(s)");
    }

    /// <summary>Stops reacting and gives every window we emptied its region back.</summary>
    public void Stop()
    {
        if (!Active)
        {
            return;
        }

        Active = false;
        _suspended = false;
        var removed = RemoveAll();
        var reapplied = _applied.Values.Sum(count => Math.Max(0, count - 1));
        _host.Info($"Taskbar regions removed from {removed} window(s); re-applied {reapplied} time(s) while active");
        _applied.Clear();
        _known.Clear();
        _hungLogged.Clear();
        _breaker.Reset();
    }

    /// <summary>
    /// Removes our empty regions: the known windows, the marker's record, and any other live Explorer taskbar window
    /// with an empty region. A window whose region could not be removed stays recorded. Returns how many were removed.
    /// </summary>
    public int RemoveAll()
    {
        var result = Sweep(_host, _host.ReadRecords().Concat(_known).Concat(_recorded));
        _host.ReplaceRecords(result.Remaining);
        _recorded.Clear();
        foreach (var handle in result.Remaining)
        {
            _recorded.Add(handle);
        }

        return result.Removed;
    }

    /// <summary>
    /// Removes the empty region from every live Explorer taskbar window that has one: the <paramref name="recorded"/>
    /// windows (see <see cref="TaskbarRegionPolicy.RegionsToClear"/>) and any other found. Explorer never sets an
    /// empty region, so one we did not record is still not Explorer's; erring towards a visible taskbar is the safe
    /// side. Only the host's Win32 calls are used, so it may run on the crash path.
    /// </summary>
    public static TaskbarRegionSweep Sweep(ITaskbarRegionHost host, IEnumerable<long> recorded)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(recorded);
        var candidates = recorded.Concat(host.FindExplorerTaskbarWindows()).Distinct().ToList();
        var present = new Dictionary<long, int>();
        var remaining = new List<long>();
        foreach (var handle in candidates)
        {
            if (!host.IsExplorerTaskbarWindow(handle))
            {
                continue;
            }

            if (host.IsHung(handle))
            {
                remaining.Add(handle);
                continue;
            }

            present[handle] = host.GetRegionType(handle);
        }

        var removed = 0;
        foreach (var handle in TaskbarRegionPolicy.RegionsToClear(candidates, present))
        {
            if (host.RemoveIfEmpty(handle))
            {
                removed++;
            }
            else
            {
                remaining.Add(handle);
            }
        }

        return new TaskbarRegionSweep(removed, remaining);
    }

    /// <summary>
    /// For a peek: removes the regions so the taskbar can be seen, and ignores window events (but not destroys)
    /// until <see cref="Resume"/>. The marker keeps the records.
    /// </summary>
    public void Suspend()
    {
        if (!Active || _suspended)
        {
            return;
        }

        _suspended = true;
        foreach (var handle in _known.ToList())
        {
            if (!_host.IsExplorerTaskbarWindow(handle))
            {
                _known.Remove(handle);
            }
            else if (!IsHung(handle))
            {
                _host.RemoveIfEmpty(handle);
            }
        }
    }

    /// <summary>
    /// Ends a <see cref="Suspend"/>. With <paramref name="reapply"/> the regions come back now; without it the known
    /// windows are forgotten (Explorer may have restarted) and the next apply finds them again.
    /// </summary>
    public void Resume(bool reapply)
    {
        if (!_suspended)
        {
            return;
        }

        _suspended = false;
        if (reapply)
        {
            Apply();
        }
        else
        {
            _known.Clear();
        }
    }

    /// <summary>Empties every Explorer taskbar window that is not empty yet. Returns how many are empty afterwards.</summary>
    public int Apply()
    {
        if (!Active || _suspended)
        {
            return 0;
        }

        var empty = 0;
        foreach (var handle in _host.FindExplorerTaskbarWindows())
        {
            if (Ensure(handle))
            {
                empty++;
            }

            if (!Active)
            {
                break;
            }
        }

        return empty;
    }

    /// <summary>A window event. Destroys are always honoured, so a reused handle is never taken for a known window.</summary>
    public void OnWindowEvent(TaskbarRegionEvent kind, long hwnd)
    {
        if (kind == TaskbarRegionEvent.Destroy)
        {
            _known.Remove(hwnd);
            return;
        }

        if (!Active || _suspended)
        {
            return;
        }

        if (kind == TaskbarRegionEvent.ShowOrCreate || _known.Contains(hwnd))
        {
            Ensure(hwnd);
        }
    }

    private bool Ensure(long hwnd)
    {
        // Always re-checked: the known set may hold a handle that is now another process's window.
        if (!_host.IsExplorerTaskbarWindow(hwnd))
        {
            _known.Remove(hwnd);
            return false;
        }

        if (IsHung(hwnd))
        {
            return false;
        }

        if (!TaskbarRegionPolicy.NeedsEmptying(_host.GetRegionType(hwnd)))
        {
            _known.Add(hwnd);
            return true;
        }

        // Recorded first, and only once per window: if the marker cannot say so, nothing could undo the region after a crash.
        if (!_recorded.Contains(hwnd))
        {
            if (!_host.Record(hwnd))
            {
                _host.ThrottledWarn("taskbar-region-record", "Not emptying a taskbar window region: the restore marker could not record it");
                return false;
            }

            _recorded.Add(hwnd);
        }

        _known.Add(hwnd);
        if (_breaker.RecordApply(hwnd, _clock()))
        {
            Tripped = true;
            _host.Warn($"Explorer keeps rewriting the region of taskbar window 0x{hwnd:X}; no longer emptying taskbar regions this session");
            Stop();
            return false;
        }

        if (!_host.TryEmpty(hwnd))
        {
            return false;
        }

        var count = _applied.GetValueOrDefault(hwnd) + 1;
        _applied[hwnd] = count;
        if (count > 1)
        {
            _host.ThrottledInfo("taskbar-region-reapplied", $"Explorer changed the region of taskbar window 0x{hwnd:X}; emptied it again (time {count - 1})");
        }

        return true;
    }

    private bool IsHung(long hwnd)
    {
        if (!_host.IsHung(hwnd))
        {
            return false;
        }

        if (_hungLogged.Add(hwnd))
        {
            _host.Warn($"Taskbar window 0x{hwnd:X} is not responding; leaving its region alone");
        }

        return true;
    }
}
