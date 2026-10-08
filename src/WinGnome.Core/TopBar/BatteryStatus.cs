using System.Globalization;

namespace WinGnome.Core.TopBar;

/// <summary>Battery state as shown by the top bar, decoded from Win32 <c>SYSTEM_POWER_STATUS</c>.</summary>
/// <param name="HasBattery">False on desktops (no system battery) or when the state is unknown.</param>
/// <param name="Percent">Charge 0..100, or null when Windows does not know it.</param>
/// <param name="IsCharging">The battery is currently charging.</param>
/// <param name="IsPluggedIn">Running on AC power (possibly fully charged and not charging).</param>
/// <param name="Remaining">Estimated time left on battery; only known while discharging.</param>
public readonly record struct BatteryStatus(bool HasBattery, int? Percent, bool IsCharging, bool IsPluggedIn, TimeSpan? Remaining)
{
    private const byte AcOnline = 1;
    private const byte FlagCharging = 8;
    private const byte FlagNoBattery = 128;
    private const byte Unknown = 255;

    /// <summary>A machine without a battery.</summary>
    public static BatteryStatus None => new(false, null, false, false, null);

    /// <summary>Decodes the raw <c>SYSTEM_POWER_STATUS</c> fields.</summary>
    /// <param name="acLineStatus">0 offline, 1 online, 255 unknown.</param>
    /// <param name="batteryFlag">Bit flags: 8 charging, 128 no system battery, 255 unknown.</param>
    /// <param name="lifePercent">0..100, or 255 when unknown.</param>
    /// <param name="lifeTimeSeconds">Seconds of battery life left, or <see cref="uint.MaxValue"/> when unknown.</param>
    public static BatteryStatus FromPowerStatus(byte acLineStatus, byte batteryFlag, byte lifePercent, uint lifeTimeSeconds)
    {
        var unknownFlag = batteryFlag == Unknown;
        if ((!unknownFlag && (batteryFlag & FlagNoBattery) != 0) || (unknownFlag && lifePercent > 100))
        {
            return None;
        }

        var pluggedIn = acLineStatus == AcOnline;
        int? percent = lifePercent <= 100 ? lifePercent : null;
        var charging = !unknownFlag && (batteryFlag & FlagCharging) != 0;
        TimeSpan? remaining = !pluggedIn && lifeTimeSeconds != uint.MaxValue ? TimeSpan.FromSeconds(lifeTimeSeconds) : null;
        return new BatteryStatus(true, percent, charging, pluggedIn, remaining);
    }

    /// <summary>Icon fill level 0..10 (tenths of a full battery), or null when the charge is unknown.</summary>
    public int? GlyphLevel => Percent is { } p ? Math.Clamp((int)Math.Round(p / 10.0, MidpointRounding.AwayFromZero), 0, 10) : null;

    /// <summary>"73%" or an empty string when unknown.</summary>
    public string PercentText => Percent is { } p ? p.ToString(CultureInfo.InvariantCulture) + "%" : "";

    /// <summary>One-line state such as "Charging", "2 h 10 min left" or "Fully charged".</summary>
    public string StateText
    {
        get
        {
            if (!HasBattery)
            {
                return "No battery";
            }

            if (IsCharging)
            {
                return "Charging";
            }

            if (IsPluggedIn)
            {
                return Percent >= 100 ? "Fully charged" : "Plugged in, not charging";
            }

            return Remaining is { } left ? FormatRemaining(left) + " left" : "On battery";
        }
    }

    /// <summary>"73% · Charging" style summary for tooltips and the quick-settings menu.</summary>
    public string Summary => PercentText.Length == 0 ? StateText : PercentText + " · " + StateText;

    /// <summary>"2 h 05 min", or "45 min" under an hour.</summary>
    public static string FormatRemaining(TimeSpan time)
    {
        var minutes = Math.Max(0, (int)Math.Round(time.TotalMinutes));
        var hours = minutes / 60;
        minutes %= 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours} h {minutes:00} min")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes} min");
    }
}
