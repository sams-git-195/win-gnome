# Spec 0020 WP3 — Apps and Startup apps: notes for WP9

Hand-over text for WP9 to fold into KNOWN_ISSUES.md and PLAN.md (WP3 doesn't edit the shared tables).

## KNOWN_ISSUES rows

| ID | Sev | Area | Issue | Status |
|---|---|---|---|---|
| KI-089 | S4 | Settings | Apps: packaged apps show no size, and publisher, version and install date only when the row is expanded; the first expand or packaged uninstall loads the WinRT projection (`Microsoft.Windows.SDK.NET`), which then stays for the session. Uninstallers that relaunch themselves from %TEMP% exit early, so the list still shows the app until *Refresh*. A desktop app gets WinGnome's *Uninstall…* only when its command is safe (`UninstallPlan`: MSI product code → `System32\msiexec.exe /X`, bare `msiexec`/`rundll32` rooted at System32, otherwise a fully qualified `.exe`); every other one shows *Uninstall in Windows Settings*. System-signed and framework packages can't be removed here. Uninstall itself was not run during development (desktop or packaged): the plans were checked by Core tests and by reading all 282 Uninstall keys on the development machine (64 listed, 63 with a plan, 1 without an `UninstallString`). | Open |
| KI-090 | S4 | Settings | Startup apps: machine-wide items (HKLM Run, Run32, common Startup folder) are read-only with an *Open Task Manager* button, and their approval is read from HKLM `StartupApproved` only. The spec's standard-user check of where Task Manager stores an HKLM item's approval wasn't possible (the development account is an administrator, and a test HKLM item needs elevation); an HKLM item disabled earlier through Task Manager had its value under HKLM. Packaged apps' startup tasks aren't listed (no documented API; a row links to `ms-settings:startupapps`). HKCU Run values can be turned off but not removed (deferred by design). *Add app…* reuses the dock's app picker, whose heading still reads "Add app to dock" (`IDialogService.PickApp` takes no title). | Open |

Also for KI-089 (review nits not fixed): the Recycle Bin operation has no owner window (`IFileOperation.SetOwnerWindow`
isn't called; the panel has no HWND), so a shell prompt such as "delete permanently?" isn't modal to Settings; and an
uninstaller that fails to start is only logged (the list just refreshes; `ShellLaunch.StartAndWatch` doesn't report
the failure to the caller).

KI-063 (writes not checked live): the Apps panel's writes were checked live, see below.

## PLAN.md Core API rows

| Namespace | Type | Purpose |
|---|---|---|
| ControlCenter | `InstalledAppRecord`, `InstalledAppScope`, `InstalledAppFilter`, `AppSizeText`, `InstallDateText` | A desktop app's Uninstall key read from loosely typed registry values (DWORDs as text, wrong types), its icon path, the "Programs and Features" filter (no name, system component, child or update entries, duplicates across views), size and install-date text |
| ControlCenter | `UninstallPlan` | The command that uninstalls a desktop app, or none: never a program resolved through the search path (`PlannedCommand`) |
| ControlCenter | `PackagedAppRows`, `CatalogApp`, `AppListSearch` | Packaged rows from the app catalogue (AUMID → package family, one row per family) and the installed-apps search (fuzzy name, then publisher) |
| Shell | `StartupAppList`, `StartupAppRow`, `StartupApprovalLocation`, `StartupShortcutName`, `StartupApprovedSet.Encode` | The Apps panel's start-up list (which items can be switched or removed, own entry hidden), where each item's StartupApproved value lives, Windows' 12-byte approval value, and the file name of an added Startup-folder shortcut |

Module map: `Features/Settings/Panels/Apps/` (`AppsPanelViewModel`, `InstalledAppItem`/`StartupAppItem`,
`InstalledAppsService`, `PackagedAppsService` (the only WinRT user), `StartupAppsService`, view and resources),
`Interop/NativeMethods.Apps.cs` (`IShellLinkW`, `IFileOperation`, `CLSID_ShellLink`, `CLSID_FileOperation`, the
`FOF_*` flags). `SystemPanelViewModel.LoadAsync` gained an optional `channel` (newest-only per channel).

## Live checks done (2026-10-09, 25H2, administrator account with UAC)

All through the panel on a test instance (own profile, every other feature off, taskbar settings off), driven by
UI Automation; values read back with PowerShell.

- StartupApproved values of every real item (HKCU and HKLM, Run/Run32/StartupFolder) snapshotted before and after:
  identical (13 values).
- *Add app…* → Notepad (packaged): `Notepad.lnk` created in the user's Startup folder (ID-list shortcut);
  opening it started `Microsoft.WindowsNotepad…\Notepad.exe` (closed again).
- Switch off: `HKCU\…\StartupApproved\StartupFolder\Notepad.lnk` = `03 00 00 00` + the current UTC FILETIME; the
  row re-read Off. Switch on: `02 00 00 00` + 8 zero bytes; the row re-read On.
- *Remove* → confirmation → the shortcut is in the Recycle Bin (original location the Startup folder) and the row
  is gone. The dummy's leftover approval value was then deleted by hand.
- Safe mode: every switch, *Add app…* and *Uninstall…* disabled; *Open Task Manager* and the Windows Settings
  links enabled. The list showed the same on/off states as the registry (incl. `06` = on for SecurityHealth and
  `03` with a zero time = off for Docker Desktop); WinGnome's own Run value is hidden.
- No `Microsoft.Windows.SDK.NET.dll` in the process after opening Apps; it loads on the first packaged expand.
- Not checked: Task Manager's display after the toggle (it runs elevated here, so it can't be automated),
  start-up at the next sign-in, any uninstall, visuals in light/dark and at 125 % (screen capture wasn't granted;
  the view uses only theme brushes and existing styles).
