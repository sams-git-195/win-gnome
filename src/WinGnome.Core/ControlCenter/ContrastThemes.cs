namespace WinGnome.Core.ControlCenter;

/// <summary>One of Windows 11's contrast themes.</summary>
/// <param name="DisplayName">The name Windows 11 Settings shows.</param>
/// <param name="FileName">Its theme file in <c>%WINDIR%\Resources\Ease of Access Themes</c>.</param>
/// <param name="Scheme">
/// The high-contrast scheme name WinGnome writes in <c>HIGHCONTRAST.lpszDefaultScheme</c>: the English name of the
/// legacy scheme the theme file applies (Windows 11 renamed the themes but kept the files and schemes).
/// </param>
public sealed record ContrastTheme(string DisplayName, string FileName, string Scheme);

/// <summary>The choices of the High contrast row (None or one of the four themes) and how a Windows read maps to them.</summary>
public static class ContrastThemes
{
    /// <summary>The row's choice for high contrast off.</summary>
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

    public static ContrastTheme? FromDisplayName(string? displayName) =>
        All.FirstOrDefault(theme => string.Equals(theme.DisplayName, displayName, StringComparison.Ordinal));

    /// <summary>
    /// The row's choices and selection for what Windows reports. Off is <see cref="None"/>; on with a known theme is
    /// its name; on with any other scheme is that scheme's own name, added as an extra choice so the row shows what
    /// Windows really has.
    /// </summary>
    public static (IReadOnlyList<string> Choices, string Selected) ChoicesFor(bool on, string? scheme)
    {
        var known = All.Select(theme => theme.DisplayName).Prepend(None).ToList();
        if (!on)
        {
            return (known, None);
        }

        if (FromScheme(scheme) is { } theme)
        {
            return (known, theme.DisplayName);
        }

        var other = string.IsNullOrWhiteSpace(scheme) ? "Custom" : scheme.Trim();
        known.Add(other);
        return (known, other);
    }
}
