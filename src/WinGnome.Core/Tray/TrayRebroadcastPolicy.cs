namespace WinGnome.Core.Tray;

/// <summary>
/// When the tray host sends its one-shot "startup heal": a delayed TaskbarCreated re-broadcast, sent after the
/// host's front check has reliably won the z-order, so apps whose start-up registration reached Explorer only
/// (their NIM_ADD landed in a front gap) re-register with the host with full data. Not thread-safe: the tray
/// host's thread owns it, like the icon registry.
/// </summary>
public sealed class TrayRebroadcastPolicy
{
    /// <summary>How long after the host started the heal waits: covers the ~1 s the front check needs to win, with margin.</summary>
    public const long GraceMs = 2000;

    /// <summary>
    /// How long a TaskbarCreated broadcast suppresses a still-pending heal: a broadcast inside the grace window
    /// (an Explorer restart's rebroadcast) already made every app re-register, so the heal would only double
    /// the storm. Longer than any registration burst (<see cref="TrayFrontCheckSchedule.BurstDurationMs"/>).
    /// </summary>
    public const long CooldownMs = 10_000;

    private long _startedAtMs;
    private long _cooldownUntilMs;
    private bool _armed;
    private bool _healUsed;

    /// <summary>
    /// The host window was created and its start-up broadcast is out (report that one via
    /// <see cref="OnBroadcastSent"/> first): arms the one heal, due <see cref="GraceMs"/> from now. Calling it
    /// again (a host restart) re-arms and clears any cooldown — the start-up broadcast is what the heal
    /// repairs, not a broadcast that made it redundant.
    /// </summary>
    public void OnHostStarted(long nowMs)
    {
        _startedAtMs = nowMs;
        _cooldownUntilMs = 0;
        _armed = true;
        _healUsed = false;
    }

    /// <summary>
    /// A TaskbarCreated broadcast went out (the start-up one, the heal one, or an Explorer-restart rebroadcast).
    /// Starts the cooldown; a heal still pending when it is due inside that cooldown is skipped for this host
    /// start — the broadcast did the heal's job while the host was in front, so deferring the heal would
    /// re-register everyone twice.
    /// </summary>
    public void OnBroadcastSent(long nowMs) => _cooldownUntilMs = nowMs + CooldownMs;

    /// <summary>
    /// True once per host start, when the heal is due: at/after <see cref="GraceMs"/> past
    /// <see cref="OnHostStarted"/> and inside no <see cref="OnBroadcastSent"/> cooldown. The one chance is
    /// consumed by the due check either way: inside a cooldown it is skipped for this host start and never
    /// becomes true later. When it returns true, the caller sends the broadcast and reports it via
    /// <see cref="OnBroadcastSent"/> like every other one.
    /// </summary>
    public bool ShouldSendHeal(long nowMs)
    {
        if (!_armed || _healUsed || nowMs < _startedAtMs + GraceMs)
        {
            return false;
        }

        _healUsed = true;
        return nowMs >= _cooldownUntilMs;
    }
}
