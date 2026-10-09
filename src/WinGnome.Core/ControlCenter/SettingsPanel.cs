namespace WinGnome.Core.ControlCenter;

/// <summary>Sidebar groups of the settings app, in display order (GNOME Settings groups its panels the same way).</summary>
public enum PanelGroup
{
    Connectivity,
    Devices,
    Personalisation,
    AppsAndPrivacy,
    System,
    WinGnome,
}

/// <summary>How a panel is shown.</summary>
public enum PanelKind
{
    /// <summary>A page inside the settings app.</summary>
    Native,

    /// <summary>Opens <see cref="SettingsPanel.LinkUri"/> (a Windows Settings page or a control panel) instead of a page.</summary>
    Link,
}

/// <summary>One entry of the settings sidebar.</summary>
/// <param name="Id">Stable id, one of <see cref="PanelIds"/>.</param>
/// <param name="Title">Sidebar and page title.</param>
/// <param name="Icon">Segoe Fluent Icons glyph.</param>
/// <param name="Group">Sidebar group.</param>
/// <param name="Kind">Native page or link.</param>
/// <param name="LinkUri">
/// For links, what opens. For native system panels, the matching Windows Settings page, used as the fallback when the
/// panel can't read or change a setting. Null for WinGnome's own pages.
/// </param>
/// <param name="Keywords">Extra search terms.</param>
public sealed record SettingsPanel(
    string Id,
    string Title,
    string Icon,
    PanelGroup Group,
    PanelKind Kind,
    string? LinkUri,
    IReadOnlyList<string> Keywords);

/// <summary>Ids of the settings panels.</summary>
public static class PanelIds
{
    public const string Wifi = "wifi";
    public const string Network = "network";
    public const string Bluetooth = "bluetooth";

    public const string Displays = "displays";
    public const string Sound = "sound";
    public const string Power = "power";
    public const string Mouse = "mouse";
    public const string Keyboard = "keyboard";
    public const string Printers = "printers";
    public const string RemovableMedia = "removable-media";
    public const string Colour = "colour";

    public const string Appearance = "appearance";
    public const string Multitasking = "multitasking";
    public const string Notifications = "notifications";

    public const string Apps = "apps";
    public const string DefaultApps = "default-apps";
    public const string OnlineAccounts = "online-accounts";
    public const string Sharing = "sharing";
    public const string Privacy = "privacy";

    public const string RegionLanguage = "region-language";
    public const string DateTime = "datetime";
    public const string Users = "users";
    public const string Accessibility = "accessibility";
    public const string WindowsUpdate = "windows-update";
    public const string About = "about";

    public const string General = "wingnome-general";
    public const string TopBar = "wingnome-top-bar";
    public const string Dock = "wingnome-dock";
    public const string WindowButtons = "wingnome-window-buttons";
    public const string Activities = "wingnome-activities";
    public const string Streamline = "wingnome-streamline";
    public const string AboutWinGnome = "wingnome-about";
}
