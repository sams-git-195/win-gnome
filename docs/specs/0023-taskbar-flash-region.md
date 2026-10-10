# 0023 — Hide taskbar flashes with an empty window region

Status: Implemented (branch `feature/0023-taskbar-region`; live acceptance pending, see Acceptance criteria).

## Problem

In WinGnome dock mode (`General.HideWindowsTaskbar`) the Windows taskbar is set to auto-hide and its windows are
hidden with `ShowWindow`. Explorer re-shows or slides them in on its own (sign-in, work-area changes, tray churn).
WinGnome re-hides after 50 ms, ramping to 750 ms and 3 s in bursts (`TaskbarRehidePolicy`), but the user sees each
episode as a flash, mostly on the secondary monitor's taskbar (`Shell_SecondaryTrayWnd`) and also on the primary
(`Shell_TrayWnd`).

Spike, live on Windows 11 build 26200:

- `SetWindowRgn(explorerTaskbarHwnd, CreateRectRgn(0,0,0,0), true)` from another process works (returns 1;
  `GetWindowRgn` then reports NULLREGION = 1).
- Primary `Shell_TrayWnd`: the empty region persisted for 90 s through Explorer re-shows and work-area episodes.
- Secondary `Shell_SecondaryTrayWnd`: Explorer rewrites its region constantly during bursts (`GetWindowRgn` type
  flipping ERROR(0) and SIMPLE(2) every 0.5 to 2 s), so the empty region has to be re-applied whenever Explorer
  changes it.
- `SetWindowRgn(hwnd, NULL, true)` restores (type back to 0, no region).

## Behaviour

New setting `General.HideTaskbarFlashes` (bool, default **true**; additive, old settings files load with it on).
Settings, General, Taskbar group, row "Hide taskbar flashes": "Keeps the hidden Windows taskbar from flashing into
view." Shown in dock mode only.

When it is on, the taskbar mode is Hidden (dock mode) and the app is not in `--safe`, every Explorer-owned taskbar
window gets an empty window region. Whenever a taskbar window's region is not empty any more it is emptied again.
The existing hide and re-hide ramp stay as the backup.

- Peek ("show system tray"): the regions are removed for the peek and put back when it ends.
- Mode switch away from Hidden, setting turned off, feature stop, crash, `--restore-taskbar`, "Restore taskbar" in
  Settings and the start-up marker recovery all remove the regions again.
- Log: Info "Taskbar regions emptied on N window(s)" on start, "Taskbar regions removed from N window(s); re-applied M
  time(s) while active" on stop, and a throttled line for each re-application.

## Non-goals

- No change to auto-hide, the work-area handling or the re-hide ramp.
- Native taskbar mode and AutoHide mode never get a region.
- No attempt to win a race with Explorer on the secondary taskbar beyond re-applying on events (see KI-112).

## Design

Core (`WinGnome.Core/Shell`, tested):

