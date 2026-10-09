using WinGnome.Core.Settings;

namespace WinGnome.Core.Shell;

/// <summary>Modifier keys held when a launch was requested (a platform-neutral copy of WPF's ModifierKeys).</summary>
[Flags]
public enum LaunchModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>How a launch id is started.</summary>
public enum LaunchTargetKind
{
    /// <summary>"scheme:rest", such as ms-settings:, shell:RecycleBinFolder or https://.</summary>
    Uri,

    /// <summary>A file-system path, or a known-folder path ("{GUID}\app.exe").</summary>
    File,

    /// <summary>A packaged app's AUMID ("Family_hash!App") that is UWP, or whose host is unknown.</summary>
    PackagedApp,

    /// <summary>A packaged app that runs as a full-trust desktop process (Windows Terminal, the new Notepad).</summary>
    FullTrustPackagedApp,

    /// <summary>Any other shell:AppsFolder parsing name: a desktop app's explicit AUMID.</summary>
    DesktopApp,
}

/// <summary>
/// What kind of process an AppsFolder item runs as, from its PKEY_AppUserModel_HostEnvironment. Observed on
/// Windows 11: 0 for Win32 apps, 1 for UWP apps (Calculator, Store), 2 for full-trust packaged apps whose
/// manifest entry point is Windows.FullTrustApplication (Windows Terminal, Notepad, Photos).
/// </summary>
public enum AppHost
{
    /// <summary>Not read, missing, or a value we don't know.</summary>
    Unknown,
    Desktop,
    Immersive,
    PackagedDesktop,
}

/// <summary>What to start, with which arguments, and whether to ask for elevation (UAC).</summary>
public sealed record LaunchRequest(string LaunchId, string? Arguments, bool Elevate);

/// <summary>
/// Decides whether a launch is elevated, matching Start: desktop apps, elevatable files and full-trust packaged
/// apps can be; UWP apps (and packaged apps of unknown host) and URIs can't.
/// </summary>
public static class LaunchPlanner
{
    /// <summary>Holding both of these elevates, as in Start.</summary>
    public const LaunchModifiers ElevateModifiers = LaunchModifiers.Control | LaunchModifiers.Shift;

    private const string FileExplorerAppUserModelId = "Microsoft.Windows.Explorer";

    /// <summary>File types Explorer offers "Run as administrator" for.</summary>
    private static readonly string[] ElevatableFileExtensions = [".exe", ".lnk", ".bat", ".cmd", ".msc"];

    /// <summary>
    /// Plans a click or Enter on <paramref name="launchId"/>. <paramref name="pin"/> is the matching dock pin, if any:
    /// it supplies arguments and "Always run as administrator". <paramref name="host"/> comes from the app catalogue.
    /// </summary>
    public static LaunchRequest Plan(string launchId, LaunchModifiers modifiers, PinnedApp? pin, AppHost host = AppHost.Unknown)
    {
        var wantsElevation = (modifiers & ElevateModifiers) == ElevateModifiers || pin is { RunAsAdministrator: true };
        return Request(launchId, pin, host, wantsElevation);
    }

    /// <summary>Plans an explicit "Run as administrator"; not elevated when the target cannot be.</summary>
    public static LaunchRequest PlanElevated(string launchId, PinnedApp? pin, AppHost host = AppHost.Unknown) =>
        Request(launchId, pin, host, wantsElevation: true);

    /// <summary>True when "Run as administrator" makes sense for <paramref name="launchId"/>.</summary>
    public static bool CanElevate(string launchId, AppHost host = AppHost.Unknown)
    {
        if (string.IsNullOrWhiteSpace(launchId))
        {
            return false;
        }

        var id = launchId.Trim();
        return Classify(id, host) switch
        {
            // A desktop AUMID is only offered when the catalogue says it is a Win32 app; File Explorer is the shell
            // process and has no elevated mode, as in Start.
            LaunchTargetKind.DesktopApp => host == AppHost.Desktop
                && !string.Equals(id, FileExplorerAppUserModelId, StringComparison.OrdinalIgnoreCase),
            LaunchTargetKind.FullTrustPackagedApp => true,
            LaunchTargetKind.File => ElevatableFileExtensions.Any(ext => id.EndsWith(ext, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    /// <summary>
    /// Classifies a launch id by its shape (nothing is looked up on disk). <paramref name="host"/> only matters for
    /// packaged AUMIDs, telling full-trust apps from UWP ones.
    /// </summary>
    public static LaunchTargetKind Classify(string launchId, AppHost host = AppHost.Unknown)
    {
        ArgumentNullException.ThrowIfNull(launchId);
        var id = launchId.Trim();
        if (IsUri(id))
        {
            return LaunchTargetKind.Uri;
        }

        if (Path.IsPathRooted(id) || KnownFolderPath.TryParse(id, out _, out _))
        {
            return LaunchTargetKind.File;
        }

        if (!id.Contains('!', StringComparison.Ordinal))
        {
            return LaunchTargetKind.DesktopApp;
        }

        return host is AppHost.PackagedDesktop or AppHost.Desktop
            ? LaunchTargetKind.FullTrustPackagedApp
            : LaunchTargetKind.PackagedApp;
    }

    /// <summary>Maps a PKEY_AppUserModel_HostEnvironment value (null when the item has none).</summary>
    public static AppHost HostFromProperty(uint? hostEnvironment) => hostEnvironment switch
    {
        0 => AppHost.Desktop,
        1 => AppHost.Immersive,
        2 => AppHost.PackagedDesktop,
        _ => AppHost.Unknown,
    };

    /// <summary>
    /// True for "scheme:rest" strings such as "ms-settings:", "shell:RecycleBinFolder" or "https://...".
    /// A scheme is at least two characters, so drive-letter paths ("C:\...") never qualify.
    /// </summary>
    public static bool IsUri(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var colon = id.IndexOf(':', StringComparison.Ordinal);
        if (colon < 2 || !char.IsAsciiLetter(id[0]))
        {
            return false;
        }

        foreach (var c in id.AsSpan(0, colon))
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static LaunchRequest Request(string launchId, PinnedApp? pin, AppHost host, bool wantsElevation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launchId);
        var id = launchId.Trim();
        return new LaunchRequest(id, pin?.Arguments, wantsElevation && CanElevate(id, host));
    }
}
