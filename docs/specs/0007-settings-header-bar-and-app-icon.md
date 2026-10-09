# 0007 — Settings header bar with round buttons, and the app icon

Status: Implemented (branch improvements/shell-polish)

## Problem
The settings window uses the standard Windows title bar and the default app icon. WinGnome's own windows
never get round buttons: `WindowTracker.Inspect` skips WinGnome's process, deliberately, so overlays are
never decorated. The executable has no icon (KI-002) and the About page shows no logo.

## Behaviour
- `WinGnome.exe`, the settings window, Alt+Tab and Task Manager show the WinGnome logo.
- The About page shows the logo (72 DIPs) beside the name and version.
- The settings window gets a GNOME-style header bar: no Windows caption; the window's own round buttons drawn
  in WPF, using the user's window-button palette, side, order and size from `WindowButtons` settings, even when
  the Window buttons feature is off. Inactive windows dim the buttons as the overlay does.
- Hovering the maximise circle shows Windows 11 Snap Layouts. Dragging the header moves the window, double-click
  maximises, right-click opens the system menu. Resize borders, the DWM shadow and rounded corners stay.
- The app picker dialog gets the same header bar with a close button only. Confirmation dialogs stay buttonless.
- No new settings.

## Non-goals
- Decorating WinGnome's own windows through the overlay feature (would need exceptions in the tracker's
  own-process filter that every surface relies on, and would couple Settings to WindowButtons).

## Design
- Logo: `src/WinGnome/Theme/Logo.xaml`, a `DrawingImage` hand-derived from `assets/logo/wingnome.svg`
  (comment in both pointing at each other). `tools/Export-AppIcon.ps1` renders it with WPF's
  `RenderTargetBitmap` to 16, 20, 24, 32, 40, 48, 64 and 256 px and packs a PNG-compressed `.ico`
  (`src/WinGnome/Assets/wingnome.ico`, committed) plus `assets/logo/wingnome-256.png`. `ApplicationIcon` set in the
  csproj. No new packages.
- `TrafficLightButtonsView` and `GlyphGeometry` move to a shared `src/WinGnome/Controls/TrafficLights/` folder.
- Core: `CaptionButtonLayout.ComputeForHeaderBar(widthDip, heightDip, WindowButtonSettings, closeOnly)` (the synthetic
  native rect `TitleBarPreviewViewModel` builds today), used by the preview and the header bar; `closeOnly` lays out
  the dialog's single close circle. `CaptionButtonHitTest.FindInHeaderBar` maps a WM_NCHITTEST pixel position to a circle.
- `HeaderBarWindow` helper (app): applies `WindowChrome` (caption height 46, glass frame 1, resize border),
  hosts the buttons, maps clicks to `SystemCommands`, adds the maximised margin, and hooks `WM_NCHITTEST` to return
  `HTMAXBUTTON` over the maximise circle (plus `WM_NCLBUTTONDOWN/UP`, `WM_NCMOUSEMOVE/LEAVE` handling for hover
  and click there), `HTCLIENT` over the other circles.

## Safety and recovery
No system state changes.

## Footprint
None at idle; the settings window only exists while open.

## Acceptance criteria
1. Core tests for `ComputeForHeaderBar`: left/right side, both orders, exact centres at a known width, shrink path,
   diameter below minimum, NaN spacing, zero width; the preview gives the same geometry as before.
2. The built exe shows the logo in Explorer; the settings window shows it in Alt+Tab.
3. Settings shows round buttons in the user's style on the configured side; minimise, maximise/restore, close
   work; hovering maximise shows Snap Layouts; light and dark mode; 100% and one other DPI.
4. The window drags, resizes and maximises without clipping content.

## Risks and open questions
- `Logo.xaml` is a second representation of the logo; the SVG stays the source of truth.
- The maximise circle is non-client area (HTMAXBUTTON), so it has no mouse capture: pressing it and dragging off
  cancels the click, and releasing outside the window does nothing. A double-click toggles once. The self-test checks
  that the header bar's hook answers HTMAXBUTTON there, since that depends on hook order inside WPF's WindowChrome.
- The app picker drops its minimise and maximise boxes (no Snap Layouts, no double-click maximise).
- Another WinGnome instance can draw its overlay over the header bar (KI-050); the circles are UI Automation buttons and, in the header bar, a keyboard tab stop (KI-051, fixed in fea478b).
