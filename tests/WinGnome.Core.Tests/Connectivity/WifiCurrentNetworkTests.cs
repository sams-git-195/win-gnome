using System.Text;
using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class WifiCurrentNetworkTests
{
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static WifiSavedNetworkRow Saved(string name, bool connected, int? signal = null) =>
        new(name + "-profile", name, Bytes(name), WifiProfileKind.Wpa3Sae, true, connected, true, signal,
            connected ? "Connected · WPA3" : "Saved · WPA3");

    private static WifiNetworkRow Visible(string name, bool connected, int level) =>
        new(Bytes(name), name, name + "-visible", level, WifiProfileKind.Wpa2Psk, 4, true, true, connected);

    [Fact]
    public void Build_NotConnected_IsNull() =>
        Assert.Null(WifiCurrentNetworkBuilder.Build(false, Bytes("Home"), 5, [Saved("Home", true)], null));

    [Fact]
    public void Build_ConnectedSavedProfile_NamesItWithItsSecurityAndWindowsBars() =>
        Assert.Equal(
            new WifiCurrentNetwork("Home", "Connected · WPA3", 4, "Home-profile"),
            WifiCurrentNetworkBuilder.Build(true, Bytes("Home"), 4, [Saved("Work", false), Saved("Home", true)], null));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 4)]
    public void Build_WindowsBars_MapToTheListsLevels(int bars, int expected) =>
        Assert.Equal(expected, WifiCurrentNetworkBuilder.Build(true, Bytes("Home"), bars, [Saved("Home", true)], null)!.SignalLevel);

    [Fact]
    public void Build_NoWindowsBars_TakesTheVisibleRowsSignal() =>
        Assert.Equal(2, WifiCurrentNetworkBuilder.Build(true, Bytes("Home"), null, [Saved("Home", true, signal: 3)], [Visible("Home", true, 2)])!.SignalLevel);

    [Fact]
    public void Build_NoBarsAndNoVisibleList_TakesTheSavedRowsSignal() =>
        Assert.Equal(3, WifiCurrentNetworkBuilder.Build(true, Bytes("Home"), null, [Saved("Home", true, signal: 3)], null)!.SignalLevel);

    [Fact]
    public void Build_NoSignalAnywhere_IsNull() =>
        Assert.Null(WifiCurrentNetworkBuilder.Build(true, Bytes("Home"), null, [Saved("Home", true)], null)!.SignalLevel);

    [Fact]
    public void Build_NoSavedProfileButVisibleConnectedRow_UsesTheVisibleRow() =>
        Assert.Equal(
            new WifiCurrentNetwork("Cafe", "Connected · WPA2", 3, "Cafe-visible"),
            WifiCurrentNetworkBuilder.Build(true, Bytes("Cafe"), null, [Saved("Home", false)], [Visible("Other", false, 4), Visible("Cafe", true, 3)]));

    [Fact]
    public void Build_OnlyTheConnectionsName_ShowsItWithoutAProfile() =>
        Assert.Equal(
            new WifiCurrentNetwork("Netllar_Fibra_5G_78E66A", "Connected", 4, null),
            WifiCurrentNetworkBuilder.Build(true, Bytes("Netllar_Fibra_5G_78E66A"), 4, [Saved("Home", false)], null));

    [Fact]
    public void Build_ConnectedWithNoNameAtAll_SaysConnectedAndWhy() =>
        Assert.Equal(
            new WifiCurrentNetwork("Connected", "Windows doesn't share the network's name without location access.", null, null),
            WifiCurrentNetworkBuilder.Build(true, null, null, [], null));

    [Fact]
    public void Build_EmptyNameBytes_CountAsNoName() =>
        Assert.Equal("Connected", WifiCurrentNetworkBuilder.Build(true, [], null, [], null)!.Name);
}
