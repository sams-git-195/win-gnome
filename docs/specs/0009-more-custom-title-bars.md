# 0009 — Round buttons on more custom title bars

Status: Implemented (branch improvements/shell-polish; the Dia rule is verified by tests only)

## Problem
With *Decorate apps with custom title bars (experimental)* on (spec 0005), Claude desktop is decorated, but Dia,
Docker Desktop and GitHub Desktop keep their own buttons, and VS Code is unconfirmed. Probes on 2026-10-09 at 125 %:
- **Dia** (Windows App SDK) answers `HTCLIENT` at the top level; its buttons live in a
  `ReunionWindowingCaptionControls` child window (UIA scoped to that child sees Minimize/Maximize/Close in 37 ms).
- **Docker Desktop** (`Chrome_WidgetWin_0`) answers button codes, but with a 2 px `HTCAPTION` gap between maximise
  and minimise and the close button starting 9 px below the top, which the probe rejects. Its class isn't skipped,
  so its stale `DWMWA_CAPTION_BUTTON_BOUNDS` (146×22) would place circles in the wrong place.
- **GitHub Desktop** draws HTML buttons: three `HTCLIENT` "no-drag" holes (45 DIP each, 1 px `HTCAPTION` between) at
  the right edge, 34 px tall, inside an `HTCAPTION` drag region.
- **VS Code** (Electron window-controls overlay) should answer button codes like Claude.

## Behaviour
- Under the existing experimental setting:
  - Hit tests are sent to the deepest visible child window under each sample point, so WinUI 3 / Windows App SDK
    apps such as Dia are decorated.
  - Short `HTCAPTION` gaps (≤ 2 DIP) between buttons and a close button starting up to 8 DIP below the frame top are
    accepted; the three runs must be roughly equal (within 25 %) and the group at least 3 × 30 DIP wide.
  - Every `Chrome_WidgetWin_*` class is treated as drawing its own buttons (probed, never trusted from DWM).
- New setting `WindowButtons.DecorateWebTitleBarButtons` (bool, default **false**), shown under the experimental
  setting and only enabled when it's on: "Also decorate apps with web-drawn buttons (GitHub Desktop)". It enables a
  small built-in profile list keyed by executable name and window class, giving the expected button size. A window
  matching a profile is decorated only when the hit-test row shows exactly three equal `HTCLIENT` holes of the
  profile's width at the right edge, separated by ≤ 2 DIP, with `HTCAPTION` to their left. Starts with GitHub Desktop.
- Safety guards for every probed window:
  - Before acting on a click, the click point is hit-tested again (one 50 ms message off the UI thread); if the
    answer no longer matches what the probe saw there (button code, or `HTCLIENT` for a profile), the click is
    dropped and the window re-probed.
  - Windows with `WS_EX_LAYOUTRTL` aren't decorated.
  - The maximise circle's glyph follows `WS_MAXIMIZE`, never pixels.
  - At most 3 probes per window per minute; after two failed probes at the same size, wait for the next size change.
- Old settings files load with the new setting off.

## Non-goals
- Pixel/glyph recognition of buttons (rejected on review: theme and zoom changes break it silently, `PrintWindow`
  forces paints in GPU apps, and a false positive maps a click to the wrong window command).
- UI Automation as a primary detector.

## Design
- Core `CaptionDecorationRules`: `Chrome_WidgetWin_` prefix; `WebButtonProfiles` (exe + class → button width DIP).
- Core `CaptionHitTestProbe.FindGroupLeft`: gap tolerance, equal-width and minimum-span checks, `MaxTopGap` 8 DIP.
- Core `CaptionHoleProbe.FindClientHoles(xs, codes, scale, expectedWidthDip)`.
- Core click guard decision: `ProbedClickCheck.Allows(expectedCode, actualCode)` (a consistency check, strong only
  where the expected answer is a button code; see Safety and recovery).
- App `CustomCaptionProbe`: deepest-child hit testing (`ChildWindowFromPointEx` with skip-invisible/transparent,
  walking down), records the answering HWND; profile path. `ProbedCaption.Source` (HitTest, ChildHitTest, Profile)
  for logging. Click guard in `CaptionCommands` path for probed windows.
- Settings page: the new toggle with its subtitle; the experimental subtitle names the apps.

## Safety and recovery
No system state changes; probed windows are never recoloured. For windows that report button codes, the click guard
stops a stale or wrong decoration from sending a window command where the app no longer has that button. For the
web-button paths it is much weaker: over HTML holes it only confirms the point is still client area, which most
of a window is, so it catches a layout that turned into drag region but not one where the buttons moved elsewhere
in the client area (KI-041). The maximise-anchored path also re-checks the maximise zone, which is a real button
code. Those paths rely mainly on the strict row checks and stay behind their own opt-in setting.

## Footprint
Unchanged at idle. Child lookups add a few cheap calls per sample; the click guard adds one message per click (two
beside a maximise anchor). Probes are debounced and use one shared one-shot timer. The KI-016 change re-samples a
decorated window's patch once per settled move or resize by code: one screen `GetPixel` (a few milliseconds of
DWM read-back) 450 ms after the last location change, on the same kind of shared one-shot timer; user drags don't
add one, since they already sample on `MOVESIZEEND`.

