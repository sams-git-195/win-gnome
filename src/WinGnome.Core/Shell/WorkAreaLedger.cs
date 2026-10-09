namespace WinGnome.Core.Shell;

/// <summary>
/// The record-list rule behind the <c>workareas.state</c> marker: at most one record per bar and monitor key. A bar
/// that shrinks the same work area again (something else reset it) replaces its old record and moves to the end,
/// because the list is kept in application order and <see cref="WorkAreaRecovery.Plan"/> unwinds it newest first: the
/// re-shrink is the newest change to that monitor, so it must be undone first, and the old record's <c>Applied</c> is
/// no longer the live value anyway. Appending without replacing let a work area something kept reverting grow the
/// marker by one record per re-shrink — three a minute per monitor, thousands a day — each of which the plan then
/// walked on every release.
/// </summary>
public static class WorkAreaLedger
{
    /// <summary>
    /// <paramref name="records"/> with <paramref name="record"/> at the end and any record of the same owner on the
    /// same monitor key (compared case-insensitively, like every monitor-key lookup) removed. The input is not
    /// modified, so a caller can keep it as the rollback state for a shrink that fails.
    /// </summary>
    public static IReadOnlyList<WorkAreaRecord> Add(IReadOnlyList<WorkAreaRecord> records, WorkAreaRecord record)
    {
        ArgumentNullException.ThrowIfNull(records);

        var updated = new List<WorkAreaRecord>(records.Count + 1);
        foreach (var existing in records)
        {
            if (existing.Owner != record.Owner
                || !string.Equals(existing.Key, record.Key, StringComparison.OrdinalIgnoreCase))
            {
                updated.Add(existing);
            }
        }

        updated.Add(record);
        return updated;
    }
}
