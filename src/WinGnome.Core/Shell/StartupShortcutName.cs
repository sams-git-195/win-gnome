namespace WinGnome.Core.Shell;

/// <summary>The file name of a shortcut the Apps panel adds to the user's Startup folder.</summary>
public static class StartupShortcutName
{
    private const int MaxBaseLength = 100;
    private const string Extension = ".lnk";
    private const string Fallback = "App";

    // Characters Windows doesn't allow in file names (listed here rather than taken from the platform, so the result is
    // the same wherever the tests run).
    private static readonly char[] Invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// "<paramref name="appName"/>.lnk" with characters Windows doesn't allow replaced by "_", trailing dots and spaces
    /// removed and the name capped at 100 characters; "App" when nothing is left, and a name Windows reserves for a
    /// device ("CON", "nul.txt") gets a leading "_". When the name is already in <paramref name="existingFileNames"/> (ignoring case),
    /// " (2)", " (3)", ... is added.
    /// </summary>
    public static string For(string? appName, IEnumerable<string> existingFileNames)
    {
        ArgumentNullException.ThrowIfNull(existingFileNames);
        var taken = new HashSet<string>(existingFileNames, StringComparer.OrdinalIgnoreCase);
        var baseName = Clean(appName);
        var candidate = baseName + Extension;
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{baseName} ({n}){Extension}";
        }

        return candidate;
    }

    private static string Clean(string? appName)
    {
        var chars = (appName ?? "").Select(c => char.IsControl(c) || Invalid.Contains(c) ? '_' : c).ToArray();
        var name = new string(chars).Trim();
        if (name.Length > MaxBaseLength)
        {
            name = name[..MaxBaseLength];
        }

        name = name.TrimEnd('.', ' ');
        if (name.Length == 0)
        {
            return Fallback;
        }

        // Windows reserves a device name before any extension too ("NUL.txt").
        var stem = name.Split('.')[0].TrimEnd(' ');
        return ReservedNames.Contains(stem) ? "_" + name : name;
    }
}
