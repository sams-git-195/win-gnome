# 0010 — Top bar and dock on every monitor

Status: Implemented (merged in e85e68e, 2026-10-09). User decisions 2026-10-09; advisor review (Fable) applied
2026-10-09.

Plans KI-001. Owns KI-069 to KI-072. Safety-critical (AppBars, work areas, taskbar): implemented alone, before any
other spec, and reviewed by Opus before merge.

*As built / as verified (2026-10-09, on the two-monitor machine described in the QA steps):* the T criteria 1–7 and
B1–B5 are covered by the Core suite (3157 tests green at the merge). Q9–Q13, Q15's exit and force-kill paths and
Q16's `--selftest --safe` check were exercised in the live sessions on the branch and on merged main: the everyday
build (e85e68e) reserved both strips ~4.3 s after start through the outcome-B fallback — one "work area set
directly" line per monitor, marker correct, the forced pass found no further action (B6; details and log lines in
KI-099) — and `--selftest --safe` exits 0 and writes no marker. A solo non-safe test verified the shrink after an
external reset, marker discipline, graceful quit, force-kill + `--restore-taskbar` recovery and the corrupt-marker
repair's taskbar-visible branch (B9 in part).
**Still to verify, and needs the user at the machine** (runbook:
`C:\Users\samhe\AppData\Local\Temp\opencode\mm-session\RUNBOOK.md`): Q14/B7 unplug and replug; Q14/B11 scale change,
primary swap, sleep/resume; B12 native taskbar mode; the two-records-on-one-monitor unwind (Core-tested only); the
budget refusal (never triggered); the corrupt-marker repair's taskbar-hidden branch; Q15's crash-path variant
(`EmergencyRestore`, never exercised — only `taskkill /f`, which by design runs nothing); Q16's leaked-AppBar exit-1
check; and the TbExp hide-order experiment below.

## Problem
On multi-monitor setups the top bar and dock exist only on the primary monitor, and `TaskbarController` hides the
secondary taskbars (`Shell_SecondaryTrayWnd`) too, so secondary monitors have no clock, tray, launcher or window list
at all (KI-001, S3). Every layout decision today hard-codes the primary monitor (`MONITOR_DEFAULTTOPRIMARY` in
`TopBarFeature.Dock`, `DockFeature.Relayout`, `HotCornerWatcher.Poll`, `OverviewWindow.PlaceOnPrimaryMonitor`).

## Behaviour
User decisions of 2026-10-09, in substance:

- **Top bar on every monitor, each a full copy:** Activities, workspace dots, the focused app *of that monitor*, clock
  with the calendar popup, tray icons, system indicators with quick settings, and the logo menu. A popup opens on the
  monitor whose bar was clicked, under the item that was clicked. Only one popup is open at a time across all bars
  (opening one on another bar closes the first, as switching popups on one bar does today).
- **Focused app of a monitor:** the bar on the monitor that holds the foreground window shows the foreground app,
  exactly as today. Every other bar shows the app of the most recently focused task-switcher window that is still on
  its monitor and not minimised; none → empty, as on the desktop. (Decided; this is the default and only behaviour.)
- **Tray icons** appear in every bar, in the same order, with the same state. Hover, click and right-click on any
  bar reach the app exactly as on the primary bar today; menus and flyouts that apps place from the anchor WinGnome
  passes (version 4 apps) or from `Shell_NotifyIconGetRect` open next to the icon that was clicked, on that monitor.
- **Dock:** main (primary) monitor only by default. New setting to show it on all monitors, and, when on all, an
  option to show only the windows on each dock's own monitor. Pinned apps always appear on every dock.
  Super+1..9 acts on the primary monitor's dock.
- **Hot corner:** on every monitor whose top-left corner is a true screen corner: no other monitor lies directly to
  its left or directly above it at that corner (GNOME's rule; a monitor touching only diagonally doesn't disable it).
  The dwell delay (`Activities.HotCornerDelayMs`) and full-screen/drag suppression are unchanged.
  **The primary always keeps its hot corner** (user decision 2026-10-09, after the first build disabled it on a
  layout with a monitor above the primary). Where the primary's corner is not a true corner the pointer does not
  stop there, so it is a *guarded* corner: it fires only after the pointer rests inside an 8×8 physical-pixel box at
  the primary's top-left for the configured delay, but at least 300 ms. A sample on another monitor (passing through
  to the monitor above or to the left) restarts the dwell, so moving between monitors never triggers it.
- **Overview** stays on the primary monitor in this spec (not trivial: `OverviewWindow`, `OverviewLayout` input,
  `ThumbnailLayer` and the backdrop all assume one monitor). Activities buttons and hot corners on secondary
  monitors open it on the primary (KI-072).
- **Taskbar:** unchanged. `TaskbarController` already hides every `Shell_SecondaryTrayWnd`, and auto-hide is global,
  so restoring it restores every monitor. WinGnome never changes "Show my taskbar on all displays".
- **Display changes** (monitor added, removed, rearranged, resolution, DPI/scale, primary swapped, resume from
  sleep, unlock after fast user switching): affected bars and docks are rebuilt or moved within 1 s; a bar whose
  monitor goes away or moves is unregistered as an AppBar before anything else happens.

New settings (old files load unchanged: missing fields take these defaults; `SchemaVersion` stays 1):

| Setting | Type | Default | Settings page |
|---|---|---|---|
| `TopBar.Monitors` | enum `BarMonitors { All, Primary }` | `All` | Top Bar → "Show on": *All displays* / *Main display only* |
| `Dock.Monitors` | enum `BarMonitors` | `Primary` | Dock → "Show on": *Main display* / *All displays* |
| `Dock.IsolateMonitors` | bool | `false` | Dock → "Only show windows on the same display" (enabled only when *All displays*) |

`TopBar.Monitors` is kept as the escape hatch (advisor review): without it every existing multi-monitor user would
lose a strip on each secondary monitor with no way back short of disabling the bar. Hot corner text on Activities and
the Multitasking panel changes to "top-left corner of each display"; no new setting.

## Non-goals
- Overview, app grid or search on a secondary monitor; per-monitor workspaces (virtual desktops are global).
- Per-monitor settings (different bar or dock per monitor); a dock on a monitor's inner edge with pressure barriers,
  or any Settings hint about inner edges (logged only, KI-071).
- Moving the tray host window or answering `ABM_GETTASKBARPOS` per monitor (Explorer answers it; KI-069).
- Changing Explorer's multi-monitor taskbar settings (`MMTaskbarEnabled`, `MMTaskbarMode`).
- Brightness of external monitors from any bar (unchanged, KI-021 / spec 0011).
- Merging with the Settings display code: `Features/Settings/Panels/Displays/DisplayService` (CCD read/write of
  modes) and `Panels/About/SystemInfoReader` already exist and stay separate. The join key between them and this
  spec's monitors, if ever needed, is the GDI device name (`MONITORINFOEXW.szDevice` = `DisplayInfo.DeviceName`).

## Design
### Core (`src/WinGnome.Core/Monitors/`, new namespace, all tested)
- `MonitorInfo(string Key, PixelRect Bounds, PixelRect WorkArea, int Dpi, bool IsPrimary)`. `Key` is the GDI device
  name (`\\.\DISPLAY3`, from `MONITORINFOEXW.szDevice`); it is used only to match instances across a re-read, never
  persisted.
- `MonitorLayout`: immutable list plus `Primary`, `Find(key)`, `At(x, y)`. `At` is half-open like `MonitorFromPoint`:
  a point is on a monitor when `Left <= x < Right` and `Top <= y < Bottom`, so the shared edge of two side-by-side
  monitors belongs to the right/lower one. Factory normalises input: drops empty rectangles, de-duplicates identical
  keys and identical bounds (clone mode reports one monitor; a glitchy read can report two), and falls back to "the
  monitor containing (0,0)" if no monitor is flagged primary.
- `MonitorLayoutDiff.Compute(old, new)` → `Removed`, `Changed` (with flags: `Bounds`, `Dpi`, `Primary`), `Added`.
- `SurfacePlan.Compute(layout, BarMonitors mode)` → the set of monitor keys that should have a surface, and
  `SurfacePlan.Reconcile(currentKeys, desiredLayout)` → ordered steps: **all removals first, then moves/re-docks,
  then additions**. This ordering means Explorer never sees two WinGnome bars on one monitor's top edge (which would
  stack two strips), including during a primary swap. Invariant tested: at most one bar and one dock per monitor key
  and per bounds. A surface whose AppBar was detached by the guard (below) and whose key still exists is a re-dock
  step; a detached surface whose key is gone is a removal.
- `HotCornerRules.IsTrueCorner(MonitorInfo, MonitorLayout)`: false when another monitor's rectangle covers
  `(Left-1, Top)` or `(Left, Top-1)` (half-open containment, as `At`); diagonal-only neighbours keep the corner.
  `HotCornerDetector` is unchanged (it already takes the monitor rect per sample).
- `MonitorFocusTracker`: per monitor key, an MRU list of task-switcher window handles. Inputs:
  `OnForeground(hwnd, key)`, `OnWindowsChanged(IReadOnlyDictionary<nint, (string Key, bool Minimised)>)` (prunes
  closed handles, which also bounds HWND reuse to "a new window with a recycled handle starts fresh"),
  `OnLayoutChanged(MonitorLayout)`. Output: `Current(key) → nint`. Pure; the app supplies monitor keys.
- `DockWindowFilter.ForMonitor(windows, key, isolate)`: with `isolate` false returns all; true keeps windows whose
  key matches. `DockModelBuilder.Build` is unchanged; it gets the filtered running list.
- `TrayAnchor.For(iconBounds, barBounds)`: the version 4 anchor point. Today `TrayViewModel.Deliver` uses
  `Math.Max(bounds.Bottom, _barBounds.Bottom)` with the *primary* bar, which is wrong for a bar on a monitor placed
  above the primary (negative Y); the anchor must use the clicked bar's strip.
- No new edge enum: Core code that needs an edge uses `WinGnome.Core.Shell.AppBarEdge`, the app keeps
  `WinGnome.Interop.AppBarEdge` for `AppBar`, and the dock keeps mapping `DockPosition` to it (`DockFeature.ToAppBarEdge`).
- Settings: `BarMonitors` enum in `AppSettings.cs`, the three properties, `Normalize()` resets undefined enum values
  (`EnumSetting`), and `IsolateMonitors` is ignored when `Dock.Monitors == Primary` (not rewritten).
- Only if the spike lands on outcome B (see Safety): `WorkAreaAudit` for the recorded original work areas. Not
  written under outcome A.

### App
**`Interop/AppBar.cs` hardening** (shared file, one owner during this spec; lands first, as its own commit — see
Work packages). Three changes:

1. *Idempotent reposition (no `ABN_POSCHANGED` storm).* Today every `ABN_POSCHANGED` runs `Reposition()`, which always
   sends `ABM_QUERYPOS` + `ABM_SETPOS` + `SetWindowPos` + `ABM_WINDOWPOSCHANGED`. After a `SETPOS` Explorer notifies
   every other AppBar on that edge, so with N bars on the same edge (two top bars during a rebuild, or our bar plus a
   third-party top bar) the bars can ping-pong indefinitely. New rule:
   - `Dock()` (explicit: first registration, re-dock after a display change or Explorer restart) keeps the full
     sequence.
   - `ABN_POSCHANGED` sends `ABM_QUERYPOS` only, re-applies our thickness to the answer and compares it with `Bounds`.
     Only if it differs does it send `ABM_SETPOS`, move the window, send `ABM_WINDOWPOSCHANGED` and raise `Moved`.
   - A re-entrancy flag: `SHAppBarMessage` is a synchronous cross-thread `SendMessage` (through `TrayHost`'s
     forwarding to Explorer), so a notification can be dispatched to our window while we are inside one of our own
     calls. A notification that arrives while the flag is set only marks "recheck pending"; one QUERYPOS-only
     recheck runs (posted at `Background` priority) after the outer call returns.
   - A static `Interlocked` counter of `SHAppBarMessage` calls, read by the coordinators for the per-reconcile log
     line (item 6, below).
2. *Detach guard, not a monitor-lost verdict.* On `ABN_POSCHANGED`, before anything else, the bar checks that its cached
   monitor rectangle still exactly matches a live monitor: `MonitorFromRect(_monitor, MONITOR_DEFAULTTONULL)` is
   non-null **and** that monitor's `GetMonitorInfo` bounds equal `_monitor`. The exact-bounds test matters because a
   stale rectangle can overlap a different monitor after a rearrangement. If the check fails it only calls `Undock()`
   and raises `Detached`; it does not decide whether the monitor is gone or just moved/resized. The coordinator calls
   `DisplayLayoutService.Invalidate()`, and its reconcile pass (at most 250 ms later, on a real layout diff) re-docks
   the instance (key still present) or destroys it (key gone). When a monitor is removed, Windows *may* move the bar's
   window to a remaining monitor and Explorer *may* send `ABN_POSCHANGED`; without the guard, re-docking there would
   reserve a second strip on that monitor.
3. *Static registry for the crash path.* `Undock()` stays idempotent and plain Win32. A static, lock-free registry
   (`ImmutableArray<nint>` swapped with `Interlocked.CompareExchange`) holds every window that has a live `ABM_NEW`:
   added after `ABM_NEW` succeeds, removed after `ABM_REMOVE`. `AppBar.UndockAll()` sends `ABM_REMOVE` for each entry
   from any thread without touching feature state. It covers every bar instance and every `DockReservation`, because
   both go through `AppBar`.

