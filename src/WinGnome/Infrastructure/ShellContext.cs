using System.Windows.Threading;
using WinGnome.Core.Settings;
using WinGnome.Services;
using WinGnome.Services.Apps;
using WinGnome.Theme;

namespace WinGnome.Infrastructure;

/// <summary>Shared services handed to every feature's constructor.</summary>
internal sealed record ShellContext(
    Dispatcher Dispatcher,
    CommandLineOptions Options,
    SettingsService Settings,
    ThemeManager Theme,
    WindowTracker Windows,
    IAppCatalog Apps,
    IIconProvider Icons,
    IAppLauncher Launcher,
    ShellCommands Commands,
    DisplayLayoutService Displays)
{
    /// <summary>
    /// True in --safe or --selftest mode: no taskbar hiding, registry tweaks or input hooks. A self-test runs
    /// unattended (CI, QA), so it must never change the user's system state even when --safe is omitted.
    /// </summary>
    public bool IsSafeMode => Options.Safe || Options.SelfTest;

    /// <summary>
    /// True when this instance may change the shared "start with Windows" entry: not in safe mode, and only
    /// for the default profile (see <see cref="StartupEntry.IsOwnedBy"/>).
    /// </summary>
    public bool ManagesStartupEntry =>
        !IsSafeMode && StartupEntry.IsOwnedBy(Settings.Directory, SettingsStore.DefaultDirectory);
}

internal enum OverviewMode
{
    /// <summary>Window thumbnails plus search (GNOME "Activities").</summary>
    Windows,
    /// <summary>Full application grid (GNOME "Show Applications").</summary>
    Applications,
}

/// <summary>What the overview should show when opened.</summary>
/// <param name="Mode">Windows or application grid.</param>
/// <param name="OnlyWindows">When set, only these windows are shown (dock "previews" of one app).</param>
internal sealed record OverviewRequest(OverviewMode Mode, IReadOnlyList<nint>? OnlyWindows = null);

/// <summary>
/// Mediator for cross-feature actions so features never reference each other directly.
/// For example the top bar's Activities button raises <see cref="OverviewRequested"/>, which the overview handles.
/// </summary>
internal sealed class ShellCommands
{
    public event EventHandler<OverviewRequest>? OverviewRequested;
    public event EventHandler? OverviewHideRequested;
    /// <summary>The argument is the settings panel to show (a <c>PanelIds</c> value), or null for the current one.</summary>
    public event EventHandler<string?>? SettingsRequested;
    public event EventHandler? TaskbarPeekRequested;
    public event EventHandler? QuitRequested;
    public event EventHandler<int>? DockItemActivationRequested;

    /// <summary>Opens the overview, or closes it when it is already open in the same mode (toggle).</summary>
    public void ShowOverview(OverviewRequest request) => OverviewRequested?.Invoke(this, request);

    public void ShowOverview(OverviewMode mode = OverviewMode.Windows) => ShowOverview(new OverviewRequest(mode));

    public void HideOverview() => OverviewHideRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Opens the settings window, at <paramref name="panelId"/> when given (see <c>PanelIds</c>).</summary>
    public void ShowSettings(string? panelId = null) => SettingsRequested?.Invoke(this, panelId);

    /// <summary>Temporarily reveals the native taskbar (system tray access) while it is hidden.</summary>
    public void PeekTaskbar() => TaskbarPeekRequested?.Invoke(this, EventArgs.Empty);

    public void Quit() => QuitRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Activates (or launches) the dock item at zero-based <paramref name="index"/> — Super+1..9.</summary>
    public void ActivateDockItem(int index) => DockItemActivationRequested?.Invoke(this, index);
}
