# 0016 — GNOME window management: app switcher, shortcuts and tiling

Status: Agreed — user decisions 2026-10-09; advisor review (Fable) applied 2026-10-09. Phased v1/v1.1/v1.2; ready for
implementation after spec 0010 lands.

## Problem
WinGnome looks like GNOME but the keyboard and window handling are still Windows': Alt+Tab is Windows' flat window
list, Super+A/S/N open Windows' Quick Settings, Search and notification centre, there is no Super+drag, and Windows
Snap tiles windows edge to edge with no gaps and no layouts. The README roadmap lists "Super+arrow quarter tiling and
Super+drag" as item 3. The user wants GNOME's app switcher, GNOME's shortcuts and tiling modelled on the GNOME
extension *Tiling Shell* (domferr): gaps, layouts, a snap assistant and neighbours that resize together.

## Phases
The user wants all of the behaviour below eventually. It ships in three phases, each with its own work packages
(Design → Work packages), acceptance criteria and smoke test, so each phase can be reviewed and released alone:

| Phase | Scope |
|---|---|
| **v1** | Alt+Tab app switcher; Super shortcuts (A, S, N, Page Up/Down); keyboard tiling (halves, quarters, expand, maximise) with gaps; Super+Shift+Left/Right to the next monitor. Includes the WP0 hotkey spike. |
| **v1.1** | Layouts and the layout picker, Ctrl+drag zones (snap assistant), neighbours resize together. |
| **v1.2** | Super+drag move and Super+right-drag resize. The only consumer of the low-level mouse hook. |
| Later | Super+Shift+Page Up/Down (move window to workspace), once spec 0018's `IWindowDesktopMover` lands. Not part of v1–v1.2. |

Settings for a later phase are added in that phase (additive, defaults when absent), so no build persists a setting
it can't act on.

## Behaviour
All of this is off in `--safe`/`--selftest` and only one WinGnome instance per session runs it (see Design).

**Alt+Tab (GNOME app switcher)** — v1, on by default.
- Alt+Tab / Alt+Shift+Tab switch between **apps** in most-recently-used order: a centred popup of app icons with
  the app name under the selection and a window-count badge when an app has more than one window. Releasing Alt
  activates the selected app's most recent window. A quick Alt+Tab (released within 150 ms) switches without
  showing the popup, as GNOME does.
