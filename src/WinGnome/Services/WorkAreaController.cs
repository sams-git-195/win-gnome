using System.IO;
using System.Runtime.InteropServices;
using WinGnome.Core.Geometry;
using WinGnome.Core.Shell;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Services;

/// <summary>
/// Sets monitor work areas directly with the documented <c>SystemParametersInfo(SPI_SETWORKAREA)</c>, as a fallback
/// for the strips Explorer grants our AppBars but does not apply: it defers its work-area recompute while its taskbar
/// is auto-hidden and <c>SW_HIDE</c>n (measured ~35 s), and can skip it entirely after a monitor was unplugged, so
/// maximised windows would cover the bar. Spec 0010, outcome B.
/// </summary>
/// <remarks>
/// Static and plain Win32, with one lock spanning read-compute-write, so the crash path can use it from any thread and
/// two bars on one monitor can stack instead of overwriting each other. Everything is bounded: <see cref="WorkAreaBudget"/>
/// per monitor, <see cref="StripRecovery"/> per bar. A change is recorded in <c>workareas.state</c> before it is made
/// ("no record, no shrink", like the taskbar marker), so a force-kill can be recovered on the next start. Safe mode
/// never shrinks; it does recover, which is a repair of our own earlier change.
/// </remarks>
internal static class WorkAreaController
{
    private const string MarkerFileName = "workareas.state";

    private static readonly object Gate = new();
    private static readonly List<WorkAreaRecord> Records = [];
    private static readonly Dictionary<string, WorkAreaBudget> Budgets = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<long> Released = [];

    private static string? _directory;
    private static bool _enabled;

    /// <summary>Called once at start, before any feature docks a bar.</summary>
    /// <param name="enabled">False in safe mode, which changes no system state.</param>
    public static void Initialize(string settingsDirectory, bool enabled)
    {
        lock (Gate)
        {
            _directory = settingsDirectory;
            _enabled = enabled;
            Records.Clear();
            Released.Clear();
            Budgets.Clear();
        }
    }

    /// <summary>Whether this run may set a work area directly (a bar that may not re-registers its AppBar instead).</summary>
    public static bool CanShrink
    {
        get
        {
            lock (Gate)
            {
                return _enabled;
            }
        }
    }

