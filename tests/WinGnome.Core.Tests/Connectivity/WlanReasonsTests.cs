using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class WlanReasonsTests
{
    // Values from wlanapi.h / l2cmn.h: AC_BASE 0x20000, MSMSEC_BASE 0x40000, ONEX_BASE 0x50000, PROFILE_BASE 0x80000;
    // the connect half of a group starts 0x8000 in.
    [Theory]
    [InlineData(0x00000u, WlanReasonClass.Success)]
    [InlineData(0x10001u, WlanReasonClass.Other)]                  // WLAN_REASON_CODE_UNKNOWN
    [InlineData(0x20001u, WlanReasonClass.Other)]                  // NETWORK_NOT_COMPATIBLE
    [InlineData(0x20002u, WlanReasonClass.ProfileInvalid)]         // PROFILE_NOT_COMPATIBLE
    [InlineData(0x28001u, WlanReasonClass.Other)]                  // NO_AUTO_CONNECTION
    [InlineData(0x28002u, WlanReasonClass.NetworkNotAvailable)]    // NOT_VISIBLE
    [InlineData(0x28003u, WlanReasonClass.Other)]                  // GP_DENIED
    [InlineData(0x2800Bu, WlanReasonClass.NetworkNotAvailable)]    // NETWORK_NOT_AVAILABLE
    [InlineData(0x2800Cu, WlanReasonClass.ProfileInvalid)]         // PROFILE_CHANGED_OR_DELETED
    [InlineData(0x2800Du, WlanReasonClass.AuthFailure)]            // KEY_MISMATCH
    [InlineData(0x2800Eu, WlanReasonClass.Other)]                  // USER_NOT_RESPOND
    [InlineData(0x30001u, WlanReasonClass.Other)]                  // MSM range
    [InlineData(0x38005u, WlanReasonClass.Other)]
    [InlineData(0x40002u, WlanReasonClass.ProfileInvalid)]         // MSMSEC_PROFILE_PSK_PRESENT
    [InlineData(0x47FFFu, WlanReasonClass.ProfileInvalid)]
    [InlineData(0x48002u, WlanReasonClass.AuthFailure)]            // MSMSEC_AUTH_START_TIMEOUT
    [InlineData(0x48004u, WlanReasonClass.AuthFailure)]            // MSMSEC_KEY_START_TIMEOUT
    [InlineData(0x48011u, WlanReasonClass.Other)]                  // MSMSEC_CANCELLED
    [InlineData(0x48014u, WlanReasonClass.AuthFailure)]            // MSMSEC_PSK_MISMATCH_SUSPECTED
    [InlineData(0x4FFFFu, WlanReasonClass.AuthFailure)]
    [InlineData(0x50000u, WlanReasonClass.AuthFailure)]            // 802.1X group
    [InlineData(0x5FFFFu, WlanReasonClass.AuthFailure)]
    [InlineData(0x60000u, WlanReasonClass.Other)]                  // 802.3
    [InlineData(0x80001u, WlanReasonClass.ProfileInvalid)]         // profile group
    [InlineData(0x8FFFFu, WlanReasonClass.ProfileInvalid)]
    [InlineData(0x90000u, WlanReasonClass.Other)]                  // IHV
    public void Classify_ByRange(uint code, WlanReasonClass expected) => Assert.Equal(expected, WlanReasons.Classify(code));
}
