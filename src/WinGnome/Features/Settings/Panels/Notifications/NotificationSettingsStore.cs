using Microsoft.Win32;
using WinGnome.Core.ControlCenter;

namespace WinGnome.Features.Settings.Panels.Notifications;

/// <summary>The three per-app switches stored under <c>Notifications\Settings\&lt;id&gt;</c>.</summary>
internal enum NotificationAppSetting
{
    Enabled,
    ShowBanner,
    ShowInActionCenter,
}

/// <summary>Everything the Notifications panel shows, as read from the registry.</summary>
/// <param name="Master">The master switch.</param>
/// <param name="LockScreen">Whether notifications show on the lock screen.</param>
/// <param name="Keys">One snapshot per app key under <c>Notifications\Settings</c>.</param>
internal sealed record NotificationSnapshot(bool Master, bool LockScreen, IReadOnlyList<NotificationKeySnapshot> Keys);

/// <summary>
/// Reads and writes Windows' notification switches. The only class that touches these keys. Windows documents no API
/// for them: they are the HKCU values its own Settings page and notification platform use (KI-085), written as DWORDs.
/// Reads are cheap but the app list walks every app's key, so call <see cref="Read"/> off the UI thread.
/// </summary>
internal static class NotificationSettingsStore
{
    private const string Prefix = @"Software\Microsoft\Windows\CurrentVersion\";
    private const string SettingsKey = Prefix + @"Notifications\Settings";
    private const string PushKey = Prefix + @"PushNotifications";
    private const string NocToasts = "NOC_GLOBAL_SETTING_TOASTS_ENABLED";
    private const string NocAboveLock = "NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK";
    private const string ToastEnabled = "ToastEnabled";
    private const string LastNotification = "LastNotificationAddedTime";

    /// <summary>Reads the master and lock-screen switches and every app key.</summary>
    public static NotificationSnapshot Read()
    {
        using var settings = Registry.CurrentUser.OpenSubKey(SettingsKey);
        using var push = Registry.CurrentUser.OpenSubKey(PushKey);

        var master = NotificationMaster.Read(Dword(settings, NocToasts), Dword(push, ToastEnabled));
        var lockScreen = NotificationValue.IsOn(Dword(settings, NocAboveLock));

        var keys = new List<NotificationKeySnapshot>();
        foreach (var id in settings?.GetSubKeyNames() ?? [])
        {
            using var app = settings!.OpenSubKey(id);
            if (app is null)
            {
                continue;
            }

            keys.Add(new NotificationKeySnapshot(id, Dword(app, nameof(NotificationAppSetting.Enabled)),
                Dword(app, nameof(NotificationAppSetting.ShowBanner)), Dword(app, nameof(NotificationAppSetting.ShowInActionCenter)),
                app.GetValue(LastNotification) is not null));
        }

        return new NotificationSnapshot(master, lockScreen, keys);
    }

    /// <summary>Sets the master switch in both places Windows keeps it.</summary>
    public static void SetMaster(bool on)
    {
        using var settings = Registry.CurrentUser.CreateSubKey(SettingsKey, writable: true);
        settings.SetValue(NocToasts, on ? 1 : 0, RegistryValueKind.DWord);
        using var push = Registry.CurrentUser.CreateSubKey(PushKey, writable: true);
        push.SetValue(ToastEnabled, on ? 1 : 0, RegistryValueKind.DWord);
    }

    /// <summary>Sets whether notifications show on the lock screen.</summary>
    public static void SetLockScreen(bool on)
    {
        using var settings = Registry.CurrentUser.CreateSubKey(SettingsKey, writable: true);
        settings.SetValue(NocAboveLock, on ? 1 : 0, RegistryValueKind.DWord);
    }

    /// <summary>Sets one of an app's switches.</summary>
    public static void SetApp(string id, NotificationAppSetting setting, bool on)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var app = Registry.CurrentUser.CreateSubKey($@"{SettingsKey}\{id}", writable: true);
        app.SetValue(setting.ToString(), on ? 1 : 0, RegistryValueKind.DWord);
    }

    private static int? Dword(RegistryKey? key, string name) =>
        key?.GetValue(name) is int value ? value : null;
}
