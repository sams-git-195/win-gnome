using WinGnome.Core.Settings;
using WinGnome.Core.Theming;

namespace WinGnome.Features.WindowButtons;

/// <summary>Everything that decides how decorated windows look; rebuilt when settings or the theme change.</summary>
/// <param name="Settings">The window-button settings.</param>
/// <param name="Colors">Resolved traffic-light colours.</param>
/// <param name="UnifiedCaption">Title bar colour applied when title bars are unified.</param>
/// <param name="UnifiedText">Title text colour applied when title bars are unified.</param>
/// <param name="PlaceholderCaption">Surface colour shown until a title bar has been sampled.</param>
internal sealed record DecorationStyle(
    WindowButtonSettings Settings,
    TrafficLightColors Colors,
    HexColor UnifiedCaption,
    HexColor UnifiedText,
    HexColor PlaceholderCaption)
{
    /// <summary>Builds the style for the given settings and theme.</summary>
    /// <param name="settings">Window-button settings.</param>
    /// <param name="shellIsDark">WinGnome's effective theme (drives the unified Adwaita colours).</param>
    /// <param name="appsAreDark">The Windows "app mode" (what other apps' title bars most likely look like).</param>
    public static DecorationStyle Create(WindowButtonSettings settings, bool shellIsDark, bool appsAreDark) => new(
        settings,
        TrafficLightPalette.For(settings),
        TitleBarPalette.Header(shellIsDark),
        TitleBarPalette.Text(shellIsDark),
        TitleBarPalette.WindowsDefault(appsAreDark));
}
