namespace WinGnome.Core.Connectivity;

/// <summary>One Bluetooth endpoint as Windows enumerates it: a dual-mode device shows up once per protocol (Classic and LE).</summary>
/// <param name="Id">The endpoint's device id.</param>
/// <param name="ContainerId">The physical device both endpoints belong to; empty when Windows gives none.</param>
/// <param name="Name">The device name; empty while Windows hasn't read it.</param>
/// <param name="IsPaired">Whether the endpoint is paired.</param>
/// <param name="IsConnected">Whether the endpoint is connected.</param>
public sealed record BluetoothEndpoint(string Id, string ContainerId, string Name, bool IsPaired, bool IsConnected);

/// <summary>One row of the paired devices list: all of a device's paired endpoints together.</summary>
/// <param name="Key">The container id (or the endpoint id when Windows gave none); stable while the row lives.</param>
/// <param name="Name">The device name.</param>
/// <param name="IsConnected">True when any of its endpoints is connected.</param>
/// <param name="EndpointIds">The paired endpoints, which Remove Device unpairs.</param>
public sealed record BluetoothDeviceRow(string Key, string Name, bool IsConnected, IReadOnlyList<string> EndpointIds)
{
    /// <summary>"Connected" or "Not connected".</summary>
    public string Status => IsConnected ? "Connected" : "Not connected";
}

/// <summary>
/// The live set of endpoints the device watchers report, merged into the rows of the panel: only paired endpoints
/// count, the two protocols of one device become one row, nameless endpoints are left out, and connected devices
/// come first, then by name.
/// </summary>
public sealed class BluetoothDeviceList
{
    private readonly Dictionary<string, BluetoothEndpoint> _endpoints = new(StringComparer.Ordinal);

    /// <summary>Adds the endpoint or replaces what is known about it (a watcher's Added and Updated events).</summary>
    public void Upsert(BluetoothEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        _endpoints[endpoint.Id] = endpoint;
    }

    /// <summary>Forgets an endpoint (a watcher's Removed event).</summary>
    public void Remove(string id) => _endpoints.Remove(id);

    /// <summary>Forgets everything (the panel closed).</summary>
    public void Clear() => _endpoints.Clear();

    /// <summary>The rows to show now.</summary>
    public IReadOnlyList<BluetoothDeviceRow> Rows() =>
        _endpoints.Values
            .Where(e => e.IsPaired)
            .GroupBy(e => e.ContainerId.Length > 0 ? e.ContainerId : e.Id, StringComparer.Ordinal)
            .Select(group => new BluetoothDeviceRow(
                group.Key,
                group.Select(e => e.Name).FirstOrDefault(name => name.Length > 0) ?? "",
                group.Any(e => e.IsConnected),
                group.Select(e => e.Id).Order(StringComparer.Ordinal).ToList()))
            .Where(row => row.Name.Length > 0)
            .OrderByDescending(row => row.IsConnected)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Key, StringComparer.Ordinal)
            .ToList();
}
