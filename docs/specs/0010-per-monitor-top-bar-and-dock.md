# 0010 — Top bar and dock on every monitor

Status: Implemented 2026-10-09 (packages 0–6), awaiting Opus review and the manual QA that needs hot-plugging (see
*Implementation notes*). User decisions 2026-10-09; advisor review (Fable) applied 2026-10-09.

Plans KI-001. Owns KI-069 to KI-072. Safety-critical (AppBars, work areas, taskbar): implemented alone, before any
other spec, and reviewed by Opus before merge.

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
    docked at the same point and its strip appeared only ~35 s later, with no activity of its own at that moment.
    Session 1 also started unreserved. So the delay predates this spec, and re-registering during it neither helped
    nor is known to be harmless (it may restart Explorer's delay).
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
restore, no re-registration immediately before a shrink, and the monitor key derived at use time. The `TbExp`
experiment still runs in the same live session, to see whether option 1 (hide the taskbar windows only after
Explorer has applied auto-hide) can later remove the need for this; the fallback also fixes the unplugged-monitor
case, which option 1 does not.

### Why

Explorer owns work areas. WinGnome's bars are registered AppBars and Explorer grants their rectangles, but it applies
them to work areas only inside its own taskbar layout pass, which is deferred while its taskbar is auto-hidden and
`SW_HIDE`n (measured ~35 s), and which it can skip entirely when a monitor is unplugged. In both cases maximised
windows cover the bar and a strip nothing reserves is left in the desktop. `SystemParametersInfo(SPI_SETWORKAREA)` is
documented, needs no elevation and writes exactly the value Explorer writes, so WinGnome can finish the job itself —
provided it changes as little as possible, records it before it changes it, and gives it back.

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
   then 5 s and 20 s later), then left until the bar is docked afresh. The action is a shrink when shrinking is
   allowed, and a re-registration only when it is not (`--safe`, or the monitor's budget spent) — re-registering
   during Explorer's deferral is what the third live run showed to be useless and possibly harmful, so it is no
   longer on the normal path. Per monitor, `WorkAreaBudget` allows three applications per 60 s. Hitting either bound
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
- `WorkAreaRecord(int Owner, string Key, PixelRect Bounds, PixelRect Original, PixelRect Applied)`: one shrink.
  `Owner` is the shrinking bar's HWND (unique per bar, needs no plumbing); `Original` is the fresh work area before
  the shrink, `Applied` the one we set. Records are kept **in application order**, which is the restore order.
- `WorkAreaFile(IReadOnlyList<WorkAreaRecord> Records, bool Unreadable)` with `WorkAreaState.Parse(string? json)` and
  `Serialize(records)`: an absent or empty file is "no records"; a corrupt one is `Unreadable` (never an exception),
  because silently reading it as empty would strand the shrinks it described.
- `WorkAreaRecovery.Plan(records, MonitorLayout monitors, IReadOnlySet<int> released) -> WorkAreaPlan`: the whole
  restore decision, pure. Per monitor key it walks that key's records **newest first**, restoring one only when its
  owner has released *and* the running work area still equals its `Applied`, then continuing from its `Original`; it
  stops at the first record that fails either test. A record whose monitor is gone, or whose `Bounds` differ from the
  live monitor's, is dropped as stale (a mode change reset the work area anyway). The plan carries the ordered
  `Restores` (key + rectangle to write), the records to `Keep` (a live bar still owns a newer one) and `Nudge` (true
  when something was dropped or a chain broke, so Explorer gets a chance to recompute). Restoring newest first is
  what makes a top bar's and a bottom dock's strips on one monitor unwind in the right order; restoring an older
  record while a newer one is still applied would leave the newer strip behind.
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
  `ReleaseAll(bool broadcast)` and `RecoverFromMarker(settingsDirectory)`. It holds the record list, a
  `WorkAreaBudget` per monitor key and the marker path. **It derives the monitor key itself** (`MonitorFromRect` +
  `GetMonitorInfoEx`, the `MonitorKeyOf` fallback pattern) inside the lock, so `AppBar` needs no key field and no
  stale key can survive a display change; a monitor whose bounds no longer equal the cached rectangle is refused.
  Every application, restore, refusal and drop is logged: `Log.Info` with the key, the edge and both rectangles for
  an application or restore, `Log.Warn` for a spent budget, a failed call (with the Win32 error), an unwritable
  marker and a stale record.
- `AppBar.CheckStrip`: a `Shrink` step calls `TryShrink(_hwnd, _monitor, _edge, Bounds)`, a `Reregister` step calls
  the existing `Reregister()`. Applied or refused, the bar re-checks on the schedule `StripRecovery` gives it, so a
  refusal cannot spin. `TryShrink` returns false when the fresh read shows the strip already reserved, which is the
  common case and costs one `GetMonitorInfo`.
- `AppBar.Undock` (and so `Dispose`): after `ABM_REMOVE`, `WorkAreaController.Release(_hwnd)`. `AppBar.UndockAll()`
  (crash path): `ABM_REMOVE` for every bar, then `ReleaseAll(broadcast: false)`, guarded so file I/O on a faulting
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
over the old one, so a crash mid-write cannot truncate it.

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
| Crash | `AppBar.UndockAll()` → `ABM_REMOVE` for every bar → `ReleaseAll(broadcast: false)` → features' `EmergencyRestore` → `TaskbarController.RestoreFromMarker`. |
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
    and log, so a fight cannot become a loop.
13. **`SPIF_SENDCHANGE` broadcasts synchronously** to top-level windows, so a hung app could delay a shrink. It is
    skipped on the crash path for that reason; on the normal path it runs on the dispatcher and is worth its cost.
14. **The marker is per profile.** A force-kill followed by a start with a different `--settings-dir` cannot recover
    (the same limitation as `display-revert.json`, KI-068); `--restore-taskbar` from the original profile can.
15. **The crash path does file I/O** (deleting the marker) on a possibly faulting thread, like `UndockAll`'s tray
    sends (risk 5). Guarded, never fatal; a marker left behind is recovered by the next start.
