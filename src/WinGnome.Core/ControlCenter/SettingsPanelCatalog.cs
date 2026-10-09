using WinGnome.Core.Search;

namespace WinGnome.Core.ControlCenter;

/// <summary>
/// Every panel of the GNOME-style settings app: native panels, links to Windows Settings, and WinGnome's own pages.
/// The data is declarative so it is easy to review.
/// </summary>
public static class SettingsPanelCatalog
{
    /// <summary>All panels in sidebar order, grouped by <see cref="PanelGroup"/>.</summary>
    public static IReadOnlyList<SettingsPanel> All { get; } =
    [
        Link(PanelIds.Wifi, "Wi-Fi", "", PanelGroup.Connectivity, "ms-settings:network-wifi", "wireless", "wlan", "hotspot", "network"),
        Link(PanelIds.Network, "Network", "", PanelGroup.Connectivity, "ms-settings:network-status", "ethernet", "vpn", "proxy", "internet"),
        Link(PanelIds.Bluetooth, "Bluetooth", "", PanelGroup.Connectivity, "ms-settings:bluetooth", "pair", "devices", "headphones"),

        Native(PanelIds.Displays, "Displays", "", PanelGroup.Devices, "ms-settings:display", "monitor", "screen", "resolution", "refresh rate", "primary", "arrangement", "scale"),
        Native(PanelIds.Sound, "Sound", "", PanelGroup.Devices, "ms-settings:sound", "volume", "speakers", "output", "input", "microphone", "audio"),
        Native(PanelIds.Power, "Power", "", PanelGroup.Devices, "ms-settings:powersleep", "battery", "sleep", "suspend", "screen blank", "power mode", "energy"),
        Native(PanelIds.Mouse, "Mouse & Touchpad", "", PanelGroup.Devices, "ms-settings:mousetouchpad", "pointer", "speed", "acceleration", "scroll", "primary button", "trackpad"),
        Native(PanelIds.Keyboard, "Keyboard", "", PanelGroup.Devices, "ms-settings:typing", "repeat", "delay", "input sources", "layout", "language"),
        Link(PanelIds.Printers, "Printers", "", PanelGroup.Devices, "ms-settings:printers", "print", "scanner"),
        Link(PanelIds.RemovableMedia, "Removable Media", "", PanelGroup.Devices, "ms-settings:autoplay", "autoplay", "usb", "drive"),
        Link(PanelIds.Colour, "Colour", "", PanelGroup.Devices, "colorcpl.exe", "color", "colour profile", "icc", "calibration"),

        Native(PanelIds.Appearance, "Appearance", "", PanelGroup.Personalisation, "ms-settings:colors", "dark mode", "light mode", "style", "theme", "accent colour", "accent color", "wallpaper", "background"),
        Native(PanelIds.Multitasking, "Multitasking", "", PanelGroup.Personalisation, "ms-settings:multitasking", "hot corner", "workspaces", "virtual desktops", "snap", "alt+tab", "app switching"),
        Link(PanelIds.Notifications, "Notifications", "", PanelGroup.Personalisation, "ms-settings:notifications", "do not disturb", "focus", "alerts"),

        Link(PanelIds.Apps, "Apps", "", PanelGroup.AppsAndPrivacy, "ms-settings:appsfeatures", "installed", "uninstall", "programs"),
        Link(PanelIds.DefaultApps, "Default Apps", "", PanelGroup.AppsAndPrivacy, "ms-settings:defaultapps", "browser", "file types", "open with"),
        Link(PanelIds.OnlineAccounts, "Online Accounts", "", PanelGroup.AppsAndPrivacy, "ms-settings:emailandaccounts", "email", "microsoft account", "sign in"),
        Link(PanelIds.Sharing, "Sharing", "", PanelGroup.AppsAndPrivacy, "ms-settings:remotedesktop", "remote desktop", "screen sharing"),
        Link(PanelIds.Privacy, "Privacy & Security", "", PanelGroup.AppsAndPrivacy, "ms-settings:privacy", "location", "camera", "permissions", "defender"),

        Link(PanelIds.RegionLanguage, "Region & Language", "", PanelGroup.System, "ms-settings:regionlanguage", "locale", "formats", "translation", "language"),
        Native(PanelIds.DateTime, "Date & Time", "", PanelGroup.System, "ms-settings:dateandtime", "time zone", "timezone", "clock", "24-hour", "calendar"),
        Link(PanelIds.Users, "Users", "", PanelGroup.System, "ms-settings:otherusers", "accounts", "family", "password"),
        Link(PanelIds.Accessibility, "Accessibility", "", PanelGroup.System, "ms-settings:easeofaccess", "a11y", "narrator", "magnifier", "contrast", "on-screen keyboard"),
        Link(PanelIds.WindowsUpdate, "Windows Update", "", PanelGroup.System, "ms-settings:windowsupdate", "updates", "upgrade"),
        Native(PanelIds.About, "About", "", PanelGroup.System, "ms-settings:about", "system", "version", "memory", "processor", "cpu", "graphics", "gpu", "disk", "device name"),

        WinGnome(PanelIds.General, "General", "", "startup", "taskbar", "theme", "center new windows", "focus follows mouse"),
        WinGnome(PanelIds.TopBar, "Top Bar", "", "clock", "battery percentage", "tray icons", "activities button"),
        WinGnome(PanelIds.Dock, "Dock", "", "pinned apps", "icon size", "magnification", "intellihide", "launcher"),
        WinGnome(PanelIds.WindowButtons, "Window Buttons", "", "traffic lights", "title bar", "close", "minimise", "maximise"),
        WinGnome(PanelIds.Activities, "Activities", "", "overview", "hot corner", "super key", "hotkey"),
        WinGnome(PanelIds.Streamline, "Streamline", "", "tweaks", "registry", "debloat", "context menu"),
        WinGnome(PanelIds.AboutWinGnome, "About WinGnome", "", "version", "log", "reset", "quit"),
    ];

