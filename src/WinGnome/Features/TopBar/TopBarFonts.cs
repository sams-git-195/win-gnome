using System.Windows;
using System.Windows.Media;
using WinGnome.Core.Settings;

namespace WinGnome.Features.TopBar;

/// <summary>
/// The typefaces the top bar can use. The bar, its popups and its confirmation dialog read the chosen one through the
/// application resource <see cref="ResourceKey"/>, so a settings change re-fonts all of them live.
/// </summary>
internal static class TopBarFonts
{
    public const string ResourceKey = "TopBarFont";

    // WPF lists the bundled variable font's named instances (Thin..Black) as one family called "Adwaita Sans Text"
    // (its default instance's name), so FontWeight picks real weights rather than synthesised bold. Segoe UI backs up
    // any character the font lacks.
    private static readonly FontFamily AdwaitaSans =
        new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Adwaita Sans Text, Segoe UI");

    public static FontFamily For(TopBarFont font) => font switch
    {
        TopBarFont.SegoeUI => (FontFamily)Application.Current.FindResource("ShellFont"),
        _ => AdwaitaSans,
    };

    /// <summary>Publishes <paramref name="font"/> as the application-wide <see cref="ResourceKey"/>, if it changed.</summary>
    public static void Apply(TopBarFont font)
    {
        var family = For(font);
        var resources = Application.Current.Resources;

        // Replacing an application resource re-resolves every DynamicResource reference in every window, so only do
        // it when the font actually changed (this runs on every settings change).
        if (!ReferenceEquals(resources[ResourceKey], family))
        {
            resources[ResourceKey] = family;
        }
    }
}
