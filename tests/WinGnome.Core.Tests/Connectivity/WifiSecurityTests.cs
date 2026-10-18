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

    [Theory]
    [InlineData("open", "none", WifiProfileKind.Open)]
    [InlineData("OPEN", "NONE", WifiProfileKind.Open)]
    [InlineData("open", "WEP", WifiProfileKind.Wep)]
    [InlineData("Open", "wep", WifiProfileKind.Wep)]
    [InlineData("OWE", "AES", WifiProfileKind.Owe)]
    [InlineData("owe", "aes", WifiProfileKind.Owe)]
    [InlineData("WPAPSK", "TKIP", WifiProfileKind.WpaPsk)]
    [InlineData("WPAPSK", "AES", WifiProfileKind.WpaPsk)]
    [InlineData("wpapsk", "aes", WifiProfileKind.WpaPsk)]
    [InlineData("WPA2PSK", "AES", WifiProfileKind.Wpa2Psk)]
    [InlineData("wpa2psk", "aes", WifiProfileKind.Wpa2Psk)]
    [InlineData("WPA2PSK", "TKIP", WifiProfileKind.Wpa2Psk)]   // the cipher only decides for open authentication
    [InlineData("WPA2PSK", "", WifiProfileKind.Wpa2Psk)]
    [InlineData("WPA3SAE", "AES", WifiProfileKind.Wpa3Sae)]
    [InlineData("Wpa3Sae", "AES", WifiProfileKind.Wpa3Sae)]
    public void FromProfile_KnownPairs_MapToTheirKind(string authentication, string encryption, WifiProfileKind expected) =>
        Assert.Equal(expected, WifiSecurity.FromProfile(authentication, encryption, usesOneX: false));

    [Theory]
    [InlineData("WPA", "TKIP")]
    [InlineData("WPA2", "AES")]
    [InlineData("WPA3", "AES")]
    [InlineData("WPA3ENT", "AES")]
    [InlineData("WPA3ENT192", "GCMP256")]
    [InlineData("wpa2", "aes")]
    public void FromProfile_EnterpriseAuthentication_HandOff(string authentication, string encryption) =>
        Assert.Equal(WifiProfileKind.HandOff, WifiSecurity.FromProfile(authentication, encryption, usesOneX: false));

    [Theory]
    [InlineData("WPA2PSK", "AES")]
    [InlineData("open", "none")]
    [InlineData("WPA2", "AES")]
    public void FromProfile_UsesOneX_HandOffWhateverTheAuthentication(string authentication, string encryption) =>
        Assert.Equal(WifiProfileKind.HandOff, WifiSecurity.FromProfile(authentication, encryption, usesOneX: true));

    [Theory]
    [InlineData("open", "AES")]       // open authentication with an encrypting cipher is not something WinGnome writes
    [InlineData("shared", "WEP")]
    [InlineData("rubbish", "AES")]
    [InlineData("", "")]
    [InlineData(null, null)]
    [InlineData("open", null)]
    public void FromProfile_UnknownOrMissing_HandOff(string? authentication, string? encryption) =>
        Assert.Equal(WifiProfileKind.HandOff, WifiSecurity.FromProfile(authentication, encryption, usesOneX: false));
}
