namespace WinGnome.Core.Connectivity;

/// <summary>Who a saved WLAN profile belongs to, which decides how it may be written back.</summary>
public enum WifiProfileScope
{
    /// <summary>Every user of the PC (what Windows Settings saves).</summary>
    AllUsers,
    /// <summary>The current user only.</summary>
    ThisUser,
    /// <summary>Set by Group Policy: Windows refuses to change or delete it.</summary>
    GroupPolicy,
}

/// <summary>Reads and writes the profile flags of <c>WlanGetProfile</c>, <c>WlanGetProfileList</c> and <c>WlanSetProfile</c>.</summary>
public static class WifiProfileScopes
{
    /// <summary><c>WLAN_PROFILE_GROUP_POLICY</c>.</summary>
    public const uint GroupPolicyFlag = 0x1;

    /// <summary><c>WLAN_PROFILE_USER</c>.</summary>
    public const uint UserFlag = 0x2;

    /// <summary>The scope a profile's flags describe; Group Policy wins over the per-user flag.</summary>
    public static WifiProfileScope FromFlags(uint flags) =>
        (flags & GroupPolicyFlag) != 0 ? WifiProfileScope.GroupPolicy
        : (flags & UserFlag) != 0 ? WifiProfileScope.ThisUser
        : WifiProfileScope.AllUsers;

    /// <summary>
    /// The <c>WlanSetProfile</c> flags that overwrite a profile of this scope in place, so no second profile of the same
    /// name appears beside it. Null for Group Policy, which can't be written.
    /// </summary>
    public static uint? OverwriteFlags(WifiProfileScope scope) => scope switch
    {
        WifiProfileScope.AllUsers => 0,
        WifiProfileScope.ThisUser => UserFlag,
        _ => null,
    };
}
