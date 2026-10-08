# 0004 — Idle footprint: tray front check, focused-app icon cache, battery polling

*Implemented* (branch `worktree-agent-ac6913d065faf4735`)

## Problem
Reviewers of the idle-memory work left three follow-ups:

1. The tray host checks four times a second that its window is still in front of Explorer's tray window,
   even when nothing is happening.
2. The focused-app icon in the top bar is reloaded on every focus change for windows whose icon is not
   cached by `IconProvider` (UWP frames, apps without a readable executable icon): a cross-process
   `WM_GETICON` plus a new bitmap each time.
3. The battery indicator polls `GetSystemPowerStatus` every 30 s on desktops that have no battery.

## Behaviour
Nothing visible changes. No settings are added.

## Non-goals
- Replacing the tray host's front check with a pure event model. No documented event says "a window moved
  in front of yours", so a slow safety-net check stays.
- Caching window icons for the dock or overview (they already go through `IconProvider`).

## Design
**Tray front check.** `WinGnome.Core.Tray.TrayFrontCheckSchedule` decides the check interval:
- 15 ms for 4 s after a TaskbarCreated broadcast (unchanged: apps re-register in a burst then);
- 250 ms (the old steady rate) for 2 s after *shell activity*: a foreground change, a top-level window created
  or destroyed, a rude/full-screen app, a monitor change. These are the moments Explorer raises its taskbar;
- 1 s otherwise (the safety net for raises no event announces).
It returns a new interval only when it changes, so the host calls `SetTimer` only then.

The host learns about shell activity on its own thread: `RegisterShellHookWindow` on the hidden host window
(shell hook messages are posted, so no app waits for us) and an out-of-context `EVENT_SYSTEM_FOREGROUND`
WinEvent hook created on the tray thread (also asynchronous). On activity it checks the front immediately and
asks the schedule for the faster rate. Nothing new touches the dispatcher. Hooks are removed before the window is
destroyed, on the tray thread. AppBar messages that register, move or reconfigure an AppBar count as activity;
read-only queries (ABM_QUERYPOS, ABM_GETSTATE, ABM_GETTASKBARPOS, ABM_GETAUTOHIDEBAR(EX)) do not, since some apps
poll them. `ShellTrayData.ParseAppBarMessage` reads dwMessage right after the APPBARDATA block (whose cbSize is its
own size); an unreadable block counts as activity. Pruning dead icons stays at 5 s.

**Focused-app icon.** Executable icons were already cached by `IconProvider` per (path, size); the uncached case
was the window's own icon, used for UWP frames whose app process was not found (suspended or minimised apps) and
for apps without an exe icon. A window's own icon is per window and may be transient, so it is not cached. Instead,
`AppIdentity.HostedAppIconId(aumid, path)` names the AppsFolder id of an ApplicationFrameHost window with an
AppUserModelID, and `FocusedAppViewModel` uses `IconProvider.GetAppIcon(aumid)` for it (stable, cached per app),
falling back to the window icon. `IconProvider`'s cache becomes a 512-entry `LruCache`
(`WinGnome.Core.Collections`) instead of being cleared wholesale when full.

**Battery.** `BatteryStatus.NeedsPolling(batteryFlag)` is false only when Windows reports no system battery
(flag 128); an unknown state (255) keeps polling. `BatteryMonitor` stops its timer
then and keeps listening to `PowerModeChanged` (power-status change broadcasts, resume): a battery that appears
(or a status read that comes back with one) starts the timer again.

## Safety and recovery
No system state changes. The tray host still forwards every message synchronously and never waits on the
dispatcher; the new hooks are asynchronous and owned by the tray thread. `--safe` and `--selftest` behave as
before.

## Footprint
Tray thread wake-ups at rest drop from 4/s to 1/s. No battery timer on desktops. No cross-process WM_GETICON or new
bitmap on focus changes to suspended UWP apps.

## Acceptance criteria
1. Schedule: 15 ms after a broadcast for 4 s, 250 ms for 2 s after activity, 1 s at rest; no reschedule when the
   interval is unchanged; activity during the broadcast burst keeps 15 ms. (Unit tests.)
2. Only the listed shell hook codes, and AppBar messages other than read-only queries, count as activity; the
   AppBar message is parsed from the block, malformed blocks give null. (Unit tests.)
3. LRU cache evicts the least recently used entry, refreshes on read, rejects capacity < 1. (Unit tests.)
4. `HostedAppIconId` returns the AUMID only for ApplicationFrameHost windows that have one. (Unit tests.)
5. `NeedsPolling` is false only with the no-battery flag, true for unknown. (Unit tests.)
6. Build clean, tests green, self-test exits 0, idle footprint measured before and after.

## Risks and open questions
- A raise of Explorer's tray that no event announces is now caught within 1 s instead of 250 ms; a
  Shell_NotifyIcon call in that window reaches Explorer only (the same failure mode as before, longer window).
