using System.Windows.Threading;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Dock;

/// <summary>
/// The one cursor poll for edge reveal, shared by every dock: a GetCursorPos every 75 ms while at least one dock
/// wants it (hidden, or held open by the pointer), and nothing otherwise. Each sample goes to every dock that wants
/// it, so a dock held open on one monitor still sees the pointer leave for another. N docks never mean N polls.
/// </summary>
internal sealed class DockEdgePoller : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(75);

    private readonly DispatcherTimer _timer;
    private readonly List<Action<POINT>> _subscribers = [];

    public DockEdgePoller(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(PollInterval, DispatcherPriority.Input, OnTick, dispatcher) { IsEnabled = false };
    }

    /// <summary>True while the timer runs (for the log and QA).</summary>
    public bool IsRunning => _timer.IsEnabled;

    /// <summary>Starts or stops delivering samples to <paramref name="onSample"/>; the timer runs while anyone wants it.</summary>
    public void SetWanted(Action<POINT> onSample, bool wanted)
    {
        if (wanted && !_subscribers.Contains(onSample))
        {
            _subscribers.Add(onSample);
        }
        else if (!wanted)
        {
            _subscribers.Remove(onSample);
        }

        var run = _subscribers.Count > 0;
        if (run != _timer.IsEnabled)
        {
            _timer.IsEnabled = run;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        // A subscriber may unsubscribe (or another subscribe) while handling the sample.
        foreach (var subscriber in _subscribers.ToArray())
        {
            try
            {
                subscriber(cursor);
            }
            catch (Exception ex)
            {
                Log.Warn("Dock: pointer poll failed", ex);
            }
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _subscribers.Clear();
    }
}
