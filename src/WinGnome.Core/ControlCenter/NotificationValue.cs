namespace WinGnome.Core.ControlCenter;

/// <summary>How Windows stores a notification on/off switch: a DWORD where an absent value means on.</summary>
public static class NotificationValue
{
    /// <summary>True unless the value is exactly 0; an absent value (null) and any other number mean on.</summary>
    public static bool IsOn(int? value) => value != 0;
}

/// <summary>The notification master switch, which Windows keeps in two places.</summary>
public static class NotificationMaster
{
    /// <summary>
    /// Whether notifications are on. <paramref name="toastEnabled"/> (<c>PushNotifications\ToastEnabled</c>) is the value
    /// the notification platform reads and wins when present; <paramref name="nocGlobal"/>
    /// (<c>NOC_GLOBAL_SETTING_TOASTS_ENABLED</c>, which many installs never have) is the fallback.
    /// </summary>
    public static bool Read(int? toastEnabled, int? nocGlobal) => NotificationValue.IsOn(toastEnabled ?? nocGlobal);
}

/// <summary>The lock-screen notification switch, which Windows keeps in two places and policy can force off.</summary>
public static class NotificationLockScreen
{
    /// <summary>
    /// Whether notifications show on the lock screen. <paramref name="lockScreenToastEnabled"/>
    /// (<c>PushNotifications\LockScreenToastEnabled</c>) wins when present; <paramref name="nocAboveLock"/>
    /// (<c>NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK</c>) is the fallback; neither means on.
    /// </summary>
    public static bool Read(int? lockScreenToastEnabled, int? nocAboveLock) => NotificationValue.IsOn(lockScreenToastEnabled ?? nocAboveLock);

    /// <summary>
    /// True when the <c>NoToastApplicationNotificationOnLockScreen</c> policy is set (to 1) in any of the places it can
    /// live (HKCU and HKLM, <c>Explorer</c> and <c>PushNotifications</c> policy keys; null for an absent value). The
    /// switch then can't be changed from here.
    /// </summary>
    public static bool IsBlockedByPolicy(IEnumerable<int?> policyValues)
    {
        ArgumentNullException.ThrowIfNull(policyValues);
        return policyValues.Any(v => v == 1);
    }
}
