using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class WifiSecurityTests
{
    [Theory]
    [InlineData(1, 0, false, WifiProfileKind.Open)]
    [InlineData(1, 0, true, WifiProfileKind.Open)]
    [InlineData(1, 1, true, WifiProfileKind.Wep)]
    [InlineData(1, 5, true, WifiProfileKind.Wep)]
    [InlineData(1, 0x101, true, WifiProfileKind.Wep)]
    [InlineData(2, 1, true, WifiProfileKind.Wep)]
    [InlineData(4, 2, true, WifiProfileKind.WpaPsk)]
    [InlineData(4, 4, true, WifiProfileKind.WpaPsk)]
    [InlineData(7, 4, true, WifiProfileKind.Wpa2Psk)]
    [InlineData(9, 4, true, WifiProfileKind.Wpa3Sae)]
    [InlineData(10, 4, true, WifiProfileKind.Owe)]
    public void Classify_KnownAlgorithms_MapsToTheirKind(int auth, int cipher, bool secured, WifiProfileKind expected) =>
        Assert.Equal(expected, WifiSecurity.Classify(auth, cipher, secured));

    [Theory]
    [InlineData(3, 2)]   // WPA (802.1X)
    [InlineData(5, 2)]   // WPA-None (ad hoc)
    [InlineData(6, 4)]   // WPA2 (802.1X)
    [InlineData(8, 4)]   // WPA3 enterprise 192-bit
    [InlineData(11, 4)]  // WPA3 enterprise
    [InlineData(99, 4)]
    [InlineData(1, 4)]   // open authentication with an encrypting cipher is not something WinGnome writes
    public void Classify_EnterpriseAndUnknown_HandOff(int auth, int cipher) =>
        Assert.Equal(WifiProfileKind.HandOff, WifiSecurity.Classify(auth, cipher, securityEnabled: true));

    [Fact]
    public void Classify_UnsecuredButEnterpriseAuth_HandOff() =>
        Assert.Equal(WifiProfileKind.HandOff, WifiSecurity.Classify(6, 0, securityEnabled: false));

    [Theory]
    [InlineData(WifiProfileKind.Open, false)]
    [InlineData(WifiProfileKind.Owe, false)]
    [InlineData(WifiProfileKind.Wep, true)]
    [InlineData(WifiProfileKind.WpaPsk, true)]
    [InlineData(WifiProfileKind.Wpa2Psk, true)]
    [InlineData(WifiProfileKind.Wpa3Sae, true)]
    [InlineData(WifiProfileKind.HandOff, false)]
    public void NeedsPassword_OnlyPskKinds(WifiProfileKind kind, bool expected) =>
        Assert.Equal(expected, WifiSecurity.NeedsPassword(kind));

    [Theory]
    [InlineData(WifiProfileKind.Open, "Open")]
    [InlineData(WifiProfileKind.Owe, "Enhanced Open")]
    [InlineData(WifiProfileKind.Wep, "WEP")]
    [InlineData(WifiProfileKind.WpaPsk, "WPA")]
    [InlineData(WifiProfileKind.Wpa2Psk, "WPA2")]
    [InlineData(WifiProfileKind.Wpa3Sae, "WPA3")]
    [InlineData(WifiProfileKind.HandOff, "Enterprise")]
    public void Label_NamesTheSecurity(WifiProfileKind kind, string expected) =>
        Assert.Equal(expected, WifiSecurity.Label(kind));
}
