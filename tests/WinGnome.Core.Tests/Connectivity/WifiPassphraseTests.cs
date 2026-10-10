using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class WifiPassphraseTests
{
    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(63, true)]
    public void Validate_Wpa2_LengthBoundaries(int length, bool valid) =>
        Assert.Equal(valid, WifiPassphrase.Validate(WifiProfileKind.Wpa2Psk, new string('p', length)).IsValid);

    [Fact]
    public void Validate_Wpa2_SixtyFourNonHexCharacters_IsRefused() =>
        Assert.False(WifiPassphrase.Validate(WifiProfileKind.Wpa2Psk, new string('z', 64)).IsValid);

    [Fact]
    public void Validate_Wpa2_SixtyFourHexDigits_IsTheKeyItself() =>
        Assert.True(WifiPassphrase.Validate(WifiProfileKind.Wpa2Psk, new string('A', 32) + new string('0', 32)).IsValid);

    [Fact]
    public void Validate_Wpa2_SixtyThreeHexDigits_IsAnOrdinaryPassphrase() =>
        Assert.True(WifiPassphrase.Validate(WifiProfileKind.Wpa2Psk, new string('a', 63)).IsValid);

    [Fact]
    public void Validate_Wpa2_NonAscii_IsRefused()
    {
        var result = WifiPassphrase.Validate(WifiProfileKind.Wpa2Psk, "pässword1");

        Assert.False(result.IsValid);
        Assert.Equal("The password can only use letters, digits and standard symbols.", result.Message);
    }

    [Fact]
    public void Validate_Wpa_TooShort_MessageDoesNotEchoTheKey()
    {
        var result = WifiPassphrase.Validate(WifiProfileKind.WpaPsk, "secret7");

        Assert.False(result.IsValid);
        Assert.Equal("The password must be 8 to 63 characters, or 64 hexadecimal digits.", result.Message);
    }

    [Theory]
    [InlineData("abcde", true)]
    [InlineData("abcdefghijklm", true)]
    [InlineData("abcdef", false)]
    [InlineData("0123456789", true)]
    [InlineData("01234567890123456789012345", true)]
    [InlineData("012345678z", false)]
    [InlineData("abcdéf", false)]
    public void Validate_Wep_ExactLengths(string key, bool valid) =>
        Assert.Equal(valid, WifiPassphrase.Validate(WifiProfileKind.Wep, key).IsValid);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void Validate_Sae_LengthBoundaries(int length, bool valid) =>
        Assert.Equal(valid, WifiPassphrase.Validate(WifiProfileKind.Wpa3Sae, new string('p', length)).IsValid);

    [Fact]
    public void Validate_Sae_ControlCharacter_IsRefused() =>
        Assert.False(WifiPassphrase.Validate(WifiProfileKind.Wpa3Sae, "pass\tword").IsValid);

    [Fact]
    public void Validate_Sae_NonAsciiIsAllowed() =>
        Assert.True(WifiPassphrase.Validate(WifiProfileKind.Wpa3Sae, "pässwörd").IsValid);

    [Fact]
    public void Validate_HandOffKind_IsNeverValid() =>
        Assert.False(WifiPassphrase.Validate(WifiProfileKind.HandOff, "password1").IsValid);

    [Fact]
    public void Validate_OpenAndOwe_NeedNoKey()
    {
        Assert.True(WifiPassphrase.Validate(WifiProfileKind.Open, "").IsValid);
        Assert.True(WifiPassphrase.Validate(WifiProfileKind.Owe, "").IsValid);
    }
}
