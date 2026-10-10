namespace WinGnome.Core.Shell;

/// <summary>
/// Ping-pong breaker for the taskbar regions: when Explorer and WinGnome keep rewriting the same window's region,
/// emptying it again and again helps nobody and costs cross-process calls, so past
/// <see cref="MaxAppliesInWindow"/> applies to one window within <see cref="Window"/> the caller stops managing regions
/// and leaves the hide-and-re-hide ramp to cope. The caller passes the clock in.
/// </summary>
public sealed class TaskbarRegionBreaker
{
    public const int MaxAppliesInWindow = 20;

    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly Dictionary<long, Queue<DateTime>> _applies = [];

    /// <summary>Records one apply to <paramref name="handle"/> at <paramref name="now"/>; true when that is more than <see cref="MaxAppliesInWindow"/> within the window.</summary>
    public bool RecordApply(long handle, DateTime now)
    {
        if (!_applies.TryGetValue(handle, out var times))
        {
            times = new Queue<DateTime>();
            _applies[handle] = times;
        }

        // An apply exactly Window old still counts; only strictly older ones leave the window.
        while (times.Count > 0 && now - times.Peek() > Window)
        {
            times.Dequeue();
        }

        times.Enqueue(now);
        return times.Count > MaxAppliesInWindow;
    }

    /// <summary>Forgets every window's history.</summary>
    public void Reset() => _applies.Clear();
}
