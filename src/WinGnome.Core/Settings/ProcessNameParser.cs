namespace WinGnome.Core.Settings;

/// <summary>Turns what a user types into a process name suitable for <see cref="WindowButtonSettings.ExcludedProcesses"/>.</summary>
public static class ProcessNameParser
{
    private static readonly char[] Separators = ['\\', '/'];

    /// <summary>
    /// Accepts "notepad", "Notepad.exe" or a full path and yields the bare process name without ".exe".
    /// Returns false for empty input or names containing characters Windows forbids in file names.
    /// </summary>
    public static bool TryNormalize(string? input, out string name)
    {
        name = "";
        var text = input?.Trim() ?? "";
        var slash = text.LastIndexOfAny(Separators);
        if (slash >= 0)
        {
            text = text[(slash + 1)..];
        }

        if (text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^4];
        }

        text = text.Trim();
        if (text.Length == 0 || text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        name = text;
        return true;
    }
}
