# 0010 — Top bar and dock on every monitor

Status: Draft (plans KI-001; needs decisions below before it's agreed)

## Problem
On multi-monitor setups the dock and top bar exist only on the primary monitor, and `TaskbarController` hides the
secondary taskbars (`Shell_SecondaryTrayWnd`) too, so secondary monitors have neither (KI-001).

## Behaviour
- Every monitor gets a top bar. The primary's has everything; secondary bars show Activities, the clock and the
  focused app on that monitor (tray, quick settings and the calendar popup stay on the primary).
- New setting `Dock.Monitors` (`Primary` | `All`, default `Primary`). With `All`, each monitor's dock shows pinned
  apps plus running windows; new setting `Dock.OnlyThisMonitorsWindows` (bool, default false).
- Monitors added, removed, re-arranged, or a DPI change rebuild the affected instances within one second.
- Hot corner and overview stay on the primary in this spec.

## Non-goals
- Per-monitor workspaces, per-monitor overview.

## Design
- Core `Monitors/MonitorLayout` (monitors, work areas, DPI, primary) and `MonitorDiff` (added/removed/changed),
  with tests for negative coordinates, mixed DPI, primary swap and hot-plug. Window-to-monitor assignment by
  nearest monitor, tested in Core.
- App: `TopBarFeature` and `DockFeature` hold one instance per monitor key, rebuilt on `WM_DISPLAYCHANGE` and
  `HSHELL_MONITORCHANGED`. Each top bar is its own AppBar. Secondary docks are created lazily.

## Safety and recovery
AppBars are unregistered when a monitor goes away and on exit/crash. The taskbar marker already records every
taskbar window it hides; the work-area restore must cover N monitors and is tested by unplugging a monitor while
running and after `taskkill /f`.

## Footprint
One extra top-bar surface per secondary monitor (~3–5 MB each); docks only when enabled for all monitors.

## Acceptance criteria
1. Core tests for layout diffing and window assignment.
2. Manual on two monitors with different DPI: bars on both, popups on the right monitor, unplug/replug, exit and
   `taskkill /f` restore every taskbar and work area.

## Risks and open questions
- Interim option: a setting to keep the Windows taskbar on secondary monitors. Catch: `ABS_AUTOHIDE` is global.
- Should the hot corner work on every monitor?
- Large change touching the two most safety-critical features; schedule it on its own.
