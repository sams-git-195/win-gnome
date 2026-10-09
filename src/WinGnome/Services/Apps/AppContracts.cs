using System.Windows.Media;
using WinGnome.Core.Shell;

namespace WinGnome.Services.Apps;

/// <summary>An installed application as listed in shell:AppsFolder (the Start menu's "All apps").</summary>
/// <param name="Name">Display name.</param>
/// <param name="ParsingName">
/// Shell parsing name inside AppsFolder. For packaged apps this is the AUMID
/// ("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"); for desktop apps it is an explicit AUMID or a
/// known-folder path ("{6D809377-...}\app.exe"). Launch with "shell:AppsFolder\&lt;ParsingName&gt;".
/// </param>
/// <param name="AppUserModelId">PKEY_AppUserModel_ID of the item, used to match running windows.</param>
/// <param name="TargetPath">Resolved executable path for desktop apps (PKEY_Link_TargetParsingPath), if any.</param>
/// <param name="Host">PKEY_AppUserModel_HostEnvironment: tells full-trust packaged apps (elevatable) from UWP ones.</param>
internal sealed record AppEntry(string Name, string ParsingName, string? AppUserModelId, string? TargetPath, AppHost Host)
{
    /// <summary>Value stored in <c>PinnedApp.LaunchId</c> when this app is pinned.</summary>
    public string LaunchId => ParsingName;
}

/// <summary>Catalogue of installed apps. Implementations must be safe to call from the UI thread.</summary>
internal interface IAppCatalog
{
    /// <summary>All apps sorted by name. Empty until the first <see cref="RefreshAsync"/> completes.</summary>
    IReadOnlyList<AppEntry> Apps { get; }

    /// <summary>Raised on the UI thread after the catalogue has been (re)loaded.</summary>
    event EventHandler? Changed;

    /// <summary>Re-enumerates installed apps on a background STA thread.</summary>
    Task RefreshAsync();

    /// <summary>Finds an app by its launch id / parsing name / AUMID (case-insensitive).</summary>
    AppEntry? FindByLaunchId(string launchId);

    /// <summary>Finds the app a running window belongs to: AUMID match first, then executable path.</summary>
    AppEntry? FindForWindow(string? appUserModelId, string? processPath);

    /// <summary>
    /// Best-effort executable path for a launch id (catalogue TargetPath, else known-folder expansion,
    /// else the input). Used as the <c>resolvePath</c> callback for <c>AppIdentity</c>/<c>DockModelBuilder</c>.
    /// </summary>
    string ResolvePath(string launchId);
}

/// <summary>Icon lookup with caching. Returned images are frozen and safe to share.</summary>
internal interface IIconProvider
{
    /// <summary>Icon for an app launch id, AppsFolder parsing name, or file-system path.</summary>
    ImageSource? GetAppIcon(string launchIdOrPath, int sizePx);

    /// <summary>Icon for a running window (window icon, else its executable's icon).</summary>
    ImageSource? GetWindowIcon(nint hwnd, string? processPath, int sizePx);
}

/// <summary>Starts applications.</summary>
internal interface IAppLauncher
{
    /// <summary>
    /// Launches a pinned app or catalogue entry: AppsFolder parsing name / AUMID, .exe or .lnk path,
    /// or a URI (ms-settings:, shell:...). Returns false (and logs) on failure.
    /// </summary>
    bool Launch(string launchId, string? arguments = null);

    /// <summary>
    /// Launches as planned by <see cref="LaunchPlanner"/>; <see cref="LaunchRequest.Elevate"/> asks for UAC ("runas").
    /// <paramref name="started"/> runs on the calling (UI) thread once the app has been started: straight away for a
    /// normal launch, and only after the UAC prompt is accepted for an elevated one. "No" on the prompt is logged,
    /// and <paramref name="started"/> is not called. Returns false when the launch failed immediately.
    /// <paramref name="owner"/> is the WinGnome window the user clicked (dock, overview): for an elevated launch it is
    /// brought to the front and passed to UAC, so the prompt opens in front instead of as a flashing taskbar button.
    /// </summary>
    bool Launch(LaunchRequest request, Action? started = null, nint owner = 0);
}
