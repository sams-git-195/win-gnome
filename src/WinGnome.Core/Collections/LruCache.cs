namespace WinGnome.Core.Collections;

/// <summary>
/// A bounded cache that evicts the least recently used entry when full. Not thread-safe: use from one thread.
/// </summary>
public sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _entries;

    // Most recently used first.
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new();

    public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        _entries = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(capacity, comparer);
    }

    public int Capacity { get; }

    public int Count => _entries.Count;

    /// <summary>Finds an entry and marks it as most recently used.</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        if (!_entries.TryGetValue(key, out var node))
        {
            value = default!;
            return false;
        }

        _order.Remove(node);
        _order.AddFirst(node);
        value = node.Value.Value;
        return true;
    }

    /// <summary>Adds or replaces an entry as most recently used, evicting the least recently used one when full.</summary>
    public void Set(TKey key, TValue value)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            _order.Remove(existing);
            _entries.Remove(key);
        }
        else if (_entries.Count >= Capacity)
        {
            var oldest = _order.Last!;
            _order.RemoveLast();
            _entries.Remove(oldest.Value.Key);
        }

        _entries[key] = _order.AddFirst(new KeyValuePair<TKey, TValue>(key, value));
    }

    public void Clear()
    {
        _entries.Clear();
        _order.Clear();
    }
}
