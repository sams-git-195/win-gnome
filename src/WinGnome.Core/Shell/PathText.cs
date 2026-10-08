namespace WinGnome.Core.Shell;

/// <summary>Platform independent helpers for Windows-style path strings (backslash or slash separated).</summary>
internal static class PathText
{
    /// <summary>The text after the last separator, or an empty string.</summary>
    public static string FileName(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "";
        }

        var index = path.LastIndexOfAny(['\\', '/']);
        return index < 0 ? path : path[(index + 1)..];
    }

    /// <summary>The file name without its last extension.</summary>
    public static string FileNameWithoutExtension(string? path)
    {
        var name = FileName(path);
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? name : name[..dot];
    }

    /// <summary>Trims, converts '/' to '\' and lower-cases so paths compare with ordinal equality.</summary>
    public static string Canonical(string path) =>
        path.Trim().Replace('/', '\\').ToLowerInvariant();
}
