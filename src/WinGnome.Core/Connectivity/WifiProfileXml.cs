using System.Text;
using System.Xml;

namespace WinGnome.Core.Connectivity;

/// <summary>
/// A WLAN profile held in a <c>char[]</c> the caller owns, so the key never sits in an immutable string. It exposes
/// the XML as a span (and the buffer, null-terminated, for the native call) and never as a string.
/// </summary>
public sealed class WifiProfileDocument
{
    private readonly char[] _buffer;
    private int _length;

    internal WifiProfileDocument(char[] buffer, int length, string profileName, WifiProfileKind kind)
    {
        _buffer = buffer;
        _length = length;
        ProfileName = profileName;
        Kind = kind;
    }

    /// <summary>The name stored in the profile (what <c>WlanConnect</c> and <c>WlanDeleteProfile</c> take).</summary>
    public string ProfileName { get; }

    public WifiProfileKind Kind { get; }

    /// <summary>The profile XML, without the terminator.</summary>
    public ReadOnlySpan<char> Xml => _buffer.AsSpan(0, _length);

    /// <summary>The buffer, zero-terminated after the XML, for pinning and passing as an <c>LPCWSTR</c>. Clear it with <see cref="Clear"/>.</summary>
    public char[] Buffer => _buffer;

    /// <summary>Zeroes the whole buffer. Safe to call twice.</summary>
    public void Clear()
    {
        Array.Clear(_buffer);
        _length = 0;
    }

    /// <summary>The profile name and kind only: never the XML or the key, so a log line can't leak either.</summary>
    public override string ToString() => $"Wi-Fi profile \"{ProfileName}\" ({Kind})";
}

/// <summary>Builds the <c>WLANProfile</c> XML for a network WinGnome connects to for the first time.</summary>
public static class WifiProfileXml
{
    private const string Header = "<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">";

    // DOT11_CIPHER_ALGORITHM_TKIP, which only a WPA (not WPA2) network uses.
    private const int CipherTkip = 2;

    // A profile is a few kilobytes, an 802.1X one with its EAP settings perhaps a hundred.
    private const long MaxProfileCharacters = 1_000_000;

    /// <summary>
    /// Builds the profile into a new buffer. <paramref name="cipherAlgorithm"/> is the scan's cipher (it only matters
    /// for WPA, where TKIP and AES both occur). Throws <see cref="ArgumentException"/>, never naming the key, when
    /// the name or key holds characters XML can't carry, or when <paramref name="kind"/> has no profile.
    /// </summary>
    public static WifiProfileDocument Build(ReadOnlySpan<byte> ssid, WifiProfileKind kind, int cipherAlgorithm, ReadOnlySpan<char> key, string profileName)
    {
        ArgumentNullException.ThrowIfNull(profileName);
        if (kind == WifiProfileKind.HandOff)
        {
            throw new ArgumentException("This kind of network has no profile WinGnome can build.", nameof(kind));
        }

        var ssidText = SsidText.Display(ssid);
        if (!IsXmlSafe(profileName) || !IsXmlSafe(ssidText))
        {
            throw new ArgumentException("The network name holds characters a profile can't store.", nameof(profileName));
        }

        if (!IsXmlSafe(key))
        {
            throw new ArgumentException("The password holds characters a profile can't store.", nameof(key));
        }

        var (authentication, encryption, baseKeyType) = Describe(kind, cipherAlgorithm);
        var keyType = KeyTypeFor(kind, baseKeyType, key);
        var hasKey = WifiSecurity.NeedsPassword(kind);

        // Escaping grows a character to at most six ("&quot;"), so this is a safe upper bound and the buffer never moves.
        var capacity = Header.Length + 600 + 2 * ssid.Length + 6 * (2 * profileName.Length + ssidText.Length + key.Length) + 1;
        var buffer = new char[capacity];
        var writer = new BufferWriter(buffer);

        writer.Append(Header);
        writer.Append("<name>").AppendEscaped(profileName).Append("</name>");
        writer.Append("<SSIDConfig><SSID><hex>").Append(SsidText.ToHex(ssid)).Append("</hex><name>").AppendEscaped(ssidText).Append("</name></SSID></SSIDConfig>");
        writer.Append("<connectionType>ESS</connectionType><connectionMode>auto</connectionMode>");
        writer.Append("<MSM><security><authEncryption><authentication>").Append(authentication).Append("</authentication><encryption>")
            .Append(encryption).Append("</encryption><useOneX>false</useOneX></authEncryption>");
        if (hasKey)
        {
            writer.Append("<sharedKey><keyType>").Append(keyType).Append("</keyType><protected>false</protected><keyMaterial>")
                .AppendEscaped(key).Append("</keyMaterial></sharedKey>");
            if (kind == WifiProfileKind.Wep)
            {
                writer.Append("<keyIndex>0</keyIndex>");
            }
        }

        writer.Append("</security></MSM></WLANProfile>");
        return new WifiProfileDocument(buffer, writer.Length, profileName, kind);
    }

