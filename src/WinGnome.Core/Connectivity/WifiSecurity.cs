namespace WinGnome.Core.Connectivity;

/// <summary>What WinGnome can build a profile for; everything else is handed to Windows Settings.</summary>
public enum WifiProfileKind
{
    Open,
    /// <summary>Enhanced Open (OWE): encrypted, no password.</summary>
    Owe,
    Wep,
    WpaPsk,
    Wpa2Psk,
    Wpa3Sae,
    /// <summary>Enterprise (802.1X) or a combination WinGnome doesn't map.</summary>
    HandOff,
}

/// <summary>Maps the algorithms a scan reports to a <see cref="WifiProfileKind"/>.</summary>
public static class WifiSecurity
{
    // DOT11_AUTH_ALGORITHM and DOT11_CIPHER_ALGORITHM values from wlantypes.h.
    private const int AuthOpen = 1;
    private const int AuthSharedKey = 2;
    private const int AuthWpaPsk = 4;
    private const int AuthRsnaPsk = 7;
    private const int AuthWpa3Sae = 9;
    private const int AuthOwe = 10;
    private const int CipherNone = 0;
    private const int CipherWep40 = 1;
    private const int CipherWep104 = 5;
    private const int CipherWep = 0x101;

    /// <summary>
    /// The default authentication and cipher a scan reports for a network. A WPA2/WPA3 transition network reports one
    /// of the two and maps to it (WPA2 for <c>RSNA_PSK</c>, which every client of such a network can use).
    /// </summary>
    public static WifiProfileKind Classify(int authAlgorithm, int cipherAlgorithm, bool securityEnabled)
    {
        if (!securityEnabled)
        {
            return authAlgorithm is AuthOpen or 0 ? WifiProfileKind.Open : WifiProfileKind.HandOff;
        }

        var isWep = cipherAlgorithm is CipherWep40 or CipherWep104 or CipherWep;
        return authAlgorithm switch
        {
            AuthOpen when cipherAlgorithm == CipherNone => WifiProfileKind.Open,
            AuthOpen when isWep => WifiProfileKind.Wep,
            AuthSharedKey => WifiProfileKind.Wep,
            AuthWpaPsk => WifiProfileKind.WpaPsk,
            AuthRsnaPsk => WifiProfileKind.Wpa2Psk,
            AuthWpa3Sae => WifiProfileKind.Wpa3Sae,
            AuthOwe => WifiProfileKind.Owe,
            _ => WifiProfileKind.HandOff,
        };
    }

    /// <summary>True when connecting to a new network of this kind needs a password.</summary>
    public static bool NeedsPassword(WifiProfileKind kind) =>
        kind is WifiProfileKind.Wep or WifiProfileKind.WpaPsk or WifiProfileKind.Wpa2Psk or WifiProfileKind.Wpa3Sae;

    /// <summary>The short label the list shows beside the padlock.</summary>
    public static string Label(WifiProfileKind kind) => kind switch
    {
        WifiProfileKind.Open => "Open",
        WifiProfileKind.Owe => "Enhanced Open",
        WifiProfileKind.Wep => "WEP",
        WifiProfileKind.WpaPsk => "WPA",
        WifiProfileKind.Wpa2Psk => "WPA2",
        WifiProfileKind.Wpa3Sae => "WPA3",
        _ => "Enterprise",
    };
}
