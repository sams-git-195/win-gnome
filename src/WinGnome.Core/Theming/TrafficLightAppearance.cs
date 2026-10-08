using WinGnome.Core.Settings;
using WinGnome.Core.Windows;

namespace WinGnome.Core.Theming;

/// <summary>Pointer interaction with one traffic-light circle.</summary>
public enum CaptionButtonInteraction { None, Hovered, Pressed }

/// <summary>Decides how each traffic-light circle looks for a given window and pointer state.</summary>
public static class TrafficLightAppearance
{
    /// <summary>How far a hovered circle is darkened towards black.</summary>
    public const double HoverDarkening = 0.12;

    /// <summary>How far a pressed circle is darkened towards black.</summary>
    public const double PressedDarkening = 0.28;

    /// <summary>
    /// Fill colour of one circle. Unavailable buttons (e.g. maximise on a fixed-size window) are always drawn in
    /// the inactive colour. With <paramref name="dimInactive"/>, a window without focus shows all three in the
    /// inactive colour until the pointer enters the group, exactly like macOS.
    /// </summary>
    public static HexColor Fill(
        TrafficLightColors colors,
        CaptionButtonKind kind,
        bool isEnabled,
        bool isWindowActive,
        bool isGroupHovered,
        bool dimInactive,
        CaptionButtonInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (!isEnabled || (dimInactive && !isWindowActive && !isGroupHovered))
        {
            return colors.Inactive;
        }

        var fill = kind switch
        {
            CaptionButtonKind.Close => colors.Close,
            CaptionButtonKind.Minimize => colors.Minimize,
            _ => colors.Maximize,
        };

        var black = new HexColor(fill.A, 0, 0, 0);
        return interaction switch
        {
            CaptionButtonInteraction.Hovered => fill.Blend(black, HoverDarkening),
            CaptionButtonInteraction.Pressed => fill.Blend(black, PressedDarkening),
            _ => fill,
        };
    }

    /// <summary>
    /// True when the ×, − and + symbols are drawn: always for presets that show them (GNOME) or when the user
    /// turned hover-only symbols off, otherwise only while the pointer is over the group.
    /// </summary>
    public static bool ShowGlyphs(TrafficLightColors colors, WindowButtonSettings settings, bool isGroupHovered)
    {
        ArgumentNullException.ThrowIfNull(colors);
        ArgumentNullException.ThrowIfNull(settings);
        return colors.AlwaysShowGlyphs || !settings.ShowSymbolsOnHover || isGroupHovered;
    }
}
