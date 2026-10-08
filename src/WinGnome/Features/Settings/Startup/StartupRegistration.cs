using System.IO;
using System.Security;
using Microsoft.Win32;
using WinGnome.Core.Settings;
using WinGnome.Infrastructure;

namespace WinGnome.Features.Settings.Startup;

/// <summary>Keeps the HKCU Run-key entry that starts WinGnome at sign-in in step with the "Start with Windows" setting.</summary>
/// <param name="simulate">True in safe mode and self-test: the registry is never touched.</param>
internal sealed class StartupRegistration(bool simulate)
{
    private readonly string? _executablePath = Environment.ProcessPath;

    /// <summary>Writes or deletes the Run value only when it differs from what the setting asks for.</summary>
    public void Sync(bool enabled)
    {
        if (simulate)
        {
            return;
        }

        try
        {
            using var read = Registry.CurrentUser.OpenSubKey(StartupEntry.RunKey);
            var current = read?.GetValue(StartupEntry.ValueName) as string;
            var plan = StartupEntry.Plan(enabled, _executablePath, current);
            switch (plan.Action)
            {
                case StartupAction.Write:
                    using (var key = Registry.CurrentUser.CreateSubKey(StartupEntry.RunKey, writable: true))
                    {
                        key.SetValue(StartupEntry.ValueName, plan.Value!, RegistryValueKind.String);
                    }

                    Log.Info("Start with Windows enabled");
                    break;
                case StartupAction.Delete:
                    using (var key = Registry.CurrentUser.OpenSubKey(StartupEntry.RunKey, writable: true))
                    {
                        key?.DeleteValue(StartupEntry.ValueName, throwOnMissingValue: false);
                    }

                    Log.Info("Start with Windows disabled");
                    break;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            Log.Warn("Could not update the Start with Windows entry", ex);
        }
    }
}
