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
    [InlineData(PanelIds.Sound, "ms-settings:sound")]
    [InlineData(PanelIds.Displays, "ms-settings:display")]
    [InlineData(PanelIds.Power, "ms-settings:powersleep")]
    [InlineData(PanelIds.Appearance, "ms-settings:colors")]
    [InlineData(PanelIds.Mouse, "ms-settings:mousetouchpad")]
    [InlineData(PanelIds.Keyboard, "ms-settings:typing")]
    [InlineData(PanelIds.DateTime, "ms-settings:dateandtime")]
    [InlineData(PanelIds.About, "ms-settings:about")]
    [InlineData(PanelIds.Multitasking, "ms-settings:multitasking")]
    [InlineData(PanelIds.Printers, "ms-settings:printers")]
    [InlineData(PanelIds.RemovableMedia, "ms-settings:autoplay")]
    [InlineData(PanelIds.Notifications, "ms-settings:notifications")]
    [InlineData(PanelIds.Apps, "ms-settings:appsfeatures")]
    [InlineData(PanelIds.Privacy, "ms-settings:privacy")]
    [InlineData(PanelIds.RegionLanguage, "ms-settings:regionlanguage")]
    [InlineData(PanelIds.Accessibility, "ms-settings:easeofaccess")]
    [InlineData(PanelIds.WindowsUpdate, "ms-settings:windowsupdate")]
    public void NativeSystemPanels_HaveTheirWindowsSettingsPageAsFallback(string id, string uri)
    {
        var panel = SettingsPanelCatalog.Find(id);

        Assert.NotNull(panel);
        Assert.Equal(PanelKind.Native, panel.Kind);
        Assert.Equal(uri, panel.LinkUri);
    }

    [Fact]
    public void NativeSystemPanels_AllHaveALink()
    {
        // A native panel whose page isn't built (yet) opens this link instead, so it must always have one.
        var withoutLink = SettingsPanelCatalog.All
            .Where(p => p.Kind == PanelKind.Native && p.Group != PanelGroup.WinGnome && p.LinkUri is null)
            .Select(p => p.Id)
            .ToList();

        Assert.Empty(withoutLink);
    }

    [Theory]
    [InlineData(PanelIds.Wifi, "ms-settings:network-wifi")]
    [InlineData(PanelIds.Network, "ms-settings:network-status")]
    [InlineData(PanelIds.Bluetooth, "ms-settings:bluetooth")]
    [InlineData(PanelIds.Colour, "colorcpl.exe")]
    [InlineData(PanelIds.DefaultApps, "ms-settings:defaultapps")]
    [InlineData(PanelIds.OnlineAccounts, "ms-settings:emailandaccounts")]
    [InlineData(PanelIds.Sharing, "ms-settings:remotedesktop")]
    [InlineData(PanelIds.Users, "ms-settings:otherusers")]
    public void LinkedPanels_OpenTheMatchingWindowsPage(string id, string uri)
    {
        var panel = SettingsPanelCatalog.Find(id);

        Assert.NotNull(panel);
        Assert.Equal(PanelKind.Link, panel.Kind);
        Assert.Equal(uri, panel.LinkUri);
    }

    [Theory]
    [InlineData(PanelGroup.Connectivity, new[] { PanelIds.Wifi, PanelIds.Network, PanelIds.Bluetooth })]
    [InlineData(PanelGroup.Devices, new[] { PanelIds.Displays, PanelIds.Sound, PanelIds.Power, PanelIds.Mouse, PanelIds.Keyboard, PanelIds.Printers, PanelIds.RemovableMedia, PanelIds.Colour })]
    [InlineData(PanelGroup.Personalisation, new[] { PanelIds.Appearance, PanelIds.Multitasking, PanelIds.Accessibility })]
    [InlineData(PanelGroup.AppsAndPrivacy, new[] { PanelIds.Apps, PanelIds.Notifications, PanelIds.DefaultApps, PanelIds.OnlineAccounts, PanelIds.Sharing, PanelIds.Privacy })]
    [InlineData(PanelGroup.System, new[] { PanelIds.RegionLanguage, PanelIds.DateTime, PanelIds.Users, PanelIds.WindowsUpdate, PanelIds.About })]
    public void SystemGroups_ListTheirPanelsInGnomeOrder(PanelGroup group, string[] expected)
    {
        var ids = SettingsPanelCatalog.All.Where(p => p.Group == group).Select(p => p.Id).ToList();

        Assert.Equal(expected, ids);
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

    [Theory]
    [InlineData("banners", PanelIds.Notifications)]
    [InlineData("toasts", PanelIds.Notifications)]
    [InlineData("lock screen", PanelIds.Notifications)]
    [InlineData("app notifications", PanelIds.Notifications)]
    [InlineData("dnd", PanelIds.Notifications)]
    [InlineData("default printer", PanelIds.Printers)]
    [InlineData("print queue", PanelIds.Printers)]
    [InlineData("printer properties", PanelIds.Printers)]
    [InlineData("add printer", PanelIds.Printers)]
    [InlineData("startup apps", PanelIds.Apps)]
    [InlineData("autostart", PanelIds.Apps)]
    [InlineData("run at sign-in", PanelIds.Apps)]
    [InlineData("store apps", PanelIds.Apps)]
    [InlineData("packages", PanelIds.Apps)]
    [InlineData("size", PanelIds.Apps)]
    [InlineData("version", PanelIds.Apps)]
    [InlineData("sticky keys", PanelIds.Accessibility)]
    [InlineData("slow keys", PanelIds.Accessibility)]
    [InlineData("bounce keys", PanelIds.Accessibility)]
    [InlineData("filter keys", PanelIds.Accessibility)]
    [InlineData("cursor size", PanelIds.Accessibility)]
    [InlineData("pointer size", PanelIds.Accessibility)]
    [InlineData("text cursor", PanelIds.Accessibility)]
    [InlineData("caret", PanelIds.Accessibility)]
    [InlineData("animations", PanelIds.Accessibility)]
    [InlineData("reduce motion", PanelIds.Accessibility)]
    [InlineData("high contrast", PanelIds.Accessibility)]
    [InlineData("contrast themes", PanelIds.Accessibility)]
    [InlineData("osk", PanelIds.Accessibility)]
    [InlineData("screen reader", PanelIds.Accessibility)]
    [InlineData("region", PanelIds.RegionLanguage)]
    [InlineData("country", PanelIds.RegionLanguage)]
    [InlineData("date format", PanelIds.RegionLanguage)]
    [InlineData("time format", PanelIds.RegionLanguage)]
    [InlineData("first day of week", PanelIds.RegionLanguage)]
    [InlineData("number format", PanelIds.RegionLanguage)]
    [InlineData("display language", PanelIds.RegionLanguage)]
    [InlineData("camera", PanelIds.Privacy)]
    [InlineData("microphone", PanelIds.Privacy)]
    [InlineData("webcam", PanelIds.Privacy)]
    [InlineData("location", PanelIds.Privacy)]
    [InlineData("app permissions", PanelIds.Privacy)]
    [InlineData("screen lock", PanelIds.Privacy)]
    [InlineData("autoplay", PanelIds.RemovableMedia)]
    [InlineData("cd", PanelIds.RemovableMedia)]
    [InlineData("dvd", PanelIds.RemovableMedia)]
    [InlineData("memory card", PanelIds.RemovableMedia)]
    [InlineData("camera", PanelIds.RemovableMedia)]
    [InlineData("media insertion", PanelIds.RemovableMedia)]
    [InlineData("check for updates", PanelIds.WindowsUpdate)]
    [InlineData("pending updates", PanelIds.WindowsUpdate)]
    [InlineData("restart required", PanelIds.WindowsUpdate)]
    [InlineData("update history", PanelIds.WindowsUpdate)]
    public void Search_NewSystemPanelKeywords_FindTheirPanel(string query, string expected)
    {
        var results = SettingsPanelCatalog.Search(query);

        Assert.Contains(expected, results.Select(p => p.Id));
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

    [Theory]
    [InlineData(PanelIds.Wifi, "ms-settings:network-wifi")]
    [InlineData(PanelIds.Bluetooth, "ms-settings:bluetooth")]
    [InlineData(PanelIds.Network, "ms-settings:network-status")]
    [InlineData(PanelIds.Colour, "colorcpl.exe")]
    [InlineData(PanelIds.DefaultApps, "ms-settings:defaultapps")]
    [InlineData(PanelIds.Users, "ms-settings:otherusers")]
    public void DirectLinkFor_LinkPanel_ReturnsItsPage(string panelId, string expected)
    {
        Assert.Equal(expected, SettingsPanelCatalog.DirectLinkFor(panelId));
    }

    [Theory]
    [InlineData(PanelIds.Sound)]
    [InlineData(PanelIds.Displays)]
    [InlineData(PanelIds.Power)]
    [InlineData(PanelIds.DateTime)]
    [InlineData(PanelIds.About)]
    [InlineData(PanelIds.Notifications)]
    [InlineData(PanelIds.Printers)]
    [InlineData(PanelIds.Apps)]
    [InlineData(PanelIds.Accessibility)]
    [InlineData(PanelIds.RegionLanguage)]
    [InlineData(PanelIds.Privacy)]
    [InlineData(PanelIds.RemovableMedia)]
    [InlineData(PanelIds.WindowsUpdate)]
    [InlineData(PanelIds.General)]
    [InlineData("no-such-panel")]
    public void DirectLinkFor_NativePageOrUnknown_ReturnsNull(string panelId)
    {
        Assert.Null(SettingsPanelCatalog.DirectLinkFor(panelId));
    }
}