- Alt+\` (the key above Tab, whatever the layout) switches between **windows of the current app** (thumbnails). In
  the app switcher, \` or Down opens the selected app's windows; Up returns to apps; Left/Right move; Esc cancels;
  Enter or releasing Alt commits; clicking an item commits; hovering selects.
- Scope follows the existing Multitasking row *Alt+Tab shows windows from* (Windows' `VirtualDesktopAltTabFilter`):
  current workspace only, or all workspaces (choosing a window on another workspace switches to it).
- Ctrl+Alt+Tab (Windows' sticky switcher) and Alt+Esc are not part of a session and reach Windows.
- Alt+Tab from an **elevated** foreground window shows Windows' own switcher: a non-elevated low-level hook can't
  see those keys (same cause as KI-004; KI-073). Documented limitation.
- Alt+Tab from an **exclusive/borderless full-screen** app (game, video) is passed to Windows, so the user is never
  trapped by a switcher a game can't show.
- Setting **`Switcher.ReplaceAltTab`** (bool, default `true`), Settings → Multitasking → *App Switching*: "Use the
  GNOME app switcher for Alt+Tab". Off = Windows' Alt+Tab, untouched.

**Super shortcuts** — v1, each with its own switch, Settings → Keyboard → new group *Keyboard Shortcuts*:

| Shortcut | Action | Setting (bool) | Default |
|---|---|---|---|
| Super+A | App grid (`OverviewMode.Applications`), toggles | `Shortcuts.SuperAOpensAppGrid` | `true` |
| Super+S | Overview with the search box focused, toggles | `Shortcuts.SuperSOpensSearch` | `true` |
| Super+N | Top-bar calendar popup (notification list when one exists) | `Shortcuts.SuperNOpensCalendar` | `true` |
| Super+Page Up/Down | Previous/next workspace | `Shortcuts.SuperPageSwitchesWorkspace` | `true` |
| Super+Shift+Page Up/Down | Move the focused window to the previous/next workspace | — (row shown disabled: "Not supported yet") | — |
| Super+V | Unchanged: Windows clipboard history | — | — |

Super+Shift+Page Up/Down needs undocumented COM and is left out of v1 (KI-075). It will reuse spec 0018's
`IWindowDesktopMover` in a later phase, which adds `Shortcuts.SuperShiftPageMovesWindow` (default `true`); until then
the combination reaches Windows untouched.

Super alone (overview) and Super+1..9 (dock) keep their existing Activities settings. Every other Win+ combination
(Win+X, Win+E, Win+L, Win+R, Win+Tab, Win+Ctrl+arrows, Win+Shift+S, …) reaches Windows untouched. Start never opens
after a combination WinGnome handled. A shortcut whose handler isn't running (for example Super+N with the top bar
disabled) is passed to Windows instead of being swallowed. Which mechanism takes each shortcut (a registered hotkey
or the low-level hook) is decided by the WP0 spike (Design → Shortcut routing); the visible difference is stated
there.

**Tiling** — master switch `Tiling.Enabled` (bool; default decided by the rule below), Settings → Multitasking →
*Tiling*. While on, Super+arrows are WinGnome's and Windows Snap no longer sees them; Windows' drag-to-edge snap is
left alone (it stays controlled by the existing *Snap windows* row; WinGnome never writes snap settings).

*Default rule:* `Tiling.Enabled` defaults to `true` only if the cross-DPI QA (acceptance v1-9: halves, quarters and
next-monitor moves across a 100 % and a 150 % monitor, flush to the pixel after the read-back resize) passes before
the v1 release. If it doesn't, v1 ships with default `false` and the row note "Experimental across monitors with
different scaling"; the default flips to `true` in the release where that QA passes. A default flip changes only
files without an explicit value (the field is nullable in JSON and `Normalize()` fills the current default), so a
user's own choice is never overwritten.

v1 (keyboard):
- **Super+Up** from a normal window: *expand to fill* — the window fills its monitor's work area minus the gap on
  every side, without entering the maximised state. Super+Up again: real maximise. **Super+Down** steps back:
  maximised → expanded → restored to the pre-tile rectangle; Super+Down on a restored window minimises (Windows'
  behaviour; see Risks).
- **Super+Left/Right** tile to the left/right half. From a half, Super+Up/Down make the top/bottom quarter of that
  side; from a quarter, the opposite vertical arrow returns to the half. From the right half, Super+Right moves the
  window to the left half of the monitor to the right (if any); from the left half, Super+Left to the right half of
  the monitor to the left. From a half, the opposite arrow restores the window.
- **Super+Shift+Left/Right** move the window, keeping its tile (scaled to the destination's work area, DPI and gap),
  or its relative position if untiled, to the next monitor in that direction.
- **Gap:** `Tiling.Gap` (double, device-independent pixels, default `4`, range 0–32), slider in *Tiling*. The same
  gap separates windows from each other and from the work-area edges, measured on the *visible* frame (not the
  invisible resize borders), so the gaps look exact. On each monitor the gap is a whole number of physical pixels
  (`round(gap × scale)`).
- Elevated windows, windows that can't be resized (no `WS_THICKFRAME`) and full-screen apps are never tiled. On the
  low-level-hook path Super+arrows go to Windows Snap for them; on the registered-hotkey path they do nothing
  (Design → Shortcut routing; KI-076).

v1.1 (layouts and mouse):
- **Layouts and snap assistant:** `Tiling.Layout` (string id, default `"halves"`), a picker of thumbnails in
  *Tiling*. Built-in: `halves`, `one-plus-two` (left half, right half split top/bottom), `three-columns`, `grid-2x2`,
  `two-thirds-left` and `two-thirds-right`. Custom layouts: `Tiling.CustomLayouts` (list of `{ Id, Name, Zones }`, each
  zone a rectangle in fractions of the work area); v1.1 reads them from settings and lists them in the picker, the
  visual editor is a later phase (Non-goals). Dragging a window by its title bar **with Ctrl held** shows the
  layout's zones on the monitor under the pointer; dropping places the window in the highlighted zone. Without Ctrl,
  drags are untouched. Setting `Tiling.CtrlDragZones` (bool, default `true`).
- **Neighbours resize together:** resizing a WinGnome-tiled window by its border moves the shared edge of adjacent
  WinGnome-tiled windows on the same monitor so they stay flush (gap kept), live during the drag. Hung neighbours
  (`IsHungAppWindow`) are skipped. Setting `Tiling.ResizeNeighbours` (bool, default `true`).
- Dragging a tiled window away (a move, not a resize) restores its pre-tile size under the pointer (as FancyZones
  does) and forgets the tile.

v1.2 (**Super+drag**) — setting `Tiling.SuperDrag` (bool, default `true`), Settings → Multitasking → *Tiling*.
- Super+left-drag moves the window under the pointer from anywhere in it. A maximised window is restored first and
  stays under the pointer at the same relative position.
- Super+right-drag resizes it from the nearest edge or corner (the window is split into a 3×3 grid: corner cells
  pick that corner, edge cells that edge, the centre cell the nearest edge). Windows' minimum size is respected.
- Holding Ctrl during a Super+drag also shows the layout zones.
- Super+click on an elevated window is swallowed with no effect (KI-074).

Old settings files load unchanged: the new sections default when absent; no field is renamed.

## Non-goals
- Replacing Win+Tab (Task View), Ctrl+Alt+Tab (Windows' sticky switcher) or Alt+Esc; they pass through.
- Acting on elevated windows, or seeing keys while an elevated window has focus.
- Moving windows between workspaces in v1–v1.2 (undocumented API; spec 0018 owns the mover).
- Keyboard navigation between a layout's zones (Tiling Shell's Super+arrow-to-adjacent-tile); keyboard tiling uses
  halves and quarters whatever the layout. Spanning one window over several zones on drop. Per-monitor layouts.
- The visual custom-layout editor (later phase; v1.1 lists custom layouts from `settings.json`).
- Auto-tiling (placing new windows automatically), window stacks or tabbed tiles.
- A notification list (README roadmap 4); Super+N opens the calendar popup it will live in.
- Changing any Windows snap, Alt+Tab or keyboard registry value.

## Design
### Settings (Core, `Settings/AppSettings.cs`)
New `SwitcherSettings Switcher`, `ShortcutSettings Shortcuts`, `TilingSettings Tiling` (and `CustomTileLayout`)
with the fields above, each added in the phase that uses it; `Normalize()` repairs nulls, fills an absent
`Tiling.Enabled` with the current default, clamps `Gap` to 0–32, drops invalid custom zones (non-finite, outside
0..1, zero area, overlapping by more than 1 %), resets an unknown `Layout` to `halves`. `SchemaVersion` stays 1
(additive). Tests: old-shape JSON without the sections loads with defaults; malformed values normalise; an explicit
`Tiling.Enabled: false` survives a default of `true`.

### One input host, shared (safety-critical)
Today `Features/Overview/SuperKeyHook` owns the only `WH_KEYBOARD_LL` hook. A second hook per feature would double
the per-keystroke cost and split the Start-mask and swallowed-key bookkeeping, which must be global (a key-up may be
swallowed only if its key-down was). Specs 0017 (volume keys, Caps Lock) and 0019 (Win+I) need the same keys, so
there is one host for every keyboard shortcut WinGnome takes:

- **Core `Input/ShortcutRouter`** (pure, no Win32): fed every non-injected key event `(vk, scanCode, isDown)` plus,
  on Alt, Tab and Win downs only, a `PhysicalModifiers` snapshot (see *Lost key-ups*) and a `ForegroundFacts`
  snapshot; returns `RouteResult { Swallow, TapMask, Action? }`. It tracks Win/Alt/Ctrl/Shift from the events it
  sees, embeds the existing `SuperKeyStateMachine` and Super+digit logic, the hook-routed Super shortcuts, the
  hook-routed Super+arrow tiling keys and an **Alt+Tab session** (Alt down → Tab swallowed → `SwitcherOpen(Next)`;
  while open Tab/Shift+Tab/\`/arrows/Enter/Esc are swallowed and mapped; Alt release passes through after a mask tap
  so the app's menu bar doesn't activate). A Tab with Ctrl held (Ctrl+Alt+Tab) and Esc with Alt held but no session
  (Alt+Esc) pass through. \` is matched on scan code `0x29`, not a VK, so it is the key above Tab on every layout.
  Configured from settings through an immutable `ShortcutConfig`. Exhaustively unit tested (see Acceptance).
- **Extension point for other specs:** the router evaluates an ordered, fixed-size array of pure Core
  `IKeyRouteStage`s (`Route(in KeyEvent, in RouterState) → RouteResult?`, first non-null wins, no allocation). This
  spec ships the switcher, Super-key and tiling stages. Spec 0017 adds a `VolumeKeyStage` (swallows
  `VK_VOLUME_UP/DOWN/MUTE` while its setting is on, action `VolumeKey(kind)`) and observes Caps Lock without
  swallowing it (action `CapsLockToggled`); spec 0019 adds Win+I (`SettingsRequested`) if the WP0 spike sends it to
  the hook. Stages are enabled through `ShortcutConfig`; the host stays installed while any stage is enabled. Every
  stage obeys the same swallow-pair rule, which the router (not the stage) enforces. 0017 and 0019 will be revised
  to use this.
- **App `Features/Input/InputFeature`** (new, `[FeatureOrder(45)]`) owns **`InputHookHost`** (refactor of
  `SuperKeyHook`, moved from `Features/Overview/`) and **`HotkeySet`** (generalisation of `GlobalHotkey`: several ids
  on one message-only window, on the UI thread). The host has one dedicated thread with its own message loop holding
  the keyboard hook, and in v1.2 the mouse hook **only while Win is held** (installed by a `BeginInvoke` onto the hook
  thread at the first Win key-down, removed at Win release unless a drag is active, then at the button release;
  also removed on the next mouse **or keyboard** event at which `GetAsyncKeyState` shows Win up, covering Win+L).
  Outcomes are posted to the UI thread and raised through new `ShellCommands` events: `SwitcherInput(SwitcherKey)`,
  `TileRequested(TileCommand)`, `WorkspaceStepRequested(int)`, `CalendarRequested`, `WindowDragInput(DragEvent)`
  (v1.2), plus the existing `ShowOverview`/`ActivateDockItem`. Features never reference each other.
  `OverviewFeature` loses its hook code and keeps handling `OverviewRequested`.
- The host and `HotkeySet` run only when any consumer setting is on, never in safe mode, and only in the process
  holding the session role **`Local\WinGnome-InputShortcuts`** (`SessionRole`), so two instances never both act on
  Alt+Tab, a hotkey or a drag; the other waits and takes over when the first quits. This moves Super-alone and
  Super+1..9 under the role too (see Risks; AGENTS.md §8 update in WP6).
- **`--safe` installs nothing:** no keyboard or mouse hook, no registered hotkey. `OverviewFeature` currently creates
  its `GlobalHotkey` (the Activities hotkey) in safe mode; this spec fixes that (own commit in WP2): no
  `GlobalHotkey` in safe mode.

#### Shortcut routing (WP0 spike decides the column)
`RegisterHotKey` works with an elevated foreground, needs no Start mask and fails open for free when the process
dies. But Explorer registers many Win+ combinations itself, and `RegisterHotKey` fails with
`ERROR_HOTKEY_ALREADY_REGISTERED` when another process owns one. **WP0** (a throwaway console tool, not shipped)
tries each combination below with Explorer running, records the result, then checks two more things for each one
that registers: Windows' own action does **not** also happen (Explorer may handle some combinations through its own
hook, not a hotkey), and registration still holds after an Explorer restart. Results go into this table:

| Combination | Hotkey if WP0 registers it cleanly | Otherwise (low-level hook + Start mask) |
|---|---|---|
| Super+A | `HotkeySet` — **but** see below | `ShortcutRouter` Super stage |
| Super+S, Super+N | `HotkeySet` | `ShortcutRouter` Super stage |
| Super+Page Up/Down | `HotkeySet` | `ShortcutRouter` Super stage |
| Super+arrows, Super+Shift+Left/Right | `HotkeySet` | `ShortcutRouter` tiling stage |
| Win+I (spec 0019) | `HotkeySet` | `ShortcutRouter` (0019's stage) |
| Super alone, Super+1..9, Alt+Tab session, volume keys (0017) | — | always the hook (no hotkey can express them) |

Rules that hold either way:
- A combination WinGnome itself injects stays on the hook, which ignores WinGnome's injected keys: a registered
  hotkey fires for injected input too. Today `ShellShortcuts.OpenWindowsQuickSettings` injects Win+A (quick
  settings' "Windows quick settings" link), so **Super+A stays on the hook** unless that link moves to another
  mechanism first. Ctrl+Win+arrows (`SwitchDesktop`) are never registered.
- A hotkey is registered only while its handler is running and its setting is on, and unregistered otherwise, so
  "missing handler → Windows" holds.
- `ShortcutConfig` marks hotkey-routed combinations so the router's `SuperKeyStateMachine` still counts them as
  combinations (Super alone doesn't fire) but never swallows or masks them.

Behaviour difference, stated in Settings help and KI-076: with a hotkey, the keys never reach Windows, so
**Super+arrow on an untileable, elevated or full-screen window does nothing** instead of falling through to Windows
Snap, and a hung UI thread makes the shortcut do nothing until it recovers (no fallback). With the hook, those cases
reach Windows, but nothing works while an elevated window has focus (KI-073).

#### Hook rules
- **Timing:** callbacks do table lookups and `BeginInvoke` only — no allocation in the steady state, no locks shared
  with the UI thread, no `SendMessage`, no `WindowFromPoint` (it may hit-test other processes). The documented
  `LowLevelHooksTimeout` (at most 1 s since Windows 10 1709) passes the event on and may silently remove the hook.
- **Silent-removal detector:** each callback measures its own duration with `Stopwatch.GetTimestamp` (no clock
  object, no allocation). If one exceeds ~300 ms (a GC pause, paging), the callback sets a flag and posts one
  unhook/rehook onto the hook thread's dispatcher; the rehook logs the slow duration, reinstalls the keyboard hook
  (and the mouse hook if it was installed) and resets router state. The host also reinstalls on resume
  (`WM_POWERBROADCAST`), session unlock (`WM_WTSSESSION_CHANGE`) and display change, and resets router state then.
- **`GetAsyncKeyState`: hook thread only, modifiers only** (Win, Alt, Ctrl, Shift). Never called on the UI thread for
  routing or tiling decisions. Inside the callback it shows the state *before* the current event (as
  `SuperKeyHook.OnKeyDown` relies on today).
- **Lost key-ups** (Win+L, secure desktop, UAC prompt, a hook removed mid-press): on every Alt, Tab and Win **down**,
  the host passes `PhysicalModifiers` from `GetAsyncKeyState`. If the router believes a modifier is held but the
  snapshot says it isn't, it ends any switcher session (posting `SwitcherInput(Cancel)`), resets tracked modifiers
  and clears every swallowed flag. Separately, whenever a key-down will pass through, that key's stale swallowed flag
  is cleared, so its pass-through up can't be swallowed (the pattern in `SuperKeyHook.OnKeyDown` for digits,
  generalised to every swallowable key).
- **UI-hang guard (ack counter, no clock):** the hook thread increments `opened` (an `int` written only by the hook
  thread) when it starts a switcher session. The UI thread stores `acked = opened` (volatile) when it handles that
  session's `SwitcherOpen` post. At the next Alt+Tab start, if `opened != acked` the UI never handled the last
  session, so Tab passes through (Windows' switcher) and no session starts. The UI acks on handling the post, not on
  showing the popup, because a quick Alt+Tab never shows one (Risks).
- **`ForegroundFacts`** (`IsFullScreen`, `IsTileable`, `IsElevated`, monitor) is published by the UI thread into a
  volatile reference, recomputed on `WindowTracker.ForegroundChanged` and on `EVENT_OBJECT_LOCATIONCHANGE` of the
  foreground window only, so the hook can pass Alt+Tab or Super+arrows to Windows without any Win32 call.
- **Injected keys while Win is physically held:** `ShellShortcuts.SwitchDesktop` (Ctrl+Win+arrow, KI-003) must not
  inject a Win up/down while the user holds Win, or Windows would treat Win as released; it injects only Ctrl+arrow in
  that case. "Win held" is a volatile flag the host publishes from its own tracking (corrected by the lost-key-up
  check), not a UI-thread `GetAsyncKeyState`. The keyboard hook therefore stays installed while Super+Page is on,
  even if WP0 routes it through `HotkeySet`.

#### Mouse hook (v1.2)
- **Core `Input/SuperDragMachine`**: mouse button/move events while Win is held → `DragStart(button, point)`,
  `DragMove(point)`, `DragEnd`, with swallow decisions (button down and its matching up, never one without the
  other) and the Start mask; calls `SuperKeyStateMachine.OnMouseButton` so Super+click no longer opens the overview.
- **Per-move cost:** a move writes the latest point into two volatile `int` fields and posts a UI callback only if
  none is pending (`Interlocked.CompareExchange` on a pending flag, cleared by the callback before it reads the
  point). At most one `BeginInvoke` is queued however fast the mouse moves; button events are always posted.

### App switcher (v1; `Features/Switcher/`, Core `Switcher/`)
- Core `FocusHistory`: MRU of window keys. Fed from `WindowTracker.ForegroundChanged`, keeping only windows for which
  `WindowTracker.Inspect(hwnd)` is non-null (which already excludes WinGnome's own windows) and that pass the
  switcher's window rules (`WindowFilter`), so the dock, overview, switcher and top-bar popups never enter the
  history. A commit moves the chosen window to the front once; the `ForegroundChanged` that follows for the same key
  is a no-op (no double bump). Z-order is not MRU (minimising sends a window to the bottom).
- Core `AppSwitcherModel`: groups `WindowInfo`s by the dock's `AppIdentity` key in MRU order, selection, Tab wrap,
  window list per app, scope filter; `SwitcherLayout`: icon size and wrapping to the monitor width at its DPI.
- App `SwitcherFeature` + `SwitcherWindow`: WPF, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`, so it never
  takes focus (the hook delivers the keys); clicks work without activation. Icons from `IIconProvider`, window
  thumbnails via `DwmThumbnail`. Shown after the 150 ms delay (one-shot `DispatcherTimer`) on the monitor of the
  foreground window. UIA: items have names and the selection raises `SelectionItemPattern` events so Narrator reads
  the switch.
