using Microsoft.Win32;
using WinGnome.Core.Connectivity;
using WinGnome.Core.ControlCenter;
using WinGnome.Features.Settings.Panels.Privacy;
using WinGnome.Infrastructure;
using WinGnome.Services.Connectivity;

namespace WinGnome.Features.Settings.Panels.Wifi;

/// <summary>
/// Reads what Core's <see cref="WifiLocationGate"/> needs, without any call that could raise Windows' location prompt:
/// the machine policies that turn location off, the consent store (through <see cref="ConsentStore"/>, the only class
/// that touches it) and the capability check. Registry reads only, nothing is written. Each read that fails is logged
/// and counts as not set, which at worst changes the notice's wording: a refused gated call still shows the hedged one.
/// Blocks briefly (registry and WinRT), so call it off the UI thread.
/// </summary>
internal static class WifiLocationGateReader
{
    private const string LocationPolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors";
    private const string AppPrivacyPolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";
    private const string MdmSystemPolicyPath = @"SOFTWARE\Microsoft\PolicyManager\current\device\System";

    private static int _logged;

    public static WifiLocationInputs Read(WifiLocationProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var policyOff = WifiLocationGate.PolicyOff(
            ReadMachineDword(LocationPolicyPath, "DisableLocation"),
            ReadMachineDword(AppPrivacyPolicyPath, "LetAppsAccessLocation"),
            ReadMachineDword(MdmSystemPolicyPath, "AllowLocation"));

        var consent = ReadConsent();
        var inputs = new WifiLocationInputs(
            policyOff,
            consent?.Device ?? ConsentState.Absent,
            consent?.User ?? ConsentState.Absent,
            consent?.Desktop ?? ConsentState.Absent,
            consent is null ? null : WifiLocationGate.AppState(consent.Apps, Environment.ProcessPath),
            probe.Check());

        // Once per process: enough to tell from the log why the notice says what it says.
        if (Interlocked.Exchange(ref _logged, 1) == 0)
        {
            Log.Info($"Wi-Fi: location gate inputs {inputs}, reason {WifiLocationGate.Evaluate(inputs)}");
        }

        return inputs;
    }

    private static ConsentSnapshot? ReadConsent()
    {
        try
        {
            return ConsentStore.Read(PrivacyCapability.Location);
        }
        catch (Exception ex)
        {
            Log.Warn("Wi-Fi: could not read the location consent values", ex);
            return null;
        }
    }

    /// <summary>A REG_DWORD under HKLM, or null when the key or value is missing, of another type, or can't be read.</summary>
    private static int? ReadMachineDword(string path, string name)
    {
        try
        {
            // The 64-bit view, so a 32-bit build reads the same policy keys Windows applies.
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(path);
            return key?.GetValue(name) is int value ? value : null;
        }
        catch (Exception ex)
        {
            Log.Warn($@"Wi-Fi: could not read HKLM\{path}\{name}", ex);
            return null;
        }
    }
}
