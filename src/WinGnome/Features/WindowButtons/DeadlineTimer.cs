using System.Windows.Threading;
using WinGnome.Core.Collections;

namespace WinGnome.Features.WindowButtons;

/// <summary>
/// Per-window deadlines on one one-shot dispatcher timer: <see cref="Set"/> (re)schedules a window, and the
/// callback runs for every window whose deadline has passed. Setting a later deadline never restarts the timer,
/// so a stream of events (a drag) costs a dictionary write each; the timer wakes at the earliest deadline it
/// was armed for and re-arms for whatever is left. Idle when nothing is scheduled. UI thread only.
/// </summary>
internal sealed class DeadlineTimer : IDisposable
{
    private readonly DeadlineSchedule<nint> _schedule = new();
    private readonly DispatcherTimer _timer;
    private readonly Action<nint> _onDue;
    private readonly List<nint> _due = [];
    private long _armedFor = long.MaxValue;
    private bool _disposed;

    public DeadlineTimer(Dispatcher dispatcher, DispatcherPriority priority, Action<nint> onDue)
    {
        _onDue = onDue;
        _timer = new DispatcherTimer(TimeSpan.Zero, priority, OnTick, dispatcher) { IsEnabled = false };
    }

    /// <summary>Milliseconds on the clock deadlines are measured against.</summary>
    public static long Now => Environment.TickCount64;

    public bool Contains(nint hwnd) => _schedule.Contains(hwnd);

    /// <summary>The deadline of <paramref name="hwnd"/> (see <see cref="Now"/>), when it is scheduled.</summary>
    public bool TryGetDue(nint hwnd, out long dueMs) => _schedule.TryGetDue(hwnd, out dueMs);

    /// <summary>Schedules <paramref name="hwnd"/> <paramref name="delayMs"/> from now, replacing any earlier deadline.</summary>
    public void Set(nint hwnd, long delayMs) => SetAt(hwnd, Now + delayMs);

    /// <summary>Schedules <paramref name="hwnd"/> at <paramref name="dueMs"/> (see <see cref="Now"/>).</summary>
    public void SetAt(nint hwnd, long dueMs)
    {
        if (_disposed)
        {
            return;
        }

        _schedule.Set(hwnd, dueMs);
        if (dueMs < _armedFor)
        {
            Arm(dueMs);
        }
    }

    /// <summary>Unschedules <paramref name="hwnd"/>; the timer may still wake once and find nothing due.</summary>
    public void Remove(nint hwnd) => _schedule.Remove(hwnd);

    public void Clear() => _schedule.Clear();

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _schedule.Clear();
    }

    private void Arm(long dueMs)
    {
        _armedFor = dueMs;
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, dueMs - Now));
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        _armedFor = long.MaxValue;
        _due.Clear();
        _schedule.TakeDue(Now, _due);
        foreach (var hwnd in _due)
        {
            if (_disposed)
            {
                return;
            }

            _onDue(hwnd);
        }

        // A callback may have scheduled again (which re-armed the timer); otherwise wake for the next deadline.
        if (!_disposed && _schedule.NextDue is { } next && next < _armedFor)
        {
            Arm(next);
        }
    }
}
