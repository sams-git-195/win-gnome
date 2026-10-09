# 0009 — Round buttons on more custom title bars

Status: Agreed

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
- Core click guard decision: `ProbedClickCheck.Allows(expectedCode, actualCode)`.
- App `CustomCaptionProbe`: deepest-child hit testing (`ChildWindowFromPointEx` with skip-invisible/transparent,
  walking down), records the answering HWND; profile path. `ProbedCaption.Source` (HitTest, ChildHitTest, Profile)
  for logging. Click guard in `CaptionCommands` path for probed windows.
- Settings page: the new toggle with its subtitle; the experimental subtitle names the apps.

## Safety and recovery
No system state changes; probed windows are never recoloured. The click guard prevents a stale or wrong decoration
from sending a window command where the app no longer has that button.

## Footprint
Unchanged at idle. Child lookups add a few cheap calls per sample; the click guard adds one message per click.

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
- A future GitHub Desktop release could move its buttons; the hole check and click guard then leave it undecorated.
