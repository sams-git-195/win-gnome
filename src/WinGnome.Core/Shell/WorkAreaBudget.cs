namespace WinGnome.Core.Shell;

/// <summary>
/// How often one monitor's work area may be set directly: a sliding window of <see cref="MaxApplications"/> per
/// <see cref="WindowMs"/>. Setting the work area broadcasts <c>WM_SETTINGCHANGE</c>, and Explorer recomputes work
/// areas on its own, so a third-party tool (or an Explorer that keeps reverting us) must not be answerable with an
/// unbounded number of writes. Pure and clock-free: callers pass a monotonic time in milliseconds.
/// </summary>
public sealed class WorkAreaBudget
{
    public const int MaxApplications = 3;

    public const long WindowMs = 60_000;

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
