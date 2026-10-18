# 0024 — Dock window previews, per-window close and a stable running-app order

Status: Implemented (stable order e4f8458, close path 60fd575, previews 0bb9893, re-anchoring add487e). Not yet smoke-tested on screen.

**Implementation notes (differences from the text below):**

- No Esc hotkey (see the decision below), so nothing in the popover registers a hotkey.
- A foreground change closes the popover only while the pointer is not on it: closing a window from the popover moves
  the foreground to another window, and the popover must stay up and update then (Behaviour: "the popover stays open
  and updates").
- "Minimised" is shown under the app icon inside the thumbnail box rather than in the 28 DIP caption row, which has
  no room for a second line.
- `DockPreviewContent` (Core) decides which icons show the popover and what it lists, and whether a refresh needs the
  thumbnails registered again (`NeedsRebuild`) or only new titles. `DockAppearance.WindowPreviews` carries the
  setting, set by `DockViewModel.Update`.
- The thumbnails are owned by `DockPreviewWindow` (released on hide and on close); the presenter owns the window and
  the timer. The popover is re-laid-out on `DpiChanged` only when the new DPI differs from the one it was laid out for.
- A refresh lays the popover out again at the icon's current position when the icon moved or the windows changed
  (`DockPreviewContent.NeedsRelayout`), so a close or another app coming and going never leaves it pointing elsewhere.
- Known limitations: KI-120 (virtual desktops), KI-121 (minimised and protected content), KI-122 (switch frame),
  KI-123 (Store-app close skipped as reused).

**Decision (project manager):** the Esc hotkey is dropped. WinGnome never intercepts Esc, so the popover is dismissed
only by the other triggers listed (pointer leave, click, drag, dock hide, foreground change). Nothing is registered with
`RegisterHotKey`, in `--safe` or otherwise; the text below has been updated to match.

## Problem

Three dock problems, reported by the user and agreed as requirements:

1. **Unpinned running apps jump around.** `DockModelBuilder.Build` orders unpinned groups by first appearance in
   `windows`, which is `WindowTracker.Windows`: z-order, close to most-recently-used. Clicking a window raises it,
   the tracker refreshes, and its app's icon moves to the front of the unpinned section. Icons never stay where
   the user last saw them, and Super+N on an unpinned app changes meaning with every click. Window order *inside*
   an app follows z-order too, so the context menu's window list reshuffles and `DockClickAction.Cycle` only ever
   flips between the two most recent windows (the activated window becomes index 0, so "next" is always index 1).
2. **No way to tell an app's windows apart from the dock.** An icon with several windows offers only the
   right-click title list or the overview (`DockClickAction.Previews`). GNOME's Dash to Dock and the Windows
   taskbar show live thumbnails on hover.
3. **No way to close one window from the dock.** The context menu has "Quit" / "Close N windows" for the whole
   app only.

## Behaviour

### Stable order (all docks, no setting)

- Pinned apps keep their pinned order (unchanged).
- An unpinned running app takes the slot after the last unpinned app the first time it appears in a dock, and
  keeps it while it has windows, whatever window is focused or raised.
- When an app's last window closes, its slot is forgotten after a 2 s grace (an app that swaps a splash window for
  its main window, or Explorer restarting, keeps its slot). An app that comes back later is appended at the end.
- The same rule applies to the windows inside each app (pinned or not): the context-menu list, the hover preview,
  the running-indicator dots and `Cycle` use the order in which windows first appeared. "Activate" (the plain
  click) still goes to the most recently used window of the app.
- Super+1..9 therefore keep pointing at the same app while it runs.
- Pinning a running app moves it into the pinned section (unchanged). Unpinning a running app appends it to the end
  of the unpinned section, unless it was unpinned again within the 2 s grace, in which case it gets its old slot.
- With isolated docks (spec 0010), every dock keeps its own order; a window moved to another display is new there.

### Hover preview

