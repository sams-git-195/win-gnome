namespace WinGnome.Core.Dock;

/// <summary>
/// Keeps a dock's running apps, and the windows inside each app, in the order they first appeared, however the
/// window tracker's z-order shuffles. One instance per dock: it remembers what that dock has shown.
/// </summary>
/// <remarks>
/// A key (an unpinned app, or a window of an app) gets a sequence number the first time it is seen and keeps it while
/// it stays present. A key that goes missing is remembered for <see cref="ForgetAfterMs"/> so an app that swaps a
/// splash window for its main window, or comes back after Explorer restarts, keeps its slot; after that it is
/// forgotten and is appended at the end if it returns. Time is passed in, never read.
/// </remarks>
public sealed class DockRunningOrder
{
    /// <summary>How long a missing app or window keeps its slot.</summary>
    public const long ForgetAfterMs = 2000;

    private readonly Dictionary<string, Slot> _apps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Slot> _windows = new(StringComparer.OrdinalIgnoreCase);
    private long _nextSequence;

    /// <summary>
    /// The key that identifies <paramref name="app"/> in this class and in the dock's entry list:
    /// <c>pin:</c> + launch id for pinned apps, <c>run:</c> + identity for unpinned ones. Compare ordinal-ignore-case.
    /// </summary>
    public static string KeyOf(DockApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.IsPinned ? "pin:" + app.LaunchId : "run:" + app.Identity;
    }

    /// <summary>
    /// Pinned apps in input order, then unpinned apps in first-appearance order; each app's windows in
    /// first-appearance order. Every other field of every app, <see cref="DockApp.MostRecentWindow"/> included, is
    /// copied unchanged. New apps and windows seen in one call are numbered in input order.
    /// </summary>
    /// <param name="apps">The dock model for this refresh, in whatever order the builder produced.</param>
    /// <param name="nowMs">A monotonic clock reading in milliseconds, for example <c>Environment.TickCount64</c>.</param>
    public IReadOnlyList<DockApp> Apply(IReadOnlyList<DockApp> apps, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(apps);

        var seenApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenWindows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<(DockApp App, long Sequence)>(apps.Count);

        foreach (var app in apps)
        {
            var key = KeyOf(app);
            seenApps.Add(key);
            var sequence = Touch(_apps, key, nowMs);
            var windows = app.Windows
                .Select(handle => (Handle: handle, Sequence: Touch(_windows, WindowKey(key, handle), nowMs)))
                .OrderBy(w => w.Sequence)
                .Select(w => w.Handle)
                .ToList();
            foreach (var handle in app.Windows)
            {
                seenWindows.Add(WindowKey(key, handle));
            }

            ordered.Add((app with { Windows = windows }, sequence));
        }

        Forget(_apps, seenApps, nowMs);
        Forget(_windows, seenWindows, nowMs);

        return ordered.Where(e => e.App.IsPinned).Select(e => e.App)
            .Concat(ordered.Where(e => !e.App.IsPinned).OrderBy(e => e.Sequence).Select(e => e.App))
            .ToList();
    }

    /// <summary>The key's sequence number, issuing a new one when it is unknown or was gone past the grace.</summary>
    private long Touch(Dictionary<string, Slot> slots, string key, long nowMs)
    {
        if (!slots.TryGetValue(key, out var slot) || IsExpired(slot, nowMs))
        {
            slot = new Slot(_nextSequence++);
            slots[key] = slot;
        }

        slot.AbsentSinceMs = null;
        return slot.Sequence;
    }

    /// <summary>
    /// Starts the clock for keys that just went missing (never restarting it for ones already missing, so a long
    /// absence can't be stretched by later refreshes) and drops keys missing for longer than the grace.
    /// </summary>
    private static void Forget(Dictionary<string, Slot> slots, HashSet<string> present, long nowMs)
    {
        foreach (var (key, slot) in slots.ToList())
        {
            if (present.Contains(key))
            {
                continue;
            }

            slot.AbsentSinceMs ??= nowMs;
            if (IsExpired(slot, nowMs))
            {
                slots.Remove(key);
            }
        }
    }

    private static bool IsExpired(Slot slot, long nowMs) => nowMs - slot.AbsentSinceMs > ForgetAfterMs;

    private static string WindowKey(string appKey, nint handle) => appKey + "\0" + handle;

    private sealed class Slot(long sequence)
    {
        public long Sequence { get; } = sequence;

        public long? AbsentSinceMs { get; set; }
    }
}
