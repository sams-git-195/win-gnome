namespace WinGnome.Core.Connectivity;

/// <summary>The verdict on a typed Wi-Fi password. The message never contains the password.</summary>
/// <param name="IsValid">True when the password is acceptable for the network's security.</param>
/// <param name="Message">What to tell the user when it isn't; null when it is.</param>
public readonly record struct WifiPassphraseResult(bool IsValid, string? Message);

/// <summary>Checks a password against what each security type accepts, before anything is written to Windows.</summary>
public static class WifiPassphrase
{
    private const int SaeMaxLength = 128;

    /// <summary>Checks <paramref name="key"/> for a network of <paramref name="kind"/>. The span is read, never copied or kept.</summary>
    public static WifiPassphraseResult Validate(WifiProfileKind kind, ReadOnlySpan<char> key) => kind switch
    {
        WifiProfileKind.WpaPsk or WifiProfileKind.Wpa2Psk => ValidateWpa(key),
        WifiProfileKind.Wep => ValidateWep(key),
        WifiProfileKind.Wpa3Sae => ValidateSae(key),
        WifiProfileKind.Open or WifiProfileKind.Owe => new(true, null),
        _ => new(false, "WinGnome can't connect to this kind of network. Use Windows Settings."),
    };

    private static WifiPassphraseResult ValidateWpa(ReadOnlySpan<char> key)
    {
        if (key.Length == 64 && IsHex(key))
        {
            return new(true, null);
        }

        if (key.Length is < 8 or > 63)
        {
            return new(false, "The password must be 8 to 63 characters, or 64 hexadecimal digits.");
        }

        return IsPrintableAscii(key) ? new(true, null) : new(false, "The password can only use letters, digits and standard symbols.");
    }

    private static WifiPassphraseResult ValidateWep(ReadOnlySpan<char> key)
    {
        if (key.Length is 10 or 26 && IsHex(key))
        {
            return new(true, null);
        }

        if (key.Length is 5 or 13 && IsPrintableAscii(key))
        {
            return new(true, null);
        }

        return new(false, "A WEP key is 5 or 13 characters, or 10 or 26 hexadecimal digits.");
    }

    private static WifiPassphraseResult ValidateSae(ReadOnlySpan<char> key)
    {
        if (key.Length is < 1 or > SaeMaxLength)
        {
            return new(false, "The password must be 1 to 128 characters.");
        }

        foreach (var c in key)
        {
            if (char.IsControl(c))
            {
                return new(false, "The password can't contain control characters.");
            }
        }

        return new(true, null);
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

    private static bool IsPrintableAscii(ReadOnlySpan<char> key)
    {
        foreach (var c in key)
        {
            if (c is < ' ' or > '~')
            {
                return false;
            }
        }

        return true;
    }
}
