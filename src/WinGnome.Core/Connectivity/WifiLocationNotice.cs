namespace WinGnome.Core.Connectivity;

/// <summary>The location notice in place of the Visible Networks list: its words and which buttons it carries.</summary>
/// <param name="Title">The headline; empty when there is nothing to show.</param>
/// <param name="Subtitle">The explanation under it; empty when there is nothing to show.</param>
/// <param name="OfferShowNearby">"Show nearby networks": makes the first gated call, which raises Windows' consent prompt.</param>
/// <param name="OfferLocationSettings">"Open Location Settings".</param>
/// <param name="OfferWindowsList">"Show networks in Windows": opens Windows' own Wi-Fi page, which can list networks without location.</param>
public readonly record struct WifiLocationNoticeText(
    string Title,
    string Subtitle,
    bool OfferShowNearby,
    bool OfferLocationSettings,
    bool OfferWindowsList);

/// <summary>Words and buttons for each <see cref="WifiLocationBlock"/>.</summary>
public static class WifiLocationNotice
{
    private const string SavedStillWork = "Your saved networks below still work.";

    /// <summary>
    /// The notice for the reason the gate found. <paramref name="callRefused"/> is true when a gated call came back
    /// "access denied" anyway: with no better reason to give (nothing known, or only "not asked yet") the text then
    /// hedges, since the registry reads can be wrong. <see cref="WifiLocationBlock.None"/> without a refusal has
    /// nothing to show.
    /// </summary>
    public static WifiLocationNoticeText For(WifiLocationBlock block, bool callRefused)
    {
        if (callRefused && block is WifiLocationBlock.None or WifiLocationBlock.NotAsked)
        {
            return new(
                "Windows needs location access to show nearby networks",
                "Turn on location access for WinGnome (and for desktop apps) in Windows Settings, then press Refresh. Turning Wi-Fi on or off and disconnecting still work.",
                OfferShowNearby: false,
                OfferLocationSettings: true,
                OfferWindowsList: true);
        }

        return block switch
        {
            WifiLocationBlock.Policy => new(
                "Location is turned off by a policy on this PC",
                $"Windows won't tell apps which networks are nearby. {SavedStillWork} To list nearby networks here, an administrator must remove the policy, then turn on location for desktop apps.",
                OfferShowNearby: false,
                OfferLocationSettings: false,
                OfferWindowsList: true),
            WifiLocationBlock.DeviceOff => SettingsNotice(
                "Location services are off",
                $"Windows won't tell apps which networks are nearby while location services are off. Turning them on needs an administrator. {SavedStillWork}"),
            WifiLocationBlock.UserOff => SettingsNotice(
                "Location access is off for desktop apps",
                $"Turn on \"Let apps access your location\" and \"Let desktop apps access your location\" in Windows Settings, then press Refresh. {SavedStillWork}"),
            WifiLocationBlock.DesktopAppsOff => SettingsNotice(
                "Location access is off for desktop apps",
                $"Turn on \"Let desktop apps access your location\" in Windows Settings, then press Refresh. {SavedStillWork}"),
            WifiLocationBlock.AppDenied => SettingsNotice(
                "WinGnome isn't allowed to use your location",
                $"Allow WinGnome under location access for desktop apps in Windows Settings, then press Refresh. {SavedStillWork}"),
            WifiLocationBlock.NotAsked => new(
                "Windows needs your permission to show nearby networks",
                "Windows will ask whether WinGnome may use your location. Turning Wi-Fi on or off and disconnecting work without it.",
                OfferShowNearby: true,
                OfferLocationSettings: false,
                OfferWindowsList: false),
            _ => new("", "", false, false, false),
        };
    }

    private static WifiLocationNoticeText SettingsNotice(string title, string subtitle) =>
        new(title, subtitle, OfferShowNearby: false, OfferLocationSettings: true, OfferWindowsList: true);
}
