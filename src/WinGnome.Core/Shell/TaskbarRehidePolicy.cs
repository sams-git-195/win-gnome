namespace WinGnome.Core.Shell;

/// <summary>How hard Explorer is currently fighting the hidden taskbar.</summary>
public enum TaskbarRehideLevel
{
    /// <summary>Occasional shows: re-hide quickly.</summary>
    Normal,

    /// <summary>A burst of shows (typically start-up, while work-area writes churn Explorer): re-hide a little slower.</summary>
    Burst,

    /// <summary>Show after show, faster than anything legitimate: re-hide slowly so the two never fight in a tight loop.</summary>
    Runaway,
}

/// <summary>The re-hide delay for one Explorer show, and whether the level moved since the previous show.</summary>
public readonly record struct TaskbarRehideDecision(
    TimeSpan Delay,
    TaskbarRehideLevel Level,
    TaskbarRehideLevel PreviousLevel)
{
    /// <summary>True when Explorer is showing the taskbar more often than at the previous show.</summary>
    public bool Escalated => Level > PreviousLevel;

    /// <summary>True when Explorer has calmed down all the way back to <see cref="TaskbarRehideLevel.Normal"/>.</summary>
    public bool Recovered => Level == TaskbarRehideLevel.Normal && PreviousLevel != TaskbarRehideLevel.Normal;
}

/// <summary>
/// How long to wait before re-hiding a taskbar Explorer just showed. The delay ramps with how many shows happened in
/// the last <see cref="Window"/>, instead of jumping to a long delay: every moment the delay is long is a moment the
/// taskbar is visible on screen. The caller passes the clock in.
/// </summary>
public sealed class TaskbarRehidePolicy
{
    public static readonly TimeSpan NormalDelay = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan BurstDelay = TimeSpan.FromMilliseconds(750);
    public static readonly TimeSpan RunawayDelay = TimeSpan.FromSeconds(3);

    /// <summary>Shows within <see cref="Window"/> (counting the current one) that move the level to Burst.</summary>
    public const int BurstShows = 5;

    /// <summary>
    /// Shows within <see cref="Window"/> (counting the current one) that move the level to Runaway. The caller counts one
    /// show per scheduled re-hide, so a sustained loop at the Burst delay (about 13 re-hides in the window) reaches it.
    /// </summary>
    public const int RunawayShows = 12;

    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly Queue<DateTime> _shows = new();
    private TaskbarRehideLevel _level = TaskbarRehideLevel.Normal;

    /// <summary>Records a show at <paramref name="now"/> and returns the delay to re-hide with.</summary>
    public TaskbarRehideDecision OnShow(DateTime now)
    {
        // A show exactly Window old still counts; only strictly older ones leave the window.
        while (_shows.Count > 0 && now - _shows.Peek() > Window)
        {
            _shows.Dequeue();
        }

        _shows.Enqueue(now);

        var previous = _level;
        _level = _shows.Count >= RunawayShows ? TaskbarRehideLevel.Runaway
            : _shows.Count >= BurstShows ? TaskbarRehideLevel.Burst
            : TaskbarRehideLevel.Normal;

        var delay = _level switch
        {
            TaskbarRehideLevel.Runaway => RunawayDelay,
            TaskbarRehideLevel.Burst => BurstDelay,
            _ => NormalDelay,
        };
        return new TaskbarRehideDecision(delay, _level, previous);
    }
}
