using WinGnome.Core.TopBar;

namespace WinGnome.Core.Tests.TopBar;

public class NetworkStatusTests
{
    private static NetworkAdapter Up(NetworkAdapterKind kind) => new(kind, IsUp: true, HasGateway: true);

    [Fact]
    public void NoAdapters_IsDisconnected()
    {
        Assert.Equal(NetworkConnection.Disconnected, NetworkStatus.Evaluate([]));
    }

    [Fact]
    public void WirelessOnly()
    {
        Assert.Equal(NetworkConnection.Wireless, NetworkStatus.Evaluate([Up(NetworkAdapterKind.Wireless)]));
    }

    [Fact]
    public void WiredWinsOverWireless_InAnyOrder()
    {
        Assert.Equal(NetworkConnection.Wired, NetworkStatus.Evaluate([Up(NetworkAdapterKind.Wireless), Up(NetworkAdapterKind.Wired)]));
        Assert.Equal(NetworkConnection.Wired, NetworkStatus.Evaluate([Up(NetworkAdapterKind.Wired), Up(NetworkAdapterKind.Wireless)]));
    }

    [Fact]
    public void AdaptersWithoutGatewayOrDown_DoNotCount()
    {
        NetworkAdapter[] adapters =
        [
            new(NetworkAdapterKind.Wired, IsUp: true, HasGateway: false),
            new(NetworkAdapterKind.Wireless, IsUp: false, HasGateway: true),
        ];

        Assert.Equal(NetworkConnection.Disconnected, NetworkStatus.Evaluate(adapters));
    }

    [Fact]
    public void IgnoredAdapters_DoNotCount()
    {
        Assert.Equal(NetworkConnection.Wireless, NetworkStatus.Evaluate([Up(NetworkAdapterKind.Ignored), Up(NetworkAdapterKind.Wireless)]));
        Assert.Equal(NetworkConnection.Disconnected, NetworkStatus.Evaluate([Up(NetworkAdapterKind.Ignored)]));
    }

    [Theory]
    [InlineData("Hyper-V Virtual Ethernet Adapter", true)]
    [InlineData("VMware Virtual Ethernet Adapter for VMnet8", true)]
    [InlineData("VirtualBox Host-Only Ethernet Adapter", true)]
    [InlineData("TAP-Windows Adapter V9", true)]
    [InlineData("WireGuard Tunnel", true)]
    [InlineData("Tailscale Tunnel", true)]
    [InlineData("Bluetooth Device (Personal Area Network)", true)]
    [InlineData("Intel(R) Ethernet Connection (7) I219-V", false)]
    [InlineData("Realtek PCIe GbE Family Controller", false)]
    [InlineData("Intel(R) Wi-Fi 6 AX201 160MHz", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsVirtualAdapter(string? description, bool expected)
    {
        Assert.Equal(expected, NetworkStatus.IsVirtualAdapter(description));
    }
}