**`Services/DisplayLayoutService.cs`** (new, lead-owned, added to `ShellContext` as `Displays`): the one place that
reads monitors. `NativeMethods.Monitors.cs` (new partial) declares `EnumDisplayMonitors`, `MonitorFromRect` and
`GetMonitorInfoW` with `MONITORINFOEXW` (an overload next to the existing `MONITORINFO` one in `NativeMethods.cs`;
check for an existing declaration first); `GetDpiForMonitor` already exists in `NativeMethods.cs`. Sources of
invalidation, all hopped to the dispatcher: `SystemEvents.DisplaySettingsChanged` (`WM_DISPLAYCHANGE`),
`PowerModeChanged` (resume), `SessionSwitch` (unlock / console connect), `AppBar.Detached`, and `Invalidate()` called
by any bar or dock window on `WM_DPICHANGED` (a scale change sends no `WM_DISPLAYCHANGE`, but every monitor with a
WinGnome surface gets `WM_DPICHANGED`). Invalidation restarts one 250 ms one-shot `DispatcherTimer`; on tick it reads
the layout (a few microseconds, no blocking), diffs it and raises `LayoutChanged(old, new, diff)` only when something
changed or a surface is detached. If a pass found changes, one follow-up pass runs 1.5 s later to catch layouts
Windows settles late (hot-plug). No timers run at idle. The service replaces the per-feature `SystemEvents`
subscriptions in `TopBarFeature` and `DockFeature`.

`MonitorKeyOf(hwnd)` maps `MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST)` to a key through the handle→key table
built on each read (HMONITORs are not stable across display changes). If the handle is not in the table (a fresh
HMONITOR between a display change and the next read), it calls `GetMonitorInfoW` with `MONITORINFOEXW` on that handle
directly and returns `szDevice`, and calls `Invalidate()`; only if that call fails does it return null (callers treat
null as "primary").

**`Services/AppBarJanitor.cs`** (new, isolates the one undocumented behaviour; final shape depends on the spike, see
Safety and recovery). Under outcome A it is one static method: register a 1×1 hidden `WS_EX_TOOLWINDOW` window with
`ABM_NEW`, immediately `ABM_REMOVE` it, destroy the window, and log every monitor's `GetMonitorInfo` work area before
and after. It never calls `ABM_SETPOS`, so it reserves nothing. It runs once at start (after
`TaskbarController.RestoreFromMarker`, before any feature registers an AppBar) and on `--restore-taskbar`. Failure is
`Log.Warn` with the call and the work areas, never fatal.

**Top bar (`Features/TopBar/`)**, split into one coordinator and N instances:
- `TopBarFeature` (coordinator, `IFeature`, `IEmergencyRestore`): owns `TopBarServices` (shared, built once:
  `ClockViewModel`, `WorkspacesViewModel`, `SystemStatusViewModel`, the shared `TrayModel`, one `PopupHost`) and a
  `Dictionary<string, TopBarInstance>` keyed by monitor key. On `LayoutChanged`, `Detached` and settings changes it
  applies `SurfacePlan.Reconcile` steps in order. `EmergencyRestore` shrinks to `TrayModel.EmergencyRestore()` (the
  tray host steps aside); AppBars are removed by `AppBar.UndockAll()` before it runs (see Safety).
