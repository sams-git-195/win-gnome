# 0021 — Logo menu items and customisable logo icon

Status: Draft

## Problem

Three gaps in the top bar's logo menu, all raised by the user:

1. The menu's "System Settings…" row opens **WinGnome's** settings window (`TopBarAction.Settings` →
   `context.Commands.ShowSettings()`), but its name and its "Win+I" hint both say Windows Settings — Win+I opens
   `ms-settings:`, not WinGnome. Users who click it expecting Windows get WinGnome's window.
2. The classic Control Panel is unreachable from the menu, although Task Manager (its sibling System32 tool) is.
3. The bar's logo is hard-coded to the four-pane Windows mark (`WindowsLogo`). There is no way to put a personal or
   GNOME-flavoured mark there, which matters most to users running WinGnome as their shell.

## Behaviour

### 1. Menu rows

The logo menu's second group becomes:

- **Windows Settings…** — hint "Win+I" (kept; now accurate), opens the top-level `ms-settings:` URI.
- **Control Panel…** — new row directly below, launches `%SystemRoot%\System32\control.exe`.
- **WinGnome Settings…** — unchanged (opens WinGnome's settings window on its first page / last panel).
- **Microsoft Store…** — unchanged.

Keyboard navigation (arrows/Enter/Esc) picks the new row up automatically; `LogoMenuCard` collects every `Button`
child.

The quick-settings card's gear tile keeps its current behaviour (opens WinGnome's settings window): it is a
different surface and was not part of the request. See Design for how the actions are arranged to guarantee this.

### 2. Customisable logo icon

Settings → Top Bar → **Contents** group gains a **Logo icon** row, directly under the existing "Logo menu" row:

- A drop-down of five choices: **Windows** (the current four-pane mark, default), **Foot** (an original GNOME-style
  foot/"start here" mark), **Star**, **Terminal** (a `>_` prompt in a rounded box), and **Custom image…**.
- Choosing **Custom image…** opens a file picker (PNG, ICO, JPG/JPEG, BMP, GIF, TIF; WPF's `OpenFileDialog`, same
  pattern as the wallpaper picker). On success the choice persists as *Custom* with the file's path and the bar
  updates; on cancel the drop-down reverts to the previously persisted choice.
- When *Custom* is active, the row's subtitle shows the chosen file's path; otherwise the subtitle is descriptive
  text ("The mark at the left end of the bar.").
- The bar's logo and the Top Bar page's live preview both render the chosen mark.

The mark is **always a solid silhouette in the bar's foreground colour** (`TopBarSettings.ForegroundColor`,
`#FFFFFF` by default — "white" means the bar foreground, so it follows that setting like every other mark on the
bar). A custom image's own colours are never shown: its shape becomes a mask, filled with the foreground brush.

Mask rule for custom images (decided, see Risks for the alternative):

- **Images with real transparency** (at least one pixel with alpha < 255): the alpha channel is the mask; RGB is
  ignored.
- **Opaque images** (JPEG, BMP, opaque PNGs): **inverted luminance** is the mask (255 − luma, BT.601 weights), so a
  dark mark on a light background becomes a solid silhouette and antialiased edges survive. The row subtitle and
  README document that opaque images should be dark-on-light.

Fallback: a custom path that is missing, unreadable, not an image, or larger than 10 MB renders the **Windows**
mark and writes one `Log.Warn` per path change — never a dialog, never per bar, never per frame.

The image is fitted **Uniform** inside the logo's square (aspect preserved), sized exactly as today:
`BarMetrics.SnapToDevice(iconSize * LogoToIconRatio 0.875, scale)`, whole device pixels at every DPI.

### Settings added

| Property | Type | Default | Where in Settings |
|---|---|---|---|
| `TopBar.Logo` | enum `TopBarLogo { Windows, Foot, Star, Terminal, Custom }` | `Windows` | Top Bar → Contents → "Logo icon" |
| `TopBar.LogoImagePath` | `string` | `""` (empty = unset) | same row, via "Custom image…" picker |

JSON shape (both additive; old `settings.json` files simply lack them and load with the defaults — the serializer
already ignores unknown fields and `Normalize()` supplies defaults):

```json
"TopBar": {
  "Logo": "Windows",
  "LogoImagePath": ""
}
```

