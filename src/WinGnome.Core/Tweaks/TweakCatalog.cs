namespace WinGnome.Core.Tweaks;

/// <summary>The built-in "streamline" tweaks. The data is declarative so it is easy to review and edit.</summary>
public static class TweakCatalog
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string ExplorerPolicy = @"Software\Policies\Microsoft\Windows\Explorer";
    private const string ClassicMenuKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";

    /// <summary>All tweaks in display order.</summary>
    public static IReadOnlyList<TweakDefinition> All { get; } =
    [
        new TweakDefinition(
            "dark-mode",
            "Dark mode",
            "Switches Windows and apps to the dark colour scheme.",
            TweakCategory.Appearance,
            [DWord(Personalize, "AppsUseLightTheme", 0), DWord(Personalize, "SystemUsesLightTheme", 0)],
            RequiresExplorerRestart: false,
            BroadcastThemeChange: true),

        new TweakDefinition(
            "classic-context-menu",
            "Classic context menu",
            "Brings back the full right-click menu instead of the Windows 11 compact one.",
            TweakCategory.Shell,
            [new RegistryChange(ClassicMenuKey, null, RegistryValue.Text(""))],
            RequiresExplorerRestart: true,
            DeleteKeyOnRevertIfCreated: true),

        new TweakDefinition(
            "disable-web-search",
            "Disable web results in search",
            "Stops Start and Search from sending what you type to the web; takes effect after you sign out and back in.",
            TweakCategory.Privacy,
            [DWord(ExplorerPolicy, "DisableSearchBoxSuggestions", 1)],
            RequiresExplorerRestart: true),

        new TweakDefinition(
            "start-no-recommendations",
            "Hide Start recommendations",
            "Removes recommended files, recently used apps and account notifications from the Start menu.",
            TweakCategory.Shell,
            [
                DWord(ExplorerAdvanced, "Start_IrisRecommendations", 0),
                DWord(ExplorerAdvanced, "Start_TrackDocs", 0),
                DWord(ExplorerAdvanced, "Start_TrackProgs", 0),
                DWord(ExplorerAdvanced, "Start_AccountNotifications", 0),
            ],
            RequiresExplorerRestart: false),

        new TweakDefinition(
            "no-suggestions",
            "Turn off suggestions and promoted apps",
            "Stops Windows from suggesting apps and tips in Start, Settings and the lock screen, and from silently installing promoted apps.",
            TweakCategory.Privacy,
            [
                DWord(ContentDelivery, "SystemPaneSuggestionsEnabled", 0),
                DWord(ContentDelivery, "SilentInstalledAppsEnabled", 0),
                DWord(ContentDelivery, "SoftLandingEnabled", 0),
                DWord(ContentDelivery, "SubscribedContent-338387Enabled", 0),
                DWord(ContentDelivery, "SubscribedContent-338388Enabled", 0),
                DWord(ContentDelivery, "SubscribedContent-338389Enabled", 0),
                DWord(ContentDelivery, "SubscribedContent-353694Enabled", 0),
                DWord(ContentDelivery, "SubscribedContent-353696Enabled", 0),
                DWord(ContentDelivery, "SubscribedContent-310093Enabled", 0),
            ],
            RequiresExplorerRestart: false),

        new TweakDefinition(
            "disable-aero-shake",
            "Disable shake to minimise",
            "Stops shaking a window's title bar from minimising all your other windows.",
            TweakCategory.Behaviour,
            [DWord(ExplorerAdvanced, "DisallowShaking", 1)],
            RequiresExplorerRestart: true),

        new TweakDefinition(
            "show-file-extensions",
            "Show file extensions",
            "Always shows the extension (such as .txt or .exe) at the end of file names in File Explorer.",
            TweakCategory.Behaviour,
            [DWord(ExplorerAdvanced, "HideFileExt", 0)],
            RequiresExplorerRestart: true),

        new TweakDefinition(
            "disable-snap-flyout",
            "Disable the snap layouts flyout",
            "Hides the window layout picker that appears when you hover over a window's maximise button.",
            TweakCategory.Behaviour,
            [DWord(ExplorerAdvanced, "EnableSnapAssistFlyout", 0)],
            RequiresExplorerRestart: true),

        new TweakDefinition(
            "explorer-this-pc",
            "Open File Explorer to This PC",
            "Makes File Explorer start on This PC instead of Home.",
            TweakCategory.Behaviour,
            [DWord(ExplorerAdvanced, "LaunchTo", 1)],
            RequiresExplorerRestart: false),
    ];

    /// <summary>Finds a tweak by id (case-insensitive), or null.</summary>
    public static TweakDefinition? Find(string id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    private static RegistryChange DWord(string subKey, string name, int value) =>
        new(subKey, name, RegistryValue.DWord(value));
}
