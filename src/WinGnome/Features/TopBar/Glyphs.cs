using WinGnome.Core.TopBar;

namespace WinGnome.Features.TopBar;

/// <summary>Segoe Fluent Icons code points used by the top bar (same glyphs as the Windows 11 taskbar).</summary>
internal static class Glyphs
{
    public const string Wifi = "";
    public const string Wired = "";
    public const string NoNetwork = "";
    public const string Bluetooth = "";
    public const string Notifications = "";
    public const string WindowsQuickSettings = "";
    public const string SystemTray = "";

    public const string VolumeMuted = "";
    public const string VolumeSilent = "";
    public const string VolumeLow = "";
    public const string VolumeMedium = "";
    public const string VolumeHigh = "";

    public const string BatteryUnknown = "";

    // Battery0..Battery9 are contiguous, Battery10 is elsewhere; likewise for the charging set.
    private const int Battery0 = 0xE850;
    private const string Battery10 = "";
    private const int BatteryCharging0 = 0xE85A;
    private const string BatteryCharging9 = "";
    private const string BatteryCharging10 = "";

    public static string ForNetwork(NetworkConnection connection) => connection switch
    {
        NetworkConnection.Wired => Wired,
        NetworkConnection.Wireless => Wifi,
        _ => NoNetwork,
    };

    public static string ForVolume(VolumeIcon icon) => icon switch
    {
        VolumeIcon.Muted => VolumeMuted,
        VolumeIcon.Silent => VolumeSilent,
        VolumeIcon.Low => VolumeLow,
        VolumeIcon.Medium => VolumeMedium,
        _ => VolumeHigh,
    };

    public static string ForBattery(BatteryStatus status)
    {
        if (status.GlyphLevel is not { } level)
        {
            return BatteryUnknown;
        }

        if (status.IsCharging)
        {
            return level switch
            {
                10 => BatteryCharging10,
                9 => BatteryCharging9,
                _ => char.ConvertFromUtf32(BatteryCharging0 + level),
            };
        }

        return level == 10 ? Battery10 : char.ConvertFromUtf32(Battery0 + level);
    }
}
