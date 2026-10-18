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
        Assert.Empty(WifiSavedNetworkList.Build([], null, null, null));
        Assert.Empty(WifiSavedNetworkList.Build([], Bytes("Home"), null, [Visible("Home", 3)]));
    }

    [Fact]
    public void Build_OneProfile_FillsEveryField()
    {
        var rows = WifiSavedNetworkList.Build([Saved("Home", "Home", auto: false)], null, null, null);

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
        Assert.True(Assert.Single(WifiSavedNetworkList.Build([Saved("Home", "Home", auto: true)], null, null, null)).AutoConnect);

    [Fact]
    public void Build_ConnectedProfile_ComesFirstAndSaysConnected()
    {
        var rows = WifiSavedNetworkList.Build(
            [Saved("Alpha", "Alpha"), Saved("Beta", "Beta"), Saved("Gamma", "Gamma")],
            Bytes("Beta"),
            null,
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
            null,
            null);

        Assert.Equal(["Alpha", "alpha", "Beta", "beta", "Zed"], ProfileNames(rows));
    }

    [Fact]
    public void Build_SameNameTwice_KeepsTheirOriginalOrder()
    {
        var rows = WifiSavedNetworkList.Build([Saved("Home 2", "Home"), Saved("Home", "Home")], null, null, null);

        Assert.Equal(["Home 2", "Home"], ProfileNames(rows));
    }

    [Fact]
    public void Build_TwoProfilesForTheConnectedSsidAndNoProfileName_MarksNeither()
    {
        // Guessing would let Forget or the connect-mode edit act on the wrong profile.
        var rows = WifiSavedNetworkList.Build(
            [Saved("Cafe", "Cafe"), Saved("Home", "Home"), Saved("Home 2", "Home")],
            Bytes("Home"),
            null,
            null);

        Assert.Equal(["Cafe", "Home", "Home 2"], ProfileNames(rows));
        Assert.Equal([false, false, false], rows.Select(r => r.IsConnected).ToArray());
    }

    [Fact]
    public void Build_TwoProfilesForTheConnectedSsid_MarksTheOneWindowsConnectedWith()
    {
        var rows = WifiSavedNetworkList.Build(
            [Saved("Home", "Home"), Saved("Home 2", "Home"), Saved("Cafe", "Cafe")],
            Bytes("Home"),
            "Home 2",
            null);

        Assert.Equal(["Home 2", "Cafe", "Home"], ProfileNames(rows));
        Assert.Equal([true, false, false], rows.Select(r => r.IsConnected).ToArray());
    }

    [Fact]
    public void Build_ProfileNameInAnotherCase_StillMarksIt() =>
        Assert.True(Assert.Single(WifiSavedNetworkList.Build([Saved("Home", "Home")], null, "HOME", null)).IsConnected);

    [Fact]
    public void Build_ProfilesDifferingOnlyInCase_MarksTheExactCaseOne()
    {
        var rows = WifiSavedNetworkList.Build([Saved("Home", "Home"), Saved("home", "Home")], Bytes("Home"), "home", null);

        Assert.Equal("home", rows[0].ProfileName);
        Assert.Equal([true, false], rows.Select(r => r.IsConnected).ToArray());
    }

    [Fact]
    public void Build_ProfileNameWithoutSsid_MarksTheNamedProfile() =>
        Assert.True(Assert.Single(WifiSavedNetworkList.Build([Saved("Home", "Home")], null, "Home", null)).IsConnected);

    [Fact]
    public void Build_UnknownProfileName_FallsBackToAnUnambiguousSsid()
    {
        var rows = WifiSavedNetworkList.Build([Saved("Cafe", "Cafe"), Saved("Home", "Home")], Bytes("Home"), "Gone", null);

        Assert.Equal([true, false], rows.Select(r => r.IsConnected).ToArray());
        Assert.Equal("Home", rows[0].ProfileName);
    }

    [Theory]
    [InlineData("Home", "Home", "Forget \"Home\"?")]
    [InlineData("Home", "Home 2", "Forget \"Home\" (saved as \"Home 2\")?")]
    [InlineData("Home", "home", "Forget \"Home\" (saved as \"home\")?")]
    public void ForgetTitle_NamesTheProfileWhenItDiffers(string network, string profile, string expected) =>
        Assert.Equal(expected, WifiSavedNetworkList.ForgetTitle(network, profile));

    [Fact]
    public void Build_SsidThatDiffersOnlyInCase_IsAnotherNetwork()
    {
        var rows = WifiSavedNetworkList.Build([Saved("home", "home")], Bytes("Home"), null, null);

        Assert.False(Assert.Single(rows).IsConnected);
    }

    [Fact]
    public void Build_NonAsciiSsid_MatchesTheConnectedNetworkByBytesAndShowsItsText()
    {
        byte[] cafe = [0x43, 0x61, 0x66, 0xC3, 0xA9, 0x20, 0xE2, 0x98, 0x95];

        var rows = WifiSavedNetworkList.Build(
            [Saved("Café ☕ 2", "Café ☕"), Saved("Other", "Other")],
            cafe,
            null,
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
        var rows = WifiSavedNetworkList.Build([Saved("Home", "Home"), new WifiSavedProfile("Blank", new WifiProfileSummary("Blank", [], "open", "none", true, false), false, false)], current, null, null);

        Assert.All(rows, r => Assert.False(r.IsConnected));
    }

    [Fact]
    public void Build_SignalComesOnlyFromAVisibleRowWithTheSameSsid()
    {
        var rows = WifiSavedNetworkList.Build(
            [Saved("Home", "Home"), Saved("Cafe", "Cafe"), Saved("Away", "Away")],
            null,
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
        var rows = WifiSavedNetworkList.Build([Saved("Home", "Home")], null, null, [Visible("Home", 3), Visible("Home", 1)]);

        Assert.Equal(3, Assert.Single(rows).SignalLevel);
    }

    [Fact]
    public void Build_EmptyVisibleList_GivesNoSignals()
    {
        var rows = WifiSavedNetworkList.Build([Saved("Home", "Home")], Bytes("Home"), null, []);

        Assert.Null(Assert.Single(rows).SignalLevel);
    }

    [Fact]
    public void Build_SignalIsMatchedByBytesNotByCase()
    {
        var rows = WifiSavedNetworkList.Build([Saved("home", "home")], null, null, [Visible("Home", 4)]);

        Assert.Null(Assert.Single(rows).SignalLevel);
    }

    [Fact]
    public void Build_GroupPolicyProfile_CannotBeEditedAndSaysSo()
    {
        var rows = WifiSavedNetworkList.Build([Saved("Corp", "Corp", groupPolicy: true), Saved("Home", "Home")], null, null, null);

        var corp = rows.Single(r => r.ProfileName == "Corp");
        Assert.False(corp.CanEdit);
        Assert.Equal("Saved · WPA2 · Managed by your organisation", corp.Subtitle);
        Assert.True(rows.Single(r => r.ProfileName == "Home").CanEdit);
    }

    [Fact]
    public void Build_ConnectedGroupPolicyProfile_SaysConnectedAndManaged()
    {
        var rows = WifiSavedNetworkList.Build([Saved("Corp", "Corp", groupPolicy: true)], Bytes("Corp"), null, null);

        var row = Assert.Single(rows);
        Assert.True(row.IsConnected);
        Assert.False(row.CanEdit);
        Assert.Equal("Connected · WPA2 · Managed by your organisation", row.Subtitle);
    }

    [Fact]
    public void Build_PerUserProfile_SaysThisUserOnlyButCanBeEdited()
    {
        var row = Assert.Single(WifiSavedNetworkList.Build([Saved("Mine", "Mine", perUser: true)], null, null, null));

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
        var row = Assert.Single(WifiSavedNetworkList.Build([Saved("Net", "Net", authentication, encryption, oneX: oneX)], null, null, null));

        Assert.Equal(kind, row.Kind);
        Assert.Equal(subtitle, row.Subtitle);
        Assert.True(row.CanEdit);
    }

    [Fact]
    public void Build_UnreadableProfile_ListsByProfileNameAsHandOffWithoutASwitch()
    {
        var rows = WifiSavedNetworkList.Build([Unreadable("Broken")], Bytes("Broken"), null, [Visible("Broken", 4)]);

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
        var row = Assert.Single(WifiSavedNetworkList.Build([Unreadable("Corp", groupPolicy: true)], null, null, null));

        Assert.Equal("Saved · Managed by your organisation", row.Subtitle);
    }

    [Fact]
    public void Build_ProfileWithoutSsid_IsNamedAfterTheProfile()
    {
        var blank = new WifiSavedProfile("Blank", new WifiProfileSummary("Blank", [], "open", "none", true, false), false, false);

        var row = Assert.Single(WifiSavedNetworkList.Build([blank], null, null, null));

        Assert.Equal("Blank", row.Name);
        Assert.Empty(row.Ssid);
        Assert.Equal("Saved · Open", row.Subtitle);
    }

    [Fact]
    public void Build_OrdersUnreadableProfilesByTheirProfileName()
    {
        var rows = WifiSavedNetworkList.Build([Saved("Zed", "Zed"), Unreadable("Middle"), Saved("Alpha", "Alpha")], null, null, null);

        Assert.Equal(["Alpha", "Middle", "Zed"], ProfileNames(rows));
    }

    [Fact]
    public void Build_NullProfiles_Throws() =>
        Assert.Throws<ArgumentNullException>(() => WifiSavedNetworkList.Build(null!, null, null, null));

    [Fact]
    public void ToNetworkRow_SavedSecuredNetwork_IsASavedRowWithItsProfile()
    {
        var saved = new WifiSavedNetworkRow("Home profile", "Home", Bytes("Home"), WifiProfileKind.Wpa3Sae, true, false, true, 3, "Saved · WPA3");

        var row = WifiSavedNetworkList.ToNetworkRow(saved);

        Assert.Equal(new WifiNetworkRow(saved.Ssid, "Home", "Home profile", 3, WifiProfileKind.Wpa3Sae, 0, true, true, false), row);
    }

    [Fact]
    public void ToNetworkRow_OpenNetworkOutOfTheList_HasNoPadlockAndNoBars()
    {
        var saved = new WifiSavedNetworkRow("Cafe", "Cafe", Bytes("Cafe"), WifiProfileKind.Open, false, true, true, null, "Connected · Open");

        var row = WifiSavedNetworkList.ToNetworkRow(saved);

        Assert.Equal(new WifiNetworkRow(saved.Ssid, "Cafe", "Cafe", 0, WifiProfileKind.Open, 0, false, true, true), row);
    }
}
