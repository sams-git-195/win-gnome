# 0008 — Smooth overview open and close

Status: Implemented (branch improvements/shell-polish)

## Problem
Pressing Super shows a flash and then a very rapid jump into the overview. Causes found in the code:
- The window is resized from 1×1 to full screen and shown at once; with software rendering the first full-screen
  frame takes several refreshes, during which the acrylic backdrop shows without dimmer or thumbnails.
- The dimmer opacity is set in one step.
- The 220 ms cubic glide's clock starts before the first frame is on screen, so most of the motion is lost.
- Thumbnails start from the visible bounds while DWM draws the whole window, so the first frame jumps.
- Closing has no animation.

## Behaviour
- Opening: the overview appears in one clean step (the system acrylic can't fade), the dim layer eases in, and
  window thumbnails glide from their real positions to the grid over 250 ms with an ease-out-quad curve (GNOME's
  overview easing). Search and captions fade in after the glide as now.
- Closing (Esc, Super, picking a window, clicking empty space): thumbnails glide back to their windows and the dim
  layer eases out over 200 ms, then the overview hides. The picked window is activated at the start.
- Pressing Super again mid-animation reverses from wherever the thumbnails are.
- When Windows animations are off (`SPI_GETCLIENTAREAANIMATION`), opening and closing are instant.
- No new settings.

## Non-goals
- Fading the acrylic blur itself (Windows offers no way).
- Workspace strip or app-grid animations.

## Design
- Core `Overview/OverviewTransition` (replacing or extending `ThumbnailTransition`): `EaseOutQuad`, `Progress`,
  `ThumbnailTrack(From, To, FromOpacity, ToOpacity)` with `At` and `Reversed`, `Retarget`, `DimAlpha`,
  `DurationFor(animationsEnabled, opening)`.
- App: prepare while cloaked (`DWMWA_CLOAK`): resize, show, register thumbnails at their start rects, dimmer
  transparent; uncloak once the first frame has been presented (in practice the third new rendered frame) and
  start the clock from the next frame's rendering time. Start rects from `GetWindowRect` read at open time. Dimmer
  animated through its brush alpha from the same `CompositionTarget.Rendering` handler (unsubscribed when the
  animation ends). If measurement shows fewer than ~12 frames per glide, the dim layer moves to a small layered
  window behind the overview.
- Closing runs the reversed tracks, then hides and shrinks the window to 1×1 as now. Input is ignored while
  closing except Super/hot corner, which reverse it.

### As implemented
- `OverviewTransition` replaces `ThumbnailTransition` (adding `ThumbnailTrack.Still`). The open/close state machine
  is Core `OverviewTransitionState`: one position from the windows (0) to the grid (1) shared by every segment, so
  a reversal takes the full duration times the distance left; the clock starts at a segment's first frame; window
  changes during a glide (and during a close that is reversed) are applied once it settles; a watchdog delay
  (duration + 300 ms) forces a segment to finish if frames stop. `Features/Overview/OverviewAnimator` drives it
  from `CompositionTarget.Rendering`. Thumbnail tracks are retargeted from where they are at each segment start.
- WPF does render while cloaked, but `Rendering` only announces a frame: revealing on the second frame still
  showed the bare backdrop on a cold open, so the window is uncloaked on the third new frame, or 250 ms after the
  preparation ends.
- Measured 12–15 frames per 250 ms opening and 8–13 per 200 ms close: the same 40–60 fps either way, the close is
  just shorter. The dim stays in the brush alpha; no layered dim window (KI-032).
- Thumbnails are registered bottom-up in z-order so the first frame matches the desktop, and the picked window's
  thumbnail is re-registered on close so it lands on top.
- First open (KI-030, resolved in 1e4522f): WPF's first full-screen software frame made it appear 230–540 ms after
  the key. Three seconds after start, at `ApplicationIdle`, the overview is shown once cloaked and not activated,
  with window icons laid out in place of thumbnails (no DWM registration), rendered for three frames and hidden and
  shrunk again. First opens now appear 66–113 ms after the key, like later ones. An open during the warm-up takes
  the window over.
- Closing from search results or the app grid is instant. See KI-031 (paths not yet verified live) and KI-032.

## Safety and recovery
No system state changes. If the uncloak never happens (no frame rendered within 250 ms), uncloak anyway so the
overview can't get stuck invisible while holding focus.

## Footprint
Per-frame work only during the 200–250 ms animations. Nothing new at idle; the hidden window is still shrunk.
The one-time warm-up costs 100–210 ms of UI-thread time 3 s after start and raises private memory from ~56 MB to
~86 MB (WPF's software renderer and caches). That is what the first open allocates anyway: with or without the
warm-up the process sits at 87–88 MB after one open and close. It is only an extra cost for someone who never
opens the overview.

## Acceptance criteria
1. Core tests: easing at 0, 0.5, 1, clamping and NaN; progress with zero duration and negative elapsed; rect
   interpolation with negative coordinates; opacity both ways; reversed tracks; retarget from mid-way; dim alpha
   at the ends; zero duration when animations are off.
2. Manual: Super opens with no black or empty frame; the glide visibly takes about a quarter of a second; closing
   glides back; repeated Super presses mid-animation reverse smoothly; time from key-up to first frame logged and
   under 150 ms.
3. Footprint: no `Rendering` subscription remains after an animation (checked in a debug log line).

## Risks and open questions
- Whether WPF renders into a cloaked window (fallback: show with thumbnails and dimmer transparent).
- Closing adds ~200 ms before the picked window is in front (it's activated at the start, so typing works at once).
