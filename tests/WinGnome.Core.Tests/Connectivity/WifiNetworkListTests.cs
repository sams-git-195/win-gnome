using System.Text;
using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class WifiNetworkListTests
{
    private static WifiAvailableNetwork Net(string name, int quality, string profile = "", bool connected = false, bool hasProfile = false,
        bool secured = true, int auth = 7, int cipher = 4) =>
        new(Encoding.UTF8.GetBytes(name), profile, quality, secured, auth, cipher, connected, hasProfile);

    private static List<string> Names(IReadOnlyList<WifiNetworkRow> rows) => rows.Select(r => r.Name).ToList();

    [Fact]
    public void Build_OrdersConnectedThenSavedThenSignalThenName()
    {
        var rows = WifiNetworkList.Build(
            [
                Net("Strong", 90),
                Net("Weak", 20),
                Net("SavedWeak", 10, "SavedWeak", hasProfile: true),
                Net("Current", 5, "Current", connected: true, hasProfile: true),
                Net("alpha", 60),
                Net("Beta", 60),
            ],
            ["SavedWeak", "Current"],
            null);

        Assert.Equal(["Current", "SavedWeak", "Strong", "alpha", "Beta", "Weak"], Names(rows));
    }

    [Fact]
    public void Build_SameSsidTwice_BecomesOneRowWithTheStrongestSignalAndAnyProfile()
    {
        var rows = WifiNetworkList.Build(
            [Net("Home", 30, "Home", hasProfile: true), Net("Home", 80), Net("Home", 55)],
            ["Home"],
            null);

        var row = Assert.Single(rows);
        Assert.Equal(4, row.SignalLevel);
        Assert.True(row.IsSaved);
        Assert.Equal("Home", row.ProfileName);
    }

    [Fact]
    public void Build_HiddenNetworks_AreDropped()
    {
        var hidden = new WifiAvailableNetwork([], "", 70, true, 7, 4, false, false);
        var zeros = new WifiAvailableNetwork([0, 0, 0], "", 70, true, 7, 4, false, false);

        var rows = WifiNetworkList.Build([hidden, zeros, Net("Visible", 40)], [], null);

        Assert.Equal(["Visible"], Names(rows));
    }

    [Fact]
    public void Build_ProfileNameNotInSavedList_IsNotSaved()
    {
        // A flag without a profile Windows still lists (deleted since the scan) must not show as saved.
        var rows = WifiNetworkList.Build([Net("Gone", 50, "Gone", hasProfile: true)], [], null);

        Assert.False(Assert.Single(rows).IsSaved);
    }

    [Fact]
    public void Build_CurrentSsidKnown_MarksThatRowConnected()
    {
        var rows = WifiNetworkList.Build([Net("A", 90), Net("B", 20)], [], Encoding.UTF8.GetBytes("B"));

        Assert.Equal(["B", "A"], Names(rows));
        Assert.True(rows[0].IsConnected);
        Assert.False(rows[1].IsConnected);
    }

    [Fact]
    public void Build_CurrentSsidUnknown_MarksNothingByName()
    {
        var rows = WifiNetworkList.Build([Net("A", 90), Net("B", 20)], [], null);

        Assert.All(rows, r => Assert.False(r.IsConnected));
    }

    [Fact]
    public void Build_Empty_ReturnsNoRows() => Assert.Empty(WifiNetworkList.Build([], [], null));

    [Fact]
    public void Build_MapsSecurityAndSecuredFlag()
    {
        var rows = WifiNetworkList.Build(
            [Net("Open", 50, secured: false, auth: 1, cipher: 0), Net("Corp", 40, auth: 6), Net("Home", 30, auth: 9)],
            [],
            null);

        // All three are two bars, so they sort by name: Corp, Home, Open.
        Assert.Equal([WifiProfileKind.HandOff, WifiProfileKind.Wpa3Sae, WifiProfileKind.Open], rows.Select(r => r.Kind).ToList());
        Assert.Equal([true, true, false], rows.Select(r => r.IsSecured).ToList());
    }

    [Theory]
    [InlineData(true, true, WifiProfileKind.Wpa2Psk, "Connected · WPA2")]
    [InlineData(false, true, WifiProfileKind.Open, "Saved · Open")]
    [InlineData(false, false, WifiProfileKind.Wpa3Sae, "WPA3")]
    [InlineData(false, false, WifiProfileKind.HandOff, "Enterprise, opens in Windows Settings")]
    public void Subtitle_CombinesStateAndSecurity(bool connected, bool saved, WifiProfileKind kind, string expected)
    {
        var row = new WifiNetworkRow([1], "n", saved ? "n" : null, 2, kind, 4, true, saved, connected);

        Assert.Equal(expected, row.Subtitle);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(25, 1)]
    [InlineData(26, 2)]
    [InlineData(50, 2)]
    [InlineData(51, 3)]
    [InlineData(75, 3)]
    [InlineData(76, 4)]
    [InlineData(100, 4)]
    [InlineData(250, 4)]
    public void SignalLevel_Thresholds(int quality, int expected) => Assert.Equal(expected, WifiSignal.Level(quality));
}