- **Commit and the foreground lock:** after the Alt release has passed through, `WindowActivator.Activate`. The
  user's last input went to the old foreground app, so `SetForegroundWindow` from WinGnome is refused; `Activate`
  then taps **`VK_UNASSIGNED_MASK`** (already marked with `InjectedMarker`) instead of `VK_MENU`, which satisfies the
  "last input event" rule without a lone Alt reaching the old window's menu bar. `AttachThreadInput` is never used
  (it can tie WinGnome's input queue to a hung app). This changes `Interop/WindowActivator.cs`, used by the dock and
  overview too, so it is its own commit (WP3) and their activation is part of the v1 smoke test.
- **Workspace scope:** `WindowTracker` excludes cloaked windows, and windows on other virtual desktops are cloaked
  (`DWM_CLOAKED_SHELL`). For "all workspaces" the switcher asks `WindowTracker` for shell-cloaked task windows too
  (new `IncludeOtherDesktops` query, not a change to `Windows`) and confirms them with the documented
  `IVirtualDesktopManager.IsWindowOnCurrentVirtualDesktop`. The registry value is read through
  `RegistryKeyWatcher` (no restart needed, unlike KI-064). QA checks that activating a window on another desktop
  switches to that desktop; the fallback (switch with `ShellShortcuts` first, then activate) stays in the code path
  for builds where it doesn't.
- First-open latency (KI-030 measured ~0.5 s for WPF's first full-screen frame) would spoil the first Alt+Tab, so
  the window is warmed up once at idle a few seconds after start (cloaked, like the overview) and then kept hidden
  and sized down.

### Tiling (`Features/Tiling/`, Core `Tiling/`)
Core, v1: `TileState` (Restored, Expanded, Maximised, halves, quarters; Zone n from v1.1); `TileTransitions` (the
keyboard table in Behaviour, including "next monitor" outcomes); `ZoneGeometry.Compute(layout, workArea, gapDip,
scale)` → `PixelRect`s that are flush (no 1 px overlaps or holes at odd sizes; outer gap = inner gap), rounding
`gapDip × scale` to whole physical pixels inside so callers never pass fractional gaps; `FrameCompensation`
(visible target → `SetWindowPos` rectangle from `GetWindowRect` vs `DWMWA_EXTENDED_FRAME_BOUNDS`, as
`WindowGeometry.CenteredOrigin` does); `TileRegistry` (per window key: pre-tile visible rect, state, monitor device,
zone, and the **expected rect** of the last move WinGnome made); `MonitorNeighbours` (next monitor left/right by
geometry, negative coordinates).
Core, v1.1: `TileLayout` + `TileLayoutCatalog` (built-ins, custom validation); `ZoneHitTest` (pointer → zone);
`NeighbourResizePlanner` (old/new rect of one tile → new rects for neighbours sharing that edge, respecting learned
minimum sizes); `MinSizeCache` (minimum sizes learned by read-back).
Core, v1.2: `DragGeometry` (Super-drag move and 3×3 resize grip maths, minimum sizes, clamped to the virtual screen).

- **Window keys and HWND reuse:** keyed by `(hwnd, processId)`; entries removed on `EVENT_OBJECT_DESTROY` via
  `WindowTracker.RawWindowEvent`, and a key whose pid differs is a new window.
- **Our own moves are not user moves:** every `SetWindowPos` WinGnome issues records the expected visible rect in
  `TileRegistry` for that window. A `LOCATIONCHANGE` whose visible rect matches it within ±1 px is consumed silently
  (clears the expectation, no planner, no `Reconcile`). Only the window inside the current
  `MOVESIZESTART`…`MOVESIZEEND` loop drives `NeighbourResizePlanner` and drag-away untiling, so neighbour moves can't
  feed back into more neighbour moves. Outside a move-size loop, a non-matching rect makes `Reconcile` treat the
  window as restored (the app or the user moved it by other means).
- **Applying positions:** all moves of other processes' windows use `SetWindowPos` with
  `SWP_ASYNCWINDOWPOS | SWP_NOACTIVATE | SWP_NOZORDER` so a hung app can't block the dispatcher; elevated windows
  (`WindowInfo.IsElevated`) and `IsHungAppWindow` windows are skipped, including as neighbours. A maximised or
  Windows-arranged (`IsWindowArranged`) window is restored (`SW_RESTORE`) before it is placed. Maximise uses
  `SC_MAXIMIZE`; since the window's normal rectangle is then the expanded rectangle, Super+Down (`SC_RESTORE`) returns
  to expanded for free. Minimum sizes are never asked for with a cross-process `WM_GETMINMAXINFO`: the result is read
  back after `EVENT_OBJECT_LOCATIONCHANGE` and the size learned.
- **DPI and multi-monitor:** the gap is rounded per monitor inside `ZoneGeometry`. Moving to a monitor of another DPI
  makes the app rescale itself (`WM_DPICHANGED`), changing its size and invisible borders, so a cross-DPI placement is
  two steps: first move only (`SWP_NOSIZE`) to put the window on the destination monitor, then, on the read-back
  `LOCATIONCHANGE` (one entry in a `DeadlineSchedule`, 300 ms cap, then proceed with what is read), compute
  `FrameCompensation` from the new borders and set the size and position as on the same-DPI path. Same-DPI moves are
  one step.
- **ForegroundFacts input:** the tiling feature reports the foreground window's `LOCATIONCHANGE` (only that window's)
  to the code that recomputes `ForegroundFacts`.
- **Ctrl+drag zones (v1.1):** uses `WindowTracker.RawWindowEvent` (`MOVESIZESTART`/`END` and the dragged window's
  `LOCATIONCHANGE`), already hooked, so no mouse hook. Ctrl state comes from the input host (a volatile flag from its
  own tracking, plus a posted change during a drag so the zones appear without moving the mouse), not from a
  UI-thread `GetAsyncKeyState`; the cursor position decides the zone. On `MOVESIZEEND` with Ctrl held, the window is
  placed in the zone. Overlay: one click-through `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` window per
  monitor, created on first use, hidden and sized down after the drop, recreated on display change.
- **Neighbour resize (v1.1):** on `LOCATIONCHANGE` of the window in the move-size loop, coalesced to one apply per
  dispatcher render pass; untile-on-move detected when size is unchanged but position changed. Neighbour moves go
  through the expected-rect bookkeeping above.
- **Display, DPI and work-area changes** (`WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE` `SPI_SETWORKAREA`): tiled windows
  on monitors that still exist are re-laid out to their zones; tiles on removed monitors are forgotten.
- **Explorer restart:** COM objects (`IVirtualDesktopManager`) are recreated on `RPC_E_DISCONNECTED` or
  `TaskbarCreated`. Our hooks don't depend on Explorer; registered hotkeys stay ours (Explorer's re-registration of
  the same combination fails, which WP0 checks has no side effect).

