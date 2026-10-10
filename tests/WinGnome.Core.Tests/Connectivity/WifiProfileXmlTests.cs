using System.Text;
using System.Xml.Linq;
using WinGnome.Core.Connectivity;

namespace WinGnome.Core.Tests.Connectivity;

public class WifiProfileXmlTests
{
    private const string Prefix = "<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">";
    private const string Middle = "<connectionType>ESS</connectionType><connectionMode>auto</connectionMode>";
    private static readonly XNamespace Ns = "http://www.microsoft.com/networking/WLAN/profile/v1";

    private static string XmlOf(WifiProfileDocument document) => document.Xml.ToString();

    [Fact]
    public void Build_Wpa2_ProducesTheExactProfile()
    {
        var document = WifiProfileXml.Build("Home"u8, WifiProfileKind.Wpa2Psk, 4, "password1", "Home");

        Assert.Equal(
            Prefix + "<name>Home</name><SSIDConfig><SSID><hex>486F6D65</hex><name>Home</name></SSID></SSIDConfig>" + Middle
            + "<MSM><security><authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>"
            + "<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>password1</keyMaterial></sharedKey></security></MSM></WLANProfile>",
            XmlOf(document));
    }

    [Fact]
    public void Build_Open_HasNoSharedKey()
    {
        var document = WifiProfileXml.Build("Cafe"u8, WifiProfileKind.Open, 0, "", "Cafe");

        Assert.Equal(
            Prefix + "<name>Cafe</name><SSIDConfig><SSID><hex>43616665</hex><name>Cafe</name></SSID></SSIDConfig>" + Middle
            + "<MSM><security><authEncryption><authentication>open</authentication><encryption>none</encryption><useOneX>false</useOneX></authEncryption></security></MSM></WLANProfile>",
            XmlOf(document));
    }

    [Fact]
    public void Build_Wpa3_UsesSaeAndAes()
    {
        var document = WifiProfileXml.Build("Net"u8, WifiProfileKind.Wpa3Sae, 4, "hunter2hunter2", "Net");

        Assert.Equal(
            Prefix + "<name>Net</name><SSIDConfig><SSID><hex>4E6574</hex><name>Net</name></SSID></SSIDConfig>" + Middle
            + "<MSM><security><authEncryption><authentication>WPA3SAE</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>"
            + "<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>hunter2hunter2</keyMaterial></sharedKey></security></MSM></WLANProfile>",
            XmlOf(document));
    }

    [Fact]
    public void Build_Wep_UsesNetworkKeyAndKeyIndex()
    {
        var document = WifiProfileXml.Build("Old"u8, WifiProfileKind.Wep, 1, "abcde", "Old");

        Assert.Equal(
            Prefix + "<name>Old</name><SSIDConfig><SSID><hex>4F6C64</hex><name>Old</name></SSID></SSIDConfig>" + Middle
            + "<MSM><security><authEncryption><authentication>open</authentication><encryption>WEP</encryption><useOneX>false</useOneX></authEncryption>"
            + "<sharedKey><keyType>networkKey</keyType><protected>false</protected><keyMaterial>abcde</keyMaterial></sharedKey><keyIndex>0</keyIndex></security></MSM></WLANProfile>",
            XmlOf(document));
    }

    [Theory]
    [InlineData(2, "WPAPSK", "TKIP")]
    [InlineData(4, "WPAPSK", "AES")]
    public void Build_Wpa_FollowsTheNetworksCipher(int cipher, string authentication, string encryption)
    {
        var xml = XDocument.Parse(XmlOf(WifiProfileXml.Build("n"u8, WifiProfileKind.WpaPsk, cipher, "password1", "n")));

        var auth = xml.Descendants(Ns + "authEncryption").Single();
        Assert.Equal(authentication, auth.Element(Ns + "authentication")!.Value);
        Assert.Equal(encryption, auth.Element(Ns + "encryption")!.Value);
    }

    [Fact]
    public void Build_SixtyFourHexKey_IsStoredAsTheNetworkKey()
    {
        var key = new string('a', 64);

        var xml = XDocument.Parse(XmlOf(WifiProfileXml.Build("n"u8, WifiProfileKind.Wpa2Psk, 4, key, "n")));

        Assert.Equal("networkKey", xml.Descendants(Ns + "keyType").Single().Value);
    }

