using System.Globalization;
using WinGnome.Core.Connectivity;
using WinGnome.Core.TopBar;

namespace WinGnome.Features.TopBar;

/// <summary>Keys of the symbolic icons (<c>Theme/SymbolicIcons.xaml</c>) that show the network, volume and battery state.</summary>
internal static class StatusIcons
{
    /// <summary>
    /// Wi-Fi shows <c>NetworkWireless0</c> to <c>NetworkWireless3</c> for a known weaker signal and the full
    /// <c>NetworkWireless</c> wedge for the top level or when no signal could be read.
    /// </summary>
    public static string ForNetwork(NetworkConnection connection, int? signalBars = null) => connection switch
    {
        NetworkConnection.Wired => "NetworkWired",
        NetworkConnection.Wireless => WifiSignal.LevelFromBars(signalBars) switch
        {
            { } level and < 4 => "NetworkWireless" + level.ToString(CultureInfo.InvariantCulture),
            _ => "NetworkWireless",
        },
        _ => "NetworkOffline",
    };

    /// <summary>Like GNOME, a volume of zero shows the muted speaker.</summary>
    public static string ForVolume(VolumeIcon icon) => icon switch
    {
        VolumeIcon.Muted or VolumeIcon.Silent => "AudioVolumeMuted",
        VolumeIcon.Low => "AudioVolumeLow",
        VolumeIcon.Medium => "AudioVolumeMedium",
        _ => "AudioVolumeHigh",
    };

    /// <summary>BatteryLevel0 .. BatteryLevel100 in steps of 10, each with a Charging variant.</summary>
    public static string ForBattery(BatteryStatus status)
    {
        if (status.GlyphLevel is not { } level)
        {
            return "BatteryMissing";
        }

        var key = "BatteryLevel" + (level * 10).ToString(CultureInfo.InvariantCulture);
        return status.IsCharging ? key + "Charging" : key;
    }
}
