namespace WinGnome.Core.ControlCenter;

/// <summary>
/// What the system says about a restart that finishes updating. Windows 11's Update Orchestrator doesn't always set the
/// Windows Update Agent's own flag, so the servicing registry keys are read as well.
/// </summary>
/// <param name="WuaRebootRequired"><c>ISystemInformation.RebootRequired</c> from the Windows Update Agent.</param>
/// <param name="AutoUpdateRebootKey">The <c>WindowsUpdate\Auto Update\RebootRequired</c> key exists.</param>
/// <param name="CbsRebootPending">The <c>Component Based Servicing\RebootPending</c> key exists.</param>
/// <param name="ShutdownFlyoutOptions">The Update Orchestrator's <c>ShutdownFlyoutOptions</c> value (undocumented, KI-107), or null when absent or unreadable. Non-zero is what makes Start's power menu offer "Update and restart".</param>
public sealed record PendingRestartSignals(
    bool WuaRebootRequired,
    bool AutoUpdateRebootKey,
    bool CbsRebootPending,
    int? ShutdownFlyoutOptions)
{
    /// <summary>An installed update needs a restart to finish.</summary>
    public bool RestartNeeded => WuaRebootRequired || AutoUpdateRebootKey || CbsRebootPending;

    /// <summary>Updates would install as part of a restart or shutdown, so the power dialogs offer to do that.</summary>
    public bool InstallOnShutdownAvailable => RestartNeeded || ShutdownFlyoutOptions is not (null or 0);
}
