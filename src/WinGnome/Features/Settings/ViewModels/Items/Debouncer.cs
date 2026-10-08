using System.Windows.Threading;

namespace WinGnome.Features.Settings.ViewModels.Items;

/// <summary>
/// Collapses a burst of triggers (a dragged slider, typing) into one call after the burst pauses,
/// so settings are saved a few times instead of dozens of times a second.
/// </summary>
internal sealed class Debouncer
{
    /// <summary>Default quiet period before the action runs.</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(150);

    private readonly DispatcherTimer _timer;
    private readonly Action _action;

    public Debouncer(Action action, TimeSpan? delay = null)
    {
        _action = action;
        _timer = new DispatcherTimer { Interval = delay ?? DefaultDelay };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _action();
        };
    }

    /// <summary>True while a trigger is waiting to run.</summary>
    public bool IsPending => _timer.IsEnabled;

    /// <summary>(Re)starts the quiet period.</summary>
    public void Trigger()
    {
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Runs the action now if a trigger is waiting.</summary>
    public void Flush()
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            _action();
        }
    }

    /// <summary>Drops a waiting trigger without running the action.</summary>
    public void Cancel() => _timer.Stop();
}
