using System.Windows;
using System.Windows.Media;
using WinGnome.Core.Theming;

namespace WinGnome.Features.Settings.ViewModels.Items;

/// <summary>Conversions from Core's <see cref="HexColor"/> to WPF brushes, plus palette lookups for the live theme.</summary>
internal static class ColorConversion
{
    /// <summary>A frozen brush for the colour; <paramref name="opacity"/> multiplies the colour's own alpha.</summary>
    public static SolidColorBrush ToBrush(HexColor color, double opacity = 1)
    {
        var alpha = (byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1));
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>A frozen brush for a "#RRGGBB" string, or for <paramref name="fallback"/> when the text is not a colour.</summary>
    public static SolidColorBrush ToBrush(string? hex, HexColor fallback, double opacity = 1) =>
        ToBrush(HexColor.TryParse(hex, out var color) ? color : fallback, opacity);

    /// <summary>Reads a colour from the active palette (Dark.xaml or Light.xaml) by key, e.g. "AccentColor".</summary>
    public static HexColor Palette(string key)
    {
        if (Application.Current?.TryFindResource(key) is Color color)
        {
            return new HexColor(color.A, color.R, color.G, color.B);
        }

        return HexColor.FromRgb(0x80, 0x80, 0x80);
    }
}
