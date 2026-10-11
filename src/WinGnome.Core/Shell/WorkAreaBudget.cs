namespace WinGnome.Core.Shell;

/// <summary>
/// How often one bar may set a monitor's work area directly: a sliding window of <see cref="MaxApplications"/> per
/// <see cref="WindowMs"/>. Explorer recomputes work areas on its own, so a third-party tool (or an Explorer that
/// keeps reverting us) must not be answerable with an unbounded number of writes. The writes are silent and answer
/// Explorer's resets rather than provoke them, and in the first minute after start-up Explorer reset the work area
/// every few seconds (KI-115), so the default allows <see cref="DefaultMaxApplications"/>. Pure and clock-free:
/// callers pass a monotonic time in milliseconds.
/// </summary>
public sealed class WorkAreaBudget(int maxApplications = WorkAreaBudget.DefaultMaxApplications)
{
    public const int DefaultMaxApplications = 10;

    public const long WindowMs = 60_000;

    public int MaxApplications { get; } = maxApplications;

    private readonly Queue<long> _spends = new();

    /// <summary>Number of applications still inside the window (for the log line when one is refused).</summary>
    public int Spent => _spends.Count;

    /// <summary>
    /// Records an application and returns true, or returns false without recording it when the window is full. An
    /// application leaves the window exactly <see cref="WindowMs"/> after it was made.
    /// </summary>
    public bool TrySpend(long nowMs)
    {
        while (_spends.Count > 0 && nowMs - _spends.Peek() >= WindowMs)
        {
            _spends.Dequeue();
        }

        if (_spends.Count >= MaxApplications)
        {
            return false;
        }

        _spends.Enqueue(nowMs);
        return true;
    }
}
