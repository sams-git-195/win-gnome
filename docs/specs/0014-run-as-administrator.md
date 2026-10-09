# 0014 — Run as administrator, and launching without Explorer

Status: Implemented

## Problem
The user is a developer and needs to start tools elevated from the dock and overview. WinGnome itself must stay
unelevated: it receives messages from every app (tray, AppBar), and an elevated shell is a privilege-escalation
target (see spec 0013). Separately, `AppLauncher.StartViaAppsFolder` launches apps through `explorer.exe
shell:AppsFolder\…`, which ties launching to Explorer and may start Explorer as a shell in shell mode.

## Behaviour
- Dock icon context menu: **Run as administrator** (for apps that can be elevated: desktop apps; hidden for packaged
  Store apps, which can't be elevated this way).
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
- Core: `LaunchRequest(launchId, arguments, elevate)` and `LaunchPlanner.Plan(app, modifiers, pin)` deciding normal
  vs elevated and whether elevation is offered (packaged → never), with tests.
- App: `AppLauncher.Launch(LaunchRequest)`: `ShellExecuteEx` with verb `runas` when elevated; AppsFolder items
  resolved to an ID list (`SHParseDisplayName("shell:AppsFolder\\<id>")`) and launched with `SEE_MASK_IDLIST`
  (verb `runas` for desktop apps). `ERROR_CANCELLED` (1223) = user said no.
- Dock menu (`DockMenuBuilder`) and overview input handling.

## Safety and recovery
No system state changes. WinGnome never holds an elevated token.

## Footprint
None.

## Acceptance criteria
1. Core tests: planner truth table (modifiers × pin flag × packaged); old settings without the field load false.
2. Manual: Run as administrator from the dock starts e.g. Windows Terminal elevated after UAC; cancelling UAC is
   silent; Always run as administrator persists and applies on click; Ctrl+Shift+Enter in search elevates.
3. Normal launches of desktop and packaged apps still work, and no `explorer.exe` process is started for them.
