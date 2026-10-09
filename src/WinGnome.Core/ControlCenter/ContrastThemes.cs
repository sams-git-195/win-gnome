namespace WinGnome.Core.ControlCenter;

/// <summary>One of Windows 11's contrast themes.</summary>
/// <param name="DisplayName">The name Windows 11 Settings shows.</param>
/// <param name="FileName">Its theme file in <c>%WINDIR%\Resources\Ease of Access Themes</c>.</param>
/// <param name="Scheme">
/// The English name of the legacy high-contrast scheme the theme file applies (Windows 11 renamed the themes but kept
/// the files and schemes); <c>HIGHCONTRAST.lpszDefaultScheme</c> may report it.
/// </param>
public sealed record ContrastTheme(string DisplayName, string FileName, string Scheme);

/// <summary>
/// Windows 11's contrast themes and how a Windows read maps to them, for the High contrast row (read-only until the
/// spec 0020 WP4 spike proves which scheme value applies each theme cleanly; KI-087).
/// </summary>
public static class ContrastThemes
{
    /// <summary>The row's text for high contrast off.</summary>
    public const string None = "None";

    // The pairs follow the files' colours: Aquatic's #202020 background with cyan links is hcblack.theme, Desert's
    // #FFFAEF is hcwhite.theme, Dusk's #2D3236 is hc1.theme, Night sky's pure black with violet links is hc2.theme.
    public static IReadOnlyList<ContrastTheme> All { get; } =
    [
        new("Aquatic", "hcblack.theme", "High Contrast Black"),
        new("Desert", "hcwhite.theme", "High Contrast White"),
        new("Dusk", "hc1.theme", "High Contrast #1"),
        new("Night sky", "hc2.theme", "High Contrast #2"),
    ];

    /// <summary>
    /// The theme a reported scheme names: its Windows 11 name, legacy scheme name, theme file name or a path ending in
    /// that file name, ignoring case. Null when it names none of them (a custom or localised scheme, or empty).
    /// </summary>
    public static ContrastTheme? FromScheme(string? scheme)
    {
        if (string.IsNullOrWhiteSpace(scheme))
        {
            return null;
        }

        var name = scheme.Trim();
        var fileName = name[(name.LastIndexOfAny(['\\', '/']) + 1)..];
        return All.FirstOrDefault(theme =>
            string.Equals(theme.DisplayName, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(theme.Scheme, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(theme.FileName, fileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// What the row shows for what Windows reports: <see cref="None"/> when off, a known theme's Windows 11 name, or
    /// any other scheme's own name ("Custom" when it has none).
    /// </summary>
    public static string Describe(bool on, string? scheme)
    {
        if (!on)
        {
            return None;
        }

        if (FromScheme(scheme) is { } theme)
        {
            return theme.DisplayName;
        }

        return string.IsNullOrWhiteSpace(scheme) ? "Custom" : scheme.Trim();
    }
}
