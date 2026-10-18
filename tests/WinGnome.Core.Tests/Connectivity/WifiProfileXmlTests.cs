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

    // ---- ReadSummary ----

    private static void AssertSummary(WifiProfileSummary? summary, string name, byte[] ssid, string authentication, string encryption, bool autoConnect, bool usesOneX)
    {
        Assert.NotNull(summary);
        Assert.Equal(name, summary.Name);
        Assert.Equal(ssid, summary.Ssid);
        Assert.Equal(authentication, summary.Authentication);
        Assert.Equal(encryption, summary.Encryption);
        Assert.Equal(autoConnect, summary.AutoConnect);
        Assert.Equal(usesOneX, summary.UsesOneX);
    }

    [Fact]
    public void ReadSummary_Wpa2Psk_ReadsEveryField() =>
        AssertSummary(WifiProfileXml.ReadSummary(WifiProfileSamples.Wpa2Auto), "Home", "Home"u8.ToArray(), "WPA2PSK", "AES", true, false);

    [Fact]
    public void ReadSummary_Wpa3Sae_ManualMode_IsNotAutoConnect() =>
        AssertSummary(WifiProfileXml.ReadSummary(WifiProfileSamples.Wpa3Manual), "Fibra5G", "Fibra5G"u8.ToArray(), "WPA3SAE", "AES", false, false);

    [Fact]
    public void ReadSummary_Open_ReadsNoneEncryption() =>
        AssertSummary(WifiProfileXml.ReadSummary(WifiProfileSamples.Open), "Cafe Wi-Fi", "Cafe Wi-Fi"u8.ToArray(), "open", "none", true, false);

    [Fact]
    public void ReadSummary_Owe_ReadsOweAndAes() =>
        AssertSummary(WifiProfileXml.ReadSummary(WifiProfileSamples.Owe), "Guest", "Guest"u8.ToArray(), "OWE", "AES", false, false);

    [Fact]
    public void ReadSummary_Wep_ReadsOpenAndWep() =>
        AssertSummary(WifiProfileXml.ReadSummary(WifiProfileSamples.Wep), "OldRouter", "OldRouter"u8.ToArray(), "open", "WEP", true, false);

    [Fact]
    public void ReadSummary_OneX_FlagsOneXAndKeepsTheProfileName() =>
        AssertSummary(WifiProfileXml.ReadSummary(WifiProfileSamples.OneX), "Corp-Wifi", "Corp-Wifi"u8.ToArray(), "WPA2", "AES", true, true);

    [Fact]
    public void ReadSummary_NonAsciiSsid_ReadsTheHexBytesNotTheProfileName() =>
        AssertSummary(
            WifiProfileXml.ReadSummary(WifiProfileSamples.NonAscii),
            "Café ☕ 2",
            [0x43, 0x61, 0x66, 0xC3, 0xA9, 0x20, 0xE2, 0x98, 0x95],
            "WPA2PSK", "AES", true, false);

    [Theory]
    [InlineData(WifiProfileSamples.Wpa2Auto)]
    [InlineData(WifiProfileSamples.Wpa3Manual)]
    [InlineData(WifiProfileSamples.Wep)]
    [InlineData(WifiProfileSamples.OneX)]
    [InlineData(WifiProfileSamples.NonAscii)]
    public void ReadSummary_NeverReturnsTheKeyMaterial(string profile)
    {
        var summary = WifiProfileXml.ReadSummary(profile)!;

        Assert.DoesNotContain(WifiProfileSamples.KeyMarker, summary.ToString());
        Assert.DoesNotContain(WifiProfileSamples.KeyMarker, summary.Name + summary.Authentication + summary.Encryption + Convert.ToHexString(summary.Ssid));
        Assert.DoesNotContain("0100000", summary.ToString());
    }

    [Fact]
    public void ReadSummary_NoHex_UsesTheSsidNameAsUtf8()
    {
        var withoutHex = WifiProfileSamples.NonAscii.Replace("<hex>436166C3A920E29895</hex>", "");

        AssertSummary(
            WifiProfileXml.ReadSummary(withoutHex),
            "Café ☕ 2",
            [0x43, 0x61, 0x66, 0xC3, 0xA9, 0x20, 0xE2, 0x98, 0x95],
            "WPA2PSK", "AES", true, false);
    }

    [Theory]
    [InlineData("<hex></hex>")]
    [InlineData("<hex>486F6D6</hex>")]   // odd length
    [InlineData("<hex>ZZ6F6D65</hex>")]  // not hex
    public void ReadSummary_UnusableHex_FallsBackToTheSsidName(string hexElement)
    {
        var broken = WifiProfileSamples.Wpa2Auto.Replace("<hex>486F6D65</hex>", hexElement);

        Assert.Equal("Home"u8.ToArray(), WifiProfileXml.ReadSummary(broken)!.Ssid);
    }

    [Fact]
    public void ReadSummary_NoSsidAtAll_HasAnEmptySsid()
    {
        const string NoSsid = "<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>Home</name><connectionType>ESS</connectionType></WLANProfile>";

        Assert.Empty(WifiProfileXml.ReadSummary(NoSsid)!.Ssid);
    }

    [Fact]
    public void ReadSummary_NoConnectionMode_IsNotAutoConnect()
    {
        var without = WifiProfileSamples.Wpa2Auto.Replace("<connectionMode>auto</connectionMode>", "");

        Assert.False(WifiProfileXml.ReadSummary(without)!.AutoConnect);
    }

    [Theory]
    [InlineData("AUTO", true)]
    [InlineData("auto", true)]
    [InlineData("manual", false)]
    [InlineData("rubbish", false)]
    public void ReadSummary_ConnectionModeText_IsMatchedIgnoringCase(string mode, bool expected)
    {
        var xml = WifiProfileSamples.Wpa2Auto.Replace("<connectionMode>auto</connectionMode>", $"<connectionMode>{mode}</connectionMode>");

        Assert.Equal(expected, WifiProfileXml.ReadSummary(xml)!.AutoConnect);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("", false)]
    public void ReadSummary_UseOneXText_IsParsedAsABoolean(string text, bool expected)
    {
        var xml = WifiProfileSamples.Wpa2Auto.Replace("<useOneX>false</useOneX>", $"<useOneX>{text}</useOneX>");

        Assert.Equal(expected, WifiProfileXml.ReadSummary(xml)!.UsesOneX);
    }

    [Fact]
    public void ReadSummary_NameInANestedElement_IsNotTheProfileName()
    {
        // The 802.1X sample has a <name> deep inside its EAP configuration; only the top-level <name> counts.
        Assert.Equal("Corp-Wifi", WifiProfileXml.ReadSummary(WifiProfileSamples.OneX)!.Name);
    }

    [Fact]
    public void ReadSummary_ProfileWithOnlyAKey_ReadsNothingFromIt()
    {
        // Nothing else could fill the fields first, so a field that quietly took the key's text would show here.
        var onlyKey = "<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><MSM><security><sharedKey><keyType>passPhrase</keyType>"
            + $"<protected>true</protected><keyMaterial>{WifiProfileSamples.KeyMarker}</keyMaterial></sharedKey></security></MSM></WLANProfile>";

        AssertSummary(WifiProfileXml.ReadSummary(onlyKey), "", [], "", "", false, false);
    }

    [Fact]
    public async Task ReadSummary_RepeatedFieldElements_TerminatesAndTakesTheFirst()
    {
        // Regression: a second element at an already-read path used to leave the reader where it was and loop forever.
        var repeated = WifiProfileSamples.Wpa2Auto
            .Replace("<name>Home</name>\r\n\t<SSIDConfig>", "<name>Home</name><name>Second</name>\r\n\t<SSIDConfig>")
            .Replace("<name>Home</name>\n\t<SSIDConfig>", "<name>Home</name><name>Second</name>\n\t<SSIDConfig>")
            .Replace("<authentication>WPA2PSK</authentication>", "<authentication>WPA2PSK</authentication><authentication>open</authentication>");
        Assert.Contains("<name>Second</name>", repeated);

        var read = Task.Run(() => WifiProfileXml.ReadSummary(repeated));

        Assert.Same(read, await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10))));
        AssertSummary(await read, "Home", "Home"u8.ToArray(), "WPA2PSK", "AES", true, false);
    }

    [Fact]
    public void ReadSummary_BuiltProfile_ReadsBackWhatBuildWrote()
    {
        var document = WifiProfileXml.Build("Net"u8, WifiProfileKind.Wpa3Sae, 4, "hunter2hunter2", "Net");

        AssertSummary(WifiProfileXml.ReadSummary(XmlOf(document)), "Net", "Net"u8.ToArray(), "WPA3SAE", "AES", true, false);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not xml at all")]
    [InlineData("<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>Home</name>")]   // truncated
    [InlineData("<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>Home</nam></WLANProfile>")]
    [InlineData("<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>Home</name></WLANProfile><extra/>")]
    [InlineData("<Other xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>Home</name></Other>")]
    [InlineData("<WLANProfile><name>&bogus;</name></WLANProfile>")]
    public void ReadSummary_MalformedOrWrongDocument_IsNull(string? xml) =>
        Assert.Null(WifiProfileXml.ReadSummary(xml));

    [Fact]
    public void ReadSummary_Truncated_NeverReturnsAPartialSummary() =>
        Assert.Null(WifiProfileXml.ReadSummary(WifiProfileSamples.Wpa2Auto[..(WifiProfileSamples.Wpa2Auto.Length / 2)]));

    [Fact]
    public void ReadSummary_InternalDtdWithEntity_IsRejected()
    {
        var withDtd = "<?xml version=\"1.0\"?><!DOCTYPE WLANProfile [<!ENTITY x \"Home\">]>"
            + "<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>&x;</name></WLANProfile>";

        Assert.Null(WifiProfileXml.ReadSummary(withDtd));
    }

    [Fact]
    public void ReadSummary_ExternalDtd_IsRejectedWithoutBeingFetched()
    {
        var withDtd = "<?xml version=\"1.0\"?><!DOCTYPE WLANProfile SYSTEM \"http://127.0.0.1:9/evil.dtd\">"
            + WifiProfileSamples.Wpa2Auto.Replace("<?xml version=\"1.0\"?>", "");

        Assert.Null(WifiProfileXml.ReadSummary(withDtd));
    }

    // ---- WithConnectionMode ----

    private const string AutoElement = "<connectionMode>auto</connectionMode>";
    private const string ManualElement = "<connectionMode>manual</connectionMode>";

    [Fact]
    public void WithConnectionMode_AutoToManual_ChangesOnlyThatElement()
    {
        var document = WifiProfileXml.WithConnectionMode(WifiProfileSamples.Wpa2Auto, auto: false, "Home");

        Assert.Equal(WifiProfileSamples.Wpa2Auto.Replace(AutoElement, ManualElement), XmlOf(document));
        Assert.Contains(WifiProfileSamples.KeyMarker + "0100000062A1", XmlOf(document));
    }

    [Fact]
    public void WithConnectionMode_ManualToAuto_ChangesOnlyThatElement()
    {
        var document = WifiProfileXml.WithConnectionMode(WifiProfileSamples.Wpa3Manual, auto: true, "Fibra5G");

        Assert.Equal(WifiProfileSamples.Wpa3Manual.Replace(ManualElement, AutoElement), XmlOf(document));
    }

    [Fact]
    public void WithConnectionMode_AlreadyInThatMode_ReturnsTheSameXml()
    {
        Assert.Equal(WifiProfileSamples.Wpa2Auto, XmlOf(WifiProfileXml.WithConnectionMode(WifiProfileSamples.Wpa2Auto, auto: true, "Home")));
        Assert.Equal(WifiProfileSamples.Owe, XmlOf(WifiProfileXml.WithConnectionMode(WifiProfileSamples.Owe, auto: false, "Guest")));
    }

    [Fact]
    public void WithConnectionMode_NonAsciiProfile_KeepsEveryOtherCharacter()
    {
        var document = WifiProfileXml.WithConnectionMode(WifiProfileSamples.NonAscii, auto: false, "Café ☕ 2");

        Assert.Equal(WifiProfileSamples.NonAscii.Replace(AutoElement, ManualElement), XmlOf(document));
    }

    [Fact]
    public void WithConnectionMode_OneXProfile_KeepsTheEapBlockUntouched()
    {
        var document = WifiProfileXml.WithConnectionMode(WifiProfileSamples.OneX, auto: false, "Corp-Wifi");

        Assert.Equal(WifiProfileSamples.OneX.Replace(AutoElement, ManualElement), XmlOf(document));
    }

    [Theory]
    [InlineData(true, AutoElement)]
    [InlineData(false, ManualElement)]
    public void WithConnectionMode_NoConnectionModeYet_AddsItRightAfterTheConnectionType(bool auto, string expectedElement)
    {
        var without = WifiProfileSamples.Wpa2Auto.Replace(AutoElement, "");

        var document = WifiProfileXml.WithConnectionMode(without, auto, "Home");

        Assert.Equal(without.Replace("</connectionType>", "</connectionType>" + expectedElement), XmlOf(document));
    }

    [Fact]
    public void WithConnectionMode_SelfClosingConnectionMode_IsRefusedRatherThanDuplicated()
    {
        var selfClosing = WifiProfileSamples.Wpa2Auto.Replace(AutoElement, "<connectionMode/>");

        Assert.Throws<ArgumentException>(() => WifiProfileXml.WithConnectionMode(selfClosing, auto: false, "Home"));
    }

    [Fact]
    public void WithConnectionMode_NoConnectionType_ThrowsWithoutTheXmlInTheMessage()
    {
        var noType = WifiProfileSamples.Wpa2Auto.Replace("<connectionType>ESS</connectionType>", "");

        var ex = Assert.Throws<ArgumentException>(() => WifiProfileXml.WithConnectionMode(noType, auto: false, "Home"));

        Assert.Equal("The saved profile has no connection type to change the connection mode after. (Parameter 'existingProfileXml')", ex.Message);
        Assert.DoesNotContain(WifiProfileSamples.KeyMarker, ex.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<WLANProfile/>")]
    public void WithConnectionMode_NotAProfile_Throws(string xml) =>
        Assert.Throws<ArgumentException>(() => WifiProfileXml.WithConnectionMode(xml, auto: true, "Home"));

    [Fact]
    public void WithConnectionMode_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => WifiProfileXml.WithConnectionMode(null!, auto: true, "Home"));
        Assert.Throws<ArgumentNullException>(() => WifiProfileXml.WithConnectionMode(WifiProfileSamples.Wpa2Auto, auto: true, null!));
    }

    [Theory]
    [InlineData(WifiProfileSamples.Wpa2Auto, "Home", "Wi-Fi profile \"Home\" (Wpa2Psk)")]
    [InlineData(WifiProfileSamples.Open, "Cafe Wi-Fi", "Wi-Fi profile \"Cafe Wi-Fi\" (Open)")]
    [InlineData(WifiProfileSamples.OneX, "Corp-Wifi", "Wi-Fi profile \"Corp-Wifi\" (HandOff)")]
    public void WithConnectionMode_Document_CarriesTheNameAndKindAndHidesTheXml(string xml, string name, string expected)
    {
        var document = WifiProfileXml.WithConnectionMode(xml, auto: false, name);

        Assert.Equal(name, document.ProfileName);
        Assert.Equal(expected, document.ToString());
    }

    [Fact]
    public void WithConnectionMode_Buffer_IsZeroTerminatedAndClearsCompletely()
    {
        var document = WifiProfileXml.WithConnectionMode(WifiProfileSamples.Wpa2Auto, auto: false, "Home");

        Assert.Equal('\0', document.Buffer[document.Xml.Length]);

        document.Clear();
        document.Clear();

        Assert.All(document.Buffer, c => Assert.Equal('\0', c));
        Assert.Equal(0, document.Xml.Length);
    }

    [Fact]
    public void WithConnectionMode_Result_StillReadsBackWithTheNewMode()
    {
        var document = WifiProfileXml.WithConnectionMode(WifiProfileSamples.Wpa2Auto, auto: false, "Home");

        AssertSummary(WifiProfileXml.ReadSummary(XmlOf(document)), "Home", "Home"u8.ToArray(), "WPA2PSK", "AES", false, false);
    }
}
