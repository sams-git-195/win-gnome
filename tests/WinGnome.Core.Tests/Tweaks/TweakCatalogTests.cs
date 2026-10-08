using WinGnome.Core.Tweaks;

namespace WinGnome.Core.Tests.Tweaks;

public class TweakCatalogTests
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ContentDelivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    private static TweakDefinition Get(string id) => Assert.IsType<TweakDefinition>(TweakCatalog.Find(id));

    private static Dictionary<(string SubKey, string? Name), RegistryValue> Map(TweakDefinition t) =>
        t.Changes.ToDictionary(c => (c.SubKey, c.ValueName), c => c.Value);

    [Fact]
    public void ContainsExactlyTheExpectedIds_InOrder()
    {
        Assert.Equal(
            [
                "dark-mode",
                "gnome-accent",
                "neutral-chrome",
                "hide-desktop-icons",
                "hide-spotlight-icon",
                "classic-context-menu",
                "disable-web-search",
                "start-no-recommendations",
                "no-suggestions",
                "disable-aero-shake",
                "show-file-extensions",
                "disable-snap-flyout",
                "explorer-this-pc",
                "taskbar-hide-search",
                "taskbar-hide-task-view",
                "taskbar-hide-copilot",
                "taskbar-center-icons",
            ],
            TweakCatalog.All.Select(t => t.Id));
    }

    [Theory]
    [InlineData("taskbar-hide-search", @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 0)]
    [InlineData("taskbar-hide-task-view", Advanced, "ShowTaskViewButton", 0)]
    [InlineData("taskbar-hide-copilot", Advanced, "ShowCopilotButton", 0)]
    [InlineData("taskbar-center-icons", Advanced, "TaskbarAl", 1)]
    public void TaskbarTweaks_WriteSingleSupportedDWord(string id, string subKey, string name, int value)
    {
        var t = Get(id);
        Assert.Equal(TweakCategory.Taskbar, t.Category);
        Assert.False(t.RequiresExplorerRestart);
        var change = Assert.Single(t.Changes);
        Assert.Equal(subKey, change.SubKey);
        Assert.Equal(name, change.ValueName);
        Assert.Equal(RegistryValue.DWord(value), change.Value);
    }

    [Fact]
    public void HideWidgets_WasRemoved()
    {
        Assert.Null(TweakCatalog.Find("hide-widgets"));
    }

    [Fact]
    public void Ids_AreUnique_CaseInsensitively()
    {
        Assert.Equal(TweakCatalog.All.Count, TweakCatalog.All.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryTweak_HasTitleDescriptionAndChanges()
    {
        foreach (var t in TweakCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Title), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Description), t.Id);
            Assert.EndsWith(".", t.Description);
            Assert.NotEmpty(t.Changes);
            Assert.Matches("^[a-z]+(-[a-z]+)*$", t.Id);
        }
    }

    [Fact]
    public void EveryChange_TargetsAnHkcuSubKey_WithAValidValue()
    {
        foreach (var change in TweakCatalog.All.SelectMany(t => t.Changes))
        {
            Assert.True(
                change.SubKey.StartsWith(@"Software\", StringComparison.Ordinal) || change.SubKey.StartsWith(@"Control Panel\", StringComparison.Ordinal),
                change.SubKey);
            Assert.DoesNotContain("HKEY_", change.SubKey);
            Assert.False(change.SubKey.StartsWith('\\') || change.SubKey.EndsWith('\\'));
            Assert.NotNull(change.Value);
            Assert.NotNull(change.Value.Data);
        }
    }

    [Fact]
    public void NoTweak_WritesTheSameValueTwice()
    {
        foreach (var t in TweakCatalog.All)
        {
            var keys = t.Changes.Select(c => (c.SubKey.ToLowerInvariant(), c.ValueName?.ToLowerInvariant())).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }
    }

    [Fact]
    public void AllValuesAreDWords_ExceptTheClassicMenuStringAndTheAccentPalette()
    {
        foreach (var t in TweakCatalog.All.Where(t => t.Id != "classic-context-menu"))
        {
            Assert.All(t.Changes.Where(c => c.ValueName != "AccentPalette"), c => Assert.Equal(RegistryValueKind.DWord, c.Value.Kind));
        }
    }

    [Fact]
    public void Find_IsCaseInsensitive_AndReturnsNullForUnknown()
    {
        Assert.Same(Get("dark-mode"), TweakCatalog.Find("DARK-MODE"));
        Assert.Null(TweakCatalog.Find("nope"));
        Assert.Null(TweakCatalog.Find(""));
    }

    [Fact]
    public void DarkMode()
    {
        var t = Get("dark-mode");

        Assert.Equal(TweakCategory.Appearance, t.Category);
        Assert.False(t.RequiresExplorerRestart);
        Assert.True(t.BroadcastThemeChange);
        Assert.False(t.DeleteKeyOnRevertIfCreated);
        var map = Map(t);
        var key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        Assert.Equal(2, map.Count);
        Assert.Equal(RegistryValue.DWord(0), map[(key, "AppsUseLightTheme")]);
        Assert.Equal(RegistryValue.DWord(0), map[(key, "SystemUsesLightTheme")]);
    }

    [Fact]
    public void OnlyTheColourTweaks_BroadcastThemeChanges()
    {
        Assert.Equal(["dark-mode", "gnome-accent", "neutral-chrome"], TweakCatalog.All.Where(t => t.BroadcastThemeChange).Select(t => t.Id));
    }

    [Fact]
    public void ClassicContextMenu()
    {
        var t = Get("classic-context-menu");

        Assert.True(t.RequiresExplorerRestart);
        Assert.True(t.DeleteKeyOnRevertIfCreated);
        var change = Assert.Single(t.Changes);
        Assert.Equal(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", change.SubKey);
        Assert.Null(change.ValueName);
        Assert.Equal(RegistryValue.Text(""), change.Value);
    }

    [Fact]
    public void OnlyTheClassicMenu_DeletesItsKeyOnRevert()
    {
        Assert.Equal(["classic-context-menu"], TweakCatalog.All.Where(t => t.DeleteKeyOnRevertIfCreated).Select(t => t.Id));
    }

    [Fact]
    public void DisableWebSearch_IsPolicyOnly_AndMentionsSignOut()
    {
        var t = Get("disable-web-search");

        Assert.Equal(TweakCategory.Privacy, t.Category);
        Assert.True(t.RequiresExplorerRestart);
        var change = Assert.Single(t.Changes);
        Assert.Equal(@"Software\Policies\Microsoft\Windows\Explorer", change.SubKey);
        Assert.Equal("DisableSearchBoxSuggestions", change.ValueName);
        Assert.Equal(RegistryValue.DWord(1), change.Value);
        Assert.Contains("sign out", t.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartNoRecommendations()
    {
        var map = Map(Get("start-no-recommendations"));

        Assert.Equal(4, map.Count);
        foreach (var name in new[] { "Start_IrisRecommendations", "Start_TrackDocs", "Start_TrackProgs", "Start_AccountNotifications" })
        {
            Assert.Equal(RegistryValue.DWord(0), map[(Advanced, name)]);
        }
    }

    [Fact]
    public void NoSuggestions()
    {
        var map = Map(Get("no-suggestions"));
        string[] names =
        [
            "SystemPaneSuggestionsEnabled",
            "SilentInstalledAppsEnabled",
            "SoftLandingEnabled",
            "SubscribedContent-338387Enabled",
            "SubscribedContent-338388Enabled",
            "SubscribedContent-338389Enabled",
            "SubscribedContent-353694Enabled",
            "SubscribedContent-353696Enabled",
            "SubscribedContent-310093Enabled",
        ];

        Assert.Equal(names.Length, map.Count);
        foreach (var name in names)
        {
            Assert.Equal(RegistryValue.DWord(0), map[(ContentDelivery, name)]);
        }
    }

    [Theory]
    [InlineData("disable-aero-shake", "DisallowShaking", 1)]
    [InlineData("show-file-extensions", "HideFileExt", 0)]
    [InlineData("disable-snap-flyout", "EnableSnapAssistFlyout", 0)]
    [InlineData("explorer-this-pc", "LaunchTo", 1)]
    public void SingleValueExplorerAdvancedTweaks(string id, string name, int value)
    {
        var change = Assert.Single(Get(id).Changes);

        Assert.Equal(Advanced, change.SubKey);
        Assert.Equal(name, change.ValueName);
        Assert.Equal(RegistryValue.DWord(value), change.Value);
    }

    [Fact]
    public void ShowFileExtensions_IsABehaviourTweakNeedingRestart()
    {
        var t = Get("show-file-extensions");
        Assert.Equal(TweakCategory.Behaviour, t.Category);
        Assert.True(t.RequiresExplorerRestart);
    }
}
