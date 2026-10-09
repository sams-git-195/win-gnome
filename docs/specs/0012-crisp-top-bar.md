# 0012 — A crisp, GNOME-quality top bar

Status: Implemented (branch improvements/shell-round2)

## Problem
The top bar's text and icons look low resolution (user report, 2026-10-09; measured at 125 % on 2560×1600):
- Text uses `TextFormattingMode="Display"` with grayscale smoothing (`TopBarWindow.xaml:19`). The window is
  translucent, so ClearType is unavailable, and Display mode's whole-pixel glyph snapping without ClearType gives
  thin, unevenly spaced letters.
- Tray icons are 16 px bitmaps stretched to 20 device px (16 DIP at 125 %), a non-integer upscale.
- Status icons are Segoe Fluent Icons glyphs with ~1 px strokes, not GNOME's 2 px symbolic style.
- Font and icon sizes land on fractional device pixels (13.5 DIP × 1.25 = 16.875 px).
- Everything is SemiBold; GNOME's panel text is bold.
Software rendering (`App.xaml.cs`) is not the cause and stays.

## Behaviour
- Top bar and its popups use **Adwaita Sans** (GNOME 48's UI font, SIL OFL 1.1), bundled. New setting
  `TopBar.FontFamily` (`AdwaitaSans` | `SegoeUI`, default `AdwaitaSans`) on the Top Bar page as "Font".
- Clock, Activities and the battery percentage are bold; text sizes resolve to whole device pixels.
- Status icons (network, volume, battery, power, caret) are WinGnome's own GNOME-style symbolic vector icons,
  drawn on whole device pixels at 16/20/24/32 px by scale.
- Tray icons are never resampled by a non-integer factor: a 16 px icon in a 20 px slot is drawn 1:1, centred;
  larger sources scale down; integer upscales (16 → 32 at 200 %) scale up.
- Hover pills are vertically symmetric.

## Non-goals
- GPU rendering; ClearType on the translucent bar; restyling the dock or overview (follow-up if wanted).

## Design
- Core `TopBar/BarMetrics`: `SnapToDevice(dip, scale)`, `SymbolicIconPx(fontDip, scale)`,
  `TrayIconPlacement.Choose(sourcePx, slotPx)`, with tests at 1.0, 1.25, 1.5, 1.75, 2.0 and invalid inputs.
- App: `TextFormattingMode="Ideal"`, grayscale, `TextHintingMode="Auto"` on the bar and popups; `ApplySizes` uses
  `BarMetrics`; `Theme/SymbolicIcons.xaml` (frozen path data on a 16-unit grid, drawn by us — not copied from
  Adwaita's CC-BY-SA icons) and a `SymbolicIcon` element modelled on `WindowsLogo`; tray images created at 96 DPI
  with their real pixel size (`GetIconInfo`) and sized by `TrayIconPlacement`.
- Font: Adwaita Sans from GNOME's official `adwaita-fonts` release, unmodified, embedded as a WPF resource, with
  its `OFL.txt` in `assets/fonts/` and a line in the README's credits. If WPF can't select weights from the variable
  font, use the release's static instances (or Inter's static files under the same licence) and record why.

## Safety and recovery
No system state changes.

## Footprint
Fonts add a few hundred KB to the exe and are loaded once. Icons are frozen geometry; no timers, no new windows.
Idle private memory within ±2 MB of before.

## Acceptance criteria
1. Core `BarMetrics` tests, seen failing first.
2. At 125 %, a zoomed capture shows evenly spaced bold Adwaita Sans text and status icons whose edges are on whole
   pixels with strokes ≥ 2 px; a 16 px tray icon is drawn 1:1.
3. Switching the font setting applies live; 100 %, 125 % and 150 % checked; popups still fit.
4. `--selftest` passes; idle memory and thread count unchanged within tolerance.

## Risks and open questions
- Ideal mode can be slightly softer at very small sizes: compare captures before and after.
- Hand-drawn icons need a visual review against GNOME's style.

## Implementation notes
- **Font.** GNOME's `adwaita-fonts` 51.0 release ships Adwaita Sans only as a variable font
  (`sans/AdwaitaSans-Regular.ttf`, 879,796 bytes; the italic isn't needed). Its `LICENSE` is the SIL OFL 1.1 with
  copyright lines only, no Reserved Font Name. WPF lists the font's named instances (Thin..Black) under the family
  name **"Adwaita Sans Text"** (its default instance), and a test render at 40 px gave `StyleSimulations=None` for
  Normal, SemiBold and Bold with distinct advances (341.6 / 354.2 / 360.5 DIP for the same string) and visibly
  real weights, so no static instances or Inter fallback were needed. The file is embedded unmodified
  (`src/WinGnome/Assets/Fonts`), with `assets/fonts/OFL.txt` as the licence. The exe's managed DLL grows by
  ~0.9 MB.
- **Where the font applies.** `TopBarFonts.Apply` publishes the chosen family as the application resource
  `TopBarFont` (only when it changes), used by the bar, `PopupHost`'s card frame, the popup button styles and the
  end-session dialog. Popups inherit the bar's weight through their placement target, so the frame resets it to
  Regular; titles keep their own SemiBold/Bold.
- **Icon sizes.** `SymbolicIconPx` keeps the 16 / 13.5 ratio and rounds: 16, 20, 24, 28 and 32 px at 100–200 %
  (28 px at 175 %, not 32). `SymbolicIcon` moves on-curve points to whole pixels with `SnapIconUnit`, so a
  2-unit stroke is 2 or 3 px at 125 %; curves stay antialiased.
- **Tray icon size.** `Imaging.CreateBitmapSourceFromHIcon` with empty size options already yields the icon's own
  pixel size at 96 DPI (checked: a 20 px icon gives 20×20 at 96 DPI, a 40 px one 40×40), so the bitmap's
  `PixelWidth` is used instead of a separate `GetIconInfo`/`GetObject` call.
- **Icon states.** WinGnome has no Wi-Fi signal-strength source, so Wi-Fi shows one connected icon rather than
  0–4 bars (KI-055). Volume 0 shows the muted speaker, as GNOME does. The caret is used for the calendar's
  previous and next buttons, turned left and right.
- **Verified at 125 % (2560×1600)** against the old build with the same isolated profile: bold Adwaita Sans with
  even spacing, symbolic icon edges on whole pixels, live switch to Segoe UI and back, and the quick-settings
  popup. Idle private memory over two alternating runs: 84.9 / 88.0 MB before, 88.5 / 84.6 MB after; about 19
  threads in both. 100 %, 150 % and the other scales are covered by the `BarMetrics` tests. The calendar popup
  and a 16 px tray icon were not checked live: a second instance has no tray (the everyday one hosts it), and a
  popup opened through UI Automation closes at once because it can't take activation.
