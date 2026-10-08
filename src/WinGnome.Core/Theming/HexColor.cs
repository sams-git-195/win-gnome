using System.Globalization;

namespace WinGnome.Core.Theming;

/// <summary>An sRGB colour parsed from "#RGB", "#RRGGBB" or "#AARRGGBB".</summary>
public readonly record struct HexColor(byte A, byte R, byte G, byte B)
{
    public static HexColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    public static bool TryParse(string? text, out HexColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        }

        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var a = hex.Length == 8 ? (byte)(value >> 24) : (byte)255;
        color = new HexColor(a, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    public static HexColor Parse(string text) =>
        TryParse(text, out var color) ? color : throw new FormatException($"'{text}' is not a valid hex colour.");

    /// <summary>WCAG relative luminance in 0..1.</summary>
    public double RelativeLuminance
    {
        get
        {
            static double Channel(byte c)
            {
                var s = c / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(R)) + (0.7152 * Channel(G)) + (0.0722 * Channel(B));
        }
    }

    public bool IsLight => RelativeLuminance > 0.4;

    /// <summary>Mixes towards <paramref name="other"/> by <paramref name="amount"/> (0 = this, 1 = other).</summary>
    public HexColor Blend(HexColor other, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        byte Mix(byte from, byte to) => (byte)Math.Round(from + ((to - from) * amount));
        return new HexColor(Mix(A, other.A), Mix(R, other.R), Mix(G, other.G), Mix(B, other.B));
    }

    /// <summary>"#RRGGBB" when opaque, otherwise "#AARRGGBB".</summary>
    public override string ToString() =>
        A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}