    [Fact]
    public void Build_SpecialCharactersInNameAndKey_RoundTripThroughAnXmlParser()
    {
        var ssid = Encoding.UTF8.GetBytes("A&B <\"'> ünï");
        const string Key = "p&\"<>'x1234";

        var document = WifiProfileXml.Build(ssid, WifiProfileKind.Wpa2Psk, 4, Key, "A&B <\"'> ünï");
        var xml = XDocument.Parse(XmlOf(document));

        Assert.Equal("A&B <\"'> ünï", xml.Root!.Element(Ns + "name")!.Value);
        Assert.Equal("A&B <\"'> ünï", xml.Descendants(Ns + "SSID").Single().Element(Ns + "name")!.Value);
        Assert.Equal(Convert.ToHexString(ssid), xml.Descendants(Ns + "hex").Single().Value);
        Assert.Equal(Key, xml.Descendants(Ns + "keyMaterial").Single().Value);
    }

    [Fact]
    public void Build_XmlInvalidCharacterInKey_IsRejectedWithoutNamingTheKey()
    {
        var ex = Assert.Throws<ArgumentException>(() => WifiProfileXml.Build("n"u8, WifiProfileKind.Wpa2Psk, 4, "hunter\u0001two", "n"));

        Assert.Equal("The password holds characters a profile can't store. (Parameter 'key')", ex.Message);
    }

    [Theory]
    [InlineData('￿')]
    [InlineData('￾')]
    [InlineData('\u0000')]
    [InlineData('\u001F')]
    [InlineData('\uD800')] // lone high surrogate
    [InlineData('\uDC00')] // lone low surrogate
    public void Build_XmlInvalidCharacter_IsRejected(char bad) =>
        Assert.Throws<ArgumentException>(() => WifiProfileXml.Build("n"u8, WifiProfileKind.Wpa2Psk, 4, "pass" + bad + "word", "n"));

    [Fact]
    public void Build_HighSurrogateFollowedByNonSurrogate_IsRejected() =>
        Assert.Throws<ArgumentException>(() => WifiProfileXml.Build("n"u8, WifiProfileKind.Wpa2Psk, 4, "pass\uD83Dxword", "n"));

    [Fact]
    public void Build_XmlInvalidCharacterInName_IsRejected() =>
        Assert.Throws<ArgumentException>(() => WifiProfileXml.Build("n"u8, WifiProfileKind.Open, 0, "", "bad\u0002name"));

    [Fact]
    public void Build_ValidSurrogatePairInKey_IsAccepted() =>
        Assert.Contains("a😀bcdefg", XmlOf(WifiProfileXml.Build("n"u8, WifiProfileKind.Wpa3Sae, 4, "a😀bcdefg", "n")));

    [Fact]
    public void Build_HandOffKind_Throws() =>
        Assert.Throws<ArgumentException>(() => WifiProfileXml.Build("n"u8, WifiProfileKind.HandOff, 0, "", "n"));

    [Fact]
    public void Buffer_IsZeroTerminatedAfterTheXml()
    {
        var document = WifiProfileXml.Build("n"u8, WifiProfileKind.Open, 0, "", "n");

        Assert.Equal('\0', document.Buffer[document.Xml.Length]);
    }

    [Fact]
    public void ToString_OmitsTheKeyAndTheXml()
    {
        var document = WifiProfileXml.Build("Home"u8, WifiProfileKind.Wpa2Psk, 4, "hunter2hunter2", "Home");

        Assert.Equal("Wi-Fi profile \"Home\" (Wpa2Psk)", document.ToString());
    }

    [Fact]
    public void Clear_ZeroesTheWholeBuffer_AndTwiceIsFine()
    {
        var document = WifiProfileXml.Build("Home"u8, WifiProfileKind.Wpa2Psk, 4, "hunter2hunter2", "Home");

        document.Clear();
        document.Clear();

        Assert.All(document.Buffer, c => Assert.Equal('\0', c));
        Assert.Equal(0, document.Xml.Length);
    }

    [Fact]
    public void Build_SixtyFourDigitPasswordOnWpa3_StaysAPassphrase()
    {
        var xml = XDocument.Parse(XmlOf(WifiProfileXml.Build("n"u8, WifiProfileKind.Wpa3Sae, 4, new string('a', 64), "n")));

        Assert.Equal("passPhrase", xml.Descendants(Ns + "keyType").Single().Value);
    }

