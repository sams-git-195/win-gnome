namespace WinGnome.Core.Shell;

/// <summary>
/// Whether one monitor's direct work-area applications look like a fight with Explorer: a sliding window of
/// <see cref="ApplicationLimit"/> per <see cref="WindowMs"/>. Every application broadcasts <c>WM_SETTINGCHANGE</c>,
/// and in the fight state (KI-102) Explorer appears to recompute the work areas — dropping our granted strips — in
/// response to that very broadcast, so from the fourth application inside the window on the write must go out
/// silently (without <c>SPIF_SENDCHANGE</c>) to starve the loop. Applications (real writes) are counted, not loss
/// episodes: a write is what broadcasts, and episodes whose writes <see cref="WorkAreaBudget"/> refused provoked
/// nothing. In-memory only, never persisted: a restart starts unfought, so the start-up fallback's single write
/// broadcasts as it must. Pure and clock-free: callers pass a monotonic time in milliseconds.
/// </summary>
public sealed class WorkAreaFightDetector
{
    public const int ApplicationLimit = 3;

    public const long WindowMs = 600_000;

    private readonly Queue<long> _applications = new();

    /// <summary>
    /// Prunes the applications that have left the window and returns whether <see cref="ApplicationLimit"/> or more
    /// remain. Asking is not itself an application and records nothing.
    /// </summary>
    public bool IsFighting(long nowMs)
    {
        Prune(nowMs);
        return _applications.Count >= ApplicationLimit;
    }

    /// <summary>
    /// Records one work-area application and returns the new fighting state. An application leaves the window
    /// exactly <see cref="WindowMs"/> after it was made (the same edge as <see cref="WorkAreaBudget.TrySpend"/>).
    /// </summary>
    public bool Record(long nowMs)
    {
        Prune(nowMs);
        _applications.Enqueue(nowMs);
        return _applications.Count >= ApplicationLimit;
    }

    private void Prune(long nowMs)
    {
        while (_applications.Count > 0 && nowMs - _applications.Peek() >= WindowMs)
        {
            _applications.Dequeue();
        }
    }
}
