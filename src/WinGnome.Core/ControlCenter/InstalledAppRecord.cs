using System.Globalization;

namespace WinGnome.Core.ControlCenter;

/// <summary>Which Uninstall key a desktop app was registered under, in the order Windows Settings reads them.</summary>
public enum InstalledAppScope
{
    /// <summary><c>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall</c> (64-bit view).</summary>
    Machine,

    /// <summary><c>HKLM\SOFTWARE\WOW6432Node\...\Uninstall</c> (32-bit installers).</summary>
    Machine32,

    /// <summary><c>HKCU\...\Uninstall</c> (per-user installs).</summary>
    User,
}

/// <summary>
/// A desktop app's Uninstall key, as read from the registry. Built with <see cref="FromValues"/>, which accepts the
/// loosely typed values installers really write (DWORDs stored as strings, empty strings, wrong types).
/// </summary>
/// <param name="KeyName">The subkey's name: a product code GUID for MSI installs, any text otherwise.</param>
/// <param name="EstimatedSizeKb">The installer's own size estimate in KB (the <c>EstimatedSize</c> DWORD), if any.</param>
/// <param name="InstallDate">The raw <c>InstallDate</c> text (<c>yyyyMMdd</c> when well formed).</param>
public sealed record InstalledAppRecord(
    string KeyName,
    InstalledAppScope Scope,
    string? DisplayName,
    string? Publisher,
    string? DisplayVersion,
    string? DisplayIcon,
    string? InstallDate,
    long? EstimatedSizeKb,
    string? UninstallString,
    bool WindowsInstaller,
    bool NoRemove,
    bool SystemComponent,
    string? ParentKeyName,
    string? ReleaseType)
{
    /// <summary>
    /// Reads a record from the key's values. <paramref name="value"/> returns a value by name as the registry API gives
    /// it (string, int, long, byte[] or null). Text is trimmed and empty text becomes null; a flag is set when its
    /// value parses to a non-zero number.
    /// </summary>
    public static InstalledAppRecord FromValues(string keyName, InstalledAppScope scope, Func<string, object?> value)
    {
        ArgumentNullException.ThrowIfNull(keyName);
        ArgumentNullException.ThrowIfNull(value);
        return new InstalledAppRecord(
            keyName,
            scope,
            Text(value("DisplayName")),
            Text(value("Publisher")),
            Text(value("DisplayVersion")),
            Text(value("DisplayIcon")),
            Text(value("InstallDate")),
            Number(value("EstimatedSize")),
            Text(value("UninstallString")),
            Flag(value("WindowsInstaller")),
            Flag(value("NoRemove")),
            Flag(value("SystemComponent")),
            Text(value("ParentKeyName")),
            Text(value("ReleaseType")));
    }

    /// <summary>
    /// The file <c>DisplayIcon</c> points at, without quotes or the ",index" suffix, when it is a fully qualified path;
    /// otherwise null (the row shows a generic icon).
    /// </summary>
    public string? IconPath
    {
        get
        {
            if (DisplayIcon is null)
            {
                return null;
            }

            var text = DisplayIcon.Trim();
            var comma = text.LastIndexOf(',');
            if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
            {
                text = text[..comma].Trim();
            }

            text = text.Trim('"').Trim();
            return WindowsPath.IsFullyQualified(text) ? text : null;
        }
    }

    private static string? Text(object? raw)
    {
        var text = raw switch
        {
            string s => s,
            int or long => Convert.ToString(raw, CultureInfo.InvariantCulture),
            _ => null,
        };
        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>A DWORD (the registry API returns it as a signed int, so it is read back as unsigned) or numeric text.</summary>
    private static long? Number(object? raw) => raw switch
    {
        int i => unchecked((uint)i),
        long l => l,
        string s when long.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    private static bool Flag(object? raw) => Number(raw) is { } n && n != 0;
}

/// <summary>Windows path checks that behave the same on every platform the tests run on.</summary>
internal static class WindowsPath
{
    /// <summary>
    /// True for <c>C:\...</c> and <c>\\server\share\...</c> paths, which Windows never resolves through the current
    /// directory or the search path. Drive-relative (<c>C:file</c>), rooted-without-drive (<c>\file</c>) and relative
    /// paths are false.
    /// </summary>
    public static bool IsFullyQualified(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && IsSeparator(path[2]))
        {
            return true;
        }

        return path.Length >= 3 && IsSeparator(path[0]) && IsSeparator(path[1]) && !IsSeparator(path[2]);
    }

    private static bool IsSeparator(char c) => c is '\\' or '/';
}