    private const string Saved = Prefix + "<name>Home</name><SSIDConfig><SSID><hex>486F6D65</hex><name>Home</name></SSID><nonBroadcast>true</nonBroadcast></SSIDConfig>"
        + "<connectionType>ESS</connectionType><connectionMode>manual</connectionMode><MSM><security><authEncryption><authentication>WPA2PSK</authentication>"
        + "<encryption>AES</encryption><useOneX>false</useOneX></authEncryption><sharedKey><keyType>passPhrase</keyType><protected>true</protected>"
        + "<keyMaterial>01000000D08C9DDF</keyMaterial></sharedKey></security></MSM>"
        + "<MacRandomization xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v3\"><enableRandomization>true</enableRandomization></MacRandomization></WLANProfile>";

    [Fact]
    public void ReplaceKey_ChangesOnlyTheKey()
    {
        var document = WifiProfileXml.ReplaceKey(Saved, WifiProfileKind.Wpa2Psk, "newpassword1", "Home");

        Assert.Equal(
            Saved.Replace("<protected>true</protected><keyMaterial>01000000D08C9DDF</keyMaterial>",
                "<protected>false</protected><keyMaterial>newpassword1</keyMaterial>"),
            XmlOf(document));
    }

    [Fact]
    public void ReplaceKey_EscapesTheKey_AndRoundTrips()
    {
        var xml = XDocument.Parse(XmlOf(WifiProfileXml.ReplaceKey(Saved, WifiProfileKind.Wpa2Psk, "p&\"<>'x1234", "Home")));

        Assert.Equal("p&\"<>'x1234", xml.Descendants(Ns + "keyMaterial").Single().Value);
        Assert.Equal("manual", xml.Descendants(Ns + "connectionMode").Single().Value);
    }

    [Fact]
    public void ReplaceKey_SixtyFourHexOnWpa2_BecomesTheNetworkKey()
    {
        var xml = XDocument.Parse(XmlOf(WifiProfileXml.ReplaceKey(Saved, WifiProfileKind.Wpa2Psk, new string('b', 64), "Home")));

        Assert.Equal("networkKey", xml.Descendants(Ns + "keyType").Single().Value);
    }

    [Fact]
    public void ReplaceKey_NoSharedKeyYet_AddsOneInsideSecurity()
    {
        var withoutKey = Saved.Replace("<sharedKey><keyType>passPhrase</keyType><protected>true</protected><keyMaterial>01000000D08C9DDF</keyMaterial></sharedKey>", "");

        var xml = XDocument.Parse(XmlOf(WifiProfileXml.ReplaceKey(withoutKey, WifiProfileKind.Wpa2Psk, "newpassword1", "Home")));

        Assert.Equal("newpassword1", xml.Descendants(Ns + "security").Single().Element(Ns + "sharedKey")!.Element(Ns + "keyMaterial")!.Value);
    }

    [Fact]
    public void ReplaceKey_BadInput_IsRejectedWithoutNamingTheKey()
    {
        var invalid = Assert.Throws<ArgumentException>(() => WifiProfileXml.ReplaceKey(Saved, WifiProfileKind.Wpa2Psk, "hunter\u0001two", "Home"));
        Assert.Equal("The password holds characters a profile can't store. (Parameter 'key')", invalid.Message);
        Assert.Throws<ArgumentException>(() => WifiProfileXml.ReplaceKey(Saved, WifiProfileKind.Open, "", "Home"));
        Assert.Throws<ArgumentException>(() => WifiProfileXml.ReplaceKey("<WLANProfile/>", WifiProfileKind.Wpa2Psk, "newpassword1", "Home"));
    }

    [Fact]
    public void ReplaceKey_DocumentClearsAndHidesTheKey()
    {
        var document = WifiProfileXml.ReplaceKey(Saved, WifiProfileKind.Wpa2Psk, "newpassword1", "Home");

        Assert.Equal("Wi-Fi profile \"Home\" (Wpa2Psk)", document.ToString());
        document.Clear();
        Assert.All(document.Buffer, c => Assert.Equal('\0', c));
    }
}
