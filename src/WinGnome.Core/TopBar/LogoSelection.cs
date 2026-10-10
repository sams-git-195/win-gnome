using WinGnome.Core.Settings;

namespace WinGnome.Core.TopBar;

/// <summary>What the top bar's logo button should draw.</summary>
public enum LogoKind
{
    /// <summary>The built-in four-pane Windows mark.</summary>
    WindowsMark,
    /// <summary>A vector geometry from <c>Theme/LogoMarks.xaml</c>, by resource key.</summary>
    Geometry,
    /// <summary>A custom image, drawn as a silhouette through its mask.</summary>
    Image,
}

/// <summary>
/// The resolved logo target. Exactly one of <see cref="GeometryKey"/> (for <see cref="LogoKind.Geometry"/>) or
/// <see cref="Path"/> (for <see cref="LogoKind.Image"/>) is set; both are null for <see cref="LogoKind.WindowsMark"/>.
/// </summary>
public sealed record LogoTarget(LogoKind Kind, string? GeometryKey, string? Path);

/// <summary>
/// Decides which mark the logo button draws, owning every fallback (spec 0021). Pure: file existence is injected so
/// the whole decision is testable without touching the disk. The caller logs a fallback; Core does not.
/// </summary>
public static class LogoSelection
{
    /// <summary>Resolves the configured logo to a concrete target.</summary>
    /// <param name="logo">The chosen mark.</param>
    /// <param name="path">The custom image path (only meaningful for <see cref="TopBarLogo.Custom"/>).</param>
    /// <param name="fileExists">Injected file-existence check.</param>
    public static LogoTarget Resolve(TopBarLogo logo, string path, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        return logo switch
        {
            TopBarLogo.Foot => new LogoTarget(LogoKind.Geometry, "LogoFoot", null),
            TopBarLogo.Star => new LogoTarget(LogoKind.Geometry, "LogoStar", null),
            TopBarLogo.Terminal => new LogoTarget(LogoKind.Geometry, "LogoTerminal", null),

            // Custom shows the image only when a real file backs it; a blank path or a missing file falls through to
            // the Windows mark (the caller logs once). Normalize keeps a Custom+empty choice, so a file that is only
            // temporarily missing (a network drive) does not destroy the pending selection.
            TopBarLogo.Custom when !string.IsNullOrWhiteSpace(path) && fileExists(path)
                => new LogoTarget(LogoKind.Image, null, path),

            // Windows, a Custom fallback, and any undefined enum value.
            _ => new LogoTarget(LogoKind.WindowsMark, null, null),
        };
    }
}