    private static readonly Dictionary<string, SettingsPanel> ById = All.ToDictionary(p => p.Id, StringComparer.Ordinal);

    /// <summary>The panel with this id, or null.</summary>
    public static SettingsPanel? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>
    /// What a shortcut to <paramref name="panelId"/> (a top-bar row) launches instead of opening the settings window: a
    /// link panel's Windows Settings page, so a Wi-Fi row doesn't also pop up the settings app. Null for native panels
    /// and WinGnome pages, which the window shows, and for unknown ids, which the window logs.
    /// </summary>
    public static string? DirectLinkFor(string panelId) =>
        Find(panelId) is { Kind: PanelKind.Link } panel ? panel.LinkUri : null;

    /// <summary>Heading of a sidebar group.</summary>
    public static string GroupTitle(PanelGroup group) => group switch
    {
        PanelGroup.Connectivity => "Connectivity",
        PanelGroup.Devices => "Devices",
        PanelGroup.Personalisation => "Personalisation",
        PanelGroup.AppsAndPrivacy => "Apps and privacy",
        PanelGroup.System => "System",
        PanelGroup.WinGnome => "WinGnome",
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };

    /// <summary>
    /// Panels matching <paramref name="query"/>, best first; ties keep catalogue order. Titles match fuzzily
    /// (see <see cref="FuzzyMatcher"/>); keywords only when they contain the query, so a few scattered letters don't
    /// pull in every panel with a long keyword list. An empty query returns every panel in catalogue order.
    /// </summary>
    public static IReadOnlyList<SettingsPanel> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return All;
        }

        return All
            .Select((panel, index) => (Panel: panel, Score: ScoreOf(panel, query), Index: index))
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Index)
            .Select(s => s.Panel)
            .ToList();
    }

    private static int ScoreOf(SettingsPanel panel, string query)
    {
        var keywordScore = panel.Keywords
            .Select(k => FuzzyMatcher.Score(query, k))
            .Where(s => s >= FuzzyMatcher.ContainsScore)
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max(FuzzyMatcher.Score(query, panel.Title), keywordScore);
    }

    private static SettingsPanel Native(string id, string title, string icon, PanelGroup group, string fallback, params string[] keywords) =>
        new(id, title, icon, group, PanelKind.Native, fallback, keywords);

    private static SettingsPanel Link(string id, string title, string icon, PanelGroup group, string uri, params string[] keywords) =>
        new(id, title, icon, group, PanelKind.Link, uri, keywords);

    private static SettingsPanel WinGnome(string id, string title, string icon, params string[] keywords) =>
        new(id, title, icon, PanelGroup.WinGnome, PanelKind.Native, null, keywords);
}