    /// <summary>
    /// Reserves <paramref name="strip"/> on <paramref name="monitor"/> by moving only that edge of a freshly read work
    /// area, so the taskbar's and every other AppBar's strip survives. Returns false without writing when shrinking is
    /// not allowed, the monitor is gone or changed, the strip is already reserved, the monitor's budget is spent, or
    /// the record could not be written first.
    /// </summary>
    /// <param name="owner">The bar's HWND; identifies the record when the bar undocks.</param>
    public static bool TryShrink(nint owner, PixelRect monitor, Core.Shell.AppBarEdge edge, PixelRect strip)
    {
        try
        {
            lock (Gate)
            {
                if (!_enabled || _directory is null)
                {
                    return false;
                }

                var handle = NativeMethods.MonitorFromRect(RECT.From(monitor), NativeMethods.MONITOR_DEFAULTTONULL);
                if (handle == 0 || NativeMethods.GetMonitorInfoEx(handle) is not { } info)
                {
                    return false; // A display change is on its way; the coordinator's pass re-docks the bar.
                }

                var key = info.DeviceName;
                var bounds = info.rcMonitor.ToPixelRect();
                if (bounds != monitor)
                {
                    return false; // Stale rectangle: shrinking here could take a strip on the wrong monitor.
                }

                var workArea = info.rcWork.ToPixelRect();
                if (WorkAreaFallback.Shrink(edge, strip, workArea) is not { } shrunk)
                {
                    return false; // Already reserved (the common case): one GetMonitorInfo and nothing written.
                }

                if (!Budget(key).TrySpend(Environment.TickCount64))
                {
                    Log.Warn($"Not setting {key}'s work area directly: {WorkAreaBudget.MaxApplications} applications in the last {WorkAreaBudget.WindowMs / 1000} s already");
                    return false;
                }

                var record = new WorkAreaRecord(owner, key, bounds, workArea, shrunk);
                Records.Add(record);
                Released.Remove(owner); // An HWND can be recycled: whoever shrinks now is live again.
                if (!WriteMarker())
                {
                    Records.Remove(record);
                    Log.Warn($"Not setting {key}'s work area directly because its record could not be written");
                    return false;
                }

                if (!SetWorkArea(shrunk, broadcast: true))
                {
                    Records.Remove(record);
                    WriteMarker();
                    Log.Warn($"SPI_SETWORKAREA failed for {key} (error {Marshal.GetLastWin32Error()})");
                    return false;
                }

                Log.Info($"{key}: work area set directly for the {edge} strip {Format(strip)}: {Format(workArea)} -> {Format(shrunk)}");
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("The work-area fallback failed", ex);
            return false;
        }
    }

    /// <summary>A bar undocked: give back what it asked us to shrink, newest first, while it is still the live value.</summary>
    public static void Release(nint owner)
    {
        try
        {
            lock (Gate)
            {
                if (Records.Count == 0)
                {
                    return;
                }

                Released.Add(owner);
                Apply(broadcast: true, nudge: true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not give a work area back", ex);
        }
    }

    /// <summary>
    /// Crash path (any thread): releases every record. Nothing is broadcast, because <c>SPIF_SENDCHANGE</c> sends
    /// synchronously to every top-level window and one hung window would block the crash path; the work area changes
    /// all the same. No nudge either: the next start's janitor makes one.
    /// </summary>
    public static void ReleaseAll()
    {
        try
        {
            lock (Gate)
            {
                if (Records.Count == 0)
                {
                    return;
                }

                foreach (var record in Records)
                {
                    Released.Add(record.Owner);
                }

                Apply(broadcast: false, nudge: false);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not give the work areas back", ex);
        }
    }

    /// <summary>
    /// Gives back the work areas an earlier run set directly and never restored (it crashed or was killed). Run at
    /// start after the taskbar restore and before the AppBar janitor, and on <c>--restore-taskbar</c>.
    /// </summary>
    public static void RecoverFromMarker(string settingsDirectory)
    {
        try
        {
            var path = MarkerPath(settingsDirectory);
            if (!File.Exists(path))
            {
                return;
            }

            string? json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn("Could not read the work-area marker; leaving it for the next start", ex);
                return;
            }

            var file = WorkAreaState.Parse(json);
            if (file.Unreadable)
            {
                // The shrinks it described may still be in effect, so silently reading it as empty would strand them.
                Log.Warn($"The work-area marker {path} is unreadable; repairing what can be repaired");
                RepairWithoutRecords(settingsDirectory);
                Delete(path);
                return;
            }

            if (file.Records.Count == 0)
            {
                Delete(path);
                return;
            }

            lock (Gate)
            {
                _directory = settingsDirectory;
                Records.Clear();
                Released.Clear();
                Records.AddRange(file.Records);
                foreach (var record in Records)
                {
                    Released.Add(record.Owner); // The process that wrote them is gone.
                }

                Log.Info($"Recovering {Records.Count} work area(s) a previous run set directly");
                Apply(broadcast: true, nudge: false); // The janitor nudges right after this.
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not recover the work areas from the marker", ex);
        }
    }

    /// <summary>Writes the plan's restores, keeps what a live bar still owns, and nudges Explorer when asked to.</summary>
    private static void Apply(bool broadcast, bool nudge)
    {
        var plan = WorkAreaRecovery.Plan(Records, DisplayLayoutService.Read(), Released);
        foreach (var restore in plan.Restores)
        {
            if (SetWorkArea(restore.WorkArea, broadcast))
            {
                Log.Info($"{restore.Key}: work area given back: {Format(restore.WorkArea)}");
            }
            else
            {
                Log.Warn($"SPI_SETWORKAREA failed while giving {restore.Key}'s work area back (error {Marshal.GetLastWin32Error()})");
            }
        }

        // Keep is a subset of Records by position, so an equal count means nothing was restored or dropped.
        if (plan.Restores.Count > 0 || plan.Keep.Count != Records.Count)
        {
            Records.Clear();
            Records.AddRange(plan.Keep);
            WriteMarker();
        }

        if (nudge && plan.Nudge)
        {
            Log.Info("A work area we set is no longer the live one; asking Explorer to recompute");
            AppBarJanitor.Nudge();
        }
    }

    /// <summary>
    /// The marker exists but cannot be trusted, so we do not know which work areas we changed. Where the taskbar
    /// marker says the taskbar is hidden or auto-hidden, every monitor's correct work area is its full bounds; where
    /// it does not, a visible taskbar's own strip must survive and nothing is written. The janitor nudges Explorer
    /// right after, which brings back any other AppBar's strip.
    /// </summary>
    private static void RepairWithoutRecords(string settingsDirectory)
    {
        if (!TaskbarController.HasMarker(settingsDirectory))
        {
            Log.Info("The taskbar is not hidden, so no work area is reset");
            return;
        }

        foreach (var monitor in DisplayLayoutService.Read().Monitors)
        {
            if (monitor.WorkArea == monitor.Bounds)
            {
                continue;
            }

            if (SetWorkArea(monitor.Bounds, broadcast: true))
            {
                Log.Info($"{monitor.Key}: work area reset to the full monitor {Format(monitor.Bounds)} (the taskbar is hidden)");
            }
            else
            {
                Log.Warn($"SPI_SETWORKAREA failed while resetting {monitor.Key}'s work area (error {Marshal.GetLastWin32Error()})");
            }
        }
    }

    private static bool SetWorkArea(PixelRect workArea, bool broadcast)
    {
        var rect = RECT.From(workArea);

        // SPI_SETWORKAREA takes the monitor from the rectangle, in physical pixels. The wParam of the broadcast it
        // triggers is the same value as the action, hence the cast of the existing constant.
        return NativeMethods.SystemParametersInfoRect((uint)NativeMethods.SPI_SETWORKAREA, 0, ref rect,
            broadcast ? NativeMethods.SPIF_SENDCHANGE : 0);
    }

    private static WorkAreaBudget Budget(string key)
    {
        if (!Budgets.TryGetValue(key, out var budget))
        {
            budget = new WorkAreaBudget();
            Budgets[key] = budget;
        }

        return budget;
    }

    /// <summary>Atomically rewrites the marker, or deletes it when no record is left.</summary>
    private static bool WriteMarker()
    {
        if (_directory is null)
        {
            return false;
        }

        var path = MarkerPath(_directory);
        if (Records.Count == 0)
        {
            return Delete(path);
        }

        // Written aside and moved over the old one: a marker truncated by a crash would strand the shrinks it held.
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(temp, WorkAreaState.Serialize(Records));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not write the work-area marker", ex);
            return false;
        }
    }

    private static bool Delete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not delete the work-area marker; the next start recovers it", ex);
            return false;
        }
    }

    private static string MarkerPath(string directory) => Path.Combine(directory, MarkerFileName);

    private static string Format(PixelRect rect) => $"{rect.Left},{rect.Top},{rect.Right},{rect.Bottom}";
}
