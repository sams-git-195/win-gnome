using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class BluetoothDeviceListTests
{
    private static BluetoothEndpoint Endpoint(string id, string container, string name, bool paired = true, bool connected = false) =>
        new(id, container, name, paired, connected);

    [Fact]
    public void Rows_Empty_ReturnsNothing() => Assert.Empty(new BluetoothDeviceList().Rows());

    [Fact]
    public void Rows_OnlyPairedEndpointsCount()
    {
        var list = new BluetoothDeviceList();
        list.Upsert(Endpoint("a", "c1", "Headphones"));
        list.Upsert(Endpoint("b", "c2", "Stranger", paired: false));

        Assert.Equal(["Headphones"], list.Rows().Select(r => r.Name).ToList());
    }

    [Fact]
    public void Rows_ConnectedFirstThenByName()
    {
        var list = new BluetoothDeviceList();
        list.Upsert(Endpoint("a", "c1", "Zebra mouse", connected: true));
        list.Upsert(Endpoint("b", "c2", "alpha keyboard"));
        list.Upsert(Endpoint("c", "c3", "Beta speaker"));

        Assert.Equal(["Zebra mouse", "alpha keyboard", "Beta speaker"], list.Rows().Select(r => r.Name).ToList());
    }

    [Fact]
    public void Rows_ClassicAndLowEnergyEndpointsOfOneDevice_BecomeOneRow()
    {
        var list = new BluetoothDeviceList();
        list.Upsert(Endpoint("classic", "phone", "Pixel"));
        list.Upsert(Endpoint("le", "phone", "Pixel", connected: true));

        var row = Assert.Single(list.Rows());
        Assert.True(row.IsConnected);
        Assert.Equal(["classic", "le"], row.EndpointIds);
        Assert.Equal("Connected", row.Status);
    }

    [Fact]
    public void Rows_EndpointWithoutContainer_IsItsOwnRow()
    {
        var list = new BluetoothDeviceList();
        list.Upsert(Endpoint("a", "", "One"));
        list.Upsert(Endpoint("b", "", "Two"));

        Assert.Equal(2, list.Rows().Count);
    }

    [Fact]
    public void Rows_NamelessDevice_IsLeftOut()
    {
        var list = new BluetoothDeviceList();
        list.Upsert(Endpoint("a", "c1", ""));

        Assert.Empty(list.Rows());
    }

    [Fact]
    public void Rows_NameFromWhicheverEndpointHasOne()
    {
        var list = new BluetoothDeviceList();
        list.Upsert(Endpoint("a", "c1", ""));
        list.Upsert(Endpoint("b", "c1", "Watch"));

        Assert.Equal("Watch", Assert.Single(list.Rows()).Name);
    }

    [Fact]
    public void Upsert_UpdateReplacesWhatWasKnown()
    {
        var list = new BluetoothDeviceList();
        list.Upsert(Endpoint("a", "c1", "Mouse"));
        list.Upsert(Endpoint("a", "c1", "Mouse", connected: true));

        var row = Assert.Single(list.Rows());
        Assert.Equal("Not connected", new BluetoothDeviceRow("k", "n", false, []).Status);
        Assert.True(row.IsConnected);
    }

    [Fact]
    public void Upsert_EndpointBecomesUnpaired_LeavesTheList()
    {
        var list = new BluetoothDeviceList();
        list.Upsert(Endpoint("a", "c1", "Mouse"));
        list.Upsert(Endpoint("a", "c1", "Mouse", paired: false));

        Assert.Empty(list.Rows());
    }

    [Fact]
    public void Remove_ForgetsTheEndpoint_AndUnknownIdsAreFine()
    {
        var list = new BluetoothDeviceList();
        list.Upsert(Endpoint("a", "c1", "Mouse"));

        list.Remove("a");
        list.Remove("never-seen");

        Assert.Empty(list.Rows());
    }

    [Fact]
    public void Remove_OneOfTwoEndpoints_KeepsTheRowWithTheOther()
    {
        var list = new BluetoothDeviceList();
        list.Upsert(Endpoint("classic", "phone", "Pixel", connected: true));
        list.Upsert(Endpoint("le", "phone", "Pixel"));

        list.Remove("classic");

        var row = Assert.Single(list.Rows());
        Assert.False(row.IsConnected);
        Assert.Equal(["le"], row.EndpointIds);
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var list = new BluetoothDeviceList();
        list.Upsert(Endpoint("a", "c1", "Mouse"));

        list.Clear();

        Assert.Empty(list.Rows());
    }
}
