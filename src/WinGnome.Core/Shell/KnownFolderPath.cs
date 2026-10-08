namespace WinGnome.Core.Shell;

/// <summary>
/// Handles the "{GUID}\relative\path" form that shell:AppsFolder uses for classic desktop apps,
/// where the GUID is a known-folder id such as FOLDERID_ProgramFilesX86.
/// </summary>
public static class KnownFolderPath
{
    private const int GuidTextLength = 38; // "{" + 36 characters + "}"

    /// <summary>
    /// Splits "{GUID}\relative" into the folder id and the part after the separator.
    /// "{GUID}" alone yields an empty relative part.
    /// </summary>
    public static bool TryParse(string path, out Guid folderId, out string relative)
    {
        folderId = Guid.Empty;
        relative = "";
        if (string.IsNullOrEmpty(path) || path.Length < GuidTextLength || path[0] != '{' || path[GuidTextLength - 1] != '}')
        {
            return false;
        }

        if (!Guid.TryParseExact(path[..GuidTextLength], "B", out var id))
        {
            return false;
        }

        if (path.Length > GuidTextLength && path[GuidTextLength] is not ('\\' or '/'))
        {
            return false;
        }

        folderId = id;
        relative = path.Length > GuidTextLength ? path[(GuidTextLength + 1)..] : "";
        return true;
    }

    /// <summary>
    /// Replaces the GUID prefix with the folder returned by <paramref name="lookup"/>. Returns the original
    /// string when it is not in known-folder form or the folder is unknown.
    /// </summary>
    public static string Resolve(string path, Func<Guid, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        if (!TryParse(path, out var id, out var relative))
        {
            return path;
        }

        var folder = lookup(id);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return path;
        }

        var root = folder.TrimEnd('\\', '/');
        return relative.Length == 0 ? root : root + "\\" + relative;
    }

    /// <summary>
    /// Heuristic: true for rooted paths (drive letter or UNC), for anything with a directory part that ends in
    /// .exe or .lnk, and for known-folder form. AppUserModelIDs and bare names return false.
    /// </summary>
    public static bool LooksLikeFileSystemPath(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        var text = s.Trim();
        if (text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] is '\\' or '/')
        {
            return true;
        }

        if (text.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return true;
        }

        if (TryParse(text, out _, out _))
        {
            return true;
        }

        var hasDirectory = text.Contains('\\') || text.Contains('/');
        return hasDirectory
            && (text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || text.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase));
    }
}
