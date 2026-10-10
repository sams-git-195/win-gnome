namespace WinGnome.Core.Connectivity;

/// <summary>One entry of <c>WlanGetAvailableNetworkList</c>: the same SSID appears once per saved profile and per security setup.</summary>
/// <param name="Ssid">The raw network name bytes.</param>
/// <param name="ProfileName">The matching saved profile, or empty.</param>
/// <param name="SignalQuality">0 to 100, as Windows reports it.</param>
/// <param name="SecurityEnabled">Whether the network is secured.</param>
/// <param name="AuthAlgorithm">Default <c>DOT11_AUTH_ALGORITHM</c>.</param>
/// <param name="CipherAlgorithm">Default <c>DOT11_CIPHER_ALGORITHM</c>.</param>
/// <param name="IsConnected"><c>WLAN_AVAILABLE_NETWORK_CONNECTED</c>.</param>
/// <param name="HasProfile"><c>WLAN_AVAILABLE_NETWORK_HAS_PROFILE</c>.</param>
public sealed record WifiAvailableNetwork(
    byte[] Ssid,
    string ProfileName,
    int SignalQuality,
    bool SecurityEnabled,
    int AuthAlgorithm,
    int CipherAlgorithm,
    bool IsConnected,
    bool HasProfile);

/// <summary>One row of the Visible Networks list.</summary>
/// <param name="Ssid">The raw name bytes (what a connect or a new profile needs).</param>
/// <param name="Name">The name as text.</param>
/// <param name="ProfileName">The saved profile's name, or null when the network isn't saved.</param>
/// <param name="SignalLevel">0 to 4 bars.</param>
/// <param name="Kind">The security WinGnome can set up, or <see cref="WifiProfileKind.HandOff"/>.</param>
/// <param name="CipherAlgorithm">The scan's cipher, for building a WPA profile.</param>
/// <param name="IsSecured">True for a padlock.</param>
/// <param name="IsSaved">True when Windows has a profile for it.</param>
/// <param name="IsConnected">True for the network this adapter is connected to.</param>
public sealed record WifiNetworkRow(
    byte[] Ssid,
    string Name,
    string? ProfileName,
    int SignalLevel,
    WifiProfileKind Kind,
    int CipherAlgorithm,
    bool IsSecured,
    bool IsSaved,
    bool IsConnected)
{
    /// <summary>"Connected · WPA2", "Saved · Open" ... the line under the name.</summary>
    public string Subtitle
    {
        get
        {
            var state = IsConnected ? "Connected" : IsSaved ? "Saved" : null;
            var security = Kind == WifiProfileKind.HandOff ? "Enterprise, opens in Windows Settings" : WifiSecurity.Label(Kind);
            return state is null ? security : $"{state} · {security}";
        }
    }
}

/// <summary>Number of signal bars for a quality percentage.</summary>
public static class WifiSignal
{
    /// <summary>1 to 25 is one bar, 26 to 50 two, 51 to 75 three, above that four; zero or less is none.</summary>
    public static int Level(int quality) => quality <= 0 ? 0 : Math.Min(4, (quality + 24) / 25);
}

/// <summary>Turns the raw scan results into the rows the panel lists.</summary>
public static class WifiNetworkList
{
    /// <summary>
    /// Merges the per-profile duplicates of each SSID (strongest signal wins, saved or connected if any entry is),
    /// drops hidden networks, and orders: connected, then saved, then by signal, then by name.
    /// </summary>
    /// <param name="available">The entries Windows reported.</param>
    /// <param name="profileNames">Every saved profile; a network's profile only counts as saved if it is in here.</param>
    /// <param name="currentSsid">The connected network's name when known; null when it can't be read (location denied).</param>
    public static IReadOnlyList<WifiNetworkRow> Build(
        IEnumerable<WifiAvailableNetwork> available,
        IReadOnlyCollection<string> profileNames,
        byte[]? currentSsid)
    {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(profileNames);
        var currentKey = currentSsid is null ? null : SsidText.ToHex(currentSsid);

        var rows = available
            .Where(n => !SsidText.IsHidden(n.Ssid))
            .GroupBy(n => SsidText.ToHex(n.Ssid))
            .Select(group => Merge(group.OrderByDescending(n => n.SignalQuality).ToList(), profileNames, currentKey, group.Key));

        return rows
            .OrderByDescending(r => r.IsConnected)
            .ThenByDescending(r => r.IsSaved)
            .ThenByDescending(r => r.SignalLevel)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static WifiNetworkRow Merge(List<WifiAvailableNetwork> entries, IReadOnlyCollection<string> profileNames, string? currentKey, string key)
    {
        var strongest = entries[0];
        var profile = entries
            .Where(n => n.HasProfile || n.ProfileName.Length > 0)
            .Select(n => n.ProfileName)
            .FirstOrDefault(name => name.Length > 0 && profileNames.Contains(name, StringComparer.OrdinalIgnoreCase));
        var kind = WifiSecurity.Classify(strongest.AuthAlgorithm, strongest.CipherAlgorithm, strongest.SecurityEnabled);
        var connected = entries.Any(n => n.IsConnected) || key == currentKey;
        return new WifiNetworkRow(
            strongest.Ssid,
            SsidText.Display(strongest.Ssid),
            profile,
            WifiSignal.Level(strongest.SignalQuality),
            kind,
            strongest.CipherAlgorithm,
            strongest.SecurityEnabled,
            profile is not null,
            connected);
    }
}
