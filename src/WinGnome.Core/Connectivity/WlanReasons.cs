namespace WinGnome.Core.Connectivity;

/// <summary>How a <c>WLAN_REASON_CODE</c> from a finished connection attempt is treated.</summary>
public enum WlanReasonClass
{
    Success,
    /// <summary>The key was rejected (wrong password) or the security handshake failed.</summary>
    AuthFailure,
    NetworkNotAvailable,
    /// <summary>The profile itself is invalid or not compatible with the network.</summary>
    ProfileInvalid,
    Other,
}

/// <summary>
/// Sorts <c>WLAN_REASON_CODE</c> values by the ranges <c>wlanapi.h</c> and <c>l2cmn.h</c> define: every group is
/// 0x10000 wide (AC 0x20000, MSM 0x30000, MSMSEC 0x40000, 802.1X 0x50000, profile 0x80000) and the "connect" half
/// of a group starts 0x8000 in.
/// </summary>
public static class WlanReasons
{
    private const uint AcBase = 0x20000;
    private const uint AcConnectBase = AcBase + 0x8000;
    private const uint MsmSecBase = 0x40000;
    private const uint MsmSecConnectBase = MsmSecBase + 0x8000;
    private const uint OneXBase = 0x50000;
    private const uint ProfileBase = 0x80000;
    private const uint GroupSize = 0x10000;

    private const uint ProfileNotCompatible = AcBase + 2;
    private const uint NotVisible = AcConnectBase + 2;
    private const uint NetworkNotAvailable = AcConnectBase + 11;
    private const uint ProfileChangedOrDeleted = AcConnectBase + 12;
    private const uint KeyMismatch = AcConnectBase + 13;
    private const uint MsmSecCancelled = MsmSecConnectBase + 17;

    /// <summary>Classifies <paramref name="reasonCode"/>; zero is success.</summary>
    public static WlanReasonClass Classify(uint reasonCode)
    {
        if (reasonCode == 0)
        {
            return WlanReasonClass.Success;
        }

        return reasonCode switch
        {
            KeyMismatch => WlanReasonClass.AuthFailure,
            NotVisible or NetworkNotAvailable => WlanReasonClass.NetworkNotAvailable,
            ProfileNotCompatible or ProfileChangedOrDeleted => WlanReasonClass.ProfileInvalid,
            MsmSecCancelled => WlanReasonClass.Other,
            // Handshake timeouts, PSK mismatch and 802.1X failures after the profile was accepted: the key is the usual cause.
            _ when Within(reasonCode, MsmSecConnectBase, MsmSecBase + GroupSize) => WlanReasonClass.AuthFailure,
            _ when Within(reasonCode, OneXBase, OneXBase + GroupSize) => WlanReasonClass.AuthFailure,
            // The profile's own security settings (before the connect half) and the profile group.
            _ when Within(reasonCode, MsmSecBase, MsmSecConnectBase) => WlanReasonClass.ProfileInvalid,
            _ when Within(reasonCode, ProfileBase, ProfileBase + GroupSize) => WlanReasonClass.ProfileInvalid,
            _ => WlanReasonClass.Other,
        };
    }

    private static bool Within(uint code, uint inclusiveStart, uint exclusiveEnd) => code >= inclusiveStart && code < exclusiveEnd;
}
