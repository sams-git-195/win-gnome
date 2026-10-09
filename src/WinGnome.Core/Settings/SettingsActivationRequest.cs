namespace WinGnome.Core.Settings;

/// <summary>
/// The panel a second launch (<c>--settings-panel &lt;id&gt;</c>) asks the running instance to show. The second launch
/// writes <see cref="FileName"/> into the shared settings folder, then signals the activation event; the running
/// instance reads and deletes it. The content comes from outside the process, so only a plausible panel id is kept.
/// </summary>
public static class SettingsActivationRequest
{
    /// <summary>File name inside the settings folder.</summary>
    public const string FileName = "settings-request.txt";

    /// <summary>Longest accepted id; every real id is far shorter.</summary>
    public const int MaxIdLength = 64;

    /// <summary>File content for a request: the panel id, or empty for "the settings window as it was".</summary>
    public static string Format(string? panelId) => panelId?.Trim() ?? string.Empty;

    /// <summary>The panel id in <paramref name="content"/>, or null when it is empty or isn't a plausible id.</summary>
    public static string? Parse(string? content)
    {
        if (content is null)
        {
            return null;
        }

        var text = content.TrimStart();
        var lineEnd = text.AsSpan().IndexOfAny('\r', '\n');
        var id = (lineEnd < 0 ? text : text[..lineEnd]).TrimEnd();
        return id.Length is > 0 and <= MaxIdLength && id.All(IsIdChar) ? id : null;
    }

    // Panel ids are lower-case words joined by hyphens (PanelIds).
    private static bool IsIdChar(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-';
}
