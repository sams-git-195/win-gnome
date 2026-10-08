namespace WinGnome.Core.TopBar;

/// <summary>What the network indicator shows.</summary>
public enum NetworkConnection
{
    Disconnected,
    Wired,
    Wireless,
}

/// <summary>How an adapter counts towards the indicator.</summary>
public enum NetworkAdapterKind
{
    /// <summary>Loopback, tunnels and virtual switches never decide the indicator.</summary>
    Ignored,
    Wired,
    Wireless,
}

/// <summary>A network adapter snapshot.</summary>
/// <param name="Kind">Classification of the adapter.</param>
/// <param name="IsUp">Operational status is "up".</param>
/// <param name="HasGateway">Has a usable default gateway, i.e. it actually leads somewhere.</param>
public readonly record struct NetworkAdapter(NetworkAdapterKind Kind, bool IsUp, bool HasGateway);

/// <summary>Turns adapter snapshots into the single icon the top bar shows, the way Windows' own tray does.</summary>
public static class NetworkStatus
{
    // Virtual switches, VM host adapters and VPN clients report themselves as Ethernet but must not turn
    // a Wi-Fi laptop's indicator into a wired one.
    private static readonly string[] VirtualAdapterMarkers =
    [
        "virtual",
        "hyper-v",
        "vmware",
        "virtualbox",
        "vpn",
        "tap-",
        "wireguard",
        "tailscale",
        "zerotier",
        "loopback",
        "bluetooth device",
    ];

    /// <summary>
    /// Wired wins over wireless when both are connected (Windows routes through Ethernet by default);
    /// only adapters that are up and have a gateway count.
    /// </summary>
    public static NetworkConnection Evaluate(IEnumerable<NetworkAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        var result = NetworkConnection.Disconnected;
        foreach (var adapter in adapters)
        {
            if (!adapter.IsUp || !adapter.HasGateway)
            {
                continue;
            }

            switch (adapter.Kind)
            {
                case NetworkAdapterKind.Wired:
                    return NetworkConnection.Wired;
                case NetworkAdapterKind.Wireless:
                    result = NetworkConnection.Wireless;
                    break;
            }
        }

        return result;
    }

    /// <summary>True for adapter descriptions of virtual switches, VM host adapters, VPN clients and similar.</summary>
    public static bool IsVirtualAdapter(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        foreach (var marker in VirtualAdapterMarkers)
        {
            if (description.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
