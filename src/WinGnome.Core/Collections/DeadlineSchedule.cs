namespace WinGnome.Core.Collections;

/// <summary>
/// One deadline per key, for debouncing: setting a key again replaces its deadline, and a single timer only
/// needs <see cref="NextDue"/>. Meant for a handful of keys (finding the next deadline is linear). Times are
/// milliseconds on any monotonic clock. Not thread-safe: use from one thread.
/// </summary>
public sealed class DeadlineSchedule<TKey>
    where TKey : notnull
{
    private readonly Dictionary<TKey, long> _due = [];

    public int Count => _due.Count;

    /// <summary>The earliest deadline, or null when nothing is scheduled.</summary>
    public long? NextDue
    {
        get
        {
            long? next = null;
            foreach (var due in _due.Values)
            {
                if (next is null || due < next)
                {
                    next = due;
                }
            }

            return next;
        }
    }

    /// <summary>Sets (or replaces) the deadline of <paramref name="key"/>.</summary>
    public void Set(TKey key, long due) => _due[key] = due;

    public bool Remove(TKey key) => _due.Remove(key);

    public bool Contains(TKey key) => _due.ContainsKey(key);

    public void Clear() => _due.Clear();

    /// <summary>Removes every key whose deadline is at or before <paramref name="now"/> and adds it to <paramref name="into"/>.</summary>
    public void TakeDue(long now, List<TKey> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var first = into.Count;
        foreach (var (key, due) in _due)
        {
            if (due <= now)
            {
                into.Add(key);
            }
        }

        for (var i = first; i < into.Count; i++)
        {
            _due.Remove(into[i]);
        }
    }
}
