using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Tests.ControlCenter;

public class SettingsPanelCatalogTests
{
    [Fact]
    public void All_IdsAreUnique()
    {
        var ids = SettingsPanelCatalog.All.Select(p => p.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void All_IsOrderedByGroup()
    {
        var groups = SettingsPanelCatalog.All.Select(p => p.Group).ToList();

        Assert.Equal(groups.OrderBy(g => g).ToList(), groups);
    }

    [Fact]
    public void All_GroupsAppearInGnomeOrder()
    {
        var groups = SettingsPanelCatalog.All.Select(p => p.Group).Distinct().ToList();

        Assert.Equal(
            [PanelGroup.Connectivity, PanelGroup.Devices, PanelGroup.Personalisation, PanelGroup.AppsAndPrivacy, PanelGroup.System, PanelGroup.WinGnome],
            groups);
    }

    [Theory]
    [InlineData(PanelIds.Sound)]
    [InlineData(PanelIds.Displays)]
    [InlineData(PanelIds.Power)]
    [InlineData(PanelIds.Appearance)]
    [InlineData(PanelIds.Mouse)]
    [InlineData(PanelIds.Keyboard)]
    [InlineData(PanelIds.DateTime)]
    [InlineData(PanelIds.About)]
    [InlineData(PanelIds.Multitasking)]
    public void NativeSystemPanels_HaveAWindowsSettingsFallback(string id)
    {
        var panel = SettingsPanelCatalog.Find(id);

        Assert.NotNull(panel);
        Assert.Equal(PanelKind.Native, panel.Kind);
        Assert.StartsWith("ms-settings:", panel.LinkUri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PanelIds.Wifi, "ms-settings:network-wifi")]
    [InlineData(PanelIds.Network, "ms-settings:network-status")]
    [InlineData(PanelIds.Bluetooth, "ms-settings:bluetooth")]
    [InlineData(PanelIds.Printers, "ms-settings:printers")]
    [InlineData(PanelIds.RemovableMedia, "ms-settings:autoplay")]
    [InlineData(PanelIds.Colour, "colorcpl.exe")]
    [InlineData(PanelIds.Notifications, "ms-settings:notifications")]
    [InlineData(PanelIds.Apps, "ms-settings:appsfeatures")]
    [InlineData(PanelIds.DefaultApps, "ms-settings:defaultapps")]
    [InlineData(PanelIds.OnlineAccounts, "ms-settings:emailandaccounts")]
    [InlineData(PanelIds.Sharing, "ms-settings:remotedesktop")]
    [InlineData(PanelIds.Privacy, "ms-settings:privacy")]
    [InlineData(PanelIds.RegionLanguage, "ms-settings:regionlanguage")]
    [InlineData(PanelIds.Accessibility, "ms-settings:easeofaccess")]
    [InlineData(PanelIds.Users, "ms-settings:otherusers")]
    [InlineData(PanelIds.WindowsUpdate, "ms-settings:windowsupdate")]
    public void LinkedPanels_OpenTheMatchingWindowsPage(string id, string uri)
    {
        var panel = SettingsPanelCatalog.Find(id);

        Assert.NotNull(panel);
        Assert.Equal(PanelKind.Link, panel.Kind);
        Assert.Equal(uri, panel.LinkUri);
    }

    [Fact]
    public void WinGnomePages_AreNativeWithoutALink()
    {
        var pages = SettingsPanelCatalog.All.Where(p => p.Group == PanelGroup.WinGnome).ToList();

        Assert.Equal(
            [PanelIds.General, PanelIds.TopBar, PanelIds.Dock, PanelIds.WindowButtons, PanelIds.Activities, PanelIds.Streamline, PanelIds.AboutWinGnome],
            pages.Select(p => p.Id).ToList());
        Assert.All(pages, p => Assert.Equal(PanelKind.Native, p.Kind));
        Assert.All(pages, p => Assert.Null(p.LinkUri));
    }

    [Theory]
    [InlineData(PanelGroup.Connectivity, "Connectivity")]
    [InlineData(PanelGroup.Devices, "Devices")]
    [InlineData(PanelGroup.Personalisation, "Personalisation")]
    [InlineData(PanelGroup.AppsAndPrivacy, "Apps and privacy")]
    [InlineData(PanelGroup.System, "System")]
    [InlineData(PanelGroup.WinGnome, "WinGnome")]
    public void GroupTitle_NamesEachGroup(PanelGroup group, string expected)
    {
        Assert.Equal(expected, SettingsPanelCatalog.GroupTitle(group));
    }

    [Fact]
    public void Find_UnknownId_ReturnsNull()
    {
        Assert.Null(SettingsPanelCatalog.Find("no-such-panel"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Search_EmptyQuery_ReturnsEveryPanelInCatalogOrder(string query)
    {
        Assert.Equal(SettingsPanelCatalog.All, SettingsPanelCatalog.Search(query));
    }

    [Theory]
    [InlineData("sound", PanelIds.Sound)]
    [InlineData("SOUND", PanelIds.Sound)]
    [InlineData("resolution", PanelIds.Displays)]
    [InlineData("monitor", PanelIds.Displays)]
    [InlineData("wallpaper", PanelIds.Appearance)]
    [InlineData("dark", PanelIds.Appearance)]
    [InlineData("time zone", PanelIds.DateTime)]
    [InlineData("sleep", PanelIds.Power)]
    [InlineData("battery", PanelIds.Power)]
    [InlineData("hot corner", PanelIds.Multitasking)]
    [InlineData("snap", PanelIds.Multitasking)]
    [InlineData("traffic", PanelIds.WindowButtons)]
    [InlineData("memory", PanelIds.About)]
    [InlineData("dok", PanelIds.Dock)]
    public void Search_FindsPanelsByTitleOrKeyword(string query, string expectedFirst)
    {
        var results = SettingsPanelCatalog.Search(query);

        Assert.NotEmpty(results);
        Assert.Equal(expectedFirst, results[0].Id);
    }

    [Fact]
    public void Search_TitleMatchRanksAboveKeywordMatch()
    {
        // "Keyboard" is a title; the Mouse panel only mentions keys nowhere, Accessibility lists "keyboard" as a keyword.
        var results = SettingsPanelCatalog.Search("keyboard");

        Assert.Equal(PanelIds.Keyboard, results[0].Id);
        Assert.Contains(results, p => p.Id == PanelIds.Accessibility);
    }

    [Fact]
    public void Search_KeywordsNeedMoreThanAScatteredSubsequence()
    {
        // "wpr" is a subsequence of the keyword "wallpaper" but should not match it; no title contains it either.
        Assert.Empty(SettingsPanelCatalog.Search("wpr"));
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        Assert.Empty(SettingsPanelCatalog.Search("qqqzz"));
    }

    [Fact]
    public void Search_ExactTitleRanksAbovePrefix()
    {
        var results = SettingsPanelCatalog.Search("about");

        Assert.Equal([PanelIds.About, PanelIds.AboutWinGnome], results.Take(2).Select(p => p.Id).ToList());
    }

    [Fact]
    public void Search_EqualScores_KeepCatalogOrder()
    {
        // Both panels list "hot corner" as a keyword; Multitasking comes first in the catalogue.
        var results = SettingsPanelCatalog.Search("hot corner");

        Assert.Equal([PanelIds.Multitasking, PanelIds.Activities], results.Select(p => p.Id).ToList());
    }
}
