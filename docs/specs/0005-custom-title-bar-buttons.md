# 0005 — Window buttons on custom title bars (experimental)

Status: Implemented (merged in 652c6cc)

## Problem
Apps that draw their own title bar (Electron, Chromium, WinUI 3, Windows Terminal) keep their own buttons
(KI-007), because `DWMWA_CAPTION_BUTTON_BOUNDS` is empty or stale for them. The user's everyday apps are of
this kind. Apps that support Windows 11 Snap Layouts do say where their buttons are when asked what is under a
point (`WM_NCHITTEST`): Claude desktop answers `HTMINBUTTON`/`HTMAXBUTTON`/`HTCLOSE` over three 56 px zones at
125 %. GitHub Desktop and Dia answer `HTCLIENT`.

## Behaviour
- New setting `WindowButtons.DecorateCustomTitleBars` (bool, default **false**), on the Window buttons page under
  Behaviour: "Decorate apps with custom title bars (experimental)", with a subtitle saying it only works for apps
  that report their buttons (such as Claude desktop) and that the others keep their own buttons.
- When on, windows of the skipped classes (except DWM's `Ghost`) are probed. A window is decorated only when
  the probe reads, from the right, exactly three touching zones: close, maximise, minimise. It's then treated
  like any other decorated window: same look, colour sampling (these title bars are never recoloured), and the
  same click path (`WM_SYSCOMMAND`).
- When the window's size or DPI changes, the overlay hides and the window is probed again. If the zones are gone
  or don't line up, the decoration is removed and the native buttons stay.
- A failed probe is remembered for that window at that size and DPI. It is retried after a resize (move/size
  end, or the next window-list change) or a DPI change.
- Turning the setting off removes every probed decoration. Old settings files load with it off.

## Non-goals
- Apps that answer `HTCLIENT` over their buttons (HTML buttons in GitHub Desktop, Dia). They can't be supported
  without injection or app-specific geometry.
- UI Automation (measured too slow and incomplete; see spec 0001).

## Design
- Core `CaptionHitTestProbe`: sample positions (scaled by DPI), `FindGroupLeft` (order, adjacency, minimum width
  of 24 DIPs, nothing button-like further left), `FindCloseExtent` (vertical extent from a 1 px column through
  the close button), `ButtonsRect`. `CaptionDecorationRules.CanProbeSkippedClass`.
  `WindowFilter.CanDecorate(..., drawsOwnButtons)` doesn't require `WS_SYSMENU`, which Electron drops.
- App `CustomCaptionProbe.Probe` runs on the thread pool. It sends `WM_NCHITTEST` with `SendMessageTimeout`
  (`SMTO_ABORTIFHUNG`, 50 ms) about 100 times along a row 10 DIPs below the frame top, over the right-most
  200 DIPs (2 px apart), and about 64 times down a column through the close button, giving up at the first
  failure. The result (`ProbedCaption`, relative to the frame, with the frame size and DPI it is valid for)
  comes back to the dispatcher.
- `CaptionMetrics.TryReadProbed` measures probed windows from the probe and reports when it is stale.
  `DecoratedWindow` asks once for a re-probe. `CaptionOverlayManager` owns the probing set, the negative cache
  and teardown.

## Safety and recovery
No system state changes beyond what decorated windows already do. Probed windows are never recoloured
(they draw their own caption). Elevated windows are excluded as before. Nothing is sent to other processes
from the UI thread.

## Footprint
Nothing when the setting is off. When on: one probe per eligible window at start, and one per resize or DPI
change. At most about 200 cross-process messages per probe, on a pool thread. No timers and no polling. While
a probed window is being resized, probes run one after another (never more than one per window at a time),
and the overlay stays hidden until the size settles.

## Acceptance criteria
1. With the setting off, Chromium, Electron and WinUI windows are not probed or decorated.
2. With it on, Claude desktop is decorated and its patch matches its title bar. GitHub Desktop, Dia and
   Windows Terminal are left alone.
3. After a resize the circles follow the new button position.
4. Clicking a circle acts on the window through the existing path.
5. Old settings files load with the setting off (unit test).

## Risks and open questions
- An app might answer the hit-test codes but draw its buttons somewhere else. The minimum width and exact
  order checks reduce the risk; the setting is opt-in and labelled experimental.
- The row is probed 10 DIPs below the top. Apps with caption buttons shorter than that aren't found.
- Hit-testing a window takes its UI thread a few microseconds per message. A hung app gives up after one
  50 ms timeout.
