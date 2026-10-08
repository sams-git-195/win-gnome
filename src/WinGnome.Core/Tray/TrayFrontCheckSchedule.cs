namespace WinGnome.Core.Tray;

/// <summary>
/// How often the tray host checks that its window is still in front of Explorer's tray window. Explorer raises its
/// taskbar on its own, and while it is in front, Shell_NotifyIcon calls reach Explorer only. No event announces
/// that raise, so the host checks on a timer: fast while apps re-register after a TaskbarCreated broadcast, at the
/// old steady rate for a short while after shell activity (the moments Explorer raises its taskbar), and slowly
/// otherwise as a safety net.
/// </summary>
public sealed class TrayFrontCheckSchedule
{
    /// <summary>As often as user32 timers allow, while apps answer a TaskbarCreated broadcast.</summary>
    public const uint BurstIntervalMs = 15;

    /// <summary>After shell activity: the rate the host used to keep all the time.</summary>
    public const uint ActiveIntervalMs = 250;

    /// <summary>Safety net for raises no event announces.</summary>
    public const uint RestIntervalMs = 1000;

    public const long BurstDurationMs = 4000;
    public const long ActiveDurationMs = 2000;

    // Shell hook codes (HSHELL_*) after which Explorer may raise its taskbar.
    private const int WindowCreated = 1;
    private const int WindowDestroyed = 2;
    private const int WindowActivated = 4;
    private const int WindowReplaced = 13;
    private const int MonitorChanged = 16;
    private const int RudeAppActivated = 0x8004;

    private long _burstUntilMs;
    private long _activeUntilMs;
    private uint _currentMs;

    /// <summary>True for shell hook codes after which Explorer may raise its taskbar (not title redraws or flashes).</summary>
    public static bool IsShellActivity(int shellHookCode) =>
        shellHookCode is WindowCreated or WindowDestroyed or WindowActivated or WindowReplaced or MonitorChanged or RudeAppActivated;

    /// <summary>A TaskbarCreated broadcast went out. Returns the new timer interval, or null when it is unchanged.</summary>
    public uint? OnBroadcast(long nowMs)
    {
        _burstUntilMs = nowMs + BurstDurationMs;
        return Reschedule(nowMs);
    }

    /// <summary>Shell activity was seen. Returns the new timer interval, or null when it is unchanged.</summary>
    public uint? OnShellActivity(long nowMs)
    {
        _activeUntilMs = Math.Max(_activeUntilMs, nowMs + ActiveDurationMs);
        return Reschedule(nowMs);
    }

    /// <summary>The timer fired. Returns the new timer interval, or null when it is unchanged.</summary>
    public uint? OnTick(long nowMs) => Reschedule(nowMs);

    /// <summary>The interval that applies at <paramref name="nowMs"/>.</summary>
    public uint IntervalAt(long nowMs) =>
        nowMs < _burstUntilMs ? BurstIntervalMs
        : nowMs < _activeUntilMs ? ActiveIntervalMs
        : RestIntervalMs;

    private uint? Reschedule(long nowMs)
    {
        var interval = IntervalAt(nowMs);
        if (interval == _currentMs)
        {
            return null;
        }

        _currentMs = interval;
        return interval;
    }
}
