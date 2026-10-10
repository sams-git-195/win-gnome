using WinGnome.Core.Shell;
using WinGnome.Infrastructure;
using WinGnome.Interop;
using WinGnome.Services;

namespace WinGnome.Features.Taskbar;

/// <summary>
/// Keeps Explorer's taskbar windows at an empty window region while the taskbar is hidden, so a taskbar Explorer
/// re-shows has nothing to draw (spec 0023). Explorer rewrites the secondary taskbar's region on its own, so the
/// empty region is re-applied on the window events WinGnome already receives: no timer, no polling. Windows are
/// recorded in the restore marker before they are touched. UI thread only, except <see cref="RemoveAll"/> on the crash path.
/// </summary>
internal sealed class TaskbarRegionGuard
{
    private readonly string _settingsDirectory;

    /// <summary>Taskbar windows this guard has looked at, so the busiest event (location changes) is one set lookup.</summary>
    private readonly HashSet<nint> _known = [];
    private readonly Dictionary<nint, int> _applied = [];
    private bool _active;
    private bool _suspended;

    public TaskbarRegionGuard(string settingsDirectory)
    {
        _settingsDirectory = settingsDirectory;
    }

    public bool Active => _active;

    /// <summary>Empties the region of every Explorer taskbar window and starts reacting to window events.</summary>
    public void Start()
    {
        if (_active)
        {
            return;
        }

        _active = true;
        _suspended = false;
        Log.Info($"Taskbar regions emptied on {Apply()} window(s)");
    }

    /// <summary>Stops reacting and gives every window we emptied its region back.</summary>
    public void Stop()
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        _suspended = false;
        var removed = RemoveAll();
        var reapplied = _applied.Values.Sum(count => Math.Max(0, count - 1));
        Log.Info($"Taskbar regions removed from {removed} window(s); re-applied {reapplied} time(s) while active");
        _applied.Clear();
        _known.Clear();
    }

    /// <summary>
    /// Removes our empty regions (the in-memory windows plus those the marker records) and forgets the records.
    /// Plain Win32 and file I/O, so the crash path may call it. Returns how many windows got their region back.
    /// </summary>
    public int RemoveAll()
    {
        var recorded = TaskbarController.ReadEmptiedRegions(_settingsDirectory).Concat(_known.Select(h => (long)h)).Distinct().ToList();
        var removed = TaskbarRegions.RemoveRecorded(recorded);
        TaskbarController.ClearEmptiedRegionRecords(_settingsDirectory);
        return removed;
    }

    /// <summary>
    /// For a peek: removes the regions so the taskbar can be seen, and ignores window events until <see cref="Resume"/>.
    /// The marker keeps the records, so a crash during the peek still has nothing to undo.
    /// </summary>
    public void Suspend()
    {
        if (!_active || _suspended)
        {
            return;
        }

        _suspended = true;
        foreach (var hwnd in _known)
        {
            TaskbarRegions.RemoveIfEmpty(hwnd);
        }
    }

    /// <summary>Ends a <see cref="Suspend"/>; the regions come back with <paramref name="reapply"/>, or on the next re-hide.</summary>
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
    }

    /// <summary>Empties every Explorer taskbar window that is not empty yet (a new secondary taskbar, or after Explorer restarted). Returns how many are empty afterwards.</summary>
    public int Apply()
    {
        if (!_active || _suspended)
        {
            return 0;
        }

        var empty = 0;
        foreach (var hwnd in TaskbarController.FindExplorerTaskbarWindows())
        {
            if (Ensure(hwnd))
            {
                empty++;
            }
        }

        return empty;
    }

    /// <summary>
    /// A window event from the shared tracker. Location changes (what Explorer's own region rewrite raises) are only
    /// looked at for windows already known; shows and creations also find new taskbar windows.
    /// </summary>
    public void OnWindowEvent(uint eventType, nint hwnd)
    {
        if (!_active || _suspended)
        {
            return;
        }

        switch (eventType)
        {
            case WinEventHook.EVENT_OBJECT_LOCATIONCHANGE:
                if (_known.Contains(hwnd))
                {
                    Ensure(hwnd);
                }

                break;

            case WinEventHook.EVENT_OBJECT_SHOW or WinEventHook.EVENT_OBJECT_CREATE:
                Ensure(hwnd);
                break;

            case WinEventHook.EVENT_OBJECT_DESTROY:
                _known.Remove(hwnd);
                break;
        }
    }

    /// <summary>Makes sure one window has the empty region; returns whether it has it afterwards.</summary>
    private bool Ensure(nint hwnd)
    {
        if (!_known.Contains(hwnd) && !TaskbarController.IsExplorerTaskbarWindow(hwnd))
        {
            return false;
        }

        if (!TaskbarRegionPolicy.NeedsEmptying(TaskbarRegions.GetRegionType(hwnd)))
        {
            _known.Add(hwnd);
            return true;
        }

        // Recorded first: if the marker cannot say so, nothing could undo the region after a crash.
        if (!TaskbarController.RecordEmptiedRegion(_settingsDirectory, hwnd))
        {
            ThrottledLog.Warn("taskbar-region-record", "Not emptying a taskbar window region: the restore marker could not record it");
            return false;
        }

        _known.Add(hwnd);
        if (!TaskbarRegions.TryEmpty(hwnd))
        {
            return false;
        }

        var count = _applied.GetValueOrDefault(hwnd) + 1;
        _applied[hwnd] = count;
        if (count > 1)
        {
            ThrottledLog.Info("taskbar-region-reapplied", $"Explorer changed the region of taskbar window 0x{hwnd:X}; emptied it again (time {count - 1})");
        }

        return true;
    }
}