- Each reconcile pass logs one `Log.Info` line: steps applied, elapsed ms, and the number of `SHAppBarMessage` calls
  made during the pass (difference of `AppBar`'s counter). Passes are rare (display events only); QA uses the count to
  spot storms (a steady-state pass should cost a handful of calls per surface).
- `TopBarInstance` (new; today's `Build`/`Dock`/`WndProc`/full-screen code moves here): its `TopBarWindow`,
  `BlurBackdrop`, `AppBar`, per-bar `FocusedAppViewModel` and per-bar tray icon view. Creating a bar on a secondary
  monitor: `WindowStartupLocation.Manual` with `Left`/`Top` left at `NaN` (WPF never positions it in DIPs of the wrong
  monitor), create the handle (`ShellSurface.MakeNonActivating`), `SetWindowPos` it onto the target monitor in physical
  pixels *before* `Show` so WPF receives `WM_DPICHANGED` and lays out at that monitor's DPI, then `Dock()` against
  that monitor's bounds and scale. `ForegroundCoversPrimaryMonitor` becomes "covers this bar's monitor".
- `TaskbarCreated` (Explorer restart, filtered with `TrayHost.IsOwnBroadcast` as today): instances no longer handle it
  themselves. Every bar window receives the broadcast, so each instance only forwards it to the coordinator, which
  coalesces them into **one** pass at `Background` priority: undock every instance first, then re-dock them in plan
  order, then the full-screen re-check per instance (today's logic).
- `TopBarViewModel` becomes per bar: references the shared view models, owns its `FocusedAppViewModel`. XAML
  bindings keep their paths. Calendar, quick-settings and logo cards are created per bar on the first open of each
  (secondary bars rarely open them; saves their visual trees). `PopupHost` is shared, so "one popup at a time",
  Escape and click-outside dismissal work across bars unchanged; WPF places a `Popup` on its `PlacementTarget`'s
  monitor at that monitor's DPI. `ConfirmDialog` uses `CenterScreen`, which follows the cursor's monitor.
- `FocusedAppViewModel` gains a monitor key and reads `MonitorFocusTracker.Current(key)` (fed by the coordinator from
  `WindowTracker.ForegroundChanged`, `WindowsChanged`, and — only while more than one monitor exists —
  `EVENT_SYSTEM_MOVESIZEEND` and `EVENT_OBJECT_LOCATIONCHANGE` of tracked top-level windows, coalesced through one
  `DeadlineSchedule` deadline of 150 ms, so Win+Shift+Arrow moves are seen).

**Tray mirroring** (the delicate part). Today `TrayViewModel` owns the `TrayHost` *and* the icon view models, whose
`SetSlot(slotPx, scale)` is per icon — impossible with bars at different DPIs. Split:
- `Tray/TrayModel.cs` (shared, one per process): owns the single `TrayHost` (unchanged: one hidden `Shell_TrayWnd`,
  one thread, session mutex), the ordered list of `TrayIconEntry`, the hover state (`_hovered` entry, its bounds and
  bar bounds) with the one hover `DispatcherTimer`, and delivery. `TrayIconEntry` is observable: state (tooltip,
  visibility, frozen `BitmapSource` converted once, `PopupOpen`) changes are property changes on the entry.
  `Send(entry, action, iconBounds, barBounds)` takes the clicked bar's strip; delivery calls
  `TrayHost.SetIconBounds(id, iconBounds)` before posting, so `Shell_NotifyIconGetRect` answers with the icon the user
  actually used, on that monitor. `Changed(TrayChange)` on the UI thread carries **structure only** (insert, remove,
  move by index).
- `Tray/TrayBarIcons.cs` (per bar): an `ObservableCollection<TrayIconViewModel>` mirrored from `TrayModel.Changed`;
  the model is the single source of order. `TrayIconViewModel` wraps one entry, re-raises the entry's property
  changes for its bindings, and holds this bar's slot and scale (`TrayIconPlacement` as today). So a state update
  reaches all N bars through the shared entry, not through N model events. Bitmaps are shared and frozen, so N bars
  cost N small visual trees, not N icon conversions. Wrappers unsubscribe from their entry when removed or when the
  bar is destroyed.
- Hover across bars: because `_hovered` and the timer live in the model, `Leave` on bar A followed by `Enter` on bar
  B is the same as moving between two icons: the timer restarts for B's entry and bounds. `PopupOpen` is per entry,
  so the matching `NIN_POPUPCLOSE` is sent whichever bar the pointer leaves from.
- `TrayHost.SetBarBounds` keeps receiving the **primary** bar's strip (the hidden host window stays there); if the
  primary has no bar (`TopBar.Monitors` changed mid-run, or the primary bar is being rebuilt) it keeps its last strip.
  `TrayIconsView` passes its own bar's bounds into `Send`. Elevated WinGnome: no host, so no icons on any bar (KI-006).

**Dock (`Features/Dock/`)**: `DockFeature` becomes the coordinator (pins, catalogue, theme, `ExternalForeground`,
Super+N, settings) and `DockInstance` (new) holds today's per-window parts: `DockWindow`, `BlurBackdrop`,
`DockActions`, `DockMenuPresenter`, `DockVisibilityController`, `DockReservation`, laid out for its monitor (today's
`Relayout` with the `MonitorInfo` passed in). `DockVisibilityController.Configure` takes the monitor's key and
`PixelRect` (and scale) from `MonitorInfo` instead of an HMONITOR. Secondary instances are created only when
`Dock.Monitors == All`. The 75 ms edge-reveal poll moves to one shared `DockEdgePoller` that runs while *any* dock wants
it and dispatches the sample to the instance whose monitor contains the cursor (`MonitorLayout.At`) — N docks never
mean N polls. Isolation filters each instance's running list with `DockWindowFilter` using
`DisplayLayoutService.MonitorKeyOf`; click and Super+N planning (`DockClickPlanner`) then only see that monitor's
windows. A primary swap with `Monitors == Primary` moves the one instance (undock, relayout on the new monitor,
reserve). `DockReservation`'s own `TaskbarCreated` hook goes: the dock coordinator handles the broadcast in one pass
like the top bar's, and filters `TrayHost.IsOwnBroadcast` (today `DockReservation` doesn't, so it re-registers on
WinGnome's own tray broadcast as well). Relayout logs the dock's monitor and edge; inner edges are not detected or
hinted (KI-071). `DockFeature` drops `IEmergencyRestore`: its reservation is covered by `AppBar.UndockAll()`.

**Hot corner (`Features/Overview/HotCornerWatcher.cs`)**: the existing 50 ms cursor poll (unchanged cost) looks up
the monitor under the cursor with `DisplayLayoutService.Current.At`, asks `HotCornerRules.KindOf` (`Corner`,
`Guarded` for the primary when it is not a true corner, `None`), and passes that monitor to the matching detector
(the 1-pixel one with the configured delay, or an 8 px one with `GuardedDwellMs`) and the `ShouldTrigger`
full-screen check (which takes the monitor rect already). Samples of any other kind reset a detector. The ad-hoc
`IsOnAnyMonitor` checks are replaced by the Core rule.

### Threading, DPI, hostile cases
- Everything above runs on the dispatcher, except `TrayHost`'s own thread (unchanged) and the crash path.
  WinEvent callbacks only update a deadline; no Win32 enumeration happens inside a hook callback.
- All geometry is physical pixels (`PixelRect`); each instance converts with its own monitor's scale. Negative
  coordinates and mixed DPI are covered in Core tests and QA (a 150 % secondary at negative coordinates: watch
  `TopBarGeometry.Compute` rounding at 1.5 and `TrayIconsView.ScreenBounds`, whose `PointToScreen` result can drift by
  a pixel from the bar's strip on a scaled monitor).
- Elevated windows: only read (`MonitorFromWindow`, names); never moved, focused or decorated by this feature.
- Explorer restart: one coordinator pass per feature on `TaskbarCreated` (removals first); the taskbar feature
  re-hides the new secondary taskbars as it does now.
- A WinGnome bar left on a removed or moved monitor is undocked by the detach guard within one message and then
  re-docked or destroyed by the next reconcile pass.

### Work packages
0. **Spike: force-kill cleanup** (throwaway tool, not committed; results go into KI-070 and this spec). Runs first,
   in parallel with A and 1. Decides the shape of the janitor (Safety, outcome A or B).
A. **AppBar hardening** (`Interop/AppBar.cs` only: idempotent reposition, detach guard, static registry and
   `UndockAll`, call counter; `App.EmergencyRestore` calls `UndockAll` first). Its own first commit. Done when the
   **two-top-bar storm check** passes: two AppBars docked on the same monitor's top edge (a debug harness or two test
   instances with isolated `--settings-dir`) settle within one round of notifications, the `SHAppBarMessage` count
   stays flat over 30 s idle, and removing either one settles the other once. Packages 3–5 start only after A.
1. **Core monitors** (`src/WinGnome.Core/Monitors/`, settings fields, tests). Parallel with A.
2. **Display service and janitor** (`Services/DisplayLayoutService.cs`, `Services/AppBarJanitor.cs`,
   `Interop/NativeMethods.Monitors.cs`, `ShellContext`, `App.xaml.cs` start/restore/self-test wiring). After 1; the
   janitor part after 0. **Safety-critical.**
3. **Top bar instances and tray mirroring** (`Features/TopBar/`). **Safety-critical** (AppBars). After A and 2.
4. **Dock instances** (`Features/Dock/`). After A and 2; parallel with 3 (separate folders; neither edits `Interop/`).
5. **Hot corner** (`Features/Overview/HotCornerWatcher.cs`). After 2; parallel with 3 and 4.
6. **Settings UI and docs** (`Features/Settings/ViewModels|Views` for Top Bar, Dock, Activities, Multitasking
   wording; README, PLAN.md, KNOWN_ISSUES.md KI-069..KI-072). Parallel with 3–5.

## Safety and recovery
- **What changes:** each bar (and each always-visible dock) registers an AppBar on its monitor, shrinking that
  monitor's work area. The taskbar changes are unchanged (`taskbar.state` marker, global auto-hide, every taskbar
  window hidden).
- **Monitor removed or moved:** the detach guard undocks immediately; reconcile re-docks or destroys (removals first).
- **Normal exit / settings off / `TopBar.Monitors` → Primary:** instances undock in reverse creation order, then
  windows close, backdrops dispose.
- **Crash (one path only):** `App.EmergencyRestore` calls `AppBar.UndockAll()` **first**, while the tray host is still
  alive, so each `ABM_REMOVE` still forwards through it to Explorer; then the features' `EmergencyRestore` (the top
  bar's is now tray-only, `TrayHost.EmergencyRestore` steps the host aside; the dock has none); then
  `TaskbarController.RestoreFromMarker`. No per-feature snapshot of instances is kept for the crash path.
- **Force-kill:** our windows die with the process and Explorer is left with AppBar entries for dead windows. Which
  recovery ships is decided by spike 0:
  - *Spike:* a test process docks a top AppBar on the primary and one on a secondary monitor, is killed with
    `taskkill /f`, and each monitor's work area is logged immediately, after 10 s idle, after a 1×1 `ABM_NEW` +
    `ABM_REMOVE` nudge from a second process, and after another program's ordinary AppBar traffic. Repeat with
    Explorer's taskbar visible and auto-hidden.
  - **Outcome A (the plan):** the nudge (or any AppBar traffic) makes Explorer drop the dead entries and restore every
    work area. Then nothing is recorded: no `appbars.state`, no per-change disk writes, no `WorkAreaAudit`. The next
    normal start's own `ABM_NEW`/`ABM_SETPOS` is already AppBar traffic, and `AppBarJanitor` nudges once at start and on
    `--restore-taskbar`, logging work areas before and after. KI-070 records the reliance on this undocumented behaviour.
  - **Outcome B (fallback):** Explorer keeps stale strips after the nudge. Then before the first `ABM_NEW` of a run
    each monitor's original work area is recorded (`appbars.state` in the settings directory, written once per layout
    change, "no record, no reservation" like the taskbar marker), and the janitor restores stale ones with the
    documented `SystemParametersInfo(SPI_SETWORKAREA)` (not persisted to the profile). Outcome B gets its own design
    note and advisor review before coding; this spec does not detail it.
- **`--safe`:** bars and docks are created on every monitor exactly as in normal mode (AppBars are session-scoped
  and released on exit, as today's single bar is); no taskbar hiding. The janitor's start-up nudge runs in `--safe`
  too: it reserves nothing and only gives back strips left by a dead process.
- **`--selftest`:** before any feature starts (after the nudge) the app records each monitor's `GetMonitorInfo` work
  area; after every feature has been disposed at shutdown it reads them again and fails (exit 1) on any difference,
  logging both. Explorer applies `ABM_REMOVE` work-area changes promptly but not necessarily synchronously, so the
  comparison retries up to 3 times over 500 ms (dispatcher timer, no sleep) before failing. If the monitor layout
  itself changed during the run, the check logs and skips instead of failing. Unlike a check against our own
  registry, this fails whenever a strip is really left behind.

### Spike 0 results (2026-10-09): outcome A
Throwaway WinForms tool, not committed. Windows 11 build 26200; primary 2560×1600 at 125 % (`\\.\DISPLAY1`),
secondary 3440×1440 at 100 % placed above it at (−447, −1440) (`\\.\DISPLAY2`); Explorer's taskbar auto-hidden and
hidden by the user's everyday WinGnome, whose top bar holds the primary's top 40 px. One process docked a 40 px
left-edge AppBar on the primary, another on the secondary; both were killed with `taskkill /f`.
- Work areas were back to their originals within ~300 ms of the kill, on both monitors, before any other AppBar
  traffic (re-read after 2 s and 10 s: unchanged). Explorer evidently notices the AppBar window's destruction.
- A 1×1 `ABM_NEW` + `ABM_REMOVE` from a third process afterwards changed nothing (nothing was left to reclaim).
- The same held for the real app: a `--safe` instance with bars on both monitors and always-visible docks on both,
  force-killed, left every work area as before, and the next start's janitor logged identical work areas.
- Not tested: Explorer's taskbar visible (the everyday instance keeps it hidden).
- Side observation: when two spike processes registered at the same moment, one `ABM_NEW` stalled for seconds while
  the other process's thread was not pumping messages (it slept between calls). WinGnome only calls
  `SHAppBarMessage` from the dispatcher thread, which pumps; risk 5 below covers the crash-path variant.

So the janitor is the outcome A shape (one nudge at start and on `--restore-taskbar`, work areas logged), no
`appbars.state` and no `WorkAreaAudit`. KI-070 records the reliance on undocumented Explorer behaviour.

### Implementation notes (deviations from the design above)
- `SurfacePlan` re-docks in two steps, `Release` then `Dock`, and every release comes before any dock: two
  monitors trading coordinates (a primary swap) could otherwise dock one bar onto the other's old rectangle while
  that one is still registered. With `Primary` mode, a surface whose key went away is moved to the new primary
  (`Dock` step with another monitor's key) instead of destroyed and rebuilt.
- `DockEdgePoller` sends each 75 ms sample to every dock that wants polling, not only to the one under the cursor,
  so a dock held open on one monitor still sees the pointer leave for another. Still one timer and one
  `GetCursorPos` per tick.
- `HotCornerDetector.Reset()` (Core, tested) is used for samples on monitors without a true corner; feeding an empty
  rectangle at the origin would have treated (0,0) as a corner.
- Bars and docks also restore their exact strip after `WM_DPICHANGED` (as before), but only when a fresh read shows
  their monitor with unchanged bounds; otherwise the coordinator's pass decides.
- **Lost strip after unplugging a monitor** (found in the live session, 2026-10-09). When the upper monitor was
  unplugged, Explorer reset the primary's work area to the whole monitor while our primary bar stayed registered,
  sent it no `ABN_POSCHANGED`, and every later full docking sequence on that registration (QUERYPOS, SETPOS,
  WINDOWPOSCHANGED; the user changed bar settings many times, each logged as "Top bar docked") left the work area
  unreserved. The layout diff ignores work areas by design (our own strips change them), so the pass saw nothing to
  do for the kept bar. Fix: after every display pass, including the 1.5 s follow-up (now always raised), and on
  `WM_SETTINGCHANGE`/`SPI_SETWORKAREA` (debounced through the layout service), every bar and reserving dock checks
  with a fresh `GetMonitorInfo` that the work area still leaves its strip out (`AppBarReservation.IsReserved`, Core,
  tested) and, only if not, registers again (`ABM_REMOVE`, `ABM_NEW`, then the docking sequence; logged "registering
  again"). Re-registration rather than SETPOS because SETPOS on the existing registration was seen not to help; that
  re-registration restores it is expected but still to be confirmed in the next unplug test. A strip that is present
  costs one `GetMonitorInfo` and no `SHAppBarMessage`, so the storm guard is unchanged. Settings changes now re-dock
  bars only when the height, margin or corner radius changed (every settings change used to re-dock every bar).
- **Strips lost right after start** (second live session, 2026-10-09; a regression of the idempotent reposition).
  With the taskbar hidden at start, Explorer recomputed work areas after our bars had docked and left both strips
  out; the old reposition answered every `ABN_POSCHANGED` with a SETPOS, the new one compared slots only. Now the
  idempotence rule is "the slot is ours **and** a fresh work area leaves the strip out"
  (`AppBarReservation.Decide`, Core, tested; `ABN_STATECHANGE` is handled the same way), with a 2 s per-bar cool-down
  on re-registering from notifications so two bars can never feed each other. When the strip is missing the bar
  registers again: reproduced by resetting the primary's work area with `SPI_SETWORKAREA` against a `--safe`
  instance, a SETPOS of the unchanged rectangle did not bring the strip back and registering again did (for every
  AppBar on that monitor, the everyday bar's too), within 0.25 s via the `WM_SETTINGCHANGE` pass. A reset without
  the broadcast is caught by the forced passes 1.5 s and 10 s after start (`DisplayLayoutService.VerifyAfterStart`)
  or fixed by Explorer itself on the next AppBar traffic. Self-test cannot reproduce this (it never hides the
  taskbar); the manual check is the spike's `resetwork` step above, plus a non-safe start with the taskbar hidden.
- **Explorer applies strips late after the taskbar is hidden** (third live run, 2026-10-09; supersedes the
  "register again at once" part of the two notes above). Facts:
  - `TaskbarController.Hide` sends `ABM_SETSTATE(ABS_AUTOHIDE)` to Explorer's `Shell_TrayWnd` and then immediately
    `ShowWindow(SW_HIDE)`s every `Shell_TrayWnd`/`Shell_SecondaryTrayWnd` (unchanged by this spec).
  - With the taskbar visible before start: the branch registered at +0.2 s, found both strips missing at +2.3 s and
    +10.8 s and registered again each time; at +14 s neither strip was reserved. The old build (main before 0010)
    docked at the same point and its strip appeared only ~35 s later, with no activity of its own at that moment —
    and ~35 s is not a ceiling: after a restart of the pre-fix build on 2026-10-09 20:14 the primary's work area was
    still full when observed at 20:28. Session 1 also started unreserved. So the delay predates this spec, and
    re-registering during it neither helped nor is known to be harmless (it may restart Explorer's delay).
  - In a steady state (taskbar already hidden for a while) Explorer applies a strip within ~0.3 s (spike 0, storm
    check), and after `SPI_SETWORKAREA` reset the work area, registering again restored every strip at once.

  Hypothesis (unverified): switching to auto-hide makes Explorer recompute work areas only when its hide transition
  completes, and hiding the taskbar windows in the middle of that transition leaves it to a long fallback. The
  experiment tool `scratchpad\tbexp` (not committed) measures it; it refuses to run while any WinGnome runs and puts
  the taskbar back afterwards. Next user session, with every WinGnome quit, run each variant once and keep the log:
  `TbExp.exe immediate log.txt` (today's order), `TbExp.exe settle log.txt` (hide the windows only after Explorer
  has applied auto-hide), `TbExp.exe nohide log.txt` (auto-hide only), `TbExp.exe spi log.txt` (today's order plus
  `SPI_SETWORKAREA` after 2 s). Each docks a 40 px top AppBar and logs work areas every 250 ms for 60 s.

  Options, depending on the result:
  1. *Fix the order* (if `settle` reserves promptly): `TaskbarController.Hide` waits (bounded, on the dispatcher, no
     blocking) for Explorer to apply auto-hide before hiding the windows. Smallest change; no new system state.
  2. *Outcome B / hybrid* (if only `spi` is prompt): bars rely on AppBars as now, and when the expected work area is
     still not applied after a grace period, WinGnome sets it with documented `SystemParametersInfo(SPI_SETWORKAREA)`
     per monitor (not persisted). Needs the original work areas recorded before the first change (a marker file like
     `taskbar.state`), restoring them on exit, crash and next start, and care because Explorer recomputes work areas
     on its own afterwards (it would then normally produce the same rectangle). Separate design note and advisor
     review first, as this spec already requires for outcome B.
  3. *Wait it out* (the old build's behaviour): what 5c19982 shipped as an interim — `StripRecovery` waited 45 s for
     Explorer, then re-registered at most three times 60 s, 120 s and 240 s apart. **Superseded by option 2**, which
     kept the bounded one-shot timer and the "notifications cannot make it act sooner" property, but replaced waiting
     and re-registering with the work-area fallback in the design note below.
- Turning the dock off now destroys its instances (it used to hide the window and keep it).
- `AppBar.RegisterAndRemove` serves the janitor, so `SHAppBarMessage` stays in one place and is counted.
- Footprint measured on the QA machine (`--safe`, top bar only, 20 s idle): main display only 91.9 MB private,
  all displays (adds a 3440 px bar) 101.2 MB; idle CPU 0–125 ms per 20 s, 17 threads either way. Slightly above the
  4–8 MB estimate because of the bar's width.

## Footprint
- Idle: no new timers or polling. The display service's timer runs only after a display event or a detach (≤ 2
  one-shots). Location WinEvents are already hooked by `WindowTracker`; the new consumer only resets a deadline, and
  only while more than one monitor exists. The dock edge poll is shared and runs only under the same conditions as
  today. `ABN_POSCHANGED` now costs one `ABM_QUERYPOS` when nothing moved, instead of four calls.
- Memory: one bar window plus blur backdrop per extra monitor, estimated 4–8 MB each in software rendering; cards are
  created on first open. Secondary docks only with `Dock.Monitors == All` (~6–10 MB each, icon bitmaps shared through
  `IconProvider`'s cache). Measure idle private bytes and CPU before/after on two monitors (AGENTS.md QA 6).

## Acceptance criteria
Core tests (T) and manual QA (Q) on two monitors at different scales (100 % primary, 150 % secondary), the secondary
placed left of and above the primary (negative coordinates).
1. T `MonitorLayout`: normalisation (empty rects, duplicate keys/bounds, no primary flag), `At` with negative coords,
   and boundary rows: a point exactly on the shared edge belongs to the right/lower monitor, `Right`/`Bottom` itself
   is outside.
2. T `MonitorLayoutDiff`: add, remove, move, DPI-only change, primary swap, no change → empty diff.
3. T `SurfacePlan.Reconcile`: removals before moves before additions; primary swap never yields two bars per key or
   bounds; detached + key present → re-dock, detached + key gone → removal; `Dock.Monitors` Primary vs All;
   `TopBar.Monitors` Primary.
4. T `HotCornerRules`: single monitor; neighbour on the left, above, diagonal-only, offset neighbours that don't
   reach the corner; negative coordinates.
5. T `MonitorFocusTracker`: MRU per monitor, window moves monitor, minimised skipped, closed pruned, layout change
   drops removed keys.
6. T `DockWindowFilter` and `TrayAnchor` (bar above the primary at negative Y anchors on that bar).
7. T Settings: a settings file without the new fields loads with the defaults; undefined enum values normalise.
8. Q (package A) Two-top-bar storm check as defined in Work packages; reconcile log lines show the
   `SHAppBarMessage` count per pass.
9. Q Bars on both monitors with every item; clock, calendar, quick settings, logo menu and confirmation dialogs
   open on the clicked monitor at the right DPI; opening one closes an open one on the other bar. Bar created on the
   150 % secondary is laid out at 150 % from its first frame.
10. Q Tray: left/right click and hover on a version 4 app (e.g. OneDrive, Teams) and a legacy app on each bar; the
    menu opens beside the clicked icon on that monitor; icons add/remove and change state in sync on both bars;
    leaving an icon on one bar and entering one on the other restarts the hover tooltip.
11. Q Focused app per monitor follows focus and Win+Shift+Arrow moves.
12. Q Dock: Primary default; All → a dock on each; isolation shows only that monitor's windows; Super+N uses the
    primary dock; one edge poll running (log) with two intellihide docks hidden.
13. Q Hot corner fires on each true corner, not on a secondary's corner shared with the other monitor; on a primary
    whose corner is shared, it fires after resting at the corner and not when passing through to the other monitor.
14. Q Unplug, replug, rearrange, change scale, swap primary, sleep/resume, Explorer restart: instances rebuilt within
    1 s (log timestamps), one pass per event, no double strip, work areas correct (`GetMonitorInfo` logged per monitor).
15. Q Exit, crash (temporary `throw`), and `taskkill /f` + restart with the same `--settings-dir`: every work area and
    taskbar back to the original; `--restore-taskbar` after `taskkill /f` alone also clears any strip (outcome A:
    via the nudge; the log shows work areas before and after).
16. Q `--selftest --safe` exits 0 on one and on two monitors, and exits 1 with a deliberately leaked AppBar (temporary
    local change that skips one `ABM_REMOVE`, removed before committing); idle CPU/memory compared before and after.

## Risks and open questions
1. **Apps that position flyouts from the taskbar** (`ABM_GETTASKBARPOS`, `GetWindowRect` of `Shell_TrayWnd`) still
   get the primary's (hidden) taskbar or the host strip, so their flyouts open on the primary even when clicked on a
   secondary bar. Also, `Shell_NotifyIconGetRect` has one answer per icon (the last bar the user used it on), so a
   flyout the app opens on its own later appears there. KI-069 (S4).
2. **Force-kill cleanup relies on Explorer re-validating AppBars** on later AppBar traffic (undocumented). Spike 0
   found Explorer reclaims a killed bar's strip within ~300 ms and that the janitor's nudge reclaims anything left, so
   outcome A shipped; KI-070 (S4) records the reliance. Outcome B (the design note below) additionally recovers any
   work area WinGnome set itself from its own record, which does not depend on that behaviour; KI-099 (S3).
3. **Dock on an inner edge** (another monitor below or beside it): intellihide/autohide reveal at that edge is
   unreliable because the pointer crosses to the other monitor. Logged on relayout only; no Settings hint and no Core
   edge helper. KI-071 (S4).
4. **Activities and hot corners on secondary monitors open the overview on the primary**, which may feel wrong;
   per-monitor overview is a follow-up spec. KI-072 (S4).
5. **Crash on the tray thread itself:** `UndockAll` sends through the tray host's window; if the host thread is the
   one that faulted, those sends can block the crash path. Same exposure as today's single bar; noted, not fixed here.
6. **Per-reconcile `SHAppBarMessage` count is logged with `Log.Info`, not a debug level** (deviation from the
   review's "in debug"): `Log` has no debug level and QA runs Release builds; reconcile passes happen only on display
   events, so the line costs nothing at idle.
7. Taskbar peek (tray access through the native taskbar) stays primary-only; brightness in every bar's quick
   settings still controls only the built-in panel (KI-021).
8. WPF popups across mixed DPI have had scaling glitches in older .NET builds; QA 9 covers it.
9. A third-party AppBar on the same top edge (e.g. another bar app) now shares the storm protection; it still stacks
   below or above ours as Explorer decides (unchanged).
10. **Recycled HMONITOR after a display change** (Opus review, low; KI-098):
    `MonitorKeyOf` maps `MonitorFromWindow` handles through the table of the last read. If Windows reuses an old
    HMONITOR value for a different monitor, a window can be given the wrong monitor key until the next pass, at most
    250 ms (plus the 1.5 s follow-up). Effect: a bar's focused app, an isolated dock's window list or the dock's
    full-screen/intellihide check can be wrong for that moment; no AppBar or work-area effect.
11. Fixed after the Opus review (no longer open): a settings change within 250 ms of a display change re-docked bars
    and docks on their cached, possibly stale rectangle; they now re-dock only when a fresh read shows their monitor
    with the same bounds (`TopBarInstance.RestoreStrip`, `DockInstance.RelayoutIfMonitorUnchanged`) and otherwise
    leave it to the pass. A `TopBarInstance` constructor that threw after `DockOn` left its AppBar registered until
    exit; it now disposes itself (undocking first) before rethrowing.

---

## Design note: outcome B, the work-area fallback (`SPI_SETWORKAREA`)

Status: chosen by the user 2026-10-09 (option 2 above). Advisor review (DeepSeek V4 Pro) applied 2026-10-09:
atomic marker writes with a defined repair for an unreadable one, an ordered restore chain instead of a per-bar
restore, no re-registration immediately before a shrink, and the monitor key derived at use time. Implementation
review (DeepSeek V4 Pro) applied 2026-10-09: work areas are recovered **before** the taskbar is restored, because
restoring the taskbar deletes the marker the unreadable-record repair tests and shows the taskbar, which made
"full bounds" both unreachable and wrong; the Win32 error is captured before the marker write that would clobber it;
that repair moved into tested Core (`WorkAreaRecovery.RepairAll`); a spent budget waits instead of re-registering
(rule 5); and the per-owner and per-monitor bookkeeping is pruned. The `TbExp` experiment still runs in the same live
session, to see whether option 1 (hide the taskbar windows only after Explorer has applied auto-hide) can later
remove the need for this; the fallback also fixes the unplugged-monitor case, which option 1 does not.

### Why

Explorer owns work areas. WinGnome's bars are registered AppBars and Explorer grants their rectangles, but it applies
them to work areas only inside its own taskbar layout pass, which is deferred while its taskbar is auto-hidden and
`SW_HIDE`n (measured ~35 s — not a ceiling: a fresh start of the pre-fix build was observed still unreserved minutes
later), and which it can skip entirely when a monitor is unplugged. In both cases maximised windows cover the bar and
a strip nothing reserves is left in the desktop. `SystemParametersInfo(SPI_SETWORKAREA)` is documented, needs no
elevation and writes exactly the value Explorer writes, so WinGnome can finish the job itself — provided it changes
as little as possible, records it before it changes it, and gives it back.

### Rules

1. **The AppBar stays the mechanism.** The fallback never reserves a strip Explorer did not grant: it acts only for a
   bar that is registered, holds a granted `Bounds`, and whose monitor's fresh work area fails
   `AppBarReservation.IsReserved` for that `Bounds`.
2. **Only the bar's own edge moves**, and always from a **fresh** `GetMonitorInfo` work area read inside the same
   critical section as the write, so the result is a subset of the current work area: the taskbar's strip and other
   AppBars' strips survive, and two bars on one monitor stack instead of clobbering each other.
3. **Never `SPIF_UPDATEINIFILE`** — nothing reaches the user's profile. `SPIF_SENDCHANGE` is used on the normal path
   so apps and our own bars learn at once, and skipped on the crash path (risk 13).
4. **`--safe` and `--selftest` never shrink.** Both *do* recover a leftover marker from an earlier non-safe run: that
   is a repair of our own change, exactly like `TaskbarController.RestoreFromMarker`, which already runs in safe mode.
5. **Act, don't re-register, and never loop.** A missing strip is acted on at most three times per episode (1.5 s,
   then 5 s and 20 s later), then left until the bar is docked afresh. The action is a shrink, except in `--safe` and
   the self-test, which change no system state and register the AppBar again instead. A shrink that the monitor's
   budget refuses does **not** fall back to re-registering — it waits for the next attempt: a budget being spent means
   something keeps reverting the work area, and re-registering is the one call the third live run showed to be useless
   there and possibly harmful. Per monitor, `WorkAreaBudget` allows three applications per 60 s. Hitting either bound
   logs once and stops. There is no polling: every check rides an existing trigger (dock, display pass, the debounced
   `WM_SETTINGCHANGE`, `ABN_POSCHANGED`/`ABN_STATECHANGE`, the bar's one-shot recovery timer).

### Core (`src/WinGnome.Core/Shell/`, all tested)

- `WorkAreaFallback.Shrink(AppBarEdge edge, PixelRect strip, PixelRect workArea) -> PixelRect?`: the work area with
  only that edge moved past the strip (`Top = Math.Max(workArea.Top, strip.Bottom)` and the mirror for the other
  three edges). **Null** when the strip is empty, when the work area already leaves it out
  (`AppBarReservation.IsReserved`), or when the result would be empty or inverted on the moving axis (a strip taller
  than the monitor). It never grows a work area and never touches the other axis, however far the strip reaches.
- `WorkAreaBudget.TrySpend(long nowMs) -> bool`: a sliding window of three applications per 60 s, the clock injected
  by the caller (`Environment.TickCount64`), so tests use literal times.
- `WorkAreaRecord(long Owner, string Key, PixelRect Bounds, PixelRect Original, PixelRect Applied)`: one shrink.
  `Owner` is the shrinking bar's HWND (unique per bar, needs no plumbing); `Original` is the fresh work area before
  the shrink, `Applied` the one we set. Records are kept **in application order**, which is the restore order.
- `WorkAreaLedger.Add(records, record)`: the list rule behind the marker — at most one record per (owner, monitor
  key), keys compared case-insensitively. A re-shrink for a pair (something reset the work area and the bar acted
  again) replaces the pair's old record and moves to the end: the list is the unwind order and the re-shrink is the
  monitor's newest change, so replacing in place would leave the pair's record below a newer one for another bar and
  it would no longer match the live value. Without the rule a work area something keeps reverting grows the marker by
  one record per re-shrink (three a minute per monitor), each of which `Plan` walks on every release. The input list
  is not modified, so the caller can keep it as the rollback state of a shrink that fails.
- `WorkAreaFile(IReadOnlyList<WorkAreaRecord> Records, bool Unreadable)` with `WorkAreaState.Parse(string? json)` and
  `Serialize(records)`: an absent or empty file is "no records"; a corrupt one is `Unreadable` (never an exception),
  because silently reading it as empty would strand the shrinks it described.
- `WorkAreaRecovery.Plan(records, MonitorLayout monitors, IReadOnlySet<long> released) -> WorkAreaPlan`: the whole
  restore decision, pure. Per monitor key it walks that key's records **newest first**, restoring one only when its
  owner has released *and* the running work area still equals its `Applied`, then continuing from its `Original`; it
  stops at the first record that fails either test. A record whose monitor is gone, or whose `Bounds` differ from the
  live monitor's, is dropped as stale (a mode change reset the work area anyway). The plan carries the ordered
  `Restores` (key + rectangle to write), the records to `Keep` (a live bar still owns a newer one) and `Nudge` (true
  only for a record whose monitor still exists with unchanged bounds and whose `Applied` and `Original` both differ
  from the running work area: someone else's value is in effect, and writing our original would take it away, so
  Explorer gets a chance to recompute. A record dropped as already given back or as stale does not nudge, and
  neither does a chain a live bar blocks). Restoring newest first is what makes a top bar's and a bottom dock's
  strips on one monitor unwind in the right order; restoring an older record while a newer one is still applied
  would leave the newer strip behind.
- `StripRecovery` reworked, same class and same call sites: `Update(bool reserved, bool shrinkAllowed, long nowMs)`
  returns `None` (reserved — and resets), `Wait(due)`, `Shrink`, `Reregister` (only when `shrinkAllowed` is false) or
  `GiveUp`. Attempts land at +1.5 s, +5 s and +20 s, then it gives up until `Reset()`. The 45 s grace and the
  60/120/240 s re-registration back-off of 5c19982 go: waiting 45 s was only "do nothing while Explorer is slow",
  which is what the fallback replaces.

### App

- `Interop/NativeMethods.Monitors.cs`: one new overload next to the `SPI_SETWORKAREA` constant,
  `SystemParametersInfoRect(uint action, uint uiParam, ref RECT value, uint winIni)`. The `SystemParametersInfo*`
  overloads in `NativeMethods.Input.cs` take scalars or int arrays, not a RECT, so nothing is redeclared.
- `Services/WorkAreaController.cs` (new, static so the crash path can use it from any thread; plain Win32, no
  dispatcher, one lock spanning read-compute-write): `Initialize(settingsDirectory, enabled)`,
  `TryShrink(nint owner, PixelRect monitor, AppBarEdge edge, PixelRect strip) -> bool`, `Release(nint owner)`,
  `ReleaseAll()` and `RecoverFromMarker(settingsDirectory)`. It holds the record list, a `WorkAreaBudget` per
  monitor key and the marker path. `ReleaseAll()` — the crash path — broadcasts nothing, because `SPIF_SENDCHANGE`
  sends synchronously to every top-level window and one hung window would block it (the work areas change all the
  same), and does not nudge, because the next start's janitor makes one. The record list only ever changes through
  `WorkAreaLedger.Add`, so a re-shrink replaces its pair's record instead of accumulating duplicates, and a shrink
  whose marker write or `SPI_SETWORKAREA` call fails rolls the list back to the exact snapshot from before it.
  **It derives the monitor key itself** (`MonitorFromRect` +
  `GetMonitorInfoEx`, the `MonitorKeyOf` fallback pattern) inside the lock, so `AppBar` needs no key field and no
  stale key can survive a display change; a monitor whose bounds no longer equal the cached rectangle is refused.
  Every application, restore, refusal and drop is logged: `Log.Info` with the key, the edge and both rectangles for
  an application or restore, and with a count for records dropped without a write (they no longer describe a live
  work area — given back, stale, or someone else's value in effect); `Log.Warn` for a spent budget, a
  failed call (with the Win32 error) and an unwritable marker.
- `AppBar.CheckStrip`: a `Shrink` step calls `TryShrink(_hwnd, _monitor, _edge, Bounds)`, a `Reregister` step calls
  the existing `Reregister()`. Applied or refused, the bar re-checks on the schedule `StripRecovery` gives it, so a
  refusal cannot spin. `TryShrink` returns false when the fresh read shows the strip already reserved, which is the
  common case and costs one `GetMonitorInfo`.
- `AppBar.Undock` (and so `Dispose`): after `ABM_REMOVE`, `WorkAreaController.Release(_hwnd)`. `AppBar.UndockAll()`
  (crash path): `ABM_REMOVE` for every bar, then `ReleaseAll()`, guarded so file I/O on a faulting
  thread can never throw.
- `App.OnStartup`: `WorkAreaController.Initialize(dir, enabled: !options.Safe)` before any feature starts, and
  `RecoverFromMarker(dir)` immediately after `TaskbarController.RestoreFromMarker` and before `AppBarJanitor.Nudge()`,
  so the janitor's "before" line and the self-test baseline both see recovered work areas. `--restore-taskbar` does
  the same before its nudge. A second launch that loses the single-instance check returns before any of this, so it
  can never recover or clear the running instance's marker.
- **Unreadable marker** (the crash-mid-write case, which atomic writing makes unlikely but not impossible): log
  `Log.Warn`, then repair conservatively — when `taskbar.state` exists in the same directory the taskbar is hidden or
  auto-hidden, so every monitor's correct work area is its full bounds and that is what is written; when it does not,
  nothing is written (a visible taskbar's own strip must survive) — and in both cases `AppBarJanitor.Nudge()` runs
  and the file is deleted.

### The marker `workareas.state`

In the settings directory, next to `taskbar.state`: written **before** the first shrink of a run and rewritten when
a record is added, restored or dropped; deleted when the last record goes. Written to a temporary file and moved
over the old one, so a crash mid-write cannot truncate it. The list holds at most one record per (owner, monitor
key): a re-shrink replaces its pair's record and moves to the end (`WorkAreaLedger`).

```json
{"Records":[{"Owner":131479,"Key":"\\\\.\\DISPLAY1","Bounds":"0,0,2560,1600","Original":"0,0,2560,1540","Applied":"0,40,2560,1540"}]}
```

Discipline as for the taskbar marker: **no record, no shrink** — if the file cannot be written, the fallback logs a
warning and does nothing, so a force-kill can never leave a work area WinGnome cannot give back.

### Safety and recovery

| Path | What happens |
|---|---|
| Normal exit, feature off, `TopBar.Monitors` → Primary | Bars undock in reverse creation order; each `ABM_REMOVE` releases its owner, and the plan unwinds the chain newest first, so a top bar's and a dock's strips on one monitor both go back. |
| One bar off, another still docked on the same monitor | The older record is kept, not written over: its `Applied` is no longer the live work area. Explorer's `ABM_REMOVE` recompute normally gives the strip back; if it doesn't, the record is still there for the next release or the next start, and the nudge is logged. |
| Crash | `AppBar.UndockAll()` → `ABM_REMOVE` for every bar → `ReleaseAll()` → features' `EmergencyRestore` → `TaskbarController.RestoreFromMarker`. |
| Force-kill | Nothing runs. The next start's `RecoverFromMarker` restores every record whose monitor and work area still match; `--restore-taskbar` does the same on demand. |
| Two instances | Separate profiles, separate markers, and each chain is walked only while the live work area equals the record's `Applied`, so neither can write over the other's strip. |
| Explorer recomputes late | While our AppBar is registered it grants the same strip, so its rectangle equals ours and the plan finds nothing to do. A different one is caught by the next check, within the budget. |
| Displays panel (`SetDisplayConfig`) | A mode change resets work areas and changes bounds, so the record is stale and is dropped with a log line; the bars re-dock and re-check. |
| `--selftest` | Unchanged, and still fails if a work area differs after shutdown: safe mode never shrinks, and recovery runs before the baseline is read. |

### Footprint

No new timer, hook or poll. Per shrink: one `MonitorFromRect`, one `GetMonitorInfo`, one `SystemParametersInfo`, one
small file write (rare — a few per run at most). Per release: one read, at most a few writes, one nudge only when the
plan asks for it. Idle cost is unchanged.

### Acceptance criteria (outcome B)

B1. T `WorkAreaFallback.Shrink`: each of the four edges; already reserved → null, including exact equality
    (`strip.Bottom == workArea.Top`); empty strip → null; a strip taller than the monitor → null; a strip reaching
    past the monitor on the axis that doesn't move leaves that axis alone; a monitor at negative coordinates; a work
    area that already excludes the taskbar's bottom strip keeps it; a second bar on the same edge stacks on the first.
B2. T `WorkAreaBudget`: three allowed inside 60 s, the fourth refused, one allowed again 60 s after the first;
    boundary rows at exactly +60 s and +60001 ms, and a clock that doesn't move.
B3. T `StripRecovery`: `Wait` at +1.5 s, then `Shrink` at 1.5 s / 6.5 s / 26.5 s with `shrinkAllowed`, `Reregister`
    at the same times without it, then `GiveUp`; a reserved strip resets it; `Reset` (undock, re-dock) starts afresh;
    a notification or display pass between attempts cannot make it act sooner.
B4. T `WorkAreaState`: a record round-trips; absent, empty and corrupt files; `Serialize` of no records.
B5. T `WorkAreaRecovery.Plan`: one record restores; two stacked records on one key both released restore newest
    first and land on the outermost `Original`; only the older released restores nothing and keeps both; a live work
    area that differs from `Applied` restores nothing and nudges; a monitor gone or with changed bounds drops the
    record; no records → an empty plan that doesn't nudge.
B6. Q Non-safe start with the taskbar hidden: both strips in the work area within ~3 s (`tools/Get-WorkAreas.ps1`
    shows DISPLAY1 work top = 40 and DISPLAY2 work top = −1408), one "work area set directly" line per monitor, and
    no further lines over 5 idle minutes.
B7. Q Unplug the secondary: DISPLAY1 keeps top = 40 and at most budget-many shrinks are logged; replug (Win+P →
    Extend if Windows loses it): both strips back, no doubled strip (exactly 40 / −1408).
B8. Q Quit: work areas exactly the baseline (taskbar visible: DISPLAY1 bottom 1540, DISPLAY2 bottom −48) and no
    `workareas.state` left.
B9. Q Crash (a temporary `throw`, removed before committing) and `taskkill /f` + restart with the same
    `--settings-dir`: work areas back to the original and the marker gone; `--restore-taskbar` alone after a
    force-kill clears it too; a deliberately corrupted marker takes the documented repair path and leaves full work
    areas with the taskbar hidden.
B10. Q `--selftest --safe` exits 0 with the fallback compiled in, and a `--safe` run never logs a shrink.
B11. Q Scale change, primary swap, sleep/resume: strips reserved within ~3 s of the pass, no doubled strip, no
     budget warning in the log.
B12. Q Native taskbar mode (taskbar visible, not auto-hidden) with an always-visible dock at the bottom: the dock's
     strip is reserved *above* the taskbar's, and quitting leaves the taskbar's own strip intact.

### Risks

12. **WinGnome now writes a system value Explorer also writes.** Bounded by two budgets, always derived from a fresh
    read, only ever moving our own edge inward, and reversible through the record. KI-099 (S3) records it, including
    that a third-party tool which also sets work areas could fight us — after three applications in a minute we stop
    and log, so a fight cannot become a loop. **That fight was observed for real (2026-10-09, the user's everyday
    instance), and the other writer was Explorer itself, not a third-party tool:** after a display pass with no
    topology or mode change (probably a display power event), Explorer recomputed both work areas without the strips
    it had granted our still-registered AppBars, and each shrink we wrote appeared to provoke another such
    recompute — 20 direct sets across both monitors over ~7 minutes. Both budgets held and stopped the fight, but it
    ended in the three-attempt cap's give-up, which is terminal for the run: a display pass that changes nothing
    does not re-arm `StripRecovery` (only an undock/fresh dock or a strip really reserved again does — the log's
    "giving up until the next display change" overstates what re-arms it, since a display change only re-docks the
    bars when the layout actually changed), and both strips stayed missing until a restart. KI-102 (S3, now
    *Fixed*) records the measured episode and the fix directions; the fix and its live verification are in the
    addendum below.
13. **`SPIF_SENDCHANGE` broadcasts synchronously** to top-level windows, so a hung app could delay a shrink. It is
    skipped on the crash path for that reason; on the normal path it runs on the dispatcher and is worth its cost.
14. **The marker is per profile.** A force-kill followed by a start with a different `--settings-dir` cannot recover
    (the same limitation as `display-revert.json`, KI-068); `--restore-taskbar` from the original profile can.
15. **The crash path does file I/O** (deleting the marker) on a possibly faulting thread, like `UndockAll`'s tray
    sends (risk 5). Guarded, never fatal; a marker left behind is recovered by the next start.
16. **`--restore-taskbar` does not take the single-instance mutex** (as it already did not for `taskbar.state`), so
    running it while a healthy instance is up recovers and deletes that instance's marker, and gives back a strip its
    bar still holds. It self-heals: the broadcast reaches the live bar, its next check finds the strip missing and
    re-shrinks within ~1.5 s, rewriting the marker. Only a force-kill inside that window could strand it, and the
    next start's repair covers that. Noted in KI-099; no guard, because the switch exists to fix a desktop WinGnome
    itself may have broken.

---

## Addendum 2026-10-09: KI-102 — the mid-run work-area fight and the terminal give-up

Status: **Implemented** — f402e2e (fight detector), 0300eed (cool-down), 73a3c5d (app wiring), d9f85da + f9b92a6
(Part B diagnostics); live-verified 2026-10-10 except criteria 12-burst/13/14, which ride on the everyday build.
One branch (`ki-102-work-area-fight`) contains Part A (the KI-102 fix) and Part B (bundled diagnostic logging).
Evidence read for this plan:
`C:\Users\samhe\AppData\Local\Temp\opencode\fight-evidence\wingnome-before-restart.log` and
`workareas-before-restart.state` (the fight, ending at the two give-up WARNs), and
`C:\Users\samhe\AppData\Local\Temp\opencode\everyday-log4\wingnome.log` (the same run through the 21:16:09 graceful
quit — "Dropped 2 work area record(s) …" — and the 21:16:40 restart, whose fallback fixed both strips ~5 s after
start; KI-102's "~4 s" is a hair optimistic, measured 5.1 s and 5.9 s after the start line).

### Problem

KI-102 (S3), in short: 15 minutes into an everyday run, Explorer recomputed both monitors' work areas **without the
strips of WinGnome's still-registered, still-granted AppBars**, and the outcome-B fallback fought it — 18 direct
`SPI_SETWORKAREA` writes over ~7 minutes, each provoking another reset — until the per-monitor `WorkAreaBudget`
refused three attempts in one episode and both bars hit `StripRecovery`'s give-up, which is terminal for the run
(`AppBar._gaveUp` and `StripRecovery._attempts` clear only on `Undock()` or a strip genuinely reserved again). Two
defects, as KI-102 (a)/(b):

- **(a) The fallback feeds the fight.** Every shrink broadcasts (`SetWorkArea(shrunk, broadcast: true)` →
  `SPIF_SENDCHANGE`), and Explorer — which in this state recomputes work areas *without* our granted strips — appears
  to recompute on our broadcast: of the 18 fight writes, 12 were reset within ~1–14 s and four more within 69–83 s;
  only the first pair of the fight (20:50:42/43, before any fight write could have provoked anything) held ~3 minutes.
- **(b) The give-up never re-arms.** A no-op display pass runs `CheckStrip` but `Update` keeps returning `GiveUp`;
  the log line "giving up until the next display change" (`Interop/AppBar.cs`, the `default:` branch of `CheckStrip`)
  and the `StripRecoveryKind.GiveUp` XML doc ("the next display change or Explorer restart starts afresh",
  `WinGnome.Core/Shell/AppBarReservation.cs`) are both wrong and are corrected by this branch whatever else changes.

Two readings of the evidence sharpen KI-102 without contradicting it; the design below relies on both:

1. **The 3-attempt cap never bounded the fight — the budget did.** A successful shrink makes the next check see the
   strip reserved, and `StripRecovery.Update(reserved: true)` calls `Reset()` (locked in by the existing test
   `Recovery_StripReservedAgain_StartsAfreshNextTime`). The fight's nine writes per monitor were nine *fresh*
   episodes with `_attempts` back at 0; `MaxAttempts` fired only once budget refusals made three attempts of one
   episode fail in a row (20:57:24 → 20:57:53). Left to itself — Explorer resetting slower than the budget fills —
   the fight would have run indefinitely at up to `WorkAreaBudget.MaxApplications` writes per monitor per minute,
   all broadcast. Any fix that only re-arms the cap therefore makes things *worse* unless the write rate itself is
   bounded and the feedback loop is broken.
2. **The fight's first trigger was an external work-area write, not (directly) a display event.** The 20:50:41.006
   pass logged "Displays re-checked: removed 0, changed 0, added 0" — `DisplayLayoutService.OnTick` only logs an
   *empty* diff when the pass was **forced**, and the mid-run sources of `Invalidate(force: true)` are the bars'
   `WM_SETTINGCHANGE(SPI_SETWORKAREA)` handlers (`TopBarInstance.WndProc`, `DockInstance.WndProc` — the dock was
   Intellihide, so only the bars), a detach (logs "its monitor … is gone or changed; undocking" first — absent) and
   an Explorer restart (logs "Explorer restarted" — absent). So at ~20:50:40.7 *some process — almost certainly
   Explorer — wrote a work area with `SPIF_SENDCHANGE`*. A plain `WM_DISPLAYCHANGE` (display power event) with no
   resulting diff would have produced **no log line at all**. KI-102's "probably a display power event, unproven"
   stands as the upstream cause; the proximate cause was Explorer's own broadcast write. Consequence for the fix:
   dropping our broadcast (direction 1) can break the *sustaining* loop but cannot prevent the *first* loss — a
   re-arm path (direction 3) is needed regardless.

What must not change (proven by the same episode and by KI-099's live verification): `WorkAreaBudget`
(3 applications / 60 s / monitor), the 3-attempt schedule inside an episode (1.5 s / 5 s / 20 s), the
one-record-per-(owner, monitor) rule (`WorkAreaLedger`), the graceful-quit "drop records that no longer describe a
live work area" behaviour (`WorkAreaRecovery.Plan`'s already-given-back branch), and "no record, no shrink".

### Fix directions — evaluation

**1. Shrink without `SPIF_SENDCHANGE` — adopt with modification (fight-gated hybrid).**
Who consumes our broadcast today:

- *Our own bars*: `TopBarInstance.WndProc`/`DockInstance.WndProc` turn `WM_SETTINGCHANGE(SPI_SETWORKAREA)` into a
  forced display pass, which is how a shrink is confirmed "reserved again" within ~1 ms in the evidence log. Without
  it, confirmation rides the triggers that already exist: the bar's one-shot `StripRecovery` timer at the next
  attempt slot (≤ 5 s / ≤ 20 s later — `CheckStrip` already calls `StartRecoveryTimer(step.DueMs)` after a `Shrink`
  step), and Explorer's *own* resets (each of which broadcasts, in the fight state and in normal operation).
  `IsStripReserved()` reads `GetMonitorInfo` fresh; broadcasts are irrelevant to correctness.
- *Maximised-window relayout* — the visible consumer. `DefWindowProc` re-lays maximised windows out on
  `WM_SETTINGCHANGE`; a silent write leaves **already**-maximised windows at their old size (spanning our strip)
  until the next work-area broadcast by anyone, or a re-maximise. *Newly* maximised windows read the live work area
  and are correct immediately. This is the cost of silence; it is bounded and it beats the alternative (today the
  fight ends with the work area full for the rest of the run and, per KI-102, maximised windows covering the bars —
  with a full-bounds work area every maximised window covers the whole monitor, which also feeds Explorer's
  `ABN_FULLSCREENAPP` and `TopBarInstance.OnFullScreenChanged`'s hide).
- *Explorer* — the consumer we must starve in the fight state: the hypothesis (KI-102 (a), supported by the 1–14 s
  reset-after-write cadence) is that Explorer's handling of the broadcast re-runs its taskbar layout pass, which in
  this state drops our granted strips.

Pure "never broadcast" is rejected: it would degrade the *normal* case (the start-up deferral of KI-099 B6 and the
unplug case), where one broadcast write finishes Explorer's job and immediately relayouts maximised windows — that
case measured exactly one write per monitor with no repeats, so its broadcast provably does not sustain any loop.
"Broadcast only on the first shrink of an episode" is also rejected: episodes restart on every *successful* shrink
(reading 1 above), so in a fight every cycle's first shrink would broadcast — the loop survives. The chosen hybrid
keys silence to a per-monitor **fight detector** (below): the first three applications on a monitor inside any
10-minute window broadcast as today; from the fourth onwards, while applications keep arriving, writes are silent;
when the window slides clear (10 quiet minutes), the next application broadcasts again. "One broadcast at the end"
was considered and rejected: noticing "the end" needs a new transition timer (idle-footprint rule), and a manual
`WM_SETTINGCHANGE` (`SystemBroadcast` pattern) would re-provoke exactly the Explorer recompute the silence exists to
starve; oversized maximised windows heal on the next natural broadcast or a re-maximise.

**2. Nudge Explorer after a shrink (ABM_SETPOS re-assert or the janitor's 1×1 ABM_NEW+ABM_REMOVE) — reject for the
fight path.** The evidence already in this spec is against both: "a SETPOS of the unchanged rectangle did not bring
the strip back" (implementation note, third live run), and re-registering "during the deferral … may have restarted
that deferral" (KI-099) — outcome-B rule 5 bans the re-register fallback for the same reason. The janitor nudge
(`AppBarJanitor.Nudge`) and `Reregister()` are AppBar traffic that makes Explorer recompute *and broadcast*; in the
fight state Explorer's recompute is precisely the operation that drops granted strips, so a nudge is far more
likely to trigger the next reset than to include our strip. The steady-state observation that registering again
restored strips "at once" (implementation note, second live run) was after an *external* `SPI_SETWORKAREA` reset
with Explorer healthy — the mid-run-fight state is neither, exactly as KI-102 says. Because the hypothesis cannot be
settled from the armchair: the live-test plan includes an **optional exploratory step** (run a nudge from a scratch
tool during an artificial fight and record whether strips come back or reset faster; results go into KI-102, not
into shipped code).

**3. Re-arm `StripRecovery` — adopt, as a cool-down, not per display pass.** Re-arming on *every* display pass
(including no-op ones) is rejected: in a fight, forced passes fire on every Explorer broadcast (the evidence log
shows one per reset, seconds apart), so per-pass re-arming restarts episodes every few seconds and recreates the
unbounded fight at the budget's full 180 writes/h/monitor. Chosen design: `StripRecovery`'s give-up becomes a
**5-minute cool-down** (`ReArmDelayMs = 300_000`): the first `GiveUp` after exhaustion carries
`DueMs = now + ReArmDelayMs`; the bar points its *existing* one-shot `_recoveryTimer` at that instant
(`CheckStrip`'s `default:` branch calls `StartRecoveryTimer(step.DueMs)` instead of `StopRecoveryTimer()`); when a
check runs at or after the due time (the timer, or any notification/pass that happens to arrive first), `Update`
clears `_attempts` internally and returns `Wait(now + FirstActionMs)` — a fresh, fully bounded episode. No new
timer type, no polling: the timer runs only while a bar is given up *and* its strip is missing, and `Reset()`
(undock, reserved) cancels it exactly as today. A *real* display change still re-arms immediately, because it
re-docks the bar (`SurfacePlan.Reconcile` → `Undock()` → `Reset()`) — that part of the old wording was only ever
true via the re-dock, and the corrected docs will say so. Worst-case rate with cool-down, **per fighting bar**: one
cycle = 5 min cool-down + ~46.5 s episode = 346.5 s and lands ≤ 3 writes → 3 / 346.5 s ≈ **31.2 writes/h per bar**.
One monitor with two reserving bars (top bar + always-visible dock) whose cycles drift apart therefore sits at
≤ ~64 writes/h/monitor — still under `WorkAreaBudget`'s 180/h/monitor, which remains the hard bound and trims
aligned cycles (both bars' six attempts inside ~27 s → at most three pass `TrySpend`). Broadcasts stay
≤ 18/h/monitor (three per 10-minute fight window) because the fight detector is per monitor, not per bar. Today, by
contrast: ≤ 180 broadcast writes/h/monitor until the terminal give-up. Interaction
with the cap: the 3-attempt cap stays (direction 4) and now bounds work *per cool-down cycle*; re-arming cannot
recreate the unbounded fight because both the budget and the detector's silence are per write, not per episode.

**4. Give up sooner — reject "sooner", keep 3; fix the WARN.** With the cool-down the cap is less load-bearing
(reading 1: the budget is the real bound), but 3 attempts remain right: attempt 1 covers the normal deferral
(KI-099 B6: one write sufficed), attempts 2–3 cover Explorer applying late or a second reset inside one episode,
and one episode's three attempts fit exactly inside one `WorkAreaBudget` window — a bar that cannot land three
writes in a minute is definitionally in a fight, and the cool-down (not more attempts) is the correct response.
Giving up after fewer attempts would abandon the slow-Explorer cases the schedule exists for. The give-up stays
`Log.Warn` (WinGnome has no user-facing notification surface; spec 0017's OSD is not implemented — logged as an
open question) with corrected, actionable wording:
`"AppBar 0x…: Explorer still hasn't reserved the strip … after 3 attempts; not acting again for 5 minutes (a re-dock or Explorer applying the strip ends the cool-down at once)"`.

**5. TbExp / spec 0010 option 1 — not in this branch.** The hide-order experiment (hide the taskbar windows only
after Explorer applied auto-hide) needs the user physically at the machine and attacks the start-up deferral, which
the fallback already survives; it cannot fix the mid-run recompute or the unplugged-monitor case (this spec says so
in the design note's option list). This branch must not prejudice it:

- Do not touch `TaskbarController.Hide`/`SetAutoHideOnly`'s order or timing (option 1 is exactly a re-ordering
  there). Part B's change to `ShowWindows`/`HideWindows` (return the count of windows acted on) is additive and
  leaves `Hide()`'s `SetAutoHide(true); HideWindows();` sequence intact.
- Do not tune `WorkAreaFightDetector` or `ReArmDelayMs` to the start-up timeline: both key on *repeated*
  applications, so if option 1 later makes Explorer apply strips promptly, the fallback fires once (broadcast, as
  B6 measured) and the new machinery stays inert.
- Do not remove or bypass the AppBar mechanism, and do not delete or modify `tools/TbExp`.

**Fight detection: yes, a distinct per-monitor state — but the strategy switch is "write silently", not "stop
writing".** Stopping writes in a fight leaves the work area full for as long as Explorer keeps resetting: every
maximised window then spans the whole monitor, covers the bar's strip and (through `ABN_FULLSCREENAPP` →
`TopBarInstance.OnFullScreenChanged`) hides the bar itself — KI-102's end state, now by design. Silent writing keeps
the work area correct between Explorer's own events (which, with the broadcast loop starved, should return to their
natural minutes-apart cadence — the 20:50:43 → 20:53:49 gap in the evidence), keeps new maximisations correct, and
costs only the immediate relayout of already-maximised windows. The state lives in Core per the project's
logic-in-Core rule: `WorkAreaFightDetector` (new file `src/WinGnome.Core/Shell/WorkAreaFightDetector.cs`), a pure
sliding-window counter with an injected clock, held by `WorkAreaController` in a `Dictionary<string,
WorkAreaFightDetector>` next to `Budgets` (per monitor key, because the fight is a property of a monitor's work
area — two bars on one monitor share it, exactly like the budget).

### The chosen design (Part A)

**Core (`src/WinGnome.Core/Shell/`, all tested):**

- `WorkAreaFightDetector` (new): `const int ApplicationLimit = 3; const long WindowMs = 600_000;` —
  `bool IsFighting(long nowMs)` prunes applications older than the window (edge semantics identical to
  `WorkAreaBudget.TrySpend`: an application leaves it exactly `WindowMs` after it was made) and returns whether
  `ApplicationLimit` or more remain; `bool Record(long nowMs)` appends one shrink application and returns the new
  state. In-memory only (not persisted): a restart starts unfought, which is what B6 needs (its single write must
  broadcast). Counts *applications* (real writes), not loss episodes: a write is what broadcasts, and episodes
  whose writes the budget refused did not provoke anything; counting writes needs no new event plumbing from
  `AppBar` into the controller.
- `StripRecovery` (modified, same class and call sites): new `const long ReArmDelayMs = 300_000`. The exhaustion
  branch of `Update` stamps `_reArmAtMs = nowMs + ReArmDelayMs` on the first `GiveUp` it returns (later `GiveUp`s
  return the *same* due time — the cool-down is not a sliding one), returns `GiveUp` with `DueMs = _reArmAtMs`
  while `nowMs < _reArmAtMs`, and at/after it clears `_attempts`/`_reArmAtMs`, sets
  `_nextAttemptMs = nowMs + FirstActionMs` and returns `Wait(_nextAttemptMs)` — a fresh episode. `Reset()` clears
  `_reArmAtMs` too. XML docs updated: `StripRecoveryKind.GiveUp` ("the attempts are used up; the bar stops acting
  until the cool-down (`StripRecovery.ReArmDelayMs`) has passed, after which the next check — the bar's own one-shot
  timer, a shell notification or a display pass — starts a fresh episode; an undock, a re-dock or the strip being
  reserved ends it sooner") and the class summary (drop "gives up until it is docked afresh"). The cool-down applies
  equally to safe mode's `Reregister` cycle (bounded, writes nothing).
- Unchanged: `WorkAreaBudget`, `WorkAreaFallback.Shrink`, `WorkAreaLedger`, `WorkAreaRecord`, `WorkAreaState`,
  `WorkAreaRecovery` (files untouched; their suites must stay green).

**App:**

- `Services/WorkAreaController.cs`: a `Fighters` dictionary beside `Budgets` (cleared in `Initialize`, pruned in
  `Prune` like the budgets). In `TryShrink`, after `TrySpend` succeeds and before the write:
  `var fighting = Fight(key).IsFighting(now)` (one `Environment.TickCount64` read for the whole call); the write
  becomes `SetWorkArea(shrunk, broadcast: !fighting)`; on success `Fight(key).Record(now)`, and on a
  false→true transition one `Log.Warn`:
  `"{key}: Explorer keeps resetting this work area (3 direct sets in 10 minutes); setting it without a broadcast from the next write on — our own WM_SETTINGCHANGE may be what provokes the resets (KI-102)"`
  — "from the next write on", not "from now on": the write that trips the detector has itself just broadcast, and a
  replayed log must not contradict the behaviour.
  The applied line gains `" (without a broadcast)"` when silent, so every write's mode is greppable. A failed
  `SPI_SETWORKAREA` is not recorded (it never broadcast); detector state is deliberately *not* rolled back with the
  record list — over-counting only makes later writes more silent, and the window bounds it. Restore paths keep
  broadcasting exactly as today (`Release` → `Apply(broadcast: true, nudge: true)`, `RecoverFromMarker` →
  `Apply(broadcast: true, …)`, `RepairWithoutRecords` → `broadcast: true`); giving a work area back is not fight
  traffic. `ReleaseAll()` (crash path) stays broadcast-free by design. Class doc updated ("Bounded twice" → three
  bounds: budget, StripRecovery with its cool-down, fight detector).
- `Interop/AppBar.cs`: `CheckStrip`'s `default:` branch keeps the log-once `_gaveUp` flag, logs the corrected WARN
  (direction 4 wording) and calls `StartRecoveryTimer(step.DueMs)` — the re-arm rides the existing one-shot timer.
  The branch forwards `step.DueMs` **verbatim** (it never recomputes a deadline), and re-pointing the timer is
  idempotent: a forced-pass storm during a fight calls `Update` repeatedly, and every pre-expiry `GiveUp` returns
  the *same* stamped `_reArmAtMs` (guaranteed by Core, pinned by `Recovery_GiveUp_DoesNotSlideTheCoolDown`), so
  re-scheduling shortens the remaining interval but can never push the cool-down deadline later.
  In the `Wait` branch, when `_gaveUp` was set, log once
  `"AppBar 0x…: cool-down over; checking the strip … again"` and clear the flag. `EnsureReserved`'s XML doc:
  "… at most three times, 5 s and 20 s apart, then a five-minute cool-down after which the checks resume".
  Small diagnostic (Part B, same file, same owner): `WndProc` passes the notification name into
  `OnPositionChanged`/`CheckStrip`, so the trigger reads `ABN_POSCHANGED` / `ABN_STATECHANGE` instead of the
  ambiguous "a shell notification" — the evidence log cannot say which notification accompanied Explorer's
  recomputes, and the next field occurrence should.
- No changes to `TopBarInstance`, `DockInstance`, `DisplayLayoutService`, `AppBarJanitor`, `TaskbarController`'s
  hide/restore logic, or the marker format.

**Safety and recovery (Part A):**

| Path | Effect of this branch |
|---|---|
| Recorded/restored state | **Nothing changes.** `workareas.state` format, "no record, no shrink", `WorkAreaLedger` replacement, `WorkAreaRecovery.Plan` unwind, the drop-without-write branches: all untouched. A silent write records identically (the broadcast flag is not part of a record). |
| Normal exit / crash / force-kill / next start / `--restore-taskbar` | Unchanged (`Release` broadcasts as today; `ReleaseAll` stays broadcast-free; `RecoverFromMarker` unchanged). |
| `--safe` / `--selftest` | Unchanged: `CanShrink` false ⇒ `Reregister` steps only, the detector is never consulted (no writes), the self-test's work-area equality check is indifferent to broadcast flags. |
| Worst case, sustained fight | Hard bound unchanged: 3 writes/min/monitor (`WorkAreaBudget`, 180/h). Effective sustained bound: **≤ ~31 writes/h per fighting bar** (≤ 3 writes per 346.5-s cool-down cycle), so ≤ ~64/h/monitor with two reserving bars (top bar + always-visible dock) whose cycles drift apart, and less when the budget trims aligned cycles; broadcasts ≤ 18/h/monitor (the fight detector is per monitor). Today: ≤ 180/h/monitor, *all* broadcast, until the terminal give-up. One small marker rewrite per write, as today. |
| New degradation | A silent write does not relayout already-maximised windows until the next broadcast (ours after 10 quiet minutes, Explorer's, or a re-maximise). Logged in KNOWN_ISSUES with the fix. |
| New degradation (safe mode) | A long-running `--safe` instance whose strip stays missing now re-registers every cool-down cycle instead of stopping forever: 3 `ABM_REMOVE`+`ABM_NEW` cycles per bar per ~5.8 min. Bounded, writes no system state; accepted. |

**Footprint (Part A):** no new polling, no new timer *type*. At idle (strips reserved) nothing runs, exactly as
today: the detector is O(1) queue work inside the existing `TryShrink` lock, and the re-arm timer exists only
while a bar is given up with its strip missing (one `DispatcherTimer` per bar, one tick per cool-down). Idle
CPU/memory must measure unchanged (QA 6).

### Part B — bundled diagnostic logging

All lines are event-driven (zero idle cost); anything that can repeat in a burst goes through `ThrottledLog`,
which **moves** from `Features/WindowButtons/` to `Infrastructure/` (features never reference each other) and gains
an `Info` twin of its `Warn` (same 1/minute/key + suppressed-count mechanics; still UI-thread-only, so the tray
host's line below uses plain `Log`). Its existing WindowButtons call sites (14 `ThrottledLog.Warn` calls across six
files) only change `using`s.

**B1. `TaskbarFeature` re-hide correlation** (the user sees random taskbar flashes; today only the backoff WARN is
logged):

- `ScheduleRehide` grows a source-hwnd parameter; when it actually starts the idle timer from
  `OnRawWindowEvent` (i.e. `EVENT_OBJECT_SHOW` on a taskbar window while hidden and not peeking):
  `ThrottledLog.Info("taskbar-rehide-scheduled", $"Explorer showed taskbar window 0x{hwnd:X}; re-hiding in {delay.TotalMilliseconds:0} ms")`.
  The `OnTaskbarCreated` path passes no hwnd and keeps its existing line. Throttled because `ScheduleRehide` can
  re-run every 250 ms in a burst; the suppressed count preserves the correlation.
- `OnRehideTimer`'s execution branch: `TaskbarController.HideWindows()` returns the number of windows it hid
  (additive signature change; `Hide()`/`RestoreFromMarker`/`EndPeek` ignore or use it), logged as
  `ThrottledLog.Info("taskbar-rehide-executed", $"Re-hid {n} taskbar window(s) Explorer had shown")`.
- Backoff transitions: the entry WARN stays verbatim ("Explorer keeps showing the taskbar again; re-hiding it less
  often"); the exit — today a silent `_backoffLogged = false` in `NextRehideDelay` — logs
  `Log.Info("Explorer stopped re-showing the taskbar; re-hiding at the normal rate again")`.

**B2. The peek** (`TopBarActions.ShowSystemTray` → `ShellCommands.PeekTaskbar` → `TaskbarFeature.OnPeekRequested`;
reportedly the button does nothing, and there is no success log at all today):

- `TopBarActions.Execute`, `case ShowSystemTray`: `Log.Info("Quick settings 'System tray': requesting a taskbar peek")` — distinguishes "the command never fired" from "the feature ignored it".
- `OnPeekRequested` early return when `!IsHidden`: `Log.Info($"Taskbar peek ignored: the taskbar is not hidden (mode {_mode})")`. (The `!StillOwnsTaskbar` return is already covered by `StillOwnsTaskbar`'s own line.)
- After `ShowWindows`: `Log.Info($"Taskbar peek: showed {n} taskbar window(s); Explorer's tray window is 0x{primary:X}")`; `primary == 0` adds `Log.Warn("Taskbar peek: Explorer's taskbar window was not found; the peek may stay invisible")`.
- After `WindowActivator.Activate(primary)` (which keeps its own failure WARN): `Log.Info($"Taskbar peek: foreground window is now 0x{fg:X}{(fg == primary ? " (the taskbar)" : "")}")`.
- `OnPeekTimer` cursor-over re-check: `ThrottledLog.Info("taskbar-peek-recheck", "Taskbar peek: the cursor is over the taskbar; re-checking in 1 s")`.
- `EndPeek` grows a reason string; one line whenever a peek really ends: `Log.Info($"Taskbar peek ended ({reason})")` plus `"; re-hid {n} taskbar window(s)"` when it re-hides. Reasons: `"timeout"`, `"foreground changed to 0x…"`, `"reveal failed"`, `"taskbar mode changed"`, `"Explorer recreated the taskbar"`.

**B3. `TrayModel` delivery** (for the Windows Security icon investigation: a version-4 icon whose `NIM_SETVERSION`
we missed is encoded in legacy format by `TrayCallback.Encode` and silently ignored by the app):

- `Deliver`, for click-class actions only (gate: `TrayCallback.MayTakeForeground(action)` — Enter/Hover/Leave stay
  silent, so hovering the tray costs nothing), one line before the send loop:
  `Log.Info($"Tray {action} on \"{tip or "(no tooltip)"}\" (owner 0x{owner:X}, id {state.Id.Id}, guid {state.Id.ItemGuid or "none"}, version {state.Version}, callback 0x{state.CallbackMessage:X}): sending [{notification list, hex}]; anchor {x},{y}, icon {l,t,r,b}, bar {l,t,r,b}")`.
  The tooltip is arbitrary app-supplied text (and potential PII): truncate it to 64 characters with an ellipsis
  before it reaches the line. A missed SETVERSION shows up as `version 0` on an icon known to be v4 — the exact
  evidence needed. The existing delivery-failure WARN stays.
- Silent no-op paths, click-class actions only: `state.CallbackMessage == 0` →
  `Log.Info($"Tray {action} on \"{tip}\" (owner 0x…, id …) did nothing: the icon registered no callback message")`;
  empty notification set (defensive; unreachable for the defined button actions) → `… produced no notifications;
  nothing sent`; and in `Send`'s early return when `_host is null` →
  `Log.Info($"Tray {action} did nothing: the tray host is not running")` (this one throttled, key `"tray-nohost"`).
- `Services/Tray/TrayHost.cs`, `OnNotifyIcon`: when `command.Message == SetVersion` and the registry accepted it,
  `Log.Info($"Tray icon (owner 0x{command.Owner:X}, id {command.Id}) set callback version {command.Version}")` —
  plain `Log` (host thread, thread-safe), once per icon registration, so a *received* SETVERSION is as greppable as
  a missed one.

**B4.** The `AppBar` notification-name trigger (above, in the Part A app section — same file, same work package).

### Acceptance criteria

T = Core unit test, M = mutation check (see table below), L = live check in the session described next,
R = code review. Statuses recorded 2026-10-10 on branch build f9b92a6 (the measured numbers are in KI-102's
verification record):

1. T/M `WorkAreaFightDetector`: first two applications inside the window keep `IsFighting` false; the third makes
   it true; an application exactly at `WindowMs` leaves the window; `IsFighting` prunes without recording.
   **PASS** (mutations run; see the correction under the table).
2. T/M `StripRecovery` cool-down: `GiveUp` carries `DueMs = firstGiveUp + ReArmDelayMs` and does not re-stamp;
   before the due time `Update` keeps returning `GiveUp`; at/after it a fresh episode starts (`Wait(due+1500)`,
   then `Shrink` at +1.5/+5/+20 s, then `GiveUp` again); `reserved` or `Reset()` during the cool-down ends it
   immediately. Criterion 2's first test is the KI-102(b) regression test: it must be run against the *unfixed*
   Core and fail (today `Update` returns `GiveUp` forever). **PASS** (seen red against the unfixed Core; every
   mutation run — rows 8/9 were crossed as drafted, both directions proven red).
3. T The existing `Recovery_StillMissing_SetsTheWorkAreaThreeTimes_ThenGivesUp` is updated for the new `DueMs`
   semantics, and `WorkAreaBudget`/`WorkAreaLedger`/`WorkAreaRecovery`/`WorkAreaState`/`WorkAreaFallback` suites
   pass with those five source files untouched (the must-not-change list in the Problem section, enforced by the
   diff). **PASS** (suites green, five files untouched).
4. L Normal case unchanged (B6 regression): non-safe start with the taskbar hidden → one *broadcast* write per
   monitor within ~4 s, the immediate "Displays re-checked" pass and "reserved again" lines as in KI-099's B6, no
   fight WARN, no repeats over 5 idle minutes. **PASS** (twice — 01:55 and 02:25 starts: one broadcast write per
   monitor within ~4.2 s, zero warnings, marker with exactly two records, 5 idle minutes clean).
5. L Silent-write switch: four artificial resets of DISPLAY2 inside 10 minutes. Writes 1–3 broadcast (each followed
   by the forced re-check pass within ~0.5 s), and write 3 logs the transition WARN after it; write 4 is silent —
   the applied line's "(without a broadcast)" suffix, **no** "Displays re-checked" pass within 2 s of the write, and
   `tools/Get-WorkAreas.ps1` still showing the shrunk work area (the write lands; only the broadcast is gone).
   **PASS** (transition WARN 00:01:34.041 after the 3rd application; write 4 at 00:02:14.872 with " (without a
   broadcast)", no re-check pass within 2 s after it, the recovery timer confirming +5.004 s later, work area
   still shrunk).
6. L Cool-down re-arm: four resets inside 60 s (spaced ~10 s, so each shrink lands before the next reset) exhaust
   the budget → three refused attempts → the give-up WARN in its new wording → no writes for ~5 minutes → the
   re-arm INFO line, a fresh `Wait`→`Shrink` episode, and the strip restored with no external help. Timing note
   for the tester: the give-up WARN lands one `ThirdActionMs` (20 s) after the third refused attempt — ~46.5 s
   after the episode's first loss detection — because the exhaustion check rides the next scheduled slot; that
   tail is the schedule, not a failure. **PASS** (budget refusals ×3 at +1.499/+5.003/+19.996 s; the give-up WARN
   in the new wording at 00:06:05.504, +46.496 s after first detection; 5:00.003 of silence; the re-arm INFO at
   00:11:05.507; the fresh episode wrote silently +1.499 s later and "reserved again" +4.999 s later — the strip
   restored with no external help).
7. L Fight-window slide: ≥ 10 quiet minutes after the last application, a fresh reset produces a broadcast write
   again (re-check pass visible) with no transition WARN until three more applications accumulate. (Long pole; may
   run unattended in the session.) **PASS**.
8. L Marker discipline through a fight: after ≥ 6 resets, `workareas.state` holds exactly one record per
   (owner, monitor) with `Applied` equal to the live work area; graceful quit gives back or drops per
   `WorkAreaRecovery.Plan` and deletes the marker; `taskkill /f` + `--restore-taskbar` from the same profile
   restores; work areas and taskbar end at the documented baseline (RUNBOOK table: taskbar visible D1
   `0,0,2560,1540`, D2 `-447,-1440,2993,-48`). **PASS** (exactly one record, `Owner` 0x4E09D2; `taskkill /f` left
   the marker byte-identical (md5 equal); `--restore-taskbar` dropped it and deleted the marker, exit 0; the final
   capture equalled the exact baseline).
9. L `--selftest --safe` exits 0; a `--safe` run never logs a shrink (unchanged). **PASS** (exit 0, no marker).
10. L Footprint: 20 idle minutes with strips reserved → no new log lines, no timer activity, CPU/memory within
    noise of the pre-branch build (AGENTS.md QA 6). **PASS** (13+ idle minutes, zero new log lines — 64 before
    and after; profile B added 5 more with zero B1–B3 lines).
11. R Wording corrections landed: `AppBar.CheckStrip` give-up message, `StripRecoveryKind.GiveUp` doc,
    `StripRecovery` class doc, `EnsureReserved` doc, `WorkAreaController` class doc; no source comment still claims
    the give-up lasts until "the next display change". **PASS** (review confirmed no such wording remains).
12. L Re-hide correlation (profile B): a tool shows `Shell_TrayWnd` → scheduled line (hwnd, delay) then executed
    line (count); five shows inside 10 s → the backoff WARN, 5 s delays, and the exit INFO once quiet; during a
    sustained burst both throttled keys log ≤ 1 line/minute with suppressed counts. **PARTIAL** — the
    scheduled/executed chain fired live on real Explorer-initiated shows (two startups: "Explorer showed taskbar
    window 0xB708A2; re-hiding in 250 ms" → "Re-hid 2 taskbar window(s) Explorer had shown" ~258 ms later); the
    burst/backoff/throttle path was not exercised — a scripted `ShowWindow` on a locked workstation produced no
    `EVENT_OBJECT_SHOW` at all. Field data rides on the everyday build.
13. L Peek chain (profile B, user clicks the "System tray" tile): request → revealed (count + tray hwnd) →
    foreground → ended (reason, re-hide count) lines all present; with the taskbar not hidden (profile A) the
    ignored line appears instead. The reported "does nothing" must be attributable to exactly one logged branch.
    **DEFERRED** — needs a real click and the workstation was locked (`SendInput` goes to the secure desktop);
    rides on the everyday build.
14. L Tray clicks (profile B, user clicks): a v4 icon (e.g. OneDrive) and a legacy icon each produce one delivery
    line with the correct `version`/`callback`/notification set/anchor; hovering produces no lines; icons that
    send `NIM_SETVERSION` logged it at registration. For the Windows Security icon: its click line's `version`
    field is the investigation's answer (0 ⇒ we missed its SETVERSION). **DEFERRED** — as 13; the Windows
    Security click line's `version` field will be answered on the everyday build.
15. L Idle silence: 5 idle minutes with the taskbar hidden produce no B1–B3 lines at all. **PASS** (profile B
    5 minutes and profile A 13 minutes, zero B1–B3 lines).

### Live-test plan (no everyday instance is touched)

Session scratch: `%TEMP%\opencode\ki102-session\`, modelled on `mm-session`: `build\` (the branch published with
`dotnet publish src/WinGnome -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o …\build`),
`profile-a\` and `profile-b\`, and the copied tools `reset-upper.cmd` + `reset-upper-workarea.ps1` (resets the one
non-primary monitor with `bounds.Top < 0` — DISPLAY2, `-447,-1440,2993,0` — to its full bounds with
`SPI_SETWORKAREA`+`SPIF_SENDCHANGE`; refuses primary monitors and any monitor with `bounds.Top >= 0`; exits 1 on
refusal), `capture-workareas.cmd` (`tools/Get-WorkAreas.ps1`), `start-/stop-/kill-test.cmd`,
`restore-taskbar.cmd`, and one new `show-taskbar.cmd` (PowerShell `FindWindowEx("Shell_TrayWnd")` + `ShowWindow
SW_SHOW` — fires the same `EVENT_OBJECT_SHOW` Explorer's flashes do, for criterion 12). Everything the agent runs
is launched through `explorer.exe <path>` so it acts on the real session (KI-010); the everyday log/settings are
only read through Explorer-launched copies.

- **Profile A** (criteria 5–8, 10, and the artificial fight for exploratory step (i)): non-safe, taskbar
  untouched — `{"General":{"HideWindowsTaskbar":false,"NativeTaskbarAutoHide":false}}`, `TopBar` on with
  `Monitors:All`, `Margin:0`, `CornerRadius:0`, `Height:32`, `Dock:{"Enabled":false}`,
  `WindowButtons:{"Enabled":false}`, `ShowTrayIcons:false`. With the taskbar visible Explorer applies granted
  strips promptly (~0.3 s, spec 0010's steady-state measurement), so no start-up fallback write muddies the
  artificial resets — the fallback is triggered *only* by `reset-upper.cmd`, exactly as in KI-099's verified
  recovery-path session. Resets are spaced ~10–60 s apart so each shrink lands before the next reset.
- **Profile B** (criteria 4, 12–15, and exploratory step (ii)): same but `HideWindowsTaskbar:true` and
  `ShowTrayIcons:true` (start-up fallback, re-hide, peek, tray clicks).
- Criterion 9 (`--selftest --safe`) needs no profile: the standard self-test command with its own scratch
  `--settings-dir`, agent-driven.
- **User-required steps** (runbook session, user at the machine): quit the everyday WinGnome (quick settings →
  Quit; the agent verifies with `tasklist` and records the baseline capture) and restart it after the session;
  click the peek tile and the tray icons (criteria 13–14); the optional exploratory steps below. Everything else —
  launching/stopping instances, resets, captures, log reads, the 5-minute cool-down wait — is agent-driven.
- **Two-instance rule:** profile-B runs and any profile-A run that writes work areas happen only while the
  everyday instance is quit; two instances would each react to the resets (the everyday build still broadcasts),
  invalidating criteria 5–7. Window buttons stay off in both profiles (KI-042).
- **Session appearance (say so before starting):** every step runs with the everyday instance quit, so for the
  whole session the Windows taskbar is visible and there is no GNOME shell — the desktop looks "broken" mid-session
  and that is expected, not a test failure. The session's last step restarts the everyday instance through
  `explorer.exe` (outside any sandbox, KI-010).
- **Optional exploratory steps** (evidence for KI-102, not pass/fail): (i) during an artificial fight, run a
  janitor-style 1×1 `ABM_NEW`+`ABM_REMOVE` nudge from a scratch tool and record whether strips return or reset
  faster (direction 2); (ii) [PHYSICAL] with profile B running and both strips reserved, the user powers
  DISPLAY2 off and on (or unplugs/replants) to try to reproduce the natural trigger — if Explorer recomputes
  without the strips, the log now shows whether the silence + cool-down design ends the loop in the field. Results
  go into KI-102 either way; a non-reproduction proves nothing and blocks nothing.

### Tests and mutations (every new Core behaviour is seen failing)

| # | Test (file) | Mutation that must turn it red |
|---|---|---|
| 1 | `Fight_TwoApplicationsWithinTheWindow_DoNotFight` (`WorkAreaFightDetectorTests.cs`, new) | `ApplicationLimit` 3→2 |
| 2 | `Fight_ThirdApplicationWithinTheWindow_Fights` | `ApplicationLimit` 3→4 |
| 3 | `Fight_ApplicationExactlyAtTheWindowEdge_LeavesTheWindow` (records at t, t+1 s, t+600 000 → not fighting; t+599 999 → fighting) | prune `>=`→`>` |
| 4 | `Fight_ApplicationAfterTheWindowSlides_DropsTheOldest` (t, t+60 s, then t+600 001 → not fighting) | delete the prune loop |
| 5 | `Fight_IsFightingAfterTheWindow_IsFalseWithoutRecording` | `IsFighting` skips pruning |
| 6 | `Recovery_GiveUp_CarriesTheReArmDueTime` (`AppBarReservationTests.cs`) — asserts every post-exhaustion `GiveUp` step carries the stamped non-zero deadline (never `DueMs = 0`), which is what `CheckStrip` forwards verbatim | `GiveUp` returns `DueMs = 0` |
| 7 | `Recovery_GiveUp_DoesNotSlideTheCoolDown` (second `GiveUp` keeps the first due time) | re-stamp `_reArmAtMs` on every `GiveUp` |
| 8 | `Recovery_AfterTheCoolDown_StartsAFreshEpisode` (**the KI-102(b) regression test** — run against unfixed Core first and watch it fail: today `Update` at `due` returns `GiveUp`) | expiry `>=`→`>` off-by-one |
| 9 | `Recovery_BeforeTheCoolDownEnds_StillGivesUp` (`due − 1 ms`) | delete the expiry branch (a before-due check falls through to a fresh `Wait`) |
| 10 | `Recovery_ReservedDuringTheCoolDown_EndsItAtOnce` — also asserts `Reset()` cleared the deadline itself: a fresh episode driven to `GiveUp` afterwards stamps a **new** `DueMs` from the new time, not the stale one | `Reset()` leaves `_reArmAtMs` set |
| 11 | `Recovery_Reset_ClearsTheCoolDown` (undock path; same stale-deadline assertion as row 10) | same |
| 12 | Updated `Recovery_StillMissing_SetsTheWorkAreaThreeTimes_ThenGivesUp` (new `DueMs` on the `GiveUp` steps; the far-future `Update` now returns `Wait`) | any of the above |

Correction from the run: the plan as drafted had the mutation column of rows 8/9 crossed — the expiry `>=`→`>`
off-by-one kills row 8's test (it probes exactly at the deadline), and deleting the expiry branch kills row 9's
(a before-due check then falls through to a fresh `Wait`). Both directions were proven red.

App-layer wiring (broadcast choice, timer re-pointing, log lines) is interop and is covered by criteria 4–15 live,
per AGENTS.md §3; where a live criterion has a cheap local mutation (e.g. `broadcast: !fighting` → `broadcast:
true` must make criterion 5's "no re-check pass" observation fail), the session runs it once on a scratch build.

### Work packages

- **WP1 — Core fight detector + StripRecovery cool-down + tests** (one implementer, ~half a day, no dependencies).
  Owns: `src/WinGnome.Core/Shell/WorkAreaFightDetector.cs` (new), `src/WinGnome.Core/Shell/AppBarReservation.cs`,
  `tests/WinGnome.Core.Tests/Shell/WorkAreaFightDetectorTests.cs` (new),
  `tests/WinGnome.Core.Tests/Shell/AppBarReservationTests.cs`. Done when the table's tests 1–12 are green; test 8
  has been seen red against the pre-fix Core; test 6 asserts no post-exhaustion `GiveUp` step ever carries
  `DueMs = 0` (the deadline `CheckStrip` forwards verbatim — the forwarding itself is WP2's checklist); tests
  10–11 assert `Reset()` clears `_reArmAtMs`; and no other Core file changed.
- **WP2 — App wiring (safety-critical)** (one implementer, ~half a day + session prep; after WP1). Owns:
  `src/WinGnome/Services/WorkAreaController.cs`, `src/WinGnome/Interop/AppBar.cs` (fight-gated broadcast, re-arm
  timer, corrected WARN/docs, B4's notification-name trigger). Checklist item the implementer must not get wrong:
  `CheckStrip`'s `default:`/`GiveUp` branch forwards `step.DueMs` **verbatim** (never recomputes a deadline) and
  re-points the *existing* one-shot `_recoveryTimer` at it; re-pointing must be idempotent under a forced-pass
  storm — Core returns the same stamped deadline on every pre-expiry `GiveUp`
  (`Recovery_GiveUp_DoesNotSlideTheCoolDown`), so re-scheduling can shorten the remaining interval but never push
  the cool-down later. `dotnet build -c Release -warnaserror` and `dotnet test` green; no other file touched.
- **WP3 — Diagnostic logging** (one implementer, ~half a day; parallel with WP1/WP2 — disjoint files). Owns:
  `src/WinGnome/Infrastructure/ThrottledLog.cs` (moved from `Features/WindowButtons/`, gains `Info`), the six
  WindowButtons call-site files (usings only), `src/WinGnome/Features/Taskbar/TaskbarFeature.cs`,
  `src/WinGnome/Services/TaskbarController.cs` (`ShowWindows`/`HideWindows` return counts — additive),
  `src/WinGnome/Features/TopBar/TopBarActions.cs` (one line), `src/WinGnome/Features/TopBar/Tray/TrayModel.cs`,
  `src/WinGnome/Services/Tray/TrayHost.cs` (SETVERSION line). No behaviour changes beyond the return types.
- **WP4 — Live verification + docs** (one implementer + the user's session; after WP1–3 merge-candidates exist).
  Runs the live-test plan; then owns: `docs/KNOWN_ISSUES.md` (KI-102: fix summary, the two evidence readings
  above, worst-case rates, the new silent-write degradation, exploratory-step results; close as *Fixed* with the
  commit once criteria 4–15 pass; KI-099: cross-reference update in its risk paragraph **and** its limits bullet
  "at most three actions per bar per missing-strip episode … then it gives up until the bar is docked afresh"
  (~lines 643–644 of KNOWN_ISSUES.md), which the cool-down makes stale; a new S4 entry for
  "silent writes leave already-maximised windows oversized until the next broadcast" if the session confirms it),
  this spec (addendum status → Implemented; risk 12's KI-102 paragraph points at the addendum),
  `docs/PLAN.md` (Core API table, Shell row: add `WorkAreaFightDetector`, replace "then give up" with the
  cool-down), README (expected: no change — no new flags or settings; verify).

Ordering: WP1 → WP2; WP3 in parallel; WP4 last. Commits stay separable (Core, app wiring, logging, docs) per
AGENTS.md §7.

### Risks and open questions

1. **The provocation hypothesis is unconfirmed.** The fight's *first* reset preceded any fight write (reading 2),
   so silence cannot prevent fight onset — only stop us from sustaining it. If Explorer keeps recomputing at the
   same rate on its own, the design still degrades gracefully: bounded silent writes, automatic re-arm, strips
   restored between Explorer's events, and the transition WARN + "(without a broadcast)" lines make the next field
   occurrence measurable. The exploratory session step (ii) may add evidence.
2. **Which notification accompanies Explorer's recompute** (`ABN_POSCHANGED` vs `ABN_STATECHANGE`) is unknown from
   the old log; B4 answers it next time. If it is `ABN_STATECHANGE`, Explorer is re-applying the taskbar's
   auto-hide state mid-run, which would point at TbExp/option 1 territory.
3. **Oversized maximised windows after a silent fight** (Safety table): accepted, bounded, logged; a settle
   broadcast was rejected (direction 1) — revisit only if field logs show it persisting.
4. **Detector false positives**: three legitimate applications on one monitor inside 10 minutes (e.g. start-up
   write + a quick unplug/replug pair) silence the fourth+ write of that window; the cost is one missed immediate
   relayout, and the window self-heals. Accepted.
5. **Cool-down length** (5 min) and **fight window** (10 min / 3 applications) are reasoned constants, not measured
   ones; both live in Core as named consts so a field occurrence can argue with them.
6. **User-visible give-up hint**: log-only for now (no OSD surface until spec 0017 exists); open question whether
   a persistent give-up should ever raise an OS-level notification.
7. **KI-102's "restart fixes it within ~4 s"** measured 5.1–5.9 s in `everyday-log4` — cosmetic, corrected in the
   KI-102 update.

### Review notes

Advisor review (deepseek-v4-pro-0813, 2026-10-09): plan sound; both must-fix clarifications and all five
nice-to-haves folded in; the silent-write premise remains a hypothesis whose failure mode is the bounded status
quo, not a new failure.
