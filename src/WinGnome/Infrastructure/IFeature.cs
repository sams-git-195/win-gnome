using WinGnome.Core.Settings;

namespace WinGnome.Infrastructure;

/// <summary>
/// A self-contained piece of WinGnome (top bar, dock, window buttons, ...). Features are created
/// once, started once, receive every settings change, and are disposed on shutdown.
/// All members are called on the UI thread.
/// </summary>
internal interface IFeature : IDisposable
{
    /// <summary>Human-readable name used in logs and the self-test report.</summary>
    string Name { get; }

    /// <summary>Starts the feature with the current settings. Must tolerate the feature being disabled in settings.</summary>
    void Start(AppSettings settings);

    /// <summary>Applies changed settings live (enable/disable included).</summary>
    void ApplySettings(AppSettings settings);
}

/// <summary>
/// Implemented by features that change system state outside WinGnome (taskbar, other windows'
/// caption colours, system parameters). Called from crash handlers, possibly off the UI thread,
/// so implementations must only make direct Win32 calls: no WPF, no locks, no throwing.
/// </summary>
internal interface IEmergencyRestore
{
    void EmergencyRestore();
}
