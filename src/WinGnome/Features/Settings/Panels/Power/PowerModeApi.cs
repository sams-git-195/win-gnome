using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.Power;

/// <summary>
/// Windows' "power mode" (Best power efficiency / Balanced / Best performance) through the UNDOCUMENTED powrprof
/// overlay functions Windows Settings itself uses. A Windows without them, or a power plan that has no power mode,
/// only hides the row; the panel links to Windows Settings. See KI-061.
/// </summary>
internal static class PowerModeApi
{
    /// <summary>"Best power efficiency".</summary>
    public static readonly Guid PowerSaver = new("961CC777-2547-4F9D-8174-7D86181B8A7A");

    /// <summary>"Balanced" is the absence of an overlay.</summary>
    public static readonly Guid Balanced = Guid.Empty;

    /// <summary>"Best performance".</summary>
    public static readonly Guid Performance = new("DED574B5-45A0-4F42-8737-46345C09C238");

    /// <summary>The current power mode, or null when it is unavailable (logged).</summary>
    public static Guid? Read()
    {
        switch (NativeMethods.PowerGetEffectiveOverlayScheme(out var mode))
        {
            case null:
                Log.Warn("This Windows has no power mode functions in powrprof.dll; the power mode row is hidden");
                return null;
            case 0:
                return mode;
            case var error:
                // Windows 11 has no power mode while a plan other than Balanced is active, and reports an error then.
                Log.Info($"PowerGetEffectiveOverlayScheme failed (error {error}); the power mode row is hidden");
                return null;
        }
    }

    /// <summary>Sets the power mode. Runs on a worker thread.</summary>
    public static bool Write(Guid mode)
    {
        var error = NativeMethods.PowerSetActiveOverlayScheme(mode);
        if (error == 0)
        {
            return true;
        }

        Log.Warn($"PowerSetActiveOverlayScheme failed (error {error?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "missing export"})");
        return false;
    }
}