## Acceptance criteria
1. Core tests: Docker's row (45/43/gap 2/43 px at 125 %) accepted; a 5 DIP gap rejected; unequal runs rejected; wrong
   order rejected; close starting 9 px down accepted, 12 DIP rejected; `Chrome_WidgetWin_0` and `_2` skipped, `Ghost`
   never probed; GitHub Desktop's hole row accepted (restored, with 7 px resize edge, and maximised, touching the
   edge); two or four holes, unequal holes, holes away from the edge, no caption to the left all rejected; click guard
   truth table.
2. Manual: Claude still decorated. VS Code (isolated `--user-data-dir`), Dia (new window), Docker (if its window is
   shown) and GitHub Desktop (with the new toggle) decorated, circles over the app's buttons, all three actions work.
3. With the new toggle off, GitHub Desktop keeps its own buttons.

## Risks and open questions
- Dia's restored geometry wasn't measured (it was minimised during research).
- A future GitHub Desktop release could move its buttons. The hole check then leaves it undecorated at the next
  probe; until then the click guard only notices if the old position stopped being client area (KI-041).

## Implementation notes
- **Dia isn't decorated.** Measured on a restored Dia window (2251×1419 at 125 %, 2026-10-09): the deepest child
  under the maximise button is `InputNonClientPointerSource`, which answers `HTMAXBUTTON` (for Snap Layouts), but
  under the close and minimise buttons it's `Microsoft.UI.Content.DesktopChildSiteBridge`, which answers `HTCLIENT`;
  `ReunionWindowingCaptionControls` is 0 px wide and answers nothing. The row reads, from the right: 5 px
  `HTRIGHT`, 54 px `HTCLIENT` (close), 50 px `HTMAXBUTTON`, then `HTCLIENT`. So deepest-child hit testing (kept:
  it's how input is routed, and other Windows App SDK apps that set all their non-client regions will answer) finds
  only one button.
- **Addendum: maximise-anchored rule for Dia** (agreed after the measurements above). Under
  `DecorateWebTitleBarButtons`, and only for windows with a visible `ReunionWindowingCaptionControls` child
  (Windows App SDK caption controls), Core `CaptionMaxAnchorProbe.FindGroup` accepts, from the right: at most
  8 DIPs of resize border, an `HTCLIENT` close zone, immediately an `HTMAXBUTTON` zone of at least 30 DIPs, then
  immediately `HTCLIENT` for at least the maximise width (the minimise zone, taken to be that wide), with no
  other button code in the row. Two points differ from the rule as first proposed, because Dia's measured row
  wouldn't pass them: the close zone (59 px with the border) is 9 px wider than maximise (49–50 px), so the
  widths must be within the usual 25 % ratio rather than 2 DIPs; and left of the minimise zone Dia reports more
  `HTCLIENT` (its caption input sink), not `HTCAPTION`, so nothing is required there beyond the minimise width.
  The controls child is 0 px wide on Dia, so "has the child" means present and visible, not non-empty. The
  vertical extent comes from a column through the maximise zone (`FindZoneExtent`). The click guard expects
  `HTMAXBUTTON` over maximise and `HTCLIENT` over close and minimise, and for those two also re-checks that
  maximise still answers `HTMAXBUTTON` (two hit tests). Verified by Core tests from the measured row only; Dia
  stayed minimised, and it isn't relaunched by agents (that restored the user's window).
- Docker Desktop's window was hidden throughout, so its row is covered by Core tests from the research numbers only.
- The click guard hit-tests the middle of the clicked button's third of the probed group, on the probe row (10 DIPs
  below the frame top), not the click point itself: with the circles on the left or smaller than the native
  buttons, the click point isn't over the native button.
- Probes wait for a size to settle (300 ms after the last location change) instead of re-probing continuously
  during a live resize. A failed probe is retried once 2 s later; two failures at one size block that size. A
  decorated window that returns to the size of its previous probe (restore after maximise) reuses it without
  probing, so toggling maximise doesn't use up the 3 probes a minute.
- For web buttons the column through the close hole must end at a non-client row (GitHub Desktop has a 1 px
  `HTCAPTION` line under its buttons); the overlay starts at the frame top because the top resize border is drawn
  over the buttons. The hole row is sampled every pixel (the strips between holes are 1 px).
- Glyphs: the circles' glyphs never depend on pixels; maximise/restore uses `IsZoomed` (`WS_MAXIMIZE`).

## Verification (2026-10-09, 125 %)
- Claude desktop, VS Code (isolated `--user-data-dir`) and GitHub Desktop (web toggle on) were decorated; the
  overlays lay exactly over the native groups (VS Code 1861–2032 px, Claude 2087–2258, GitHub Desktop 1306–1474).
  With the web toggle off GitHub Desktop wasn't decorated.
- VS Code, resized and moved by code and maximised/restored three times: the overlay followed each size within
  1.5 s (maximised 2388–2560 at the top edge).
- Clicking the circles wasn't exercised (it needs real input on the user's desktop with the everyday instance's
  overlays on top); the guard's decision logic is covered by Core tests.