- `TaskbarRegionPolicy`: `ShouldManage(safe, hidden, setting)`; `NeedsEmptying(regionType)` (anything but
  NULLREGION: the guard against re-apply loops, since our own `SetWindowRgn` can raise window events);
  `RegionsToClear(recorded, present)` (only recorded windows that are still live Explorer taskbar windows and whose
  region is currently empty; a SIMPLE region is Explorer's and is never cleared).
- `TaskbarMarker`: parse and serialize of `taskbar.state` (`WasAutoHide` plus `EmptiedRegions`, the HWNDs we
  emptied). A marker from before this spec has only `WasAutoHide` and parses with no regions.

App:

- `Interop/NativeMethods.TaskbarRegion.cs`: `CreateRectRgn`, `GetWindowRgn` (`SetWindowRgn` and `DeleteObject` exist).
- `Services/TaskbarRegions`: Win32 wrapper (region type, empty, remove-if-empty, remove recorded). After a successful
  `SetWindowRgn` the system owns the region; it is deleted here only when the call fails. Failures are logged with
  the HWND and error, never fatal.
- `Services/TaskbarController`: `IsExplorerTaskbarWindow` (class match **and** same process as `GetShellWindow()`, so
  the tray host's own `Shell_TrayWnd` is never touched), marker record/clear helpers, and region removal inside
  `RestoreFromMarker`.
- `Features/Taskbar/TaskbarRegionGuard`: holds the active/suspended state and the set of known windows.
- `TaskbarFeature`: creates the guard, starts and stops it from `ApplySettings`/`SwitchTo`, forwards the raw window
  events, suspends it around a peek and re-applies after the re-hide timer.

Re-apply triggers (no timer, no polling, no new hook): `WindowTracker.RawWindowEvent` already forwards
`EVENT_OBJECT_CREATE`, `SHOW`, `DESTROY` and `EVENT_OBJECT_LOCATIONCHANGE` from Explorer (the hook skips only our own
process). On a location change the guard does one set lookup and acts only for taskbar windows it knows; a show or
create of an Explorer taskbar window (a new monitor's taskbar) adds it. The window's `GetWindowRgn` type is read
first and nothing is applied when it is already NULLREGION. Backups: the same `Apply()` runs after the re-hide timer
fires (every Explorer re-show) and after a TaskbarCreated re-hide, and once at start and at every switch to Hidden.

Threading: UI thread only, except `RemoveAll` on the crash path, which is plain Win32 and file I/O. `SetWindowRgn`
on Explorer's windows is a cross-process call like the existing `ShowWindow` hide.

## Safety and recovery

State changed: the window region of Explorer's taskbar windows. Before a window is emptied, its HWND is written to
`taskbar.state` (`EmptiedRegions`); if the marker cannot be written the region is not applied. The marker already
exists whenever Hidden mode is in effect, so no new file.

| Event | Result |
|---|---|
| Mode switch, setting off, feature stop, Dispose | `Stop()` removes the regions (known windows plus recorded) and clears the records |
| Restore from Settings, `--restore-taskbar`, start-up recovery | `RestoreFromMarker` removes recorded empty regions first |
| Crash handler | `EmergencyRestore` calls `RemoveAll()`, then `RestoreFromMarker` |
| Force-kill | Next start (or `--restore-taskbar`) reads the records |
| Explorer restart | Old handles are gone; stale records are ignored (not live, or not empty) and cleared by the next restore |
| `--safe`, `--selftest` | `ShouldManage` is false: nothing is touched |

Only windows whose current region is NULLREGION are cleared, and only if they are live Explorer taskbar windows, so a
reused HWND or a region Explorer set is never changed.

## Footprint

No timer, hook or window added. Per raw window event, one `HashSet` lookup; per taskbar show, a few `GetWindowRgn`
calls. A burst of Explorer region rewrites costs one `GetWindowRgn` and at most one `SetWindowRgn` per event.

## Acceptance criteria

1. With dock mode and the setting on, `GetWindowRgn` on every Explorer taskbar window reports NULLREGION; the
   tray host's `Shell_TrayWnd` is untouched. (Live)
2. After Explorer rewrites a secondary taskbar's region, it is NULLREGION again within a second. (Live)
3. Peek shows the taskbar; when it ends the regions are empty again. (Live)
4. Switching to native mode, turning the setting off and quitting leave every taskbar window with no region. (Live)
5. After `taskkill /f` and `--restore-taskbar` (or the next start), no taskbar window keeps an empty region. (Live)
6. `--safe` and `--selftest` never touch a region. (Self-test)
7. An old `taskbar.state` (`{"WasAutoHide":true}`) still parses; recorded handles round-trip. (Tests:
   `TaskbarMarkerTests`)
8. Only an empty region on a recorded, live window is ever cleared; the setting defaults to on for old settings
   files. (Tests: `TaskbarRegionPolicyTests`, `SettingsSerializerTests`)

## Risks and open questions

- The secondary taskbar's region is a race with Explorer; a flash can still get through (KI-112).
- `SetWindowRgn` on another process's window is supported API, but Explorer's own use of window regions is
  undocumented and may change.
- A hung Explorer could block the UI thread inside `SetWindowRgn`, as it already can in `ShowWindow`.
