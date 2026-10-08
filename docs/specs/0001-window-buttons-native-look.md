# 0001 — Window buttons that blend into the title bar

Status: Implemented (merged in 45ae75a)

## Problem
The user reports that the round window buttons sit on an opaque patch that "looks weird" and does not match
the title bar (dark theme, MacOS preset, unified colour on), and that the circles appear on some windows only.

Reproduced on Windows 11 at 125 % DPI (dark mode, unified colour on):

1. **Wrong colour on Mica title bars.** File Explorer and Notepad paint their own (Mica) title bar, so the patch
   colour is sampled from the screen 150 ms after an activation change. Mica cross-fades between its active
   and inactive look over about 300 ms (measured: `#202020` → `#120314`, settled at ~300 ms), so the sample lands
   mid-fade and the patch keeps an in-between colour: active Explorer `#140312` behind a `#1B1219` patch,
   inactive `#202020` behind `#1D171C`.
2. **The patch hides the window border.** DWM's caption-button rectangle includes the window's outermost
   pixel row and column, where Windows 11 draws the 1 px window border (grey, or the accent colour). The patch
   covers that border along the top and right edge of the buttons, so the border visibly stops at the patch and
   the patch reads as a rectangle stuck on the window. The rounded cut uses the outer window radius, so the
   border's arc at the corner is hidden too.

Coverage: every standard-frame window tested is decorated (Control Panel, File Explorer, Notepad, Character
Map). Windows that are not decorated draw their own caption buttons: Electron/Chromium (Claude, GitHub
Desktop) and WinUI 3 (Dia) and UWP (Settings, Calculator) report an empty `DWMWA_CAPTION_BUTTON_BOUNDS`;
Windows Terminal is skipped by class.

## Behaviour
- The patch stops inside the window border: its top and right edges are inset by the DWM border thickness
  (`DWMWA_VISIBLE_FRAME_BORDER_THICKNESS`, 1 px fallback) where the native buttons touch the frame edge, and its
  rounded corner follows the border's inner radius. Maximised windows have no border and are not inset.
- After an activation change the title bar is sampled twice: at 150 ms (quick feedback) and again at 450 ms,
  after the Mica fade has settled, so the final colour is the real title bar colour.
- No settings added or changed.

## Non-goals
- Decorating apps that draw their own caption buttons (Chromium, Electron, WinUI 3, UWP, Terminal). See Risks.
- Reproducing Mica's horizontal gradient under the circles (the sampled colour is flat).

## Design
- Core: `CaptionButtonGeometry.InsideBorder(buttons, frame, border)` returns the buttons rectangle without the
  border pixels on the sides where it touches the frame; `CaptionButtonGeometry.InnerCornerRadius(outer, border)`.
- App: `CaptionMetrics` reads the border thickness; `DecoratedWindow` lays out over the inset rectangle and uses
  the inner radius; `CaptionOverlayManager` runs a second, settle sample pass from the same one-shot timer.

## Safety and recovery
No new system state. Caption colouring is unchanged.

## Footprint
One extra `DwmGetWindowAttribute` per layout read, one extra one-shot timer tick and one `GetPixel` per decorated
window whose colour is sampled, per activation change. No polling.

## Acceptance criteria
1. `InsideBorder` removes the top row and right column only where the buttons touch the frame (unit tests).
2. Captured active and inactive Explorer title bars match the patch colour exactly once settled.
3. The window border is visible along the top and right edge of the patch in captures.
4. Unified-colour windows (Control Panel) still match exactly.

## Risks and open questions
- Apps that draw their own buttons report an empty `DWMWA_CAPTION_BUTTON_BOUNDS`. Options measured:
  - **UI Automation**: Dia's tree had 3,896 buttons and took 2.97 s to search, with no caption buttons found.
    GitHub Desktop exposed nothing until Chromium accessibility is switched on, which is expensive inside the
    target app. Rejected.
  - **`WM_NCHITTEST` probing** (`SendMessageTimeout`, no injection): apps that support Snap Layouts answer
    `HTMINBUTTON`/`HTMAXBUTTON`/`HTCLOSE` over their own buttons. Claude desktop (Electron, Windows Controls
    Overlay) answered three 56 px runs at 125 %. GitHub Desktop (HTML buttons) and Dia answered `HTCLIENT`. This is
    the realistic route for a follow-up: probe once per size change (never per move), stop at the first timeout,
    remember failures for each window size, take the colour from sampling, and keep it behind an opt-in setting.
    Not done here because it sends cross-process messages from the UI thread and needs its own spec.
