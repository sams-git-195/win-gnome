using WinGnome.Core.Shell;

namespace WinGnome.Core.Windows;

/// <summary>
/// Produces stable string keys that identify "the same app" across running windows and pinned launchers.
/// Keys look like "aumid:...", "path:..." or "pid:...".
/// </summary>
public static class AppIdentity
{
    private const string FileExplorerAppUserModelId = "Microsoft.Windows.Explorer";

    /// <summary>Identity of a running window: AppUserModelID first, then process path, then process id.</summary>
    public static string ForWindow(string? appUserModelId, string? processPath, int processId)
    {
        if (!string.IsNullOrWhiteSpace(appUserModelId))
        {
            return "aumid:" + appUserModelId.Trim().ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return "path:" + PathText.Canonical(processPath);
        }

        return "pid:" + processId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The AppsFolder id whose icon stands for a window hosted by ApplicationFrameHost (a UWP frame whose app process
    /// was not found, for instance while it is suspended), or null when the executable's or window's own icon applies.
    /// The frame's own icon is ApplicationFrameHost's, or whatever the frame shows at that moment.
    /// </summary>
    public static string? HostedAppIconId(string? appUserModelId, string? processPath) =>
        !string.IsNullOrWhiteSpace(appUserModelId)
        && PathText.FileNameWithoutExtension(processPath).Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)
            ? appUserModelId.Trim()
            : null;

    /// <summary>
    /// The AppUserModelID a window stands for when it carries none of its own, or null. File Explorer folder
    /// windows (class CabinetWClass in explorer.exe) set no AUMID, and the "File Explorer" Start entry
    /// (Microsoft.Windows.Explorer) targets a shell CLSID rather than explorer.exe, so neither identity nor path
    /// would tie them to that pin. Other explorer.exe windows (the desktop, the taskbar, dialogs) keep no AUMID.
    /// </summary>
    public static string? ImpliedAppUserModelId(string? processPath, string? className) =>
        string.Equals(className, "CabinetWClass", StringComparison.Ordinal)
        && PathText.FileName(processPath).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
            ? FileExplorerAppUserModelId
            : null;

    /// <summary>
    /// Identity of a pinned launcher. File-system launch ids become "path:" keys (after
    /// <paramref name="resolvePath"/> has expanded known-folder prefixes); anything else is an AppUserModelID.
    /// </summary>
    public static string ForLaunchId(string launchId, Func<string, string>? resolvePath = null)
    {
        ArgumentNullException.ThrowIfNull(launchId);

        if (!KnownFolderPath.LooksLikeFileSystemPath(launchId))
        {
            return "aumid:" + launchId.Trim().ToLowerInvariant();
        }

        var resolved = resolvePath is null ? launchId : resolvePath(launchId);
        return "path:" + PathText.Canonical(string.IsNullOrWhiteSpace(resolved) ? launchId : resolved);
    }

    /// <summary>True when both identity keys are equal, ignoring case.</summary>
    public static bool IsSameApp(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>A display name derived from an executable path: file name without extension, first letter upper-cased.</summary>
    public static string DisplayNameFromPath(string? path)
    {
        var name = PathText.FileNameWithoutExtension(path).Trim();
        if (name.Length == 0)
        {
            return "Unknown";
        }

        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
