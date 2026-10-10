namespace WinGnome.Core.Connectivity;

/// <summary>What Windows says about this app's access to the data that Wi-Fi calls need (location, from 24H2).</summary>
public enum WifiLocationAccess
{
    /// <summary>The state couldn't be read; the calls are tried and their own refusal handled.</summary>
    Unknown,
    Allowed,
    DeniedByUser,
    DeniedBySystem,
    /// <summary>Windows hasn't asked yet: the first gated call raises its consent prompt.</summary>
    UserPromptRequired,
}

/// <summary>What the Wi-Fi panel may call and what it shows for a given access state.</summary>
/// <param name="CallGated">True when the network list, the current connection and scans may be called now.</param>
/// <param name="ShowNotice">True when the list is replaced by the location notice.</param>
/// <param name="OfferShowNearby">True when the notice carries a "Show nearby networks" button (access not asked yet).</param>
public readonly record struct WifiLocationDecision(bool CallGated, bool ShowNotice, bool OfferShowNearby);

/// <summary>
/// Decides, from the access state, whether the location-gated Wi-Fi calls may run. A denied state must not be probed
/// again (each refused call can re-raise Windows' "Location has been turned off" dialog and light the location
/// icon), and an unasked state must not raise the consent prompt by itself, only when the user presses the button.
/// </summary>
public static class WifiLocationPolicy
{
    /// <param name="access">What Windows reports.</param>
    /// <param name="userAskedForNetworks">True after the user pressed "Show nearby networks" in this panel visit.</param>
    public static WifiLocationDecision Decide(WifiLocationAccess access, bool userAskedForNetworks) => access switch
    {
        WifiLocationAccess.Allowed or WifiLocationAccess.Unknown => new(true, false, false),
        WifiLocationAccess.UserPromptRequired when userAskedForNetworks => new(true, false, false),
        WifiLocationAccess.UserPromptRequired => new(false, true, true),
        _ => new(false, true, false),
    };
}
