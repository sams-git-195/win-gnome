namespace WinGnome.Core.Tray;

/// <summary>
/// The click fallback for dead tray icons: known Windows components whose icon neither re-registers on
/// TaskbarCreated (so the startup heal cannot repair it) nor carries usable raw callback fields on its
/// unflagged updates (so learning has nothing to learn) — measured in the field for SecurityHealthSystray,
/// spec 0022 addendum 2. A tiny built-in table maps the owner's process file name to the URI the icon's own
/// click opens, so a click on the dead icon opens the app instead of doing nothing. Deliberately data and
/// nothing else: one entry per field-confirmed dead icon, no generic "activate the app" heuristic. Hardcoding
/// Windows' own components has precedent (<c>NotificationAppList.SystemNames</c>).
/// </summary>
public static class TrayClickFallback
{
    // Keyed by lower-case process file name; matching ignores case. Values are launch URIs the app layer
    // hands to its launcher (the same path TopBarActions uses for ms-settings: & friends).
    private static readonly Dictionary<string, string> LaunchUris = new(StringComparer.OrdinalIgnoreCase)
    {
        ["securityhealthsystray.exe"] = "windowsdefender://",
    };

    /// <summary>
    /// The minimum gap between two fallback launches for one icon: a physical double-click delivers
    /// LeftUp, LeftDoubleClick and LeftUp in quick succession, and one launch must serve them all.
    /// </summary>
    public const long LaunchGapMs = 750;

    /// <summary>
    /// The URI to open for a dead icon owned by the process whose image is <paramref name="processFileName"/>,
    /// or null when the table has no entry. Accepts a bare file name or a full path (the app layer resolves
    /// full paths); only the file-name part is matched, case-insensitively.
    /// </summary>
    public static string? LaunchUriFor(string processFileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(processFileName);
        return LaunchUris.TryGetValue(Path.GetFileName(processFileName), out var uri) ? uri : null;
    }

    /// <summary>
    /// Whether a fallback launch is due: true for the first one (<paramref name="lastLaunchMs"/> null) and once
    /// <see cref="LaunchGapMs"/> has fully elapsed since the last, so one physical click sequence (down, up,
    /// double-click, up) opens the app exactly once. Assumes a monotonic clock (Environment.TickCount64).
    /// </summary>
    public static bool ShouldLaunch(long nowMs, long? lastLaunchMs) =>
        lastLaunchMs is null || nowMs - lastLaunchMs >= LaunchGapMs;
}
