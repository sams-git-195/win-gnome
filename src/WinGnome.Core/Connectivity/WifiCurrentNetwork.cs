namespace WinGnome.Core.Connectivity;

/// <summary>The Current network row of the Wi-Fi panel.</summary>
/// <param name="Name">The network's name, or "Connected" when Windows doesn't say which network it is.</param>
/// <param name="Subtitle">"Connected · WPA2" and the like, or why the name is missing.</param>
/// <param name="SignalLevel">0 to 4 bars, or null when nothing reported a signal.</param>
/// <param name="ProfileName">The saved profile in use, or null when it isn't known (no Forget then).</param>
public sealed record WifiCurrentNetwork(string Name, string Subtitle, int? SignalLevel, string? ProfileName);

/// <summary>Puts the Current network row together from what each read could tell, without the gated calls if need be.</summary>
public static class WifiCurrentNetworkBuilder
{
    private const string NameWithheld = "Windows doesn't share the network's name without location access.";

    /// <summary>
    /// Null when the adapter isn't connected. Otherwise the connected saved profile names the network and its security;
    /// without one, the visible list's connected row does, then the connection's name alone. Signal comes from
    /// Windows' own bars first (no location needed), then from the visible list.
    /// </summary>
    /// <param name="isConnected">What the adapter's state says.</param>
    /// <param name="currentSsid">The connected network's name bytes, or null when no read could give them.</param>
    /// <param name="signalBars">Windows' 0 to 5 bars for the connection (<c>ConnectionProfile.GetSignalBars</c>), or null.</param>
    /// <param name="saved">The Saved Networks rows.</param>
    /// <param name="visible">The Visible Networks rows, or null when Windows withholds the list.</param>
    public static WifiCurrentNetwork? Build(
        bool isConnected,
        byte[]? currentSsid,
        int? signalBars,
        IReadOnlyList<WifiSavedNetworkRow> saved,
        IReadOnlyList<WifiNetworkRow>? visible)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (!isConnected)
        {
            return null;
        }

        var savedRow = saved.FirstOrDefault(r => r.IsConnected);
        var visibleRow = visible?.FirstOrDefault(r => r.IsConnected);
        var signal = WifiSignal.LevelFromBars(signalBars) ?? visibleRow?.SignalLevel ?? savedRow?.SignalLevel;

        if (savedRow is not null)
        {
            return new WifiCurrentNetwork(savedRow.Name, savedRow.Subtitle, signal, savedRow.ProfileName);
        }

        if (visibleRow is not null)
        {
            return new WifiCurrentNetwork(visibleRow.Name, visibleRow.Subtitle, signal, visibleRow.ProfileName);
        }

        return currentSsid is { Length: > 0 }
            ? new WifiCurrentNetwork(SsidText.Display(currentSsid), "Connected", signal, null)
            : new WifiCurrentNetwork("Connected", NameWithheld, signal, null);
    }
}
