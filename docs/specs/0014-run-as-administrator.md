# 0014 — Run as administrator, and launching without Explorer

Status: Implemented

## Problem
The user is a developer and needs to start tools elevated from the dock and overview. WinGnome itself must stay
unelevated: it receives messages from every app (tray, AppBar), and an elevated shell is a privilege-escalation
target (see spec 0013). Separately, `AppLauncher.StartViaAppsFolder` launches apps through `explorer.exe
shell:AppsFolder\…`, which ties launching to Explorer and may start Explorer as a shell in shell mode.

## Behaviour
- Dock icon context menu: **Run as administrator** for apps that can be elevated, matching Start: desktop apps,
  `.exe`/`.lnk`/`.bat`/`.cmd`/`.msc` files, and full-trust packaged apps (Windows Terminal, the new Notepad,
  PowerShell 7 from the Store). Hidden for UWP apps (Calculator, Store), which can't be elevated.
- The dock's launch animation plays only once the app has started: for an elevated launch, after UAC is accepted.
- Overview app grid and search results: **Ctrl+Shift+Enter** or **Ctrl+Shift+click** launches elevated (as in Start),
  plus the same menu item on right-click if the grid has a context menu.
- Per pinned app: **Always run as administrator** (checkbox in the dock menu). Persisted on the pin as
  `PinnedApp.RunAsAdministrator` (bool, default false); old settings files load with it false.
- Every elevated launch shows the normal UAC prompt; cancelling it is silent (no error toast), logged at Info.
- Launching no longer goes through `explorer.exe`: AppsFolder items are launched in-process with `ShellExecuteEx`
  on the item's ID list (and `IApplicationActivationManager` for packaged apps, as today).

## Non-goals
- Running WinGnome elevated; a uiAccess build (would fix KI-004, needs signing and a Program Files install —
  possible later spec); an elevated helper service.

## Design
- Core: `LaunchRequest(launchId, arguments, elevate)` and `LaunchPlanner.Plan(launchId, modifiers, pin, host)`
  deciding normal vs elevated and whether elevation is offered, with tests. `Classify` sorts launch ids into URI,
  file, desktop app, UWP packaged app and full-trust packaged app; only the last needs `AppHost`.
- Full trust is read in `AppCatalog` from each AppsFolder item's `PKEY_AppUserModel_HostEnvironment`
  (`IShellItem2::GetUInt32`): 0 = Win32, 1 = UWP, 2 = full-trust packaged (verified: Terminal, Notepad and Photos
  are 2 with manifest entry point `Windows.FullTrustApplication`; Calculator and Store are 1). Missing or unknown
  values count as UWP, so elevation is never offered for an app we can't classify. No `PackageManager` or manifest
  reads are needed.
- App: `AppLauncher.Launch(LaunchRequest, started)`: AppsFolder items resolved to an ID list
  (`SHParseDisplayName("shell:AppsFolder\\<id>")`) and launched with `ShellExecuteEx` + `SEE_MASK_INVOKEIDLIST`.
  Elevated launches use verb `runas` (on the file, or on the ID list for desktop and full-trust packaged apps) on
  their own STA thread, since the call blocks until UAC is answered; `started` is posted back to the UI thread
  only on success. `ERROR_CANCELLED` (1223) = user said no. Normal packaged launches keep
  `IApplicationActivationManager`.
- Dock menu (`DockMenuBuilder`) and overview input handling.

## Safety and recovery
No system state changes. WinGnome never holds an elevated token.

## Footprint
None.

## Acceptance criteria
1. Core tests: planner truth table (modifiers × pin flag × desktop / UWP / full-trust packaged); host mapping; old
   settings without the field load false.
2. Manual: Run as administrator from the dock starts e.g. Windows Terminal elevated after UAC; cancelling UAC is
   silent; Always run as administrator persists and applies on click; Ctrl+Shift+Enter in search elevates.
3. Normal launches of desktop and packaged apps still work, and no `explorer.exe` process is started for them.
