namespace WinGnome.Core.Connectivity;

/// <summary>Chooses the name of a profile WinGnome creates.</summary>
public static class WifiProfileName
{
    /// <summary>
    /// The SSID text, unless a saved profile already has that name (it belongs to another network, since a profile for
    /// this one would have been matched already): then a short hex suffix of the SSID bytes keeps the two apart.
    /// </summary>
    public static string Choose(string ssidText, ReadOnlySpan<byte> ssid, IReadOnlyCollection<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(existingNames);
        var name = ssidText.Length == 0 ? "Wi-Fi" : ssidText;
        if (!existingNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return name;
        }

        // FNV-1a: stable across runs (string.GetHashCode is not), so the same network always gets the same suffix.
        var hash = 2166136261u;
        foreach (var b in ssid)
        {
            hash = (hash ^ b) * 16777619u;
        }

        return $"{name} ({hash & 0xFFFF:X4})";
    }
}
