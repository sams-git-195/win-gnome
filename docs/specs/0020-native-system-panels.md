# 0020 — More native system panels

Status: Agreed — user decisions 2026-10-09; advisor review (Fable) applied 2026-10-09. Ready for implementation after spec 0010; WP0 first.

## Problem
Spec 0015 shipped nine native panels; the other GNOME panels are arrows that open Windows Settings. In shell mode
(spec 0013) and for anyone who wants one GNOME-style place for daily settings, the most-used of those links
(Notifications, Printers, Apps, Accessibility, Region & Language, Privacy, Removable Media, Windows Update) should be
pages inside WinGnome's Settings app. Startup apps are also listed in 0015's phase 2, and the Core model for them
(`StartupRunner`, `StartupApprovedSet`, spec 0013) exists but is unwired.

## Behaviour
Eight link panels become native pages, built on `SystemPanelViewModel` like Sound, Power, Mouse and Keyboard: read
when opened, released when left, writes through `SystemSettingWriter`, a problem banner with the Windows Settings
link on failure, read-only in `--safe`. Each keeps its `ms-settings:` page as the fallback link at the bottom.
**Default Apps, Online Accounts, Users, Sharing and Colour stay links.**

Every switch and drop-down on these pages is **verified-set**: the value is written, read back from Windows, and the
row shows what Windows reports (busy while the write is in flight). No optimistic UI: a value Windows rejects or
rewrites shows as it really is, with the problem banner.

1. **Notifications** — *Do Not Disturb* switch (the shared DND service from spec 0017; this panel only consumes
   it); *Notifications* master switch; *Lock screen notifications* switch; a list of apps that have registered for
   notifications, each with an on/off switch (and, expanded, *Banners* and *Show in notification centre*).
2. **Printers** — every local and connected printer with its status (Ready, Offline, Paper jam, *n* jobs), the
   default marked; per printer *Set as default*, *Print queue*, *Printer properties*, *Printing preferences*.
   A *Let Windows manage my default printer* switch. *Add printer…* opens `ms-settings:printers`.