New setting **`Dock.WindowPreviews`** (bool, default **true**; additive, so settings files without the field load
with it on). Settings → Dock, first group, directly under "Click action": row **"Window previews"**, subtitle
"Hover an app with several windows to see and close them." A toggle.

When it is on:

- Hovering a dock icon whose app has **two or more windows on this dock** for **400 ms** opens a popover beside
  the dock: above a bottom dock, right of a left dock, left of a right dock. It is centred on the icon along the
  dock and kept inside the dock's monitor work area (8 DIP from its edges), on the dock's monitor only.
- The popover lists the app's windows in the stable order above. Each item has a caption row (window title,
  ellipsised, and a close **×** on the right) above a **live DWM thumbnail** fitted into a fixed 224 × 140 DIP box.
  Items shrink together (down to a 112 DIP wide box) when they don't fit along the monitor; beyond that the last
  item becomes "+N more", which opens the overview with only this app's windows (the existing
  `OverviewRequest(OverviewMode.Windows, app.Windows)`).
- The × is shown on hover of its item (opacity, so it stays in the UI Automation tree) and never on dock icons.
- Clicking an item (left button) focuses that window, restoring it if minimised, and closes the popover. Middle
  click on an item closes that window (as in the overview). Clicking × closes that window; the popover stays open
  and updates, and closes once the app has no windows left on this dock.
- While the popover is open, moving onto another icon with several windows switches the popover at once (no new
  delay). Moving onto any other icon, or off the dock, starts a **300 ms** grace; reaching the popover within it
  keeps it open, so crossing the gap between icon and popover never flickers.
- The popover closes on: a click (any button) on a dock icon, a context menu opening, a pin drag or a file drag
  starting over the dock, the dock sliding out or being disabled, the foreground window changing (while the pointer is
  not on the popover), a settings change, a relayout, a DPI or display change, and the app's last window on this dock
  closing. Esc does nothing: WinGnome never intercepts it.
  After a click dismissed it, the same icon does not re-open it until the pointer has left that icon.
- The popover never takes activation or keyboard focus, and keeps the dock on screen while it is open (as menus do).
- Only one popover is open across all docks.
- Windows that DWM gives no usable picture for (minimised windows usually, see Risks) show the app icon centred
  in the box and the subtitle "Minimised" under the title. A thumbnail that fails to register shows the icon too.
- **Single-window apps:** no popover. The existing tooltip (the app name, 350 ms) already covers them and is kept.
  For icons that show a popover the tooltip is turned off so the two never overlap.
- When the setting is off nothing changes from today.

### Close from the context menu

- Each window row in the right-click menu gets a **×** at its right edge (same glyph as the popover). Clicking it
  closes that window and closes the menu. Clicking the rest of the row still focuses the window.
- With a window row highlighted (arrow keys), **Delete** closes that window. The menu already takes the foreground
  when it opens (`DockMenuPresenter`), so this is the keyboard path for per-window close.
- Elevated windows get no × (in the menu or the popover) and Delete does nothing on them; they can still be
  focused. "Quit" / "Close N windows" now skip elevated windows too and log how many they skipped.

### Closing is polite

Close is exactly "click the window's own close button": `WM_SYSCOMMAND`/`SC_CLOSE` posted with `PostMessage`
(as `WindowActivator.Close` already does for Quit and the overview). The app may ask to save. Nothing is ever
terminated, and nothing waits on the target window.

## Non-goals

- Drag-reordering unpinned apps, or persisting the running order across WinGnome restarts.
- A × or close gesture on dock icons themselves.
- Keyboard navigation into the dock or the popover (the dock has no keyboard focus model; keyboard users get focus
  and close through the context menu). Screen readers can invoke popover items through UI Automation.