    /// <summary>
    /// Reads the fields the panel needs from a saved profile's XML, never the key. Null when the text isn't a
    /// well-formed <c>WLANProfile</c>, or has a DTD (never processed).
    /// </summary>
    public static WifiProfileSummary? ReadSummary(string? profileXml)
    {
        if (string.IsNullOrWhiteSpace(profileXml))
        {
            return null;
        }

        // No DTD and no resolver: a profile comes from the system, but the text is still parsed as untrusted.
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersInDocument = MaxProfileCharacters,
        };

        try
        {
            using var reader = XmlReader.Create(new StringReader(profileXml), settings);
            return ReadFields(reader);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            return null;
        }
    }

    private enum Field
    {
        None,
        ProfileName,
        SsidHex,
        SsidName,
        ConnectionMode,
        Authentication,
        Encryption,
        UseOneX,
    }

    // Walks the document once, keeping the names of the open elements, and takes the text of only the fields above.
    // Every other element, the key included, is stepped over and never read as a value.
    private static WifiProfileSummary? ReadFields(XmlReader reader)
    {
        var path = new List<string>();
        var values = new string?[Enum.GetValues<Field>().Length];
        var sawRoot = false;

        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                path.Add(reader.LocalName);
                if (path.Count == 1)
                {
                    if (reader.LocalName != "WLANProfile")
                    {
                        return null;
                    }

                    sawRoot = true;
                }

                var field = FieldAt(path);
                if (field != Field.None && !reader.IsEmptyElement)
                {
                    // Reading the text moves past the end tag, so the reader is already on the next node. It is read
                    // even when the field is already set, or the reader would stay put; the first occurrence wins.
                    var text = reader.ReadElementContentAsString();
                    values[(int)field] ??= text;
                    path.RemoveAt(path.Count - 1);
                    continue;
                }

                if (reader.IsEmptyElement)
                {
                    path.RemoveAt(path.Count - 1);
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                path.RemoveAt(path.Count - 1);
            }

            reader.Read();
        }

        if (!sawRoot)
        {
            return null;
        }

        var name = values[(int)Field.ProfileName] ?? "";
        var ssidName = values[(int)Field.SsidName];
        var ssid = TryParseHex(values[(int)Field.SsidHex]) ?? (ssidName is null ? [] : Encoding.UTF8.GetBytes(ssidName));
        return new WifiProfileSummary(
            name,
            ssid,
            values[(int)Field.Authentication] ?? "",
            values[(int)Field.Encryption] ?? "",
            string.Equals(values[(int)Field.ConnectionMode]?.Trim(), "auto", StringComparison.OrdinalIgnoreCase),
            bool.TryParse(values[(int)Field.UseOneX], out var oneX) && oneX);
    }

    private static Field FieldAt(List<string> path) => path.Count switch
    {
        2 when path[1] == "name" => Field.ProfileName,
        2 when path[1] == "connectionMode" => Field.ConnectionMode,
        4 when path[1] == "SSIDConfig" && path[2] == "SSID" => path[3] switch
        {
            "hex" => Field.SsidHex,
            "name" => Field.SsidName,
            _ => Field.None,
        },
        5 when path[1] == "MSM" && path[2] == "security" && path[3] == "authEncryption" => path[4] switch
        {
            "authentication" => Field.Authentication,
            "encryption" => Field.Encryption,
            "useOneX" => Field.UseOneX,
            _ => Field.None,
        },
        _ => Field.None,
    };

    // Null for empty, odd-length or non-hex text, so the caller falls back to the <name> element.
    private static byte[]? TryParseHex(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length % 2 != 0 || !IsHex(text))
        {
            return null;
        }

        return Convert.FromHexString(text);
    }

    /// <summary>
    /// Sets the profile's <c>connectionMode</c> to <c>auto</c> or <c>manual</c> and changes nothing else (the key stays
    /// encrypted exactly as it was). Throws <see cref="ArgumentException"/>, never containing the XML, when the XML has
    /// no <c>connectionType</c> to anchor on.
    /// </summary>
    public static WifiProfileDocument WithConnectionMode(string existingProfileXml, bool auto, string profileName)
    {
        ArgumentNullException.ThrowIfNull(existingProfileXml);
        ArgumentNullException.ThrowIfNull(profileName);

        const string Open = "<connectionMode>";
        const string Close = "</connectionMode>";
        const string TypeClose = "</connectionType>";
        var element = auto ? "<connectionMode>auto</connectionMode>" : "<connectionMode>manual</connectionMode>";

        var typeEnd = existingProfileXml.IndexOf(TypeClose, StringComparison.Ordinal);
        if (typeEnd < 0)
        {
            throw new ArgumentException("The saved profile has no connection type to change the connection mode after.", nameof(existingProfileXml));
        }

        // Replace the element where it is, or add it straight after <connectionType> (where Windows writes it).
        int cutStart;
        int cutEnd;
        var start = existingProfileXml.IndexOf(Open, StringComparison.Ordinal);
        if (start >= 0)
        {
            var end = existingProfileXml.IndexOf(Close, start, StringComparison.Ordinal);
            if (end < 0)
            {
                throw new ArgumentException("The saved profile's connection mode can't be changed.", nameof(existingProfileXml));
            }

            cutStart = start;
            cutEnd = end + Close.Length;
        }
        else if (existingProfileXml.Contains("<connectionMode", StringComparison.Ordinal))
        {
            // <connectionMode/> or <connectionMode >: adding a second element would make the profile invalid.
            throw new ArgumentException("The saved profile's connection mode can't be changed.", nameof(existingProfileXml));
        }
        else
        {
            cutStart = cutEnd = typeEnd + TypeClose.Length;
        }

        // Copied span by span, so no piece of the profile (and its encrypted key) becomes a second string.
        var source = existingProfileXml.AsSpan();
        var buffer = new char[source.Length - (cutEnd - cutStart) + element.Length + 1];
        source[..cutStart].CopyTo(buffer);
        element.CopyTo(0, buffer, cutStart, element.Length);
        source[cutEnd..].CopyTo(buffer.AsSpan(cutStart + element.Length));

        var summary = ReadSummary(existingProfileXml);
        var kind = summary is null ? WifiProfileKind.HandOff : WifiSecurity.FromProfile(summary.Authentication, summary.Encryption, summary.UsesOneX);
        return new WifiProfileDocument(buffer, buffer.Length - 1, profileName, kind);
    }

    /// <summary>True when every character can appear in XML 1.0 (no control characters, no lone surrogates, no U+FFFE or U+FFFF).</summary>
    public static bool IsXmlSafe(ReadOnlySpan<char> text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(c) || !(c is '\t' or '\n' or '\r' || (c >= ' ' && c <= '�')))
            {
                return false;
            }
        }

        return true;
    }

    private static (string Authentication, string Encryption, string KeyType) Describe(WifiProfileKind kind, int cipherAlgorithm) => kind switch
    {
        WifiProfileKind.Open => ("open", "none", ""),
        WifiProfileKind.Owe => ("OWE", "AES", ""),
        WifiProfileKind.Wep => ("open", "WEP", "networkKey"),
        WifiProfileKind.WpaPsk => ("WPAPSK", cipherAlgorithm == CipherTkip ? "TKIP" : "AES", "passPhrase"),
        WifiProfileKind.Wpa2Psk => ("WPA2PSK", "AES", "passPhrase"),
        WifiProfileKind.Wpa3Sae => ("WPA3SAE", "AES", "passPhrase"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    // A 64-digit hex WPA or WPA2 key is the key itself, not a passphrase to hash. (WPA3 has no raw keys: a 64-digit
    // password there is an ordinary password.)
    private static string KeyTypeFor(WifiProfileKind kind, string keyType, ReadOnlySpan<char> key) =>
        kind is WifiProfileKind.WpaPsk or WifiProfileKind.Wpa2Psk && key.Length == 64 && IsHex(key) ? "networkKey" : keyType;

    /// <summary>
    /// Replaces only the key of an existing profile's XML (as <c>WlanGetProfile</c> returns it, with the old key still
    /// encrypted), so everything else the user or Windows set (connection mode, non-broadcast, MAC randomisation,
    /// metered settings) survives a corrected password. Throws <see cref="ArgumentException"/>, never naming the key,
    /// when the key holds characters XML can't carry, the kind takes no key, or the XML has no security section.
    /// </summary>
    public static WifiProfileDocument ReplaceKey(string existingProfileXml, WifiProfileKind kind, ReadOnlySpan<char> key, string profileName)
    {
        ArgumentNullException.ThrowIfNull(existingProfileXml);
        ArgumentNullException.ThrowIfNull(profileName);
        if (!WifiSecurity.NeedsPassword(kind))
        {
            throw new ArgumentException("This kind of network has no key to replace.", nameof(kind));
        }

        if (!IsXmlSafe(key))
        {
            throw new ArgumentException("The password holds characters a profile can't store.", nameof(key));
        }

        const string Open = "<sharedKey>";
        const string Close = "</sharedKey>";
        var start = existingProfileXml.IndexOf(Open, StringComparison.Ordinal);
        var end = start < 0 ? -1 : existingProfileXml.IndexOf(Close, start, StringComparison.Ordinal);
        string head;
        string tail;
        if (start >= 0 && end > start)
        {
            head = existingProfileXml[..start];
            tail = existingProfileXml[(end + Close.Length)..];
        }
        else
        {
            var security = existingProfileXml.IndexOf("</security>", StringComparison.Ordinal);
            if (security < 0)
            {
                throw new ArgumentException("The saved profile has no security section.", nameof(existingProfileXml));
            }

            head = existingProfileXml[..security];
            tail = existingProfileXml[security..];
        }

        var keyType = KeyTypeFor(kind, kind == WifiProfileKind.Wep ? "networkKey" : "passPhrase", key);
        var buffer = new char[head.Length + tail.Length + 6 * key.Length + 128 + 1];
        var writer = new BufferWriter(buffer);
        writer.Append(head).Append(Open).Append("<keyType>").Append(keyType)
            .Append("</keyType><protected>false</protected><keyMaterial>").AppendEscaped(key).Append("</keyMaterial>").Append(Close).Append(tail);
        return new WifiProfileDocument(buffer, writer.Length, profileName, kind);
    }

    private static bool IsHex(ReadOnlySpan<char> key)
    {
        foreach (var c in key)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Appends into a fixed buffer, escaping text on the way in. Nothing here becomes a string.</summary>
    private sealed class BufferWriter(char[] buffer)
    {
        private int _length;

        public int Length => _length;

        public BufferWriter Append(string text)
        {
            text.CopyTo(0, buffer, _length, text.Length);
            _length += text.Length;
            return this;
        }

        public BufferWriter AppendEscaped(string text) => AppendEscaped(text.AsSpan());

        public BufferWriter AppendEscaped(ReadOnlySpan<char> text)
        {
            foreach (var c in text)
            {
                var replacement = c switch
                {
                    '&' => "&amp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '"' => "&quot;",
                    '\'' => "&apos;",
                    _ => null,
                };
                if (replacement is null)
                {
                    buffer[_length++] = c;
                }
                else
                {
                    Append(replacement);
                }
            }

            return this;
        }
    }
}

/// <summary>What the Wi-Fi panel reads from a saved profile. The key is deliberately not part of it.</summary>
/// <param name="Name">The profile name (what connect, delete and overwrite take).</param>
/// <param name="Ssid">The network name bytes: the profile's <c>&lt;hex&gt;</c>, else its <c>&lt;name&gt;</c> as UTF-8.</param>
/// <param name="Authentication">The <c>authentication</c> text, as stored (<c>WPA2PSK</c>, <c>open</c>...); empty if absent.</param>
/// <param name="Encryption">The <c>encryption</c> text, as stored; empty if absent.</param>
/// <param name="AutoConnect">True when <c>connectionMode</c> is <c>auto</c>.</param>
/// <param name="UsesOneX">True when <c>useOneX</c> is true (802.1X).</param>
public sealed record WifiProfileSummary(string Name, byte[] Ssid, string Authentication, string Encryption, bool AutoConnect, bool UsesOneX);