`TopBarSettings.Normalize()` gains:

- `Logo = EnumSetting.Normalize(Logo, TopBarLogo.Windows);` (the lenient enum converter turns an unknown name or
  number into an undefined value, which this resets).
- `LogoImagePath`: `null` → `""`; trim; longer than 260 characters → `""`. `Custom` with an empty path is left
  alone by `Normalize` — the render-time fallback handles it, so a temporarily missing file (e.g. a network drive)
  doesn't destroy the user's choice.

## Non-goals

- No icon packs, themes or galleries; just the four built-in marks plus one custom file.
- No URL/remote images, no SVG (WPF has no native SVG decoder), no animated images (GIF decodes its first frame).
- No colour logos, no per-monitor logos, no logo in the dock/overview/settings chrome.
- No change to the quick-settings gear, the `WinGnomeSettings` action, or any keyboard shortcut.
- No copying the custom image into the WinGnome profile (see Safety).

## Design

### Actions and launches (app layer, `Features/TopBar/TopBarActions.cs`)

Add two enum members, **keep `Settings` as it is**:

- `TopBarAction.WindowsSettings` → `context.Launcher.Launch("ms-settings:")`. `LaunchPlanner.IsUri` classifies it
  as a URI, so `AppLauncher` hands it to the scheme's handler — the same path `MicrosoftStore` already uses.
- `TopBarAction.ControlPanel` → `context.Launcher.Launch(Path.Combine(Environment.SystemDirectory, "control.exe"))`.

Why new members instead of repointing `Settings`: `Settings` has a second caller — the quick-settings card's gear
tile (`QuickSettingsCard.xaml`, tooltip "Settings"). Repointing would silently change that surface; adding members
keeps the change inside the logo menu. `LogoMenuCard.xaml`'s "System Settings…" row is relabelled "Windows
Settings…" and retagged `WindowsSettings`; a "Control Panel…" row is added below it; everything else keeps its tag.

Why `AppLauncher` and not `ShellLaunch.SystemTool` for control.exe: Task Manager, two cases above in the same
switch, launches through `context.Launcher` with the identical `Environment.SystemDirectory` pattern, and
`ShellLaunch` is internal to `Features/Settings` — calling it from `Features/TopBar` would cross the feature
independence rule. `AppLauncher` shell-executes the rooted path (never a search-path substitution, never elevated).

### Core (`WinGnome.Core`, testable)

- `Settings/AppSettings.cs`: the `TopBarLogo` enum, the two properties and the `Normalize()` additions above, next
  to `TopBarFont`/`BarMonitors`.
- New `TopBar/LogoSelection.cs`: the decision logic as a pure function, file existence injected:

  ```csharp
  public enum LogoKind { WindowsMark, Geometry, Image }
  public sealed record LogoTarget(LogoKind Kind, string? GeometryKey, string? Path);
  public static class LogoSelection
  {
      public static LogoTarget Resolve(TopBarLogo logo, string path, Func<string, bool> fileExists);
  }
  ```

  Rules: `Windows` → `WindowsMark`; `Foot`/`Star`/`Terminal` → `Geometry("LogoFoot"/"LogoStar"/"LogoTerminal")`;
  `Custom` with a non-blank path that `fileExists` accepts → `Image(path)`; anything else (`Custom` with an empty
  path, a missing file) → `WindowsMark` — the caller logs, Core doesn't. The geometry-key map lives here so it is
  covered by tests.
- Tests in `tests/WinGnome.Core.Tests/TopBar/LogoSelectionTests.cs` and the existing settings tests: every rule
  above, `Normalize` clamps, and old-file compatibility (a literal pre-0021 `settings.json` fragment deserialises
  to `Windows` + `""`).

### Rendering (app layer)

- **`Theme/LogoMarks.xaml`** — new frozen `StreamGeometry` dictionary (merged like `SymbolicIcons.xaml`) with
  `LogoFoot`, `LogoStar`, `LogoTerminal`. Original artwork for this project (the same rule `SymbolicIcons.xaml`
  states: not copied from Adwaita), drawn on the 16-unit grid with solid fills, corners on whole units so
  pixel snapping keeps them sharp.
