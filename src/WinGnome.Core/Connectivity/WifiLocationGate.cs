using WinGnome.Core.ControlCenter;

namespace WinGnome.Core.Connectivity;

/// <summary>Why the location-gated Wi-Fi calls (network list, scans, current connection) can't be used right now.</summary>
public enum WifiLocationBlock
{
    /// <summary>Nothing known blocks them.</summary>
    None,
    /// <summary>A policy turns location off for the whole PC (Group Policy, MDM or the app-privacy policy): no prompt can ever appear.</summary>
    Policy,
    /// <summary>The device-wide location switch (HKLM consent value) is off; only an administrator can turn it on.</summary>
    DeviceOff,
    /// <summary>The user's "Let apps access your location" switch is off.</summary>
    UserOff,
    /// <summary>"Let desktop apps access your location" is off.</summary>
    DesktopAppsOff,
    /// <summary>WinGnome's own desktop-app entry is denied.</summary>
    AppDenied,
    /// <summary>Nothing is off but Windows hasn't asked yet: the first gated call raises its consent prompt.</summary>
    NotAsked,
}

/// <summary>What the registry and the access check say about location, read without raising a prompt.</summary>
/// <param name="PolicyOff">True when a machine policy turns location off.</param>
/// <param name="Device">The HKLM (device-wide) consent value.</param>
/// <param name="User">The HKCU "Let apps access" value.</param>
/// <param name="DesktopApps">The "Let desktop apps access" value.</param>
/// <param name="ThisApp">WinGnome's own entry under the desktop apps, or null when it has none.</param>
/// <param name="Capability">What <c>AppCapability("wiFiControl")</c> reports; the last input because it misreports under a policy.</param>
public readonly record struct WifiLocationInputs(
    bool PolicyOff,
    ConsentState Device,
    ConsentState User,
    ConsentState DesktopApps,
    ConsentState? ThisApp,
    WifiLocationAccess Capability);

/// <summary>Finds the real reason the Wi-Fi panel can't list nearby networks, so the notice and its buttons match it.</summary>
public static class WifiLocationGate
{
    /// <summary>
    /// The first reason that applies, in the order Windows applies them: policy, device, user, desktop apps, this app,
    /// then the access check. An absent consent value counts as allowed.
    /// </summary>
    public static WifiLocationBlock Evaluate(WifiLocationInputs inputs)
    {
        if (inputs.PolicyOff)
        {
            return WifiLocationBlock.Policy;
        }

        if (!ConsentValue.IsAllowed(inputs.Device))
        {
            return WifiLocationBlock.DeviceOff;
        }

        if (!ConsentValue.IsAllowed(inputs.User))
        {
            return WifiLocationBlock.UserOff;
        }

        if (!ConsentValue.IsAllowed(inputs.DesktopApps))
        {
            return WifiLocationBlock.DesktopAppsOff;
        }

        if (inputs.ThisApp is { } app && !ConsentValue.IsAllowed(app))
        {
            return WifiLocationBlock.AppDenied;
        }

        // The access check goes last: under a policy it says "ask" although no prompt can appear.
        return inputs.Capability switch
        {
            WifiLocationAccess.UserPromptRequired => WifiLocationBlock.NotAsked,
            WifiLocationAccess.DeniedByUser => WifiLocationBlock.AppDenied,
            WifiLocationAccess.DeniedBySystem => WifiLocationBlock.DeviceOff,
            _ => WifiLocationBlock.None,
        };
    }

    /// <summary>
    /// The name of an app's entry under the consent store's <c>NonPackaged</c> key: the executable path with every
    /// backslash replaced by <c>#</c>. Windows may store it in any case, so compare with <see cref="StringComparison.OrdinalIgnoreCase"/>.
    /// </summary>
    public static string AppKeyFor(string processPath)
    {
        ArgumentNullException.ThrowIfNull(processPath);
        return processPath.Replace('\\', '#');
    }

    /// <summary>
    /// True when a machine policy turns location off: Group Policy's "Turn off location" (<c>DisableLocation</c> = 1),
    /// the app-privacy policy forcing denial (<c>LetAppsAccessLocation</c> = 2) or MDM (<c>AllowLocation</c> = 0).
    /// Null means the value isn't set; any other value leaves the choice to the user.
    /// </summary>
    public static bool PolicyOff(int? disableLocation, int? letAppsAccessLocation, int? allowLocation) =>
        disableLocation == 1 || letAppsAccessLocation == 2 || allowLocation == 0;

    /// <summary>
    /// WinGnome's own value among the consent store's app entries: the desktop entry named after
    /// <paramref name="processPath"/> (see <see cref="AppKeyFor"/>), or null when there is none or the path is unknown.
    /// </summary>
    public static ConsentState? AppState(IEnumerable<ConsentKeySnapshot> apps, string? processPath)
    {
        ArgumentNullException.ThrowIfNull(apps);
        if (string.IsNullOrEmpty(processPath))
        {
            return null;
        }

        var key = AppKeyFor(processPath);
        var entry = apps.FirstOrDefault(a => a.IsDesktop && string.Equals(a.Name, key, StringComparison.OrdinalIgnoreCase));
        return entry is null ? null : ConsentValue.Parse(entry.Value);
    }
}