- Previews for single-window apps, for pinned-but-not-running apps, or on the "Show Applications" button.
- Iconic (app-supplied) thumbnails (`DwmSetIconicThumbnail`) or snapshots of minimised windows.
- Fixing KI-033 (an open overview does not narrow to "+N more"'s window set).
- Showing apps whose windows are all on another virtual desktop (the tracker already drops cloaked windows).

## Design

### Core (`WinGnome.Core/Dock`, tested)

| Type | Purpose |
|---|---|
| `DockApp` | Gains `nint MostRecentWindow = 0` (last positional parameter, defaulted so existing constructions compile): the app's first window in z-order. |
| `DockModelBuilder` | Sets `MostRecentWindow` to the first window of each group in input order (0 when not running). Ordering is otherwise unchanged; stable order is a separate step. |
| `DockRunningOrder` | Stateful, one per dock. `IReadOnlyList<DockApp> Apply(IReadOnlyList<DockApp> apps, long nowMs)`; `const long ForgetAfterMs = 2000`. See "Stable order (small fix)". |
| `DockClickPlanner` | "Activate" uses `app.MostRecentWindow` when it is one of `app.Windows`, else `Windows[0]`. `Cycle` steps through `Windows` (now stable). The doc comment "must be ordered most-recently-used first" is replaced. |
| `DockPreviewHover` | The hover state machine. Phases `Idle`, `Pending(key, due)`, `Open(key)`, `Leaving(key, due)`. Inputs `PointerOverIcon(string? key, bool hasPreview, long nowMs)`, `PointerOverPopover(bool over, long nowMs)`, `Tick(long nowMs)`, `Dismiss()`, `SetSuppressed(bool)` (menu, drag, dock hidden). Each input returns `DockPreviewChange` (`None`, `Show`, `Switch`, `Hide`); `Deadline` (nullable ms) tells the app when to call `Tick`. Constants `ShowDelayMs = 400`, `LeaveGraceMs = 300`. After `Dismiss` the key under the pointer is disarmed until `PointerOverIcon` reports another key or null. |
| `DockPreviewLayout` | Pure geometry, physical pixels in, physical pixels out. Inputs: dock position, the dock window rectangle (`DockFrame.Window`, which includes magnification headroom), the anchor (icon centre along the dock, px), monitor bounds and work area (may be negative), DPI scale, and per window a source size (px, nullable). Output: popover rectangle, per item the caption, close-button and thumbnail-box rectangles and the fitted thumbnail rectangle (all relative to the popover's client area, px), a placeholder flag per item, and the overflow count. Rules below. |
| `DockWindowClose` | `readonly record struct CloseTarget(nint Handle, int ProcessId, bool IsElevated)`; `enum CloseDecision { Post, Gone, Reused, Elevated }`; `Decide(CloseTarget captured, CloseTarget? live)`: captured elevated → `Elevated`; live null → `Gone`; different process id → `Reused`; live elevated → `Elevated`; else `Post`. |

`DockPreviewLayout` rules (DIP constants, converted with the scale and rounded half away from zero per edge):
thumbnail box `t × 0.625t`, `t` from 224 down to 112; item = box + 8 padding each side + 28 caption, so a full-size
item is 240 × 184; 4 between items; 8 popover padding; 8 gap from the dock window's inner edge; 8 margin to the
clamp area. Bottom dock: items in a row, popover bottom = dock window top − gap; left dock: a column, popover left =
dock window right + gap; right dock: a column, popover right = dock window left − gap. Along the dock it is centred
on the anchor and clamped to the clamp area (work area ∩ monitor bounds; monitor bounds when that is empty). `t` is
the largest value ≤ 224 that fits both along the dock and across (the space between the dock window and the far
edge of the clamp area); if even `t = 112` does not fit along the dock, show `k − 1` windows plus a "+N more"
item, where `k` items fit. A source size that is null or under 32 px in either dimension is a placeholder (the
overview's `MinUsableSourcePx` rule); otherwise the thumbnail is the largest rectangle of the source's aspect
ratio centred in the box. Inside an item the caption sits at the top (8, 8, t, 28) and the box below it
(8, 36, t, 0.625t). Worked example, 100 %, bottom dock, two 1600×900 windows: popover 500 × 200 DIP; first item at
(8, 8), its caption at (16, 16, 224, 28), its box at (16, 44, 224, 140) and its thumbnail at (16, 51, 224, 126); the
second item at (252, 8).

### App (`WinGnome/Features/Dock`, thin)

- `DockViewModel.Update` runs `DockRunningOrder.Apply(apps, Environment.TickCount64)` (its own instance) before
  building entries, so `AppEntries`, Super+N, clicks and menus all see the stable order.
- `DockPreviewWindow.xaml(.cs)` (new): a borderless, **non-layered** WPF window (DWM does not draw thumbnails into
  layered windows, see `OverviewBackdrop`), `ShowInTaskbar=False`, `ShowActivated=False`, made non-activating and
  topmost with `ShellSurface.MakeNonActivating`, rounded with `ShellSurface.SetCornerPreference`, themed background
  from the dock's resources (light and dark). Items are WPF buttons (`Focusable=False`) placed on a canvas at the
  rectangles `DockPreviewLayout` returns; the thumbnails are `Interop/DwmThumbnail` instances drawn on top of the
  thumbnail boxes, so the caption and × are never under a thumbnail. UI Automation: window name "{App} windows",
  item name = title, item status "Minimised" where it applies, × name "Close {title}".
- `DockPreviewPresenter.cs` (new, one per `DockInstance`): owns the `DockPreviewHover` machine, one one-shot
  `DispatcherTimer` armed to `Deadline` only while one is pending, the popover window (created on first show, then
  hidden and resized to 1 × 1 px on dismiss), the thumbnails (registered on show, re-registered on content change,
  all unregistered on hide and in `Dispose`). It registers no hotkey. Show order: build content, set bounds
  (`ShellSurface.SetBounds`, physical px), register thumbnails and set their rectangles, then show without
  activation. On `DpiChanged` of the popover it lays out again. Captured per item: `CloseTarget` from the tracker's
  `WindowInfo` (`ProcessId` is the app process for UWP frames, so the live side must come from
  `WindowTracker.Inspect` too, never `GetWindowThreadProcessId`).
- No Esc: the popover never intercepts keys (no `RegisterHotKey`, no keyboard hook); Esc goes to the foreground app.
- `DockWindow`: raises `EntryHovered(DockAppEntry? entry, FrameworkElement? container)` only when the hovered
  container changes (from the existing `Items` mouse events plus `SlideHost.MouseLeave`), and `Concealed` when it
  starts sliding out. The app-entry template binds `ToolTipService.IsEnabled` to a new `DockAppEntry.ShowsPreview`
  (`WindowPreviews` on and two or more windows; `DockAppearance` carries the setting).
- `DockInstance`: forwards hover, click, menu, drag, `Concealed`, relayout, settings and DPI events to the
  presenter; computes the anchor from the container (`PointToScreen`, physical px); passes `_menuOpen || _dragging
  || previewOpen` to `DockVisibilityController.SetInteracting`; on `Refresh` hands the presenter the new `DockApp`
  for its key (content updates in place; titles come from the tracker, which already refreshes on name changes).
  Raises `PreviewOpened`.
- `DockFeature`: on `PreviewOpened` dismisses every other dock's popover; on the external foreground change
  (where it already closes menus) dismisses popovers.
- `DockActions`: `CloseWindow(CloseTarget)` reads the live target with `WindowTracker.Inspect`, asks
  `DockWindowClose.Decide`, and posts through `WindowActivator.TryClose`; `Quit` becomes an instance method that
  closes each window through the same path. `Gone`/`Reused`/`Elevated` are logged at Info with the handle; a failed
  post is `Log.Warn` with the handle and the Win32 error.
- `WindowActivator.TryClose(nint)` (new, beside `Close`): the same post, returning false and the
  `Marshal.GetLastPInvokeError()` value on failure. `Close` stays for the overview.
- `DockMenuBuilder`: window rows get a `DockPanel` header (title, then a small `Focusable=False` × button,
  hidden for elevated windows) whose click marks the event handled, closes the window and closes the menu; a
  `PreviewKeyDown` handler on each window row maps Delete to the same action.
- Settings: `DockSettings.WindowPreviews` in `AppSettings.cs`; the row in `DockPage.xaml` and
  `DockPageViewModel.cs`.

Features stay independent: nothing references `Features/Overview`; `DwmThumbnail` and `GetRestoredSize` are shared
interop. No new NuGet package, process, hook or network access.

Threading: everything on the dispatcher. No cross-process `SendMessage`: closes are posted, `Inspect` uses
`GetWindowText` (which does not message other processes) and cached process facts. DWM calls are made from the UI
thread as in the overview.

DPI and monitors: the layout is computed in physical pixels from the dock's own `MonitorInfo` (bounds, work area,
scale), so negative coordinates and per-monitor DPI need nothing special; the popover is only ever on the dock's
monitor, so it never straddles a DPI boundary.

Elevated windows: thumbnails and activation work as they do in the overview; close is never offered or sent (PLAN
safety rule "never act on elevated windows"), even when WinGnome itself runs elevated.

## Safety and recovery

No persistent system state changes: no registry, taskbar, work area or settings files beyond the new boolean.

| Effect | Undo / bound |
|---|---|
| DWM thumbnails | Owned by the popover; unregistered on hide and dispose; released by DWM with the process on crash or force-kill. |
| Window close | Posted `SC_CLOSE` only, after a live re-check (`Gone`, `Reused`, `Elevated` skip). Never `TerminateProcess`, `DestroyWindow` or `EndTask`. |
| Popover window | Non-activating; cannot leave focus lost. Hidden on every dismiss path; destroyed with the dock. |

`--safe`: previews and close work (user-initiated, no hooks, no hotkeys). `--selftest`: nothing is created until
a hover, so self-test behaviour is unchanged.

## Footprint

- Idle: no timer, no polling, no hook. The hover machine is fed by WPF mouse events the dock already receives; the
  one-shot timer runs only for a pending 400 ms show or 300 ms grace.
- `DockRunningOrder`: a dictionary entry per app and per window on the dock; pruned on every refresh.
- Popover: one extra WPF window per dock after the first preview, kept hidden at 1 × 1 px. QA measures it; if it adds
  more than 3 MB working set per dock, switch to destroy-on-dismiss (record the result in this spec).
- DWM composites live thumbnails only while the popover is visible.

## Acceptance criteria

1. Unpinned apps keep their first-appearance order when other windows are raised or focused. (Tests:
   `DockRunningOrderTests`; QA: open A, B, C; click windows of C, then A; icons stay A, B, C)
2. A new app is appended at the end; an app gone for more than 2 s comes back at the end; within 2 s it keeps its
   slot (boundary exactly at 2000 ms keeps it, 2001 ms appends). (Tests: `DockRunningOrderTests`)
3. A long gap between refreshes while an app stays present never reorders it. (Test)
4. Windows inside an app keep their first-appearance order; `MostRecentWindow` is the z-order first; plain click
   activates the most recent; `Cycle` visits every window in turn. (Tests: `DockRunningOrderTests`,
   `DockModelBuilderTests`, `DockClickPlannerTests`; QA: three Notepad windows, `Cycle`, visits all three)
5. Pinned apps' order is unchanged by the stable-order step. (Test)
6. Hovering a multi-window icon for 400 ms opens the popover; 399 ms does not; single-window and not-running apps
   never open one. (Tests: `DockPreviewHoverTests`; QA)
7. Icon → popover across the gap within 300 ms keeps it open; leaving both closes it after 300 ms; switching
   between multi-window icons while open is immediate; after a click-dismiss the same icon does not re-open until
   the pointer leaves it. (Tests: `DockPreviewHoverTests`; QA)
8. Popover geometry for bottom, left and right docks, at 100 % and 150 %, on a monitor with negative coordinates,
   clamped at both ends of the monitor, shrinking to 112 DIP and overflowing to "+N more", with placeholders for
   null and sub-32 px sources. (Tests: `DockPreviewLayoutTests`; QA: light and dark, 100 % and 150 %, both monitors)
9. Clicking a thumbnail focuses (and restores a minimised) window and closes the popover; neither the popover nor
   hovering ever changes the foreground window. (QA, with the foreground logged)
10. × in the popover and in the menu, middle click and Delete close exactly that window with a posted `SC_CLOSE`; a
    window with unsaved changes shows its save prompt; nothing is terminated; the popover updates in place.
    (QA: Notepad with unsaved text)
11. Close is skipped for a gone handle, a reused handle (different process) and an elevated window; no × is shown
    for elevated windows. (Tests: `DockWindowCloseTests`; QA: an elevated Terminal shows no ×)
12. The dock stays on screen while the popover is open in Intellihide and Autohide, and hides normally after it
    closes. (QA)
13. Every dismiss trigger in Behaviour closes the popover, and Esc is never intercepted; only one popover is open
    across docks. (QA with docks on two monitors)
14. With the setting off, behaviour and tooltips are as before; old settings files load with it on; `false`
    round-trips. (Tests: `SettingsSerializerTests`; QA)
15. No new idle CPU; idle memory after the first preview within the Footprint bound. (QA: Task Manager before and
    after, 5 minutes idle)
16. Build clean with `-warnaserror`, all tests green with each new test seen failing, `--selftest --safe` exits 0.

## Stable order (small fix)

This part stands alone and can ship before the previews.

**Cause.** `DockModelBuilder.Build` groups windows in the order it receives them, and `DockFeature.RunningFor`
passes the tracker's z-order list. Any activation reorders both the unpinned apps and each app's `Windows`.

**Change (Core).** Keep `Build` pure and add `DockRunningOrder`, one instance per dock (held by `DockViewModel`):

```csharp
public sealed class DockRunningOrder
{
    public const long ForgetAfterMs = 2000;
    public IReadOnlyList<DockApp> Apply(IReadOnlyList<DockApp> apps, long nowMs);
}
```

- Group key: `"pin:" + LaunchId` for pinned apps, `"run:" + Identity` for unpinned ones (the keys `DockViewModel`
  already uses), compared ordinal-ignore-case. Window key: (group key, handle).
- Each key has a sequence number from a private counter and an `AbsentSinceMs` (null while present).
- On `Apply`: a present key that is unknown, or absent for more than `ForgetAfterMs` (`nowMs - AbsentSinceMs >
  ForgetAfterMs`), gets the next sequence number; a present key clears `AbsentSinceMs`; a known key missing from
  this call gets `AbsentSinceMs = nowMs` if it was null; keys absent for more than `ForgetAfterMs` are removed.
  Absence is measured from the refresh that first missed the key, never from the last refresh that saw it, so a
  long quiet period with the app present cannot age it out.
- New keys seen in the same call are numbered in input order (z-order), so simultaneous newcomers are deterministic.
- Output: pinned apps in input order, then unpinned apps by ascending sequence; each app's `Windows` sorted by
  window sequence; every other field, `MostRecentWindow` included, copied unchanged.

`DockModelBuilder` sets `MostRecentWindow`; `DockClickPlanner` uses it for Activate (falling back to `Windows[0]`
when it is 0 or not in `Windows`) and steps through the stable `Windows` for Cycle.

**Regression tests** (`tests/WinGnome.Core.Tests/Dock/DockRunningOrderTests.cs`, literal values, no loops). With
apps A, B, C unpinned (identities `path:c:\a.exe` …) and P pinned:

- `Apply_ZOrderFlips_KeepsFirstAppearanceOrder`: `[A,B]` at 0, `[B,A]` at 100 → `[A,B]`. This is the regression test:
  it fails against today's dock, where the second refresh yields `[B,A]`.
- `Apply_NewApp_IsAppendedEvenWhenTopmost`: `[A,B]`, then `[C,A,B]` → `[A,B,C]`.
- `Apply_TwoNewAppsInOneCall_AreNumberedInInputOrder`: `[]`, then `[C,A]` → `[C,A]`.
- `Apply_AppBackWithinGrace_KeepsItsSlot` / `..._AfterGrace_GoesToTheEnd` as a `[Theory]`: `[A,B,C]` at 0, `[B,C]` at
  100, `[A,B,C]` at 2100 → `[A,B,C]`; at 2101 → `[B,C,A]`.
- `Apply_LongGapWhilePresent_DoesNotReorder`: `[A,B]` at 0, `[B,A]` at 600000 → `[A,B]`.
- `Apply_PinnedApps_KeepInputOrder`: `[P2,P1,A]` (pinned P2, P1) → `[P2,P1,A]`; then `[P1,P2,A]` → `[P1,P2,A]`.
- `Apply_WindowsInsideAnApp_KeepFirstAppearanceOrder`: A with windows `[1,2]`, then `[2,1]` → `[1,2]`, and
  `MostRecentWindow` stays 2 as given.
- `Apply_NewWindow_IsAppendedInsideItsApp`: `[1,2]`, then `[3,1,2]` → `[1,2,3]`.
- `Apply_HandleReusedByAnotherApp_IsNewInThatApp`: window 5 in A at 0; at 100 A has `[6]` and B has `[7,5]` → B's
  windows `[7,5]` (handle 5 has no history in B).
- `Apply_IdentityCaseDiffers_KeepsTheSlot`: `path:c:\a.exe` then `PATH:C:\A.EXE`.
- `Apply_UnpinnedThenPinned_LeavesOtherSlotsInOrder`: `[A,B,C]`, then A pinned → `[A(pinned),B,C]`.
- `Apply_EmptyInput_ReturnsEmpty`, and `Apply_AllGoneThenBackAfterGrace_StartsFresh`.

Also: `DockModelBuilderTests.MostRecentWindow_IsFirstWindowInInputOrder` (pinned and unpinned; 0 for a pinned app
that isn't running); `DockClickPlannerTests` for Activate with `MostRecentWindow` = 22 and `Windows` `[11,22,33]`
→ 22, the stale fallback (`MostRecentWindow` 99 → 11), and `Cycle` focused on 22 → 33 with stable `Windows`. See
each test fail by reverting the sort in `Apply` (or the planner change) before trusting it.

## Risks and open questions

- **No Esc (decided).** A temporary global Esc hotkey was considered and dropped as global input interception; the
  popover closes on pointer leave, clicks and the other triggers instead.
- **Store-app close skipped as reused.** The UWP frame host's process can differ between capture and close, so
  `Decide` returns `Reused` and the close is skipped (logged at Info). Safe but surprising; KI-123.
- **Minimised windows.** `DwmRegisterThumbnail` usually has no picture for a minimised window (a tiny source size),
  so those items show the icon and "Minimised". Protected content (DRM video) shows black. Both are DWM limits;
  log as a KI with the implementation.
- **Virtual desktops.** Apps whose windows are all on another desktop leave the dock (the tracker drops cloaked
  windows) and come back at the end after the 2 s grace. Log as a KI; fix direction: let `DockRunningOrder` treat
  identities with cloaked-by-desktop windows as present.
- **Switching popover content** between icons re-registers thumbnails; a single frame with the old thumbnails at
  new rectangles is possible. If it shows in QA, hide the thumbnails before re-registering.
- **Non-layered popover** has no blur and square corners on Windows 10 (DWM rounding is Windows 11 only).
- **TOCTOU on close.** A handle could be reused between the live check and the post (milliseconds). The pid check
  makes a wrong-window close need both the handle and the process id reused in that window; accepted.
- **Grace length.** 2 s is a guess; a longer grace keeps slots through slower window swaps but delays "reopen goes
  to the end". Revisit after use.