3. **Apps** — two sections:
   - *Installed apps*: desktop apps (Uninstall registry keys) with icon, publisher, version, size (when the app
     reports it) and install date; packaged apps for the current user (from WinGnome's app catalogue) with icon and
     name, and publisher, version and install date when the row is expanded. Searchable. *Uninstall…* asks for
     confirmation, then starts the app's own uninstaller (desktop) or removes the package for the current user
     (packaged). Any UAC prompt is the uninstaller's own; WinGnome never elevates. A desktop app whose uninstall
     command can't be run safely (see Design) shows *Uninstall in Windows Settings* instead.
   - *Startup apps* (GNOME Tweaks style): each startup item with a switch (StartupApproved) and *Add…* (app
     picker). **User decision (2026-10-09), safer option:** items are *disabled*, not deleted. The only *Remove* is
     for shortcuts in the user's own Startup folder, which go to the Recycle Bin. HKCU Run values have a switch but
     no *Remove* (deleting them is deferred; Task Manager and the app's own setting remain). Machine-wide items show
     their state read-only.
4. **Accessibility** — *Sticky keys*, *Slow keys*, *Bounce keys* (slow and bounce exclusive, see Design); *Cursor
   size* (1–15); *Text cursor thickness* (1–20 px); *Reduce animation*; *High contrast* (None or one of Windows 11's
   four contrast themes: Aquatic, Desert, Dusk, Night sky; Windows shows its own "Please wait" while it switches);
   launch buttons for *On-screen keyboard*, *Magnifier* and *Screen reader (Narrator)*. Cursor colour, text cursor
   indicator and text size are rows that open their Windows Settings page (see Design).
5. **Region & Language** — *Region* (country, `SetUserGeoName`); *Formats*: the format locale's name read-only
   with *Change…* linking to Windows, then *First day of week*, *Short date*, *Long date*, *Short time*, *Long time*,
   each a drop-down of the locale's own patterns with a live sample, and *Reset to defaults*. *Display language*
   read-only with a link to `ms-settings:regionlanguage`.
6. **Privacy** — for Camera, Microphone and Location: *Let apps access your …* and *Let desktop apps access your …*
   (current user), the device-wide state read-only (it needs an administrator; link), and an expandable per-app list:
   packaged apps with a switch, desktop apps read-only with *In use* or *Last used*. *Screen lock* row links to
   `ms-settings:lockscreen`.
7. **Removable Media** — *Never prompt or start programs on media insertion* (AutoPlay off); per media type
   (Removable drive, Memory card, plus every known event the machine has handlers for, e.g. DVD movie, Audio CD,
   Camera) a drop-down: *Ask what to do*, *Do nothing*, *Open folder*, or an installed handler.
8. **Windows Update** — read-only status: *Last checked*, *Last installed*, pending updates (count and titles),
   *Restart required*. *Check for updates* opens `ms-settings:windowsupdate-action`; *Update history* and
   *Advanced options* are links. Status is read off the UI thread with a spinner.

**Sidebar and search** (`SettingsPanelCatalog`): the eight panels change from `Link` to `Native`. Groups:
Notifications moves to *Apps and privacy* (after Apps, as in GNOME 47); Accessibility moves to *Personalisation*
(after Multitasking); the rest keep their group. New keywords:

| Panel | Keywords added |
|---|---|
| Notifications | banners, toasts, lock screen, app notifications, dnd |
| Printers | default printer, print queue, printer properties, add printer |
| Apps | startup apps, autostart, run at sign-in, store apps, packages, size, version |
| Accessibility | sticky keys, slow keys, bounce keys, filter keys, cursor size, pointer size, text cursor, caret, animations, reduce motion, high contrast, contrast themes, osk, screen reader |
| Region & Language | region, country, date format, time format, first day of week, number format, display language |
| Privacy | camera, microphone, webcam, location, app permissions, screen lock |
| Removable Media | autoplay, cd, dvd, memory card, camera, media insertion |
| Windows Update | check for updates, pending updates, restart required, update history |

**Settings added:** none in `settings.json`. Every value lives in Windows. Old settings files load unchanged.
WinGnome writes no file of its own for these panels. The Startup apps list hides WinGnome's own Run value (owned by
General → *Start with Windows*).

## Non-goals
- Installing updates, pausing updates, update policies (admin, and Windows Settings does it well).
- Adding printers or drivers, sharing printers, print server settings.
- Changing the format locale as a whole, the display language, or installing language packs (no documented API).
- Device-wide privacy switches, HKLM StartupApproved, HKLM AutoPlay (admin; HKCU-only rule).
- Deleting HKCU Run values (deferred; disabling covers the need without destroying data).
- Per-app notification priority, notification sounds per app, Focus sessions.
- Repair/modify of desktop apps, app execution aliases, optional features, app sizes for packaged apps.
- Text size (`TextScaleFactor`) and cursor colour as native controls (see Design: link rows).
- Making WinGnome's own surfaces follow high contrast (separate work; KI-088).

## Design
### Shared (work package 0, lands first)
- **Catalogue** (`WinGnome.Core/ControlCenter/SettingsPanelCatalog.cs`): the eight entries become `Native(...)` with
  their current URI as fallback, regrouped and re-keyworded as above. `DirectLinkFor` then returns null for them
  (top-bar rows open the settings window on that panel). Tests in `SettingsPanelCatalogTests` updated: kinds, groups,
  order, search hits for each new keyword, `DirectLinkFor` rows moved from the link theory to the native theory, and
  a new fact that **every `Native` system panel has a non-null `LinkUri`** (the fallback below always has a target).
- **Native entries without a page fall back to their link.** `SidebarEntry.IsLink` becomes
  `Panel.Kind == PanelKind.Link || Page is null`, so `SettingsWindowViewModel` stops filtering page-less native
  entries out, and both `ShowPanel` and the sidebar (`OpenLinkEntry`, the `SelectedEntry` setter's ignore path) open
  `Panel.LinkUri` for them. The catalogue can therefore flip before every page lands, and a page that fails to build
  still leaves a working link; top-bar rows keep working throughout. No separate Core plan type (one boolean in the
  app layer; the Core guarantee is the `LinkUri` fact above).
- **`SystemPanelContext`** gains one `SystemPanelServices Services` record: `IAppCatalog Apps`,
  `IIconProvider Icons`, `IAppLauncher Launcher` (the interface, from `ShellContext`), `IDialogService Dialogs`, and
  spec 0017's DND service (nullable until 0017 lands). Panels take only the record, so adding a service doesn't
  change every constructor. As built in WP0 the record has the first four; the DND member is added (nullable) by
  whichever of 0017 or WP1 lands second, since its type doesn't exist yet.
- **`SystemPanelViewModel.LoadAsync<T>(Func<T> read, Action<T> show, bool longRunning = false)`**: runs `read` on the
  thread pool, or with `longRunning` on a dedicated named background thread (`"WinGnome panel: <id>"`, MTA), and
  calls `show` on the dispatcher only if the panel is still open from the same `Open` (a generation counter), logging
  exceptions and setting `Problem`. `longRunning` is required for WUA, `EnumPrinters` and the uninstall-key walk, so
  a stuck RPC or COM call never starves the pool. Replaces the ad-hoc `Task.Run(...).ContinueWith` in About/Displays
  for new panels only (existing panels untouched).
- **`ShellThread`** (`Services/Apps/ShellThread.cs`): `AppLauncher.RunOnShellThread` extracted unchanged into a
  shared helper (`ShellThread.Run(string what, Func<bool> work, Action? done)`: STA background thread, logs every
  exception, posts `done` to the caller's context). `AppLauncher` calls it; this extraction is its own refactor
  commit in WP0. Every shell COM call these panels make (`IShellLinkW` + `IPersistFile.Save`, `IFileOperation`,
  `ShellExecuteEx`) runs on it — never on the dispatcher (they pump and can block) and never on the MTA
  `LoadAsync` worker (shell objects need STA).
- **`ShellLaunch`** in the app (`Features/Settings/Panels/ShellLaunch.cs`), on `ShellThread`:
  - `SystemTool(string exe, string args)`: `ShellExecuteEx` on `%SystemRoot%\System32\<exe>` (never a bare name:
    no search-path hijack), no `runas` verb, logs failures. `ShellExecuteEx`, not `CreateProcess`, so manifests that
    need elevation or uiAccess (osk.exe, magnify.exe, Narrator, uninstallers) get their own prompt instead of
    `ERROR_ELEVATION_REQUIRED`.
  - `StartAndWatch(PlannedCommand, Action exited)`: same, with `SEE_MASK_NOCLOSEPROCESS`. `hProcess` may be 0 (the
    file was handed to an already-running process, or the launch was a DDE/handler hand-off): then `exited` is
    posted at once (refresh immediately), as it is when the launch fails. Otherwise a one-shot `RegisteredWaitHandle`
    posts `exited`; the handle is always closed (`CloseHandle`) when the wait fires or is unregistered on `Close`.
    It returns an `IDisposable` the panel disposes in `Close` (cancels the wait, closes the handle, drops `exited`).
    `PlannedCommand(Executable, Arguments)` is a Core record (`ControlCenter/PlannedCommand.cs`, WP0) that WP3's
    `UninstallPlan` returns; `StartAndWatch` rejects an executable that isn't fully qualified.
- **`IndirectString.Load("@res.dll,-123")`** (`SHLoadIndirectString`, declared once in `NativeMethods.Shell.cs`),
  used by Notifications, Removable Media and Apps.
- **`Panels/PanelRegistry.cs`**: one line per system panel (`[PanelIds.X] = context => new XPanelViewModel(context)`),
  read by `SettingsWindowViewModel`. WP0 moves the nine existing system-panel lines into it (refactor commit, no
  behaviour change).
- **`Panels/PanelResources.xaml`**: a merged-dictionaries list, merged once by `SettingsWindow.xaml`; each panel's
  `DataTemplate` lives in its own `Panels/<Name>/<Name>PanelResources.xaml`. Existing templates stay in
  `SettingTemplates.xaml`.
- WP1–8 therefore each add exactly one line to `PanelRegistry.cs` and one to `PanelResources.xaml`; nothing else is
  shared.

### Per panel
Each panel owns `src/WinGnome/Features/Settings/Panels/<Name>/` (view, view model, resources, one service), Core files
named below in `src/WinGnome.Core/ControlCenter/` (or `Shell/` for startup), tests mirrored in
`tests/WinGnome.Core.Tests/`, and at most one **new** Interop file. No panel edits another's files or an existing
`NativeMethods.*` partial; existing declarations (e.g. `SPI_GETCLIENTAREAANIMATION` in `NativeMethods.Overview.cs`,
`SystemParametersInfoGet/Set`, `SendMessageTimeout`, `ShellExecuteEx`) are reused, not redeclared. Every write is
verified-set (write through the service, re-read, show the re-read value).

**1. Notifications** — `Panels/Notifications/`, `NotificationSettingsStore` (the only class touching the keys).
- Undocumented HKCU storage, isolated, all under `Software\Microsoft\Windows\CurrentVersion\`:
  - Master: read `Notifications\Settings` value `NOC_GLOBAL_SETTING_TOASTS_ENABLED` first, falling back to
    `PushNotifications` value `ToastEnabled` when it's absent; write **both** (DWORD).
  - Lock screen: `Notifications\Settings` value `NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK` (a value under
    `Settings`, not a subkey).
  - Per app: `Notifications\Settings\<AUMID>` values `Enabled`, `ShowBanner`, `ShowInActionCenter` (DWORD;
    absent = on).
- Apps listed: subkeys of `Notifications\Settings` that have `LastNotificationAddedTime` or any of the three values.
  This is a subset of Windows' own list, which lives in the notification platform's database (`wpndatabase.db`);
  an app that registered but never wrote a key isn't shown (KI-085). Names:
  `IAppCatalog.FindForWindow(aumid, null)`; `Windows.SystemToast.*` ids map through a small table in Core, unknown
  system ids are hidden.
- DND: spec 0017's service from `Services`; the row is hidden if it is null or not available.
- Core: `NotificationAppList.Build(IEnumerable<NotificationKeySnapshot>, Func<string,string?> nameOf)` → sorted
  rows (name, aumid, enabled, banner, centre), hidden rules; `NotificationValue.IsOn(int?)`;
  `NotificationMaster.Read(int? nocGlobal, int? toastEnabled)`. Tests: absent/0/1/other values, NOC value wins over
  `ToastEnabled`, fallback when absent, system-id table, unnamed packaged id hidden, sort is culture-invariant
  ordinal-ignore-case.
- After a write, `WM_SETTINGCHANGE` is not needed. Per-app `Enabled` is expected to be picked up live by the
  notification platform (AC 3 checks it); needing sign-out is the failure case, and then the row says "Takes effect
  after you sign out" and KI-085 records it.

**2. Printers** — `Panels/Printers/`, `PrinterService`, `Interop/NativeMethods.Printing.cs` (winspool:
`EnumPrintersW` level 2 with `PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS`, `GetDefaultPrinterW`,
`SetDefaultPrinterW`).
- `PRINTER_INFO_2` has everything shown (name, status, attributes, job count); no other level or per-printer call.
- Enumeration can block on a network print server's RPC: always through `LoadAsync(..., longRunning: true)`.
  Refresh on open, after each action and from a *Refresh* button; no change-notification thread, no polling.
- Queue, properties and preferences: `rundll32.exe printui.dll,PrintUIEntry /o|/p|/e /n "<name>"` via
  `ShellLaunch.SystemTool` (documented PrintUIEntry).
- *Let Windows manage my default printer*: HKCU `Software\Microsoft\Windows NT\CurrentVersion\Windows\
  LegacyDefaultPrinterMode` (1 = user manages; undocumented but stable since 1511). *Set as default* while Windows
  manages first writes 1 (as Windows Settings does; the switch updates), then calls `SetDefaultPrinter`.
- Core: `PrinterStatusText.Describe(status, attributes, jobs)` (PRINTER_STATUS_* bits, `WORK_OFFLINE`, job count),
  `PrintUiCommand.Build(PrintUiAction, name)`: the only name it rejects is one containing `"` (it can't be quoted
  for PrintUIEntry); everything else is quoted. Tests: each status bit, combined bits pick the most serious, offline
  attribute, 0/1/many jobs, quoting, names with spaces, commas and backslashes (`\\server\queue`), a `"` rejected.

**3. Apps** — `Panels/Apps/` with `InstalledAppsService`, `PackagedAppsService`, `StartupAppsService`;
`Interop/NativeMethods.Apps.cs` only if `IShellLinkW`/`IPersistFile`/`IFileOperation` aren't already declared.
- Desktop: read (never write) `HKLM\...\Uninstall`, `HKLM\SOFTWARE\WOW6432Node\...\Uninstall`, `HKCU\...\Uninstall`
  through `LoadAsync(..., longRunning: true)` (hundreds of keys). Values are read with environment expansion.
- Packaged: **no WinRT on open.** Rows come from `IAppCatalog.Apps` (already loaded by the shell): entries whose
  parsing name is an AUMID (`PFN!AppId`), grouped by package family name, with icons from
  `IIconProvider.GetAppIcon(aumid, size)`. `Windows.Management.Deployment.PackageManager` loads only when the user
  expands a packaged row (publisher, version, install date via `FindPackagesForUser(string.Empty, familyName)`, on
  the worker) or uninstalls one (`RemovePackageAsync(fullName)`, current user, no admin). `FindPackages()` for all
  users needs admin and is not used. WinRT is in the TFM already; no package added.
- Uninstall: confirmation (`Services.Dialogs`) → `ShellLaunch.StartAndWatch` of the planned command; the list
  refreshes when it exits (or at once when there's no process handle). Uninstallers that relaunch themselves from
  %TEMP% exit early; the list then still shows the app until *Refresh* (KI-089). Blocked in `--safe` (the button is
  disabled with the safe-mode note).
- Core: `InstalledAppRecord` (from raw values), `InstalledAppFilter` (ARP rules: no `DisplayName`,
  `SystemComponent=1`, `ParentKeyName`, `ReleaseType` update/hotfix, duplicate across views by name+version+publisher),
  `AppSizeText` (EstimatedSize KB), `InstallDateText` (`yyyyMMdd`, malformed → none), `PackagedAppRows.Build(entries)`
  (AUMID → family name, one row per family, desktop entries ignored), `AppListSearch` over `FuzzyMatcher`, and
  `UninstallPlan.For(record, systemDirectory)`:
  1. `NoRemove=1` → none.
  2. `WindowsInstaller=1` **and** the key name parses as a GUID → `<System32>\msiexec.exe /X {GUID}`.
  3. Otherwise `StartupRunner.TryParseCommand(UninstallString)` (an MSI key whose name isn't a GUID falls through
     to here). A bare `msiexec`/`msiexec.exe` or `rundll32`/`rundll32.exe` is rewritten to `<System32>\<name>.exe`.
  4. Any other executable must be a fully qualified path (`C:\…` or `\\server\share\…`) ending in `.exe`; a bare
     name, relative path, other file type or unexpanded `%VAR%` → none. (As built, WP3: `.exe` only, because an
     unquoted `C:\Program Files\App\uninst /S` splits at the first space and `C:\Program` could then resolve to a
     planted `C:\Program.exe`. On the development machine 63 of 64 listed apps get a plan; the other has no
     `UninstallString`.)
  5. None → the row's button becomes *Uninstall in Windows Settings* (`ms-settings:appsfeatures`).
  Tests for each filter rule and each plan case: MSI with GUID key, MSI with non-GUID key (falls through), bare
  msiexec and rundll32 rewritten, quoted/unquoted rooted paths with spaces, bare `setup.exe` → none, relative path →
  none, unexpanded `%ProgramFiles%` → none, empty/missing `UninstallString` → none, `NoRemove`, malformed values.
- **Startup apps**: reads HKCU Run, HKLM Run, HKLM WOW6432Node Run, the user and common Startup folders, and the
  matching `Explorer\StartupApproved\Run|Run32|StartupFolder` values in both hives into `StartupEntry` /
  `StartupApprovedSet` (existing Core). Note the name clash with `WinGnome.Core.Settings.StartupEntry`; alias it.
  - Toggle (**Disable**): HKCU entries only — writes `HKCU\...\StartupApproved\Run` or `\StartupFolder` with the
    12-byte value Windows uses: enabled = `02 00 00 00` + 8 zero bytes; disabled = `03 00 00 00` + the FILETIME
    (UTC, little-endian) of the change. **WP3 step 1** confirms on 25H2, by toggling an HKLM Run item in Task
    Manager as a standard user, where Windows stores its approval; expected HKLM (admin), so HKLM and common-folder
    items are read-only with a *Change in Task Manager* link (`taskmgr.exe /0 /startup`). If Windows stores it in
    HKCU, the result goes in KI-090 and they stay read-only in this spec. *Result (WP3, 2026-10-09):* the check
    couldn't be run as a standard user (the development account is an administrator with a split token, and
    creating a test HKLM Run item needs elevation, which WP3 doesn't use). Read-only evidence: an HKLM Run item
    disabled earlier through Task Manager has its `03 …` value under **HKLM** `StartupApproved\Run`, nothing under
    HKCU. Machine items stay read-only, approval read from HKLM only (KI-090).
  - Add: the existing app picker (`AppPickerWindow`; as built its heading still reads "Add app to dock", since
    `IDialogService.PickApp` takes no title and WP3 doesn't edit shared views, KI-090) → a `.lnk` in the user's Startup folder created with
    `IShellLinkW` from the picked AppsFolder item's ID list and saved with `IPersistFile.Save`, on `ShellThread`, so
    packaged apps work too.
  - Remove: user Startup-folder shortcuts only, after confirmation, to the Recycle Bin (`IFileOperation`,
    `FOFX_RECYCLEONDELETE`, on `ShellThread`); their `StartupApproved\StartupFolder` value is left (Windows ignores
    approvals without a file). HKCU Run values have no *Remove*.
  - Packaged startup tasks aren't listed (no documented way to read or toggle another package's `StartupTask`); a
    row links to `ms-settings:startupapps`.
  - Core: `StartupApprovedSet.Encode(StartupApproval, long fileTimeUtc)` (12 bytes; tests compare literal byte
    arrays captured from Windows for both states, and that `Parse(Encode(x))` round-trips),
    `StartupAppList.Build(entries, approvals, ownValueName)` → rows (name, command, scope, enabled, editable,
    removable: user folder only), own entry hidden, `StartupShortcutName.For(appName)` (invalid file-name characters,
    collisions get " (2)").

**4. Accessibility** — `Panels/Accessibility/`, `AccessibilityService`, `Interop/NativeMethods.Accessibility.cs`
(`STICKYKEYS`, `FILTERKEYS`, `HIGHCONTRASTW`, `SPI_GET/SETSTICKYKEYS`, `SPI_GET/SETFILTERKEYS`,
`SPI_GET/SETHIGHCONTRAST`, `SPI_SETCLIENTAREAANIMATION`, `SPI_GET/SETCARETWIDTH`, `SPI_SETCURSORS`; struct overloads
of `SystemParametersInfoW`).
- All documented SPI writes persist and notify through `SystemSettingWriter` (they broadcast, so never on the UI
  thread). Struct calls pass `uiParam = cbSize = sizeof(struct)` (`Marshal.SizeOf<FILTERKEYS>()` etc.).
- Read-modify-write keeps flags WinGnome doesn't show. Sticky keys change only `SKF_STICKYKEYSON` and keep every
  other `SKF_*` bit. Filter keys change only `FKF_FILTERKEYSON` and the timings, and keep `FKF_HOTKEYACTIVE`,
  `FKF_CONFIRMHOTKEY`, `FKF_AVAILABLE` and the sound/indicator bits. If the read returns flags without
  `SKF_AVAILABLE`/`FKF_AVAILABLE` (0 included), the plan uses Windows' defaults (`AVAILABLE | HOTKEYACTIVE |
  CONFIRMHOTKEY`) instead, so a bad read can never write a state the keyboard shortcut can't undo.
- *Slow* and *bounce keys*: one Win32 feature (`FILTERKEYS`). `iBounceMSec ≠ 0` requires `iWaitMSec`,
  `iDelayMSec` and `iRepeatMSec` all 0, so the two can't both be on (GNOME allows it). Turning one on turns the other
  off; the UI says so (KI-088).
- *Reduce animation* = `SPI_SETCLIENTAREAANIMATION` off. WinGnome has no animation setting of its own; the overview
  reads this value (`NativeMethods.AreClientAreaAnimationsEnabled`).
- *Cursor size*: `HKCU\Control Panel\Cursors\CursorBaseSize` (32..256 in steps of 16) and
  `HKCU\Software\Microsoft\Accessibility\CursorSize` (1..15), then `SPI_SETCURSORS` (undocumented values, isolated in
  `CursorSizeStore`, KI-087). The row is disabled with a link to `ms-settings:easeofaccess-mousepointer` when
  `Accessibility\CursorType` ≠ 0 (black, inverted or custom colour: Windows regenerates those cursor files through
  a private API) or a custom cursor scheme is active (`Control Panel\Cursors` `Scheme Source` = 1, a user scheme).
- *High contrast*: `SPI_SETHIGHCONTRAST` with `HCF_HIGHCONTRASTON` and the theme's scheme name; off clears the flag
  (Windows restores the previous theme). Windows shows its own "Please wait" for several seconds during the switch,
  and WinGnome's own `SystemParameters.HighContrast` (and WPF's system resources) flip mid-write. So the write runs on
  the writer thread, the row stays busy until the writer returns, and the panel re-reads only then (verified-set),
  ignoring intermediate change notifications. **Spike first** (WP4 step 1): on 25H2, check which
  `lpszDefaultScheme` value applies each Windows 11 theme cleanly — the display names (Aquatic, Desert, Dusk,
  Night sky) or the file names (`%WINDIR%\Resources\Ease of Access Themes\hc*.theme`) — and that WinGnome's
  windows survive the flip. If none works cleanly, the row becomes a link to `ms-settings:easeofaccess-highcontrast`
  and the spike result goes in KI-087.
- *Text size* (`TextScaleFactor`) evaluated: undocumented, and Windows applies it through a private broadcast; a raw
  write shows up only after sign-out in many apps. **Link row** showing the current percentage read-only. *Text
  cursor indicator* is run by Windows (no API): link row.
- OSK, Magnifier, Narrator: `ShellLaunch.SystemTool("osk.exe" | "magnify.exe" | "narrator.exe")`. Allowed in
  `--safe` (launching a tool changes no setting).
- Core: `StickyKeysFlags.With(flags, on)`, `FilterKeysPlan.For(current, slow, bounce)` (exclusive rule; bounce sets
  wait/delay/repeat to 0; restores Windows' defaults 1000 ms slow / 500 ms bounce when the previous value was 0;
  defaults for a read without `FKF_AVAILABLE`), `CursorSizeScale` (1..15 ↔ 32 + 16·(n−1) px, clamps both ways),
  `CursorSizeAvailability.For(cursorType, schemeSource)`, `CaretWidth.Clamp`, `ContrastThemes` (display name ↔
  file ↔ scheme). Tests for each, including flags preserved bit-for-bit and **a 0-flags read still yielding
  `FKF_HOTKEYACTIVE | FKF_CONFIRMHOTKEY | FKF_AVAILABLE` on write** (and the `SKF_*` equivalent).

**5. Region & Language** — `Panels/RegionLanguage/`, `RegionService`, `Interop/NativeMethods.Locale.cs`
(`GetUserDefaultLocaleName`, `GetLocaleInfoEx`, `SetLocaleInfoW`, `EnumDateFormatsExEx`, `EnumTimeFormatsEx`,
`GetDateFormatEx`, `GetTimeFormatEx`, `GetUserDefaultGeoName`, `SetUserGeoName`, `EnumSystemGeoNames`,
`GetUserPreferredUILanguages`).
- Writes are documented user overrides: `SetLocaleInfoW(LOCALE_USER_DEFAULT, LOCALE_SSHORTDATE | LOCALE_SLONGDATE |
  LOCALE_SSHORTTIME | LOCALE_STIMEFORMAT | LOCALE_IFIRSTDAYOFWEEK, …)` and `SetUserGeoName`, then
  `SystemBroadcast.SettingChanged("intl")` on the writer thread. *Reset to defaults* writes each value's
  `LOCALE_NOUSEROVERRIDE` value. Samples come from `GetDateFormatEx`/`GetTimeFormatEx` with the picture.
- The process caches culture data: on `WM_SETTINGCHANGE "intl"` the app calls
  `CultureInfo.CurrentCulture.ClearCachedData()`, which suffices (no other cache to drop, no restart), as one line
  in the app's settings-change handling (part of WP5).
- Core: `RegionFormatChoices.Build(patterns, current)` (dedupe, current first if not in the list), `FirstDayOfWeek`
  (Win32 0 = Monday … 6 = Sunday ↔ `DayOfWeek`), `GeoList` (sort and search by display name). Tests: mapping both
  ways, out-of-range values, custom current pattern kept.

**6. Privacy** — `Panels/Privacy/`, `ConsentStore` (the only class touching the keys).
- HKCU `Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{webcam,microphone,location}`:
  `Value` (REG_SZ `Allow`/`Deny`) = *Let apps access*; `NonPackaged\Value` = *Let desktop apps access*; packaged
  per-app `<PackageFamilyName>\Value`; desktop apps `NonPackaged\<path with # for \>` with `LastUsedTimeStart` and
  `LastUsedTimeStop` (FILETIME QWORDs). *In use* when `LastUsedTimeStart ≠ 0` and `LastUsedTimeStop == 0`; otherwise
  *Last used* from `LastUsedTimeStop`. Device-wide state read from the same path under HKLM, never written.
  Undocumented storage (KI-086); the capability access manager service (camsvc) watches these keys, so writes are
  expected to take effect live (AC 7). If a write needs sign-out the panel shows that note; if it has no effect at
  all, the panel becomes read-only with links.
- Core: `ConsentValue.Parse(string?)` (Allow/Deny/absent/unknown → absent counts as allowed, unknown as unknown and
  read-only), `ConsentAppList.Build(subkeys, names, now)` (packaged vs NonPackaged, `#` decoding, in-use rule,
  last-used text with injected now), `ConsentEffective.For(device, user, app)` (device off ⇒ every row shown off and
  disabled). Tests include start ≠ 0/stop 0 (in use), both set, both 0, stop without start.

**7. Removable Media** — `Panels/RemovableMedia/`, `AutoplayStore`.
- HKCU `Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers`: `DisableAutoplay` (DWORD) and, per
  event, `UserChosenExecuteHandlers\<Event>` plus `EventHandlersDefaultSelection\<Event>` (both written, as Windows
  Settings does). Available handlers from HKLM `...\AutoplayHandlers\EventHandlers\<Event>` (read). Handler names
  from `AutoplayHandlers\Handlers\<Handler>\Action` in **both HKCU and HKLM** (HKCU wins; per-user app handlers
  register there) via `IndirectString`. Special handlers `MSPromptEachTime`, `MSTakeNoAction`, `MSOpenFolder`. No
  broadcast: Explorer reads at insertion. In shell mode AutoPlay itself is gone (spec 0013).
- Core: `AutoplayModel.Build(events, handlersByEvent, names, chosen)` → rows with ordered choices (specials first),
  `AutoplayModel.WritesFor(event, handler)`, the event-name table (`StorageOnArrival` = Removable drive,
  `ShowPicturesOnArrival` = Memory card, `PlayDVDMovieOnArrival` = DVD movie, …; **unknown events hidden**). Tests
  for each, including a handler named only in HKCU and an HKCU name overriding HKLM.

**8. Windows Update** — `Panels/WindowsUpdate/`, `UpdateStatusService`, `Interop/WindowsUpdateApi.cs`
(`[ComImport]` `IAutomaticUpdates2`, `IAutomaticUpdatesResults`, `IUpdateSession`, `IUpdateSearcher`, `ISearchJob`,
`ISearchCompletedCallback`, `ISearchResult`, `IUpdateCollection`, `IUpdate`, `ISystemInformation` — only the members
used).
- Documented WUA: `Microsoft.Update.AutoUpdate` → `Results.LastSearchSuccessDate`, `LastInstallationSuccessDate`;
  `Microsoft.Update.SystemInfo.RebootRequired`; `Microsoft.Update.Session` → searcher with **`Online = false`**
  (cached results only: no network, no scan) and `"IsInstalled=0 and IsHidden=0"`.
- All WUA calls on one `LoadAsync(..., longRunning: true)` thread (MTA is fine: the WUA classes are
  `ThreadingModel=Both`); every object is created there and released with `Marshal.FinalReleaseComObject` there.
  The search is `BeginSearch` with a small callback object that sets an event, then `EndSearch`. If the event isn't
  set within 20 s, the worker calls `ISearchJob.RequestAbort()`, waits briefly for completion, releases everything and
  reports *Couldn't read updates* with the link. No abandoned blocking call is left on a thread. Closing the panel
  drops late results via the generation counter. Service stopped/disabled or `0x8024xxxx` → Core message.
- Core: `UpdateStatusText.Build(lastChecked, lastInstalled, pending, rebootRequired, error, now)` — relative times
  ("Today at 14:05", "3 days ago"), "Never", `DateTime.MinValue` handling, HRESULT → message table. Tests with a
  fixed `now` and culture.

### Threading, DPI and hostile cases
- UI thread only reads cheap registry/SPI values; anything that can block (EnumPrinters, WUA, uninstall registry
  walk, PackageManager, broadcasts) goes through `LoadAsync` (`longRunning` for the first three) or
  `SystemSettingWriter`. Shell COM (`IShellLinkW`, `IPersistFile`, `IFileOperation`, `ShellExecuteEx`) runs only on
  `ShellThread` (STA). No cross-process `SendMessage` without `SMTO_ABORTIFHUNG` and a timeout.
- These are list pages inside the Settings window: no screen geometry, so DPI and multi-monitor only matter for the
  existing window (checked at 125 %). App icons come from `IIconProvider` at the window's DPI.
- No HWNDs of other apps are touched. Uninstallers and tools may run elevated; WinGnome only waits on their process
  handle and never interacts with their windows.
- Explorer restart, sleep/resume and display changes don't affect these panels; each re-reads on open. A setting
  changed elsewhere while a panel is open shows on the next open or *Refresh* (no watchers).

### Work packages
| WP | Owns | Depends on | Parallel | Safety-critical |
|---|---|---|---|---|
| 0 Shared | `SettingsPanelCatalog.cs`, `SettingsPanel.cs`, `SidebarEntry.cs` and the fallback in `SettingsWindowViewModel.cs`, `SystemPanelViewModel.cs` (LoadAsync), `SystemPanelContext` + `SystemPanelServices`, `ShellThread.cs` (extracted from `AppLauncher.cs`), `ShellLaunch.cs`, `PanelRegistry.cs`, `PanelResources.xaml`, `IndirectString` + `SHLoadIndirectString` in `NativeMethods.Shell.cs`, catalogue tests | — | First, alone (small) | No |
| 1 Notifications | `Panels/Notifications/`, Core `Notification*` | WP0; 0017's DND service | Yes | No |
| 2 Printers | `Panels/Printers/`, `NativeMethods.Printing.cs`, Core `Printer*`, `PrintUiCommand` | WP0 | Yes | No |
| 3 Apps + Startup | `Panels/Apps/`, `NativeMethods.Apps.cs`, Core `InstalledApp*`, `UninstallPlan`, `PackagedAppRows`, `AppListSearch`, `Shell/StartupApp*`, `StartupShortcutName`, `StartupApprovedSet.Encode` (in `StartupRunner.cs`) | WP0 | Yes (largest; may split Startup into 3b) | **Yes** (uninstall, startup disable/remove) |
| 4 Accessibility | `Panels/Accessibility/`, `NativeMethods.Accessibility.cs`, Core `StickyKeys*`, `FilterKeysPlan`, `CursorSize*`, `CaretWidth`, `ContrastThemes` | WP0 | Yes (spike first) | **Yes** (filter/sticky keys can make the keyboard unusable; high contrast) |
| 5 Region | `Panels/RegionLanguage/`, `NativeMethods.Locale.cs`, Core `RegionFormatChoices`, `FirstDayOfWeek`, `GeoList`, the `ClearCachedData` line | WP0 | Yes | No |
| 6 Privacy | `Panels/Privacy/`, Core `Consent*` | WP0 | Yes | No |
| 7 Removable Media | `Panels/RemovableMedia/`, Core `AutoplayModel` | WP0 | Yes | No |
| 8 Windows Update | `Panels/WindowsUpdate/`, `WindowsUpdateApi.cs`, Core `UpdateStatusText` | WP0 | Yes | No |
| 9 Integration | README, PLAN.md (Core API, module map), KNOWN_ISSUES (KI-085..KI-092), this spec's status | WP1–8 merged | Last, alone | No |

Conflict rules: WP1–8 never edit `SettingsWindowViewModel.cs`, `SettingTemplates.xaml`, the catalogue, docs or an
existing `NativeMethods.*` file. Each adds exactly one line to `PanelRegistry.cs` and one to `PanelResources.xaml`
(append-only; a merge conflict there is resolved by keeping both lines) and hands WP9 its doc notes. Until a panel's
line lands, WP0's fallback keeps it a working link.

## Safety and recovery
- Like spec 0015, these panels change Windows settings on purpose and don't back them up; every write is per-user
  (HKCU, per-user SPI, `SetDefaultPrinter`, `SetLocaleInfo`, `SetUserGeoName`, current-user package removal).
  WinGnome writes no HKLM value and never elevates. HKLM is only read.
- Irreversible actions ask first: *Uninstall* and package removal (confirmation naming the app), startup *Remove*
  (user Startup-folder shortcut to the Recycle Bin, restorable from there). Startup items are otherwise only
  disabled, which Windows' own StartupApproved value records and Task Manager can undo; no Run value is deleted.
- Uninstall never runs a command that could be resolved through the search path: `msiexec`/`rundll32` are rooted at
  System32 and every other executable must be a fully qualified path, otherwise WinGnome hands over to Windows
  Settings.
- Accessibility: switching sticky/filter keys preserves Windows' own hotkey flags (Shift ×5, hold right Shift), and
  a read with no `AVAILABLE` bit falls back to Windows' defaults, so the user can always turn them off from the
  keyboard; *Bounce*/*Slow keys* timings are clamped to Windows' ranges so a mis-set value can't make typing
  impossible. High contrast off restores the previous theme (Windows does).
- Nothing persists in WinGnome that needs restoring on exit, crash or force-kill: these panels write no file of
  their own.
- `--safe`: every write is skipped by `SystemSettingWriter`; *Uninstall*, *Remove*, *Add* and *Set as default* are
  disabled; links and tool launches (OSK, Magnifier, Narrator, print queue) still work. `--selftest` doesn't open
  the Settings window; Core tests cover the decisions.
- KNOWN_ISSUES entries (added by WP9; each WP hands over its text):
  - KI-085 S4 Settings — Notifications use undocumented HKCU values (`NOC_GLOBAL_SETTING_TOASTS_ENABLED`,
    `ToastEnabled`, `NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK`, per-app `Enabled`); the app list is the registry
    subset of Windows' own list (`wpndatabase.db`), so registered apps without a key are missing; sign-out if live
    pick-up fails (AC 3).
  - KI-086 S4 Settings — Privacy switches write the undocumented `ConsentStore`; device-wide switches are read-only.
  - KI-087 S4 Settings — Accessibility: cursor size uses undocumented `CursorBaseSize`/`CursorSize` and is disabled
    for coloured or custom cursors; colour, text size and text cursor indicator stay in Windows Settings (By
    design); the high-contrast spike result.
  - KI-088 S4 Settings — Slow keys and bounce keys can't both be on (Windows' FILTERKEYS); WinGnome's own surfaces
    don't follow high contrast (By design / separate work).
  - KI-089 S4 Settings — Apps: packaged apps show no size, and publisher/version only when expanded (first expand
    or packaged uninstall loads the WinRT projection for the rest of the session); self-relaunching uninstallers
    leave the list stale until Refresh; desktop apps with a non-rooted uninstall command link to Windows Settings.
  - KI-090 S4 Settings — Startup apps: machine-wide items and packaged startup tasks are read-only/linked; HKCU Run
    values can be disabled but not removed (deferred); the HKLM-approval check result.
  - KI-091 S4 Settings — `LegacyDefaultPrinterMode` and AutoPlay handler keys are undocumented.
  - KI-092 S4 Settings — The format locale and display language are changed in Windows Settings (By design).
  - KI-063 extended to the new panels until each write is checked live.

## Footprint
- Nothing while the window is closed or the panel isn't showing; services are created in `Open` and released in
  `Close`. No timers, hooks, watchers or polling. The only waits are a one-shot `RegisteredWaitHandle` on a launched
  uninstaller (until it exits or the panel closes) and the 20 s WUA cut-off. `longRunning` threads end when their
  read returns.
- Apps: opening the panel loads no WinRT; packaged rows reuse the already-loaded app catalogue and icon cache.
  The WinRT projection (`Microsoft.Windows.SDK.NET.dll`, ~15–30 MB working set) loads only on the first packaged-row
  expand or packaged uninstall and then stays for the session (assemblies can't unload); other WinRT users (spec
  0015 phase 2, 0017) share it. The desktop uninstall-key walk (a few hundred keys) targets < 1 s on its thread;
  the list is virtualised.
- WUA COM objects live only during a read. Printers: one `EnumPrinters` per refresh.

## Acceptance criteria
1. Core tests: catalogue kinds, groups, order, every new keyword, and every native system panel having a `LinkUri`
   (WP0). Manual (WP0): before any new page exists, each of the eight panels opens its Windows Settings page from
   the sidebar, from search and from its top-bar row.
2. Core tests for every type named in Design (WP1–8), each seen failing once.
3. Notifications (manual): master, lock screen and one app's switch match Windows Settings after a change in either
   direction (both master values written); an app turned off stops showing test toasts **without sign-out** (if it
   needs sign-out, the row's note is shown and KI-085 says so); DND row follows the top bar.
4. Printers (manual): list matches `Get-Printer`; *Set as default* while Windows manages turns managing off and the
   default changes in Windows Settings; queue/properties/preferences windows open; a network printer that's offline
   doesn't freeze the window.
5. Apps (manual): desktop list matches Windows Settings → Installed apps within the filter rules; packaged apps
   appear without loading WinRT (no `Microsoft.Windows.SDK.NET` in the process's modules after opening Apps);
   search works; uninstalling a test MSI and a test EXE app (and removing a test MSIX for the current user) works
   from a standard account, with any UAC prompt coming from the uninstaller; an app with a bare or relative
   uninstall command shows the Windows Settings link; *Uninstall* is disabled in `--safe`.
6. Startup apps (manual): toggling an HKCU item updates Task Manager → Startup apps and the stored bytes match
   Windows' for both states; after sign-out the disabled item doesn't start; *Add* starts a desktop and a packaged
   app at next sign-in; *Remove* on a user Startup-folder shortcut puts it in the Recycle Bin; HKCU Run items have no
   *Remove*; HKLM items are read-only; WinGnome's own entry isn't listed.
7. Privacy (manual): turning *Let apps access your camera* off makes the Camera app report no access without
   sign-out (or the panel's note is accurate); per-app switch likewise; a desktop app using the camera shows *In
   use*; device-wide state matches Windows Settings.
8. Accessibility (manual): each switch matches Windows Settings both ways; Shift ×5 still toggles sticky keys after a
   change; slow and bounce are exclusive; cursor size changes the pointer at once, and the row is disabled with a
   black or custom cursor; *Reduce animation*: Windows reports the same value (Windows Settings → Animation effects
   matches after a change either way); high contrast per the spike result, with WinGnome still responsive after the
   switch; OSK, Magnifier and Narrator start from a standard account.
9. Region (manual): changing short date and first day of week updates the File Explorer date column and the
   calendar after the `intl` broadcast; *Reset* restores locale defaults; region change shows in Windows Settings.
10. Removable Media (manual): AutoPlay off suppresses the prompt on a USB stick; *Open folder* opens it.
11. Windows Update (manual): values match Windows Settings; with the network off the panel loads from cache within
    20 s; a search held past 20 s is aborted and the error shown; *Check for updates* opens Windows Settings and
    starts a check.
12. Every panel: `--safe` shows values and disables changes; light and dark; 125 %; leaving a panel mid-load doesn't
    throw or update a closed page (log is clean); every changed row shows the re-read value.
13. QA definition of done (AGENTS.md §4): build, tests, self-test, smoke test, Opus review of WP0, WP3 and WP4 at
    least.

## Risks and open questions
- Spec 0017's DND service must exist with its read/set/changed contract (`IsAvailable`, `ReadAsync`, `SetAsync`,
  `Changed`); until it lands, `Services` carries null and the row is hidden.
- Several stores are undocumented (Notifications, ConsentStore, cursor size, LegacyDefaultPrinterMode, AutoPlay);
  each is isolated in one class, verified-set shows what Windows really holds, and each falls back to the link.
- High contrast via SPI on Windows 11 contrast themes is unproven: the spike may turn it into a link.
- Advisor review notes where this spec adapts an item rather than taking it literally:
  - *Sidebar fallback tested in existing sidebar tests*: there are none, and `SidebarEntry` is app-layer
    (`tests/` has only `WinGnome.Core.Tests`). The rule is one boolean; Core tests the guarantee it relies on (every
    native panel has a `LinkUri`) and AC 1 checks the behaviour manually.
  - *Filter keys "0-flags read still yields hotkey bits"*: applied when the read lacks `FKF_AVAILABLE`; a valid read
    where the user turned the shortcut off is preserved bit-for-bit, so WinGnome doesn't silently re-enable it.
  - *Shell COM on `ShellThread`*: `ShellExecuteEx` for tools and uninstallers is also moved there (it can block on
    handler and elevation checks), consistent with `AppLauncher`.
- During the WP0→WP1–8 gap a top-bar row for a not-yet-built panel opens the settings window and the Windows
  Settings page (`DirectLinkFor` is already null); acceptable for the short gap and gone when the page lands.
- Standard-user WUA searches may be denied by policy on managed machines: shown as an error with the link.