### Workspaces (v1)
`VirtualDesktopMonitor` moves from `Features/TopBar/Services/` to `Services/` (exposed on `ShellContext`) so the
input shortcuts work with the top bar off; the top bar keeps using it (refactor in its own commit). Super+Page Up/Down
call `PlanSwitchBy`. Moving another process's window to a desktop is not possible with documented API
(`IVirtualDesktopManager.MoveWindowToDesktop` returns `E_ACCESSDENIED` for other processes' windows), so this spec
adds no undocumented COM; Super+Shift+Page waits for spec 0018's `IWindowDesktopMover`.

### Interop
`NativeMethods.Input.cs` (owner: WP2): v1.2 adds `WH_MOUSE_LL`, `MSLLHOOKSTRUCT`, `LowLevelMouseProc`, mouse
messages. `RegisterHotKey`/`UnregisterHotKey`/`WM_HOTKEY` already exist in `NativeMethods.Overview.cs`; reuse them.
New `NativeMethods.Tiling.cs` (owner: WP4): `IsWindowArranged`, `IsHungAppWindow`, `SWP_ASYNCWINDOWPOS`.
`Interop/VirtualDesktopManager.cs` (documented COM, WP5). `WindowActivator.Activate`'s mask tap (WP3). Check existing
partials first; `SetWindowPos`, `GetVisibleBounds`, `MonitorFromPoint`, `GetMonitorRects`, `GetMonitorScale`,
`GetAsyncKeyState`, `VK_UNASSIGNED_MASK` already exist.

### Work packages
| Phase | WP | Owns | Depends on | Notes |
|---|---|---|---|---|
| v1 | 0 | Spike: `RegisterHotKey` per combination with Explorer running (tool in the scratch folder, not committed); fills the routing table in this spec | — | First. Result decides `HotkeySet` contents |
| v1 | 1a | Core `Input/ShortcutRouter`, `IKeyRouteStage`, switcher/Super/tiling stages, `ShortcutConfig`, tests | 0 | Parallel. Safety-critical logic |
| v1 | 1b | Core `Tiling/` v1 types, tests | — | Parallel |
| v1 | 1c | Core `Switcher/*`, v1 settings classes in `AppSettings.cs` + serializer tests | — | Parallel; sole owner of `AppSettings.cs` |
| v1 | 2 | `Features/Input/` (`InputFeature`, `InputHookHost`, `HotkeySet`), removal from `Features/Overview/`, safe-mode `GlobalHotkey` fix, `ShellContext.cs` (all new `ShellCommands` events in its first commit), `NativeMethods.Input.cs` | 0, 1a | **Safety-critical**; Opus review mandatory |
| v1 | 3 | `Features/Switcher/`, `WindowActivator` mask tap (own commit) | 1c, 2 | Parallel with 4 and 5 |
| v1 | 4 | `Features/Tiling/` keyboard tiling, `NativeMethods.Tiling.cs` | 1b, 2 | Parallel with 3 and 5 |
| v1 | 5 | `Services/VirtualDesktopMonitor` move, `Interop/VirtualDesktopManager.cs`, top-bar calendar handler | 2 | |
| v1 | 6 | Multitasking and Keyboard panel rows, README, PLAN.md, KNOWN_ISSUES, AGENTS.md §8 (input role) | 1c | Last |
| v1.1 | 7 | Core layouts, `ZoneHitTest`, `NeighbourResizePlanner`, `MinSizeCache`, settings fields, tests | v1 | |
| v1.1 | 8 | Ctrl+drag zone overlays, neighbour resize, layout picker row, docs | 7 | Own smoke test |
| v1.2 | 9 | Core `SuperDragMachine`, `DragGeometry`, `SuperDrag` setting, tests | v1 | |
| v1.2 | 10 | Mouse hook in `InputHookHost`, drag handling in `Features/Tiling/`, docs | 9 | **Safety-critical**; Opus review mandatory; own smoke test |

## Safety and recovery
- **No persistent system state changes.** No registry writes, no `SystemParametersInfo`, no snap settings. Window
  positions are the user's to keep; nothing needs restoring on exit, crash or `taskkill /f`.
- **Hooks and hotkeys:** removed on exit (`Dispose`, reverse order: hotkeys, mouse hook, keyboard hook, thread) and by
  Windows when the process dies. A swallowed key-down always has its key-up swallowed, a key-up is swallowed only if
  its down was, stale swallowed flags are cleared when a down passes through or a lost key-up is detected, and Win and
  Alt downs/ups are never swallowed (masks are tapped instead), so a crash between down and up can't leave a key
  logically stuck. Exceptions in callbacks are caught and the event passed on (existing rule).
- **Fail open:** hook install failure, silent hook removal (detector), a hung UI thread (ack counter), a full-screen
  or elevated foreground, a combination WP0 couldn't register, or a missing handler → Windows' own behaviour. A
  registered hotkey fails open only when the process exits (see Shortcut routing).
- **Super+drag** (v1.2) swallows the click; if no movable window is found under the pointer (resolved on the UI
  thread by walking `EnumWindows` z-order against visible bounds, no messages sent), the drag is a no-op.
- `--safe` and `--selftest`: no hooks, no registered hotkeys (including the Activities hotkey), no switcher, no
  tiler, no Ctrl+drag zones (parallel test instances must not both react to drags); `--selftest` still constructs the
  features to prove they start and dispose cleanly.

## Footprint
- Keyboard hook: already installed by default (Super+1..9 is on); the router adds a table lookup per key and a
  `GetAsyncKeyState` read only on Alt, Tab and Win downs. The duration check is two `GetTimestamp` calls.
- Registered hotkeys: no per-keystroke cost.
- Mouse hook (v1.2): only between Win down and Win up (plus an active drag); zero at idle; at most one queued UI
  callback for moves.
- WinEvents: none new; tiling filters `RawWindowEvent` with one dictionary lookup unless a drag is active.
- Switcher: one hidden, sized-down WPF window after warm-up (target < 5 MB); zone overlays only after the first
  Ctrl+drag. No timers at idle (the 150 ms delay and the DPI read-back cap are one-shot); the UI-hang guard uses no
  clock.
- Compare idle CPU and working set before and after in Task Manager (AGENTS QA 6), once per phase.

## Acceptance criteria
**v1**
1. Core: `AppSettings` loads a pre-0016 file with defaults; normalises gap, unknown layout; an explicit
   `Tiling.Enabled: false` survives.
2. Core `ShortcutRouter` (theory rows): each hook-routed shortcut swallowed only when its setting is on and passed
   otherwise; hotkey-routed combinations never swallowed but still count as combinations; Win+X/E/L/R/V/Tab/
   Ctrl+arrows never swallowed; every swallowed down has a swallowed up and no up is swallowed alone; mask tapped
   after swallowed Win combos and before Alt release in a switcher session; Alt+Tab passed when the foreground is
   full screen, `opened != acked` or `ReplaceAltTab` is off; \` matched by scan code; Ctrl held → Tab passes
   (Ctrl+Alt+Tab); Alt+Esc passes; **Alt-up never seen → next Tab passes**; Win-up never seen (Win+L) → next press
   starts clean and Super alone works; a stale swallowed flag is cleared when the same key's down passes through.
3. Core switcher: MRU grouping by app, Shift+Tab wrap, \`/Down into windows, scope filter, window count;
   `FocusHistory` ignores filtered windows and bumps once per commit.
4. Core tiling v1: `TileTransitions` table rows (theory), `ZoneGeometry` flush at odd sizes and at 125 %/150 %/175 %
   (gap rounded to whole pixels, outer = inner), negative-coordinate monitors, `FrameCompensation`, `MonitorNeighbours`,
   `TileRegistry` HWND reuse (same hwnd, new pid → new window) and expected-rect matching (±1 px consumed, 2 px not).
5. Each new test seen failing against a deliberately broken implementation.
6. WP0 results recorded in the routing table, including whether Windows' own action also fired.
7. Manual: Alt+Tab, Alt+Shift+Tab, Alt+\`, \`/Down/Esc/Enter/click in the switcher; no menu-bar activation in
   Notepad after Alt+Tab; quick Alt+Tab switches without the popup; elevated foreground shows Windows' switcher; a
   full-screen video passes to Windows; scope setting honoured with two workspaces, and choosing a window on the
   other workspace switches to it; dock and overview activation still work after the `WindowActivator` change.
8. Manual: Super+A/S/N/Page Up/Page Down; Start never opens after them; Super alone still opens the overview; Win+X,
   Win+E, Win+V, Win+Shift+S still work; quick settings' Windows quick-settings link still opens Windows' flyout;
   Super+Shift+Page reaches Windows; each switch off restores Windows' behaviour.
9. Manual: Super+Up/Up/Down/Down cycle with 4 px gaps measured in a screenshot; halves, quarters, next-monitor moves
   across a 100 % and a 150 % monitor flush to the pixel (this decides the `Tiling.Enabled` default); elevated and
   fixed-size windows untouched (and the documented outcome for the chosen routing).
10. Safety: `taskkill /f` mid-Alt+Tab leaves no stuck Alt/Win; Win+L mid-Alt+Tab and mid-Super combination recovers
    on the next press; sleep/resume and lock/unlock keep shortcuts working; a forced slow callback (temporary local
    `Thread.Sleep(400)` in a debug build, removed before commit) logs and rehooks; Explorer restart keeps workspace
    shortcuts and hotkeys working; `--safe` installs no hook and registers no hotkey (log); a second instance takes
    over the role when the first quits.
11. Footprint: idle CPU and memory compared before/after.
12. Light and dark mode, 100 % and 150 % for the switcher.

**v1.1**
13. Core: layout catalog and custom-zone validation, `ZoneHitTest`, `NeighbourResizePlanner` with min sizes; a
    neighbour's own `LOCATIONCHANGE` matching its expected rect never re-enters the planner.
14. Manual: Ctrl+drag zones for every built-in layout; neighbour resize stays flush and doesn't oscillate; a hung
    neighbour is skipped; drag-away restores size; zone overlays in light/dark at 100 % and 150 %; footprint compared.

**v1.2**
15. Core `SuperDragMachine`: down/up pairs, drag start/move/end sequence, overview disarmed by a Super+click;
    `DragGeometry` grip selection at cell boundaries.
16. Manual: Super+drag move and Super+right-drag resize on each corner/edge, on a maximised window, across monitors;
    `taskkill /f` mid-drag leaves no stuck Win or mouse button; no mouse hook present when Win is not held, and the
    mouse hook is gone after Win+L (log); fast mouse moves queue at most one UI callback (log counter in a debug build).

## Risks and open questions
- **Hotkey vs hook (WP0).** Explorer may own some combinations (`ERROR_HOTKEY_ALREADY_REGISTERED`) or handle them
  through its own hook so that a successful registration still lets Windows act too; WP0 checks both. Code finding:
  `ShellShortcuts.OpenWindowsQuickSettings` injects Win+A, and a registered Win+A would catch that injection and open
  the app grid instead, so Super+A stays on the hook regardless (rule in Shortcut routing). With hotkeys, untileable
  windows get no Super+arrow fallback to Snap (KI-076).
- **Ack counter, deviation from the review wording.** The review says the UI acks "when the popup is up". A quick
  Alt+Tab (released within 150 ms, Behaviour) never shows the popup, so acking only then would make every Alt+Tab
  after a quick switch fall back to Windows. The UI acks when it handles the session's open post instead, which is
  what proves it is responsive.
- **`WindowActivator` mask tap** changes activation for the dock and overview too. Expected to be strictly better (no
  lone Alt reaching the old window), but smoke-tested in v1 acceptance 7.
- **Super+N** replaces Windows' notification centre shortcut, but WinGnome has no notification list yet; until one
  exists Super+N shows only the calendar, so keyboard users lose quick access to notifications (quick settings still
  links to Windows' notification centre). Default `true` as decided, noted in Settings.
- **Tiling default** follows the rule in Behaviour (cross-DPI QA decides). Super+Down on a restored window
  minimises, as Windows (GNOME does nothing).
- **Input role and parallel instances.** Super-alone and Super+1..9 move under `Local\WinGnome-InputShortcuts`, so a
  second non-safe instance (for example a smoke-test profile while the everyday copy runs) no longer gets them, and
  older builds without the role (such as a `publish\` copy) still install their own hook alongside, so Super alone
  could toggle the overview twice (like KI-042). AGENTS.md §8 needs a note in WP6: check the log for which instance
  holds the input role, and quit pre-role builds when testing shortcuts. `--safe` test instances are unaffected (no
  hooks either way).
- **Hook-thread `GetAsyncKeyState`** reflects the state before the current event, and on Win+L / UAC the lost-up check
  runs only at the next Alt, Tab or Win down; until then a tracked modifier may be wrong, but nothing is swallowed on
  other keys except while a session is open, which the next Alt/Tab/Win down ends.
- Activating a window on another workspace may not switch desktops on every build; QA (acceptance 7) decides, and the
  `ShellShortcuts` switch-first fallback stays available.
- Windows' Snap Layouts flyout (drag to the top edge) can appear under the Ctrl+drag zones; documented, the existing
  Streamline tweak can disable it.
- A GC pause on the hook thread delays keystrokes (existing risk); the router must not allocate per event, and the
  duration detector rehooks if Windows may have dropped the hook.
- Injected input (on-screen keyboard, AutoHotkey, remote tools) is ignored by the hook, so their Alt+Tab reaches
  Windows' switcher. By design. Registered hotkeys do fire for injected input.
- KNOWN_ISSUES entries (added in WP6): **KI-073** (S4) Alt+Tab and hook-routed Super shortcuts fall back to Windows
  when an elevated window has focus (extends KI-004). **KI-074** (S4, v1.2) Super+click on an elevated window is
  swallowed with no effect. **KI-075** (S4) Super+Shift+Page Up/Down not supported until spec 0018's mover lands.
  **KI-076** (S4) Hotkey-routed Super+arrow does nothing on untileable, elevated or full-screen windows instead of
  falling back to Windows Snap (only if WP0 routes arrows through `HotkeySet`; otherwise closed as not applicable).
