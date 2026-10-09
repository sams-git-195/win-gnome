namespace WinGnome.Core.Windows;

/// <summary>The window size and DPI a probe was made at: its answer is valid only while they hold.</summary>
public readonly record struct ProbeSize(int Width, int Height, uint Dpi);

/// <summary>
/// Limits how often one window's caption buttons are probed (spec 0009): at most <see cref="MaxStarts"/> probes
/// per <see cref="PeriodMs"/>, and none at a size where <see cref="MaxFailuresPerSize"/> probes in a row found
/// nothing, until the size changes. Times are milliseconds on any monotonic clock. Not thread-safe.
/// </summary>
public sealed class CaptionProbeThrottle
{
    public const int MaxStarts = 3;
    public const long PeriodMs = 60_000;
    public const int MaxFailuresPerSize = 2;

    // The most recent starts, oldest first once full.
    private readonly long[] _starts = new long[MaxStarts];
    private int _startCount;
    private int _nextSlot;

    private ProbeSize _failedSize;
    private int _failures;

    /// <summary>True when the last probe found nothing (and no probe has succeeded since).</summary>
    public bool HasFailed => _failures > 0;

    /// <summary>The earliest time, at or after <paramref name="nowMs"/>, at which another probe may start.</summary>
    public long NextStartAt(long nowMs)
    {
        if (_startCount < MaxStarts)
        {
            return nowMs;
        }

        // The slot to be overwritten next holds the oldest of the last MaxStarts starts.
        return Math.Max(nowMs, _starts[_nextSlot] + PeriodMs);
    }

    /// <summary>True when probing again at <paramref name="size"/> is pointless until the size changes.</summary>
    public bool IsBlocked(ProbeSize size) => _failures >= MaxFailuresPerSize && _failedSize == size;

    public void RecordStart(long nowMs)
    {
        _starts[_nextSlot] = nowMs;
        _nextSlot = (_nextSlot + 1) % MaxStarts;
        _startCount = Math.Min(_startCount + 1, MaxStarts);
    }

    public void RecordFailure(ProbeSize size)
    {
        _failures = _failures > 0 && _failedSize == size ? _failures + 1 : 1;
        _failedSize = size;
    }

    public void RecordSuccess() => ForgetFailures();

    /// <summary>Lets a blocked size be probed again (the settings that decide what is accepted changed).</summary>
    public void ForgetFailures()
    {
        _failures = 0;
        _failedSize = default;
    }
}
