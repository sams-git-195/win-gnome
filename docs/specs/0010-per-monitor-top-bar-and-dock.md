# 0010 — Top bar and dock on every monitor

Status: Agreed — user decisions 2026-10-09; advisor review (Fable) applied 2026-10-09. Ready for implementation, spike first.

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
the monitor under the cursor with `DisplayLayoutService.Current.At`, skips monitors where `HotCornerRules.IsTrueCorner`
is false, and passes that monitor to the existing detector and `ShouldTrigger` full-screen check (which takes the
monitor rect already). The ad-hoc `IsOnAnyMonitor` checks are replaced by the Core rule.

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
13. Q Hot corner fires on each true corner, not on a corner shared with the other monitor.
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
   decides between outcome A and B before the janitor is written. KI-070 (S3 until the spike verifies outcome A, then
   S4).
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
