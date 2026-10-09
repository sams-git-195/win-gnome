# Spec 0020 WP1 — Notifications: notes for WP9

Handed over for the integration package (not merged into the shared docs by WP1).

## KNOWN_ISSUES row

| KI-085 | S4 | Settings | Notifications use undocumented HKCU values (`PushNotifications\ToastEnabled` and `LockScreenToastEnabled` first, `NOC_GLOBAL_SETTING_TOASTS_ENABLED` / `NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK` as fallback and written only when present, the `NoToastApplicationNotificationOnLockScreen` policy, per-app `Enabled`/`ShowBanner`/`ShowInActionCenter`). The app list is the registry subset of Windows' own list (`wpndatabase.db`): apps that never wrote a key are missing, Windows' own sources are shown only from a small name table, and generated notification-icon ids are hidden. The Do Not Disturb row stays hidden until spec 0017's service is added to `SystemPanelServices` and `NotificationsPanelViewModel.DoNotDisturb` is filled in `Open`. | Open |

Detail for the KI-085 section: the registry write was checked live (master, lock screen, one app's three switches all
round-trip through the panel and match what the panel then shows), but that a disabled app stops showing toasts
*without sign-out* (AC 3) was not demonstrated: `ToastNotifier.Setting` for the test app did not change after the
per-app `Enabled` write (it may not reflect per-app state for an id the calling process doesn't own), and a test toast
from a throwaway id created no settings key. Check with a real toast from a listed app before closing AC 3.

## PLAN.md Core API rows (ControlCenter)

| Type | Purpose |
|---|---|
| `NotificationValue.IsOn(int?)` | A notification DWORD switch: absent or non-zero is on, only 0 is off |
| `NotificationMaster.Read(int? toastEnabled, int? nocGlobal)` | The master switch: `ToastEnabled` wins, the NOC value is the fallback |
| `NotificationLockScreen.Read` / `IsBlockedByPolicy` | The lock-screen switch (`LockScreenToastEnabled` first) and the policy that forces it off |
| `NotificationAppList.Build(keys, nameOf)` | Registry keys to sorted app rows (`NotificationKeySnapshot` in, `NotificationAppRow` out); built-in names for Windows' own sources, unknown system, generated and unnamed packaged ids hidden |

Module map: `Features/Settings/Panels/Notifications/` holds `NotificationSettingsStore` (the only class touching the
keys), `NotificationsPanelViewModel` (using `Panels/VerifiedSwitch`, the shared verified-set switch) and the view.
