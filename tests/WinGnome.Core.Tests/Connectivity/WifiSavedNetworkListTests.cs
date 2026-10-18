using System.Text;
using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class WifiSavedNetworkListTests
{
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static WifiSavedProfile Saved(
        string profile, string ssid, string authentication = "WPA2PSK", string encryption = "AES", bool auto = true,
        bool oneX = false, bool groupPolicy = false, bool perUser = false) =>
        new(profile, new WifiProfileSummary(profile, Bytes(ssid), authentication, encryption, auto, oneX), groupPolicy, perUser);

    private static WifiSavedProfile Unreadable(string profile, bool groupPolicy = false) => new(profile, null, groupPolicy, false);

    private static WifiNetworkRow Visible(string ssid, int level) =>
        new(Bytes(ssid), ssid, ssid, level, WifiProfileKind.Wpa2Psk, 4, true, true, false);

    private static List<string> ProfileNames(IReadOnlyList<WifiSavedNetworkRow> rows) => rows.Select(r => r.ProfileName).ToList();

    [Fact]
    public void Build_NoProfiles_IsEmpty()
    {
        Assert.Empty(WifiSavedNetworkList.Build([], null, null));
        Assert.Empty(WifiSavedNetworkList.Build([], Bytes("Home"), [Visible("Home", 3)]));
    }

    [Fact]
    public void Build_OneProfile_FillsEveryField()
    {
        var rows = WifiSavedNetworkList.Build([Saved("Home", "Home", auto: false)], null, null);

        var row = Assert.Single(rows);
        Assert.Equal("Home", row.ProfileName);
        Assert.Equal("Home", row.Name);
        Assert.Equal(Bytes("Home"), row.Ssid);
        Assert.Equal(WifiProfileKind.Wpa2Psk, row.Kind);
        Assert.False(row.AutoConnect);
        Assert.False(row.IsConnected);
        Assert.True(row.CanEdit);
        Assert.Null(row.SignalLevel);
        Assert.Equal("Saved · WPA2", row.Subtitle);
    }

    [Fact]
    public void Build_AutoConnectProfile_ShowsTheSwitchOn() =>
        Assert.True(Assert.Single(WifiSavedNetworkList.Build([Saved("Home", "Home", auto: true)], null, null)).AutoConnect);

    [Fact]
    public void Build_ConnectedProfile_ComesFirstAndSaysConnected()
    {
        var rows = WifiSavedNetworkList.Build(
            [Saved("Alpha", "Alpha"), Saved("Beta", "Beta"), Saved("Gamma", "Gamma")],
            Bytes("Beta"),
            null);

        Assert.Equal(["Beta", "Alpha", "Gamma"], ProfileNames(rows));
        Assert.True(rows[0].IsConnected);
        Assert.Equal("Connected · WPA2", rows[0].Subtitle);
        Assert.False(rows[1].IsConnected);
        Assert.False(rows[2].IsConnected);
    }

    [Fact]
    public void Build_Many_OrdersByNameIgnoringCaseThenOrdinal()
    {
        var rows = WifiSavedNetworkList.Build(
            [Saved("beta", "beta"), Saved("alpha", "alpha"), Saved("Beta", "Beta"), Saved("Alpha", "Alpha"), Saved("Zed", "Zed")],
            null,
            null);

        Assert.Equal(["Alpha", "alpha", "Beta", "beta", "Zed"], ProfileNames(rows));
    }

    [Fact]
    public void Build_SameNameTwice_KeepsTheirOriginalOrder()
    {
        var rows = WifiSavedNetworkList.Build([Saved("Home 2", "Home"), Saved("Home", "Home")], null, null);

        Assert.Equal(["Home 2", "Home"], ProfileNames(rows));
    }

    [Fact]
    public void Build_TwoProfilesForTheConnectedSsid_MarksOnlyTheFirstOne()
    {
        var rows = WifiSavedNetworkList.Build(
            [Saved("Cafe", "Cafe"), Saved("Home", "Home"), Saved("Home 2", "Home")],
            Bytes("Home"),
            null);

        Assert.Equal(["Home", "Cafe", "Home 2"], ProfileNames(rows));
        Assert.Equal([true, false, false], rows.Select(r => r.IsConnected).ToArray());
    }

    [Fact]
    public void Build_SsidThatDiffersOnlyInCase_IsAnotherNetwork()
    {
        var rows = WifiSavedNetworkList.Build([Saved("home", "home")], Bytes("Home"), null);

        Assert.False(Assert.Single(rows).IsConnected);
    }

    [Fact]
    public void Build_NonAsciiSsid_MatchesTheConnectedNetworkByBytesAndShowsItsText()
    {
        byte[] cafe = [0x43, 0x61, 0x66, 0xC3, 0xA9, 0x20, 0xE2, 0x98, 0x95];

        var rows = WifiSavedNetworkList.Build(
            [Saved("Café ☕ 2", "Café ☕"), Saved("Other", "Other")],
            cafe,
            [Visible("Café ☕", 2)]);

        var row = rows[0];
        Assert.Equal("Café ☕ 2", row.ProfileName);
        Assert.Equal("Café ☕", row.Name);
        Assert.True(row.IsConnected);
        Assert.Equal(2, row.SignalLevel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[0])]
    public void Build_NoCurrentSsid_NothingIsConnected(byte[]? current)
    {
        var rows = WifiSavedNetworkList.Build([Saved("Home", "Home"), new WifiSavedProfile("Blank", new WifiProfileSummary("Blank", [], "open", "none", true, false), false, false)], current, null);

        Assert.All(rows, r => Assert.False(r.IsConnected));
    }

    [Fact]
    public void Build_SignalComesOnlyFromAVisibleRowWithTheSameSsid()
    {
        var rows = WifiSavedNetworkList.Build(
            [Saved("Home", "Home"), Saved("Cafe", "Cafe"), Saved("Away", "Away")],
            null,
            [Visible("Home", 3), Visible("Cafe", 0), Visible("Stranger", 4)]);

        var byProfile = rows.ToDictionary(r => r.ProfileName, r => r.SignalLevel);
        Assert.Equal(3, byProfile["Home"]);
        Assert.Equal(0, byProfile["Cafe"]);
        Assert.Null(byProfile["Away"]);
    }

    [Fact]
    public void Build_SsidInTheVisibleListTwice_UsesTheFirstRow()
    {
        var rows = WifiSavedNetworkList.Build([Saved("Home", "Home")], null, [Visible("Home", 3), Visible("Home", 1)]);

        Assert.Equal(3, Assert.Single(rows).SignalLevel);
    }

    [Fact]
    public void Build_EmptyVisibleList_GivesNoSignals()
    {
        var rows = WifiSavedNetworkList.Build([Saved("Home", "Home")], Bytes("Home"), []);

        Assert.Null(Assert.Single(rows).SignalLevel);
    }

    [Fact]
    public void Build_SignalIsMatchedByBytesNotByCase()
    {
        var rows = WifiSavedNetworkList.Build([Saved("home", "home")], null, [Visible("Home", 4)]);

        Assert.Null(Assert.Single(rows).SignalLevel);
    }

    [Fact]
    public void Build_GroupPolicyProfile_CannotBeEditedAndSaysSo()
    {
        var rows = WifiSavedNetworkList.Build([Saved("Corp", "Corp", groupPolicy: true), Saved("Home", "Home")], null, null);

        var corp = rows.Single(r => r.ProfileName == "Corp");
        Assert.False(corp.CanEdit);
        Assert.Equal("Saved · WPA2 · Managed by your organisation", corp.Subtitle);
        Assert.True(rows.Single(r => r.ProfileName == "Home").CanEdit);
    }

    [Fact]
    public void Build_ConnectedGroupPolicyProfile_SaysConnectedAndManaged()
    {
        var rows = WifiSavedNetworkList.Build([Saved("Corp", "Corp", groupPolicy: true)], Bytes("Corp"), null);

        var row = Assert.Single(rows);
        Assert.True(row.IsConnected);
        Assert.False(row.CanEdit);
        Assert.Equal("Connected · WPA2 · Managed by your organisation", row.Subtitle);
    }

    [Fact]
    public void Build_PerUserProfile_SaysThisUserOnlyButCanBeEdited()
    {
        var row = Assert.Single(WifiSavedNetworkList.Build([Saved("Mine", "Mine", perUser: true)], null, null));

        Assert.True(row.CanEdit);
        Assert.Equal("Saved · WPA2 · This user only", row.Subtitle);
    }

    [Theory]
    [InlineData("open", "none", false, WifiProfileKind.Open, "Saved · Open")]
    [InlineData("OWE", "AES", false, WifiProfileKind.Owe, "Saved · Enhanced Open")]
    [InlineData("open", "WEP", false, WifiProfileKind.Wep, "Saved · WEP")]
    [InlineData("WPAPSK", "TKIP", false, WifiProfileKind.WpaPsk, "Saved · WPA")]
    [InlineData("WPA3SAE", "AES", false, WifiProfileKind.Wpa3Sae, "Saved · WPA3")]
    [InlineData("WPA2", "AES", true, WifiProfileKind.HandOff, "Saved · Enterprise")]
    public void Build_SecurityOfTheProfile_GivesTheKindAndSubtitle(string authentication, string encryption, bool oneX, WifiProfileKind kind, string subtitle)
    {
        var row = Assert.Single(WifiSavedNetworkList.Build([Saved("Net", "Net", authentication, encryption, oneX: oneX)], null, null));

        Assert.Equal(kind, row.Kind);
        Assert.Equal(subtitle, row.Subtitle);
        Assert.True(row.CanEdit);
    }

    [Fact]
    public void Build_UnreadableProfile_ListsByProfileNameAsHandOffWithoutASwitch()
    {
        var rows = WifiSavedNetworkList.Build([Unreadable("Broken")], Bytes("Broken"), [Visible("Broken", 4)]);

        var row = Assert.Single(rows);
        Assert.Equal("Broken", row.ProfileName);
        Assert.Equal("Broken", row.Name);
        Assert.Empty(row.Ssid);
        Assert.Equal(WifiProfileKind.HandOff, row.Kind);
        Assert.False(row.AutoConnect);
        Assert.False(row.IsConnected);
        Assert.False(row.CanEdit);
        Assert.Null(row.SignalLevel);
        Assert.Equal("Saved", row.Subtitle);
    }

    [Fact]
    public void Build_UnreadableGroupPolicyProfile_StillSaysManaged()
    {
        var row = Assert.Single(WifiSavedNetworkList.Build([Unreadable("Corp", groupPolicy: true)], null, null));

        Assert.Equal("Saved · Managed by your organisation", row.Subtitle);
    }

    [Fact]
    public void Build_ProfileWithoutSsid_IsNamedAfterTheProfile()
    {
        var blank = new WifiSavedProfile("Blank", new WifiProfileSummary("Blank", [], "open", "none", true, false), false, false);

        var row = Assert.Single(WifiSavedNetworkList.Build([blank], null, null));

        Assert.Equal("Blank", row.Name);
        Assert.Empty(row.Ssid);
        Assert.Equal("Saved · Open", row.Subtitle);
    }

    [Fact]
    public void Build_OrdersUnreadableProfilesByTheirProfileName()
    {
        var rows = WifiSavedNetworkList.Build([Saved("Zed", "Zed"), Unreadable("Middle"), Saved("Alpha", "Alpha")], null, null);

        Assert.Equal(["Alpha", "Middle", "Zed"], ProfileNames(rows));
    }

    [Fact]
    public void Build_NullProfiles_Throws() =>
        Assert.Throws<ArgumentNullException>(() => WifiSavedNetworkList.Build(null!, null, null));
}
