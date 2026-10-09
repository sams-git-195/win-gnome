namespace WinGnome.Core.ControlCenter;

/// <summary>The <c>FILTERKEYS</c> structure Windows reports and accepts (flags and the four timings, in milliseconds).</summary>
public readonly record struct FilterKeysState(uint Flags, int WaitMs, int DelayMs, int RepeatMs, int BounceMs)
{
    /// <summary>Filter keys are on.</summary>
    public bool IsOn => (Flags & FilterKeysPlan.On) != 0;

    /// <summary>Slow keys: filter keys on with an acceptance delay and no bounce time.</summary>
    public bool IsSlowKeysOn => IsOn && BounceMs == 0 && WaitMs > 0;

    /// <summary>Bounce keys: filter keys on with a bounce time.</summary>
    public bool IsBounceKeysOn => IsOn && BounceMs > 0;
}

/// <summary>
/// Slow keys and bounce keys are one Win32 feature (<c>FILTERKEYS</c>): a bounce time needs the acceptance, repeat
/// delay and repeat rate all zero, so the two can't both be on. This plans the structure to write for a choice,
/// changing only the on bit and the timings and keeping every other flag (the shortcut, its confirmation, sounds).
/// </summary>
public static class FilterKeysPlan
{
    /// <summary>FKF_FILTERKEYSON.</summary>
    public const uint On = 0x0001;

    /// <summary>FKF_AVAILABLE: a valid read always has it.</summary>
    public const uint Available = 0x0002;

    /// <summary>FKF_HOTKEYACTIVE: holding right Shift for eight seconds toggles filter keys.</summary>
    public const uint HotkeyActive = 0x0004;

    /// <summary>FKF_CONFIRMHOTKEY: Windows asks before the shortcut turns filter keys on.</summary>
    public const uint ConfirmHotkey = 0x0008;

    /// <summary>Windows' defaults, used in place of a read that lacks <see cref="Available"/>.</summary>
    public const uint DefaultFlags = Available | HotkeyActive | ConfirmHotkey;

    /// <summary>Windows' slow keys acceptance delay when none was set before.</summary>
    public const int DefaultWaitMs = 1000;

    /// <summary>Windows' bounce time when none was set before.</summary>
    public const int DefaultBounceMs = 500;

    /// <summary>The acceptance delays Windows Settings offers run from 0.3 s to 2 s; anything outside is pulled in.</summary>
    public const int MinWaitMs = 300;

    public const int MaxWaitMs = 2000;

    /// <summary>The bounce times Windows Settings offers run from 0.5 s to 2 s.</summary>
    public const int MinBounceMs = 500;

    public const int MaxBounceMs = 2000;

    /// <summary>
    /// The structure to write for <paramref name="slow"/> and <paramref name="bounce"/> (at most one may be true).
    /// Slow keys keep the repeat timings and use the previous acceptance delay (or 1 s); bounce keys zero the other
    /// three timings and use the previous bounce time (or 0.5 s); both off clears only the on bit. A read without
    /// <see cref="Available"/> is replaced by Windows' default flags so the keyboard shortcut still works.
    /// </summary>
    public static FilterKeysState For(FilterKeysState current, bool slow, bool bounce)
    {
        if (slow && bounce)
        {
            throw new ArgumentException("Slow keys and bounce keys can't both be on.", nameof(bounce));
        }

        var flags = (current.Flags & Available) != 0 ? current.Flags : DefaultFlags;
        if (bounce)
        {
            var bounceMs = current.BounceMs > 0 ? Math.Clamp(current.BounceMs, MinBounceMs, MaxBounceMs) : DefaultBounceMs;
            return new FilterKeysState(flags | On, 0, 0, 0, bounceMs);
        }

        if (slow)
        {
            var waitMs = current.WaitMs > 0 ? Math.Clamp(current.WaitMs, MinWaitMs, MaxWaitMs) : DefaultWaitMs;
            return current with { Flags = flags | On, WaitMs = waitMs, BounceMs = 0 };
        }

        return current with { Flags = flags & ~On };
    }

    /// <summary>The slow keys switch: on turns bounce keys off; off leaves bounce keys as they are.</summary>
    public static FilterKeysState ForSlowKeys(FilterKeysState current, bool on) =>
        For(current, slow: on, bounce: !on && current.IsBounceKeysOn);

    /// <summary>The bounce keys switch: on turns slow keys off; off leaves slow keys as they are.</summary>
    public static FilterKeysState ForBounceKeys(FilterKeysState current, bool on) =>
        For(current, slow: !on && current.IsSlowKeysOn, bounce: on);
}
