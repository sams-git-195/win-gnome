using WinGnome.Core.Geometry;
using WinGnome.Core.Monitors;

namespace WinGnome.Core.Shell;

/// <summary>One work area to write back, and the monitor it belongs to (for the log).</summary>
public readonly record struct WorkAreaRestore(string Key, PixelRect WorkArea);

/// <summary>
/// What giving the work areas back needs: the writes to make, in order, the records that must stay (a live bar still
/// owns a newer one, or the chain is blocked), and whether Explorer should be nudged to recompute because a value
/// WinGnome wrote is no longer the live one and nothing of ours could be undone.
/// </summary>
public sealed record WorkAreaPlan(IReadOnlyList<WorkAreaRestore> Restores, IReadOnlyList<WorkAreaRecord> Keep, bool Nudge)
{
    public static WorkAreaPlan Nothing { get; } = new([], [], false);
}

/// <summary>
/// Decides how to give back work areas WinGnome set directly. Records for one monitor form a chain: each was computed
/// from the work area the previous one left, so they unwind newest first, and only while each record's
/// <see cref="WorkAreaRecord.Applied"/> is still the live value. That ordering is what lets a top bar's and a bottom
/// dock's strips on one monitor both go back; restoring the older one first would leave the newer strip behind.
/// </summary>
public static class WorkAreaRecovery
{
    /// <param name="records">Every record, in application order.</param>
    /// <param name="monitors">A fresh read of the monitors.</param>
    /// <param name="released">The owners whose bar has undocked (after a force-kill, every owner in the marker).</param>
    public static WorkAreaPlan Plan(IReadOnlyList<WorkAreaRecord> records, MonitorLayout monitors, IReadOnlySet<long> released)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(released);

        if (records.Count == 0)
        {
            return WorkAreaPlan.Nothing;
        }

        if (monitors.Monitors.Count == 0)
        {
            // A read that failed, or the moment between unplugging the last monitor and the next one: keep everything
            // rather than drop the only record of a change we made.
            return new([], records, false);
        }

        var restores = new List<WorkAreaRestore>();
        var kept = new bool[records.Count];
        var nudge = false;
        foreach (var group in records.Select((record, index) => (record, index)).GroupBy(x => x.record.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (monitors.Find(group.Key) is not { } monitor)
            {
                continue; // The monitor is gone; its work area went with it.
            }

            var expected = monitor.WorkArea;
            var blocked = false;
            foreach (var (record, index) in group.OrderByDescending(x => x.index))
            {
                if (blocked)
                {
                    kept[index] = true;
                    continue;
                }

                if (record.Bounds != monitor.Bounds)
                {
                    continue; // Stale: a mode change reset this work area.
                }

                if (record.Applied == expected)
                {
                    if (!released.Contains(record.Owner))
                    {
                        // A live bar still owns this value; nothing older can be undone until it lets go.
                        kept[index] = true;
                        blocked = true;
                        continue;
                    }

                    restores.Add(new WorkAreaRestore(group.Key, record.Original));
                    expected = record.Original;
                    continue;
                }

                if (record.Original == expected)
                {
                    continue; // Already given back, by Explorer or by whoever wrote last.
                }

                // Neither the value we wrote nor the one we replaced: someone else's strip is in effect, so writing
                // our original would take it away. Leave it and let Explorer re-check its registrations.
                nudge = true;
            }
        }

        var keep = records.Where((_, index) => kept[index]).ToList();
        return new(restores, keep, nudge);
    }

    /// <summary>
    /// The repair for a marker that cannot be trusted, so nobody knows which work areas were changed. Where the
    /// taskbar is hidden or auto-hidden, every monitor's correct work area is its full bounds, so each monitor that
    /// differs is reset; where the taskbar is visible its own strip must survive, so nothing is written. Either way
    /// the caller nudges Explorer afterwards, which brings back any other AppBar's strip.
    /// </summary>
    public static IReadOnlyList<WorkAreaRestore> RepairAll(MonitorLayout monitors, bool taskbarHidden)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (!taskbarHidden)
        {
            return [];
        }

        return monitors.Monitors
            .Where(m => m.WorkArea != m.Bounds)
            .Select(m => new WorkAreaRestore(m.Key, m.Bounds))
            .ToList();
    }
}
