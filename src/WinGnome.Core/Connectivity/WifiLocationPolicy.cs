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

/// <summary>What the Wi-Fi panel may call for a given reason (the notice itself comes from <see cref="WifiLocationNotice"/>).</summary>
/// <param name="CallGated">True when the network list, the current connection and scans may be called now.</param>
public readonly record struct WifiLocationDecision(bool CallGated);

/// <summary>
/// Decides, from the gate's reason, whether the location-gated Wi-Fi calls may run. A denied state must not be probed
/// again (each refused call can re-raise Windows' "Location has been turned off" dialog and light the location
/// icon), and an unasked state must not raise the consent prompt by itself, only when the user presses the button.
/// </summary>
public static class WifiLocationPolicy
{
    /// <summary>
    /// Only <see cref="WifiLocationBlock.None"/>, or <see cref="WifiLocationBlock.NotAsked"/> after the user pressed the
    /// button, may call. Every other reason is known from the registry, so no call is made to confirm it.
    /// </summary>
    /// <param name="block">The reason <see cref="WifiLocationGate.Evaluate"/> found.</param>
    /// <param name="userAskedForNetworks">True after the user pressed "Show nearby networks" in this panel visit.</param>
    public static WifiLocationDecision Decide(WifiLocationBlock block, bool userAskedForNetworks) => block switch
    {
        WifiLocationBlock.None => new(true),
        WifiLocationBlock.NotAsked => new(userAskedForNetworks),
        _ => new(false),
    };
}