- **`Features/TopBar/Controls/LogoGlyph.cs`** — one new `FrameworkElement` replacing `WindowsLogo` (which is
  deleted; both of its call sites, `TopBarWindow.xaml` and the Top Bar preview, switch to `LogoGlyph`). DPs:
  `Size`, `Fill` (unchanged semantics), plus the resolved selection (`Kind`, `GeometryKey`, `Mask` ImageSource).
  `OnRender` branches: `WindowsMark` keeps `WindowsLogo`'s device-pixel pane arithmetic verbatim; `Geometry`
  resolves the key with `TryFindResource` and draws it with `SymbolicIcon`'s snap pipeline (extract that `Snap`
  method into a shared internal helper both controls use, with its existing cached-snapped-shape behaviour);
  `Image` draws `Fill` through the mask (`DrawRectangle` with an `ImageBrush` of the mask as opacity mask, or
  equivalent), Uniform-fitted inside the snapped square, `HighQuality` scaling. `MeasureOverride` stays
  `new Size(Size, Size)`, `OnDpiChanged` invalidates — the bar re-renders only on settings changes, as today.
- **`Features/TopBar/Controls/LogoMask.cs`** — static factory turning a file into a frozen mask, WPF-native
  (`BitmapDecoder`/`BitmapFrame.Decode` with `BitmapCacheOption.OnLoad`, so the file is read once and never
  locked; no `System.Drawing`, no new dependency). Guards: > 10 MB rejected; either dimension > 256 downscaled
  (aspect preserved) before the mask pass; decode via `CopyPixels` to Bgra32, apply the alpha-or-inverted-luma
  rule, output a frozen Gray8 `BitmapSource` (≤ 64 KB). ICO works through WPF's registered decoder (first/largest
  frame). Any exception → `false` + caller logs.
- **`Features/TopBar/TopBarServices.cs`** — a small `LogoProvider` owned by the shared services (one decode
  however many bars there are). `ApplySettings` resolves via `LogoSelection` and, only when the target path
  changed, decodes on the thread pool with a generation counter (the `AppearancePanelViewModel.LoadWallpapers`
  pattern), then posts the frozen mask to the dispatcher and raises `Changed`. Failures log once and publish
  `WindowsMark`. `Dispose` cancels/ignores in-flight work.
- **`TopBarWindow`** — gets the provider the way it already gets `PopupHost` (through `TopBarInstance`);
  `ApplySettings` sets `LogoGlyph`'s selection, and a `Changed` subscription updates the mask when the async
  decode lands. `ApplySizes` keeps sizing `Logo` exactly as now. Until a mask arrives the bar shows the Windows
  mark (first apply) or keeps the previous logo (later changes) — no flicker to blank.
- **Settings UI** — `TopBarPageViewModel` gains the choice handling (built-ins set `Logo` and clear nothing;
  "Custom image…" opens the picker, sets `Logo = Custom` + `LogoImagePath`, reverts on cancel) and exposes the
  resolved selection + mask for the preview (its own `Task.Run`/generation load through `LogoMask`, since
  `TopBarServices` is not reachable from the settings window). `TopBarPage.xaml`: the new row in the Contents
  group and `<tb:LogoGlyph>` replacing `<tb:WindowsLogo>` in the preview. `VisitAllPages` (self-test) builds the
  page; the picker only opens on an explicit selection, so the self-test never shows a dialog.

## Safety and recovery

No system state changes: no registry, no hooks, no windows owned by other processes, nothing to restore on exit,
crash or force-kill; `--safe` and `--selftest` behave as before (the new row must simply build during
`VisitAllPages`). The custom image is a **user file, opened read-only and never copied into the profile**: one
source of truth (the user can edit or replace it in place and re-pick), no profile bloat, and it matches how the
wallpaper setting already stores a path. The consequence — a deleted or moved file — is handled by the
Windows-mark fallback plus one log line, at bar creation and at every settings apply, with no dialog spam.
Launches are plain `ShellExecute` of a URI and a rooted System32 path, never elevated.

## Footprint

Zero idle cost: no timers, no hooks, no polling, no per-frame work — `LogoGlyph` repaints only when the bar
invalidates, exactly like `WindowsLogo`/`SymbolicIcon` today. The mask is decoded once per path change (not per
bar, not per render) off the dispatcher, and the cached artifact is a frozen Gray8 bitmap of at most 256×256
(64 KB), released when the selection changes or services dispose. With a built-in mark chosen there is no bitmap
at all and no file I/O.

