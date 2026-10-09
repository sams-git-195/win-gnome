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
/// <param name="LockScreenPolicy">True when policy forces lock-screen notifications off, so the switch can't change.</param>
/// <param name="Keys">One snapshot per app key under <c>Notifications\Settings</c>.</param>
internal sealed record NotificationSnapshot(bool Master, bool LockScreen, bool LockScreenPolicy, IReadOnlyList<NotificationKeySnapshot> Keys);

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
    private const string LockScreenToastEnabled = "LockScreenToastEnabled";
    private const string LockScreenPolicyName = "NoToastApplicationNotificationOnLockScreen";
    private static readonly string[] PolicyKeys =
    [
        @"Software\Policies\Microsoft\Windows\Explorer",
        @"Software\Policies\Microsoft\Windows\CurrentVersion\PushNotifications",
    ];
    private const string LastNotification = "LastNotificationAddedTime";

    /// <summary>Reads the master and lock-screen switches and every app key.</summary>
    public static NotificationSnapshot Read()
    {
        using var settings = Registry.CurrentUser.OpenSubKey(SettingsKey);
        using var push = Registry.CurrentUser.OpenSubKey(PushKey);

        var master = NotificationMaster.Read(Dword(push, ToastEnabled), Dword(settings, NocToasts));
        var lockScreen = NotificationLockScreen.Read(Dword(push, LockScreenToastEnabled), Dword(settings, NocAboveLock));

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

        return new NotificationSnapshot(master, lockScreen, LockScreenBlockedByPolicy(), keys);
    }

    /// <summary>
    /// Sets the master switch: <c>ToastEnabled</c> always, and the newer <c>NOC_GLOBAL_SETTING_TOASTS_ENABLED</c> only
    /// when this install already has it (writing a value Windows never created would invent state).
    /// </summary>
    public static void SetMaster(bool on) => SetPair(ToastEnabled, NocToasts, on);

    /// <summary>Sets whether notifications show on the lock screen, with the same rule as <see cref="SetMaster"/>.</summary>
    public static void SetLockScreen(bool on) => SetPair(LockScreenToastEnabled, NocAboveLock, on);

    /// <summary>Sets one of an app's switches.</summary>
    public static void SetApp(string id, NotificationAppSetting setting, bool on)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var app = Registry.CurrentUser.CreateSubKey($@"{SettingsKey}\{id}", writable: true);
        app.SetValue(setting.ToString(), on ? 1 : 0, RegistryValueKind.DWord);
    }

    private static void SetPair(string pushName, string nocName, bool on)
    {
        using var push = Registry.CurrentUser.CreateSubKey(PushKey, writable: true);
        push.SetValue(pushName, on ? 1 : 0, RegistryValueKind.DWord);
        using var settings = Registry.CurrentUser.OpenSubKey(SettingsKey, writable: true);
        if (settings?.GetValue(nocName) is not null)
        {
            settings.SetValue(nocName, on ? 1 : 0, RegistryValueKind.DWord);
        }
    }

    private static bool LockScreenBlockedByPolicy()
    {
        var values = new List<int?>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var path in PolicyKeys)
            {
                using var key = hive.OpenSubKey(path);
                values.Add(Dword(key, LockScreenPolicyName));
            }
        }

        return NotificationLockScreen.IsBlockedByPolicy(values);
    }

    private static int? Dword(RegistryKey? key, string name) =>
        key?.GetValue(name) is int value ? value : null;
}
