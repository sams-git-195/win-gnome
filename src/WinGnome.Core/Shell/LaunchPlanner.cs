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

    /// <summary>A packaged (Store/MSIX) app's AUMID, "Family_hash!App".</summary>
    PackagedApp,

    /// <summary>Any other shell:AppsFolder parsing name: a desktop app's explicit AUMID.</summary>
    DesktopApp,
}

/// <summary>What to start, with which arguments, and whether to ask for elevation (UAC).</summary>
public sealed record LaunchRequest(string LaunchId, string? Arguments, bool Elevate);

/// <summary>
/// Decides whether a launch is elevated. Only desktop apps can be: packaged apps have no "runas" path through
/// activation, and URIs are handed to whatever handles the scheme.
/// </summary>
public static class LaunchPlanner
{
    /// <summary>Holding both of these elevates, as in Start.</summary>
    public const LaunchModifiers ElevateModifiers = LaunchModifiers.Control | LaunchModifiers.Shift;

    private static readonly string[] ElevatableFileExtensions = [".exe", ".lnk"];

    /// <summary>
    /// Plans a click or Enter on <paramref name="launchId"/>. <paramref name="pin"/> is the matching dock pin, if any:
    /// it supplies arguments and "Always run as administrator".
    /// </summary>
    public static LaunchRequest Plan(string launchId, LaunchModifiers modifiers, PinnedApp? pin)
    {
        var wantsElevation = (modifiers & ElevateModifiers) == ElevateModifiers || pin is { RunAsAdministrator: true };
        return Request(launchId, pin, wantsElevation);
    }

    /// <summary>Plans an explicit "Run as administrator"; not elevated when the target cannot be.</summary>
    public static LaunchRequest PlanElevated(string launchId, PinnedApp? pin) => Request(launchId, pin, wantsElevation: true);

    /// <summary>True when "Run as administrator" makes sense for <paramref name="launchId"/>.</summary>
    public static bool CanElevate(string launchId)
    {
        if (string.IsNullOrWhiteSpace(launchId))
        {
            return false;
        }

        var id = launchId.Trim();
        return Classify(id) switch
        {
            LaunchTargetKind.DesktopApp => true,
            LaunchTargetKind.File => ElevatableFileExtensions.Any(ext => id.EndsWith(ext, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    /// <summary>Classifies a launch id by its shape alone (nothing is looked up on disk).</summary>
    public static LaunchTargetKind Classify(string launchId)
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

        return id.Contains('!', StringComparison.Ordinal) ? LaunchTargetKind.PackagedApp : LaunchTargetKind.DesktopApp;
    }

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

    private static LaunchRequest Request(string launchId, PinnedApp? pin, bool wantsElevation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launchId);
        var id = launchId.Trim();
        return new LaunchRequest(id, pin?.Arguments, wantsElevation && CanElevate(id));
    }
}
