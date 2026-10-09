namespace WinGnome.Core.Shell;

/// <summary>
/// Timestamps of recent WinGnome shell crashes. Immutable: every change returns a new history, so the
/// bootstrap can persist the result and a stale copy can never be mutated behind its back.
/// </summary>
public sealed class CrashHistory
{
    /// <summary>Fast loop: <see cref="FastThreshold"/> crashes within this window.</summary>
    public static readonly TimeSpan FastWindow = TimeSpan.FromMinutes(2);

    public const int FastThreshold = 3;

    /// <summary>Slow loop (a shell that dies every few minutes): <see cref="SlowThreshold"/> crashes within this window.</summary>
    public static readonly TimeSpan SlowWindow = TimeSpan.FromMinutes(30);

    public const int SlowThreshold = 5;

    public static CrashHistory Empty { get; } = new([]);

    private readonly DateTimeOffset[] _crashes;

    private CrashHistory(DateTimeOffset[] crashes) => _crashes = crashes;

    public IReadOnlyList<DateTimeOffset> Crashes => _crashes;

    /// <summary>Builds a history from persisted timestamps, in any order.</summary>
    public static CrashHistory From(IEnumerable<DateTimeOffset> crashes) => new([.. crashes.OrderBy(c => c)]);

    /// <summary>Adds a crash and drops every entry that has already left the longest window.</summary>
    public CrashHistory Record(DateTimeOffset crashedAt, DateTimeOffset now) =>
        From(_crashes.Append(crashedAt)).Prune(now);

    /// <summary>Drops entries older than <see cref="SlowWindow"/> before <paramref name="now"/>. Entries in the future are kept.</summary>
    public CrashHistory Prune(DateTimeOffset now) => new([.. _crashes.Where(c => now - c <= SlowWindow)]);

    /// <summary>
    /// Crashes in the last <paramref name="window"/> (inclusive). Timestamps after <paramref name="now"/>
    /// (the clock was set back) don't count, so a clock change can't lock the user out of the shell.
    /// </summary>
    public int CountWithin(TimeSpan window, DateTimeOffset now) => _crashes.Count(c => c <= now && now - c <= window);

    public bool IsCrashLoop(DateTimeOffset now) =>
        CountWithin(FastWindow, now) >= FastThreshold || CountWithin(SlowWindow, now) >= SlowThreshold;
}