## Acceptance criteria

1. The logo menu shows "Windows Settings…" with the "Win+I" hint and opens the Windows Settings home page.
   (Manual QA.)
2. "Control Panel…" sits directly below it and opens the classic Control Panel. (Manual QA.)
3. "WinGnome Settings…" and every other row are unchanged. (Manual QA.)
4. The quick-settings gear still opens WinGnome's settings window. (Manual QA — regression guard for keeping
   `TopBarAction.Settings`.)
5. A pre-0021 `settings.json` (no `Logo`/`LogoImagePath`) loads with `Logo == Windows`, `LogoImagePath == ""`, and
   the bar shows the Windows mark. (Core test; seen red by removing the defaults.)
6. `Normalize()`: unknown `Logo` name/number → `Windows`; null/whitespace `LogoImagePath` → `""`; > 260 chars →
   `""`; `Custom` + empty path is preserved. (Core tests, one per rule, each mutation-checked.)
7. `LogoSelection.Resolve` returns the right `LogoTarget` for all five enum values, a missing custom file, and an
   empty custom path; the geometry keys are exactly `LogoFoot`/`LogoStar`/`LogoTerminal`. (Core tests,
   mutation-checked by swapping two keys.)
8. Choosing Foot/Star/Terminal updates the bar and the Top Bar preview immediately; each mark is solid, in the bar
   foreground colour, with straight edges sharp at 100 %, 125 % and 150 % DPI. (Manual QA.)
9. "Custom image…" picks a PNG with transparency: the bar shows its silhouette in the bar foreground colour — never
   the image's own colours — fitted without distortion; an opaque JPEG renders dark-mark-on-light as a silhouette
   (inverted luminance). (Manual QA with one alpha PNG and one JPEG.)
10. A custom path that is deleted before the next start renders the Windows mark, logs one warning, and shows no
    dialog. (Manual QA: pick an image, delete it, restart with the same `--settings-dir`.)
11. Changing the bar's text colour recolours the custom silhouette. (Manual QA.)
12. The image file is decoded once per change: two monitors show the same logo from one decode, and reopening the
    menu or hovering never re-reads the file. (Code review of `LogoProvider` + manual check of the log.)
13. `dotnet build -c Release -warnaserror` and `dotnet test` pass; `--selftest --safe` exits 0 (it visits the Top
    Bar page with the new row). (CI/local.)
14. Idle CPU and memory are unchanged with the default logo; with a custom logo, working-set growth is ≈ the mask
    size (≤ 64 KB). (Manual: Task Manager before/after.)
15. Docs updated: README logo-menu bullet and top-bar settings; PLAN.md Core API table (`LogoSelection` in the
    TopBar row) and module map (`LogoGlyph`, `LogoMarks.xaml`); KNOWN_ISSUES entry for the opaque-image rule
    (criterion below). (Review.)

## Risks and open questions

- **Foot mark IP**: the GNOME foot is associated with the GNOME Foundation. The mark must be an original drawing
  "in that spirit", not a trace of Adwaita's `start-here` icon — the same policy `SymbolicIcons.xaml` already
  follows. The Windows mark itself already ships (`WindowsLogo`); this spec doesn't change that exposure.
- **Opaque images with a light mark on a dark background** invert badly under the luminance rule (the background
  becomes the silhouette). Alternatives considered: a hard threshold (aliases badly on antialiased edges) and
  "darkest-colour-is-ink" heuristics (unpredictable). Decision: inverted luminance, documented as
  dark-mark-on-light, with a KNOWN_ISSUES entry for the limitation. Alpha images — the normal case for logos —
  are unaffected.
- **Tiny source images** (< the ~21 DIP logo at high DPI) upscale softly; acceptable, no guard beyond the 256 px
  decode cap.
- **Path lifetime**: referencing the file instead of copying it means moving it silently falls back to the
  Windows mark (logged). Accepted; matches the wallpaper precedent.
- Open question (resolved by scope discipline, reopen only on request): should the quick-settings gear also target
  Windows Settings? Left as-is.
