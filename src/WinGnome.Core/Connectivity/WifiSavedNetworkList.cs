namespace WinGnome.Core.Connectivity;

/// <summary>One entry of <c>WlanGetProfileList</c> with its parsed profile.</summary>
/// <param name="ProfileName">The profile's name from the list.</param>
/// <param name="Summary">The parsed profile, or null when its XML couldn't be read.</param>
/// <param name="IsGroupPolicy">Set by an organisation's Group Policy: Windows refuses to change or delete it.</param>
/// <param name="IsPerUser">Visible to the current user only.</param>
public sealed record WifiSavedProfile(string ProfileName, WifiProfileSummary? Summary, bool IsGroupPolicy, bool IsPerUser);

/// <summary>One row of the Saved Networks list.</summary>
/// <param name="ProfileName">What connect, forget and the connect-mode edit take.</param>
/// <param name="Name">The name shown: the network's name, or the profile name when the profile couldn't be read.</param>
/// <param name="Ssid">The network name bytes; empty when the profile couldn't be read.</param>
/// <param name="Kind">The saved security, or <see cref="WifiProfileKind.HandOff"/> (enterprise, unknown or unreadable).</param>
/// <param name="AutoConnect">The profile's connect-automatically setting.</param>
/// <param name="IsConnected">True for the profile of the network this adapter is connected to.</param>
/// <param name="CanEdit">True when the connect-automatically switch and Forget are offered.</param>
/// <param name="SignalLevel">0 to 4 bars when the network is in the visible list, else null.</param>
/// <param name="Subtitle">"Connected · WPA2", "Saved · Open" ... the line under the name.</param>
public sealed record WifiSavedNetworkRow(
    string ProfileName,
    string Name,
    byte[] Ssid,
    WifiProfileKind Kind,
    bool AutoConnect,
    bool IsConnected,
    bool CanEdit,
    int? SignalLevel,
    string Subtitle);

/// <summary>Turns the saved profiles into the rows of the Saved Networks list.</summary>
public static class WifiSavedNetworkList
{
    /// <summary>
    /// One row per saved profile, the connected one first, then by name (ignoring case, then ordinal; equal names keep
    /// their profile order).
    /// </summary>
    /// <param name="saved">Every saved profile, in the order Windows lists them.</param>
    /// <param name="currentSsid">The connected network's name bytes, or null when not connected or unreadable. Only the
    /// first profile with exactly these bytes counts as connected: a second profile for the same network doesn't.</param>
    /// <param name="visible">The Visible Networks rows, or null when Windows withholds the list; they supply the signal bars.</param>
    public static IReadOnlyList<WifiSavedNetworkRow> Build(
        IReadOnlyList<WifiSavedProfile> saved,
        byte[]? currentSsid,
        IReadOnlyList<WifiNetworkRow>? visible)
    {
        ArgumentNullException.ThrowIfNull(saved);
        var currentKey = currentSsid is { Length: > 0 } ? SsidText.ToHex(currentSsid) : null;
        var signals = SignalsBySsid(visible);

        var connectedTaken = false;
        var rows = new List<WifiSavedNetworkRow>(saved.Count);
        foreach (var profile in saved)
        {
            var summary = profile.Summary;
            var ssidKey = summary is { Ssid.Length: > 0 } ? SsidText.ToHex(summary.Ssid) : null;
            var connected = !connectedTaken && ssidKey is not null && ssidKey == currentKey;
            connectedTaken |= connected;

            var kind = summary is null ? WifiProfileKind.HandOff : WifiSecurity.FromProfile(summary.Authentication, summary.Encryption, summary.UsesOneX);
            int? signal = ssidKey is not null && signals.TryGetValue(ssidKey, out var level) ? level : null;
            rows.Add(new WifiSavedNetworkRow(
                profile.ProfileName,
                ssidKey is null ? profile.ProfileName : SsidText.Display(summary!.Ssid),
                summary?.Ssid ?? [],
                kind,
                summary?.AutoConnect ?? false,
                connected,
                CanEdit: summary is not null && !profile.IsGroupPolicy,
                signal,
                Subtitle(profile, kind, connected)));
        }

        return rows
            .OrderByDescending(r => r.IsConnected)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }

    // The first visible row for an SSID wins; the network list already merges duplicates.
    private static Dictionary<string, int> SignalsBySsid(IReadOnlyList<WifiNetworkRow>? visible)
    {
        var signals = new Dictionary<string, int>();
        foreach (var row in visible ?? [])
        {
            signals.TryAdd(SsidText.ToHex(row.Ssid), row.SignalLevel);
        }

        return signals;
    }

    private static string Subtitle(WifiSavedProfile profile, WifiProfileKind kind, bool connected)
    {
        var state = connected ? "Connected" : "Saved";
        // An unreadable profile has no security to name, and "Enterprise" would be a guess.
        var text = profile.Summary is null ? state : $"{state} · {WifiSecurity.Label(kind)}";
        if (profile.IsGroupPolicy)
        {
            text += " · Managed by your organisation";
        }
        else if (profile.IsPerUser)
        {
            text += " · This user only";
        }

        return text;
    }
}
