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
    /// Whether notifications are on. <paramref name="nocGlobal"/> (<c>NOC_GLOBAL_SETTING_TOASTS_ENABLED</c>) is what
    /// the current notification platform uses and wins when present; <paramref name="toastEnabled"/>
    /// (<c>PushNotifications\ToastEnabled</c>) is the older copy used when the first is absent.
    /// </summary>
    public static bool Read(int? nocGlobal, int? toastEnabled) => NotificationValue.IsOn(nocGlobal ?? toastEnabled);
}
