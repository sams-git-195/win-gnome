using System.Globalization;
using System.Text;

namespace WinGnome.Core.Connectivity;

/// <summary>
/// A Wi-Fi network name is up to 32 raw bytes (<c>DOT11_SSID</c>), usually UTF-8 but not required to be. This turns
/// the bytes into the text the panel shows and into the hex a profile stores.
/// </summary>
public static class SsidText
{
    /// <summary>The longest SSID, in bytes.</summary>
    public const int MaxLength = 32;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The name as text: strict UTF-8 when it decodes and holds no control characters, otherwise printable ASCII as
    /// is and every other byte as <c>\xNN</c> so no byte is lost and nothing unprintable reaches the UI.
    /// </summary>
    public static string Display(ReadOnlySpan<byte> ssid)
    {
        if (ssid.IsEmpty)
        {
            return "";
        }

        try
        {
            var text = StrictUtf8.GetString(ssid);
            if (!text.Any(char.IsControl))
            {
                return text;
            }
        }
        catch (DecoderFallbackException)
        {
            // Not UTF-8: fall through to the escaped form.
        }

        var escaped = new StringBuilder(ssid.Length * 2);
        foreach (var b in ssid)
        {
            if (b is >= 0x20 and <= 0x7E)
            {
                escaped.Append((char)b);
            }
            else
            {
                escaped.Append("\\x").Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return escaped.ToString();
    }

    /// <summary>Upper-case hex of the bytes, as a profile's <c>&lt;hex&gt;</c> element and the list's merge key.</summary>
    public static string ToHex(ReadOnlySpan<byte> ssid) => Convert.ToHexString(ssid);

    /// <summary>True for a hidden network: Windows reports no name, or a name of zero bytes.</summary>
    public static bool IsHidden(ReadOnlySpan<byte> ssid)
    {
        foreach (var b in ssid)
        {
            if (b != 0)
            {
                return false;
            }
        }

        return true;
    }
}
