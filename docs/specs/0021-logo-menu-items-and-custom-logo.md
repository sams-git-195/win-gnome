# 0021 — Logo menu items and customisable logo icon

Status: Implemented (branch logo-menu-spec). Core logic is unit-tested and mutation-checked; app-level QA — clicking the real menu items, the picker dialog, DPI and light/dark logo rendering, footprint and `--selftest` — is pending with the integrator (no app run in this worktree).

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
- The bar's logo and the Top Bar page's live preview both render the chosen mark. The row only matters while the
  logo menu is shown: the bar's logo sits in the `LogoButton`, whose visibility binds `TopBarSettings.ShowLogoMenu`,
  so with the logo menu off there is no logo anywhere. No disable dependency is added to the row.

The mark is **always a solid silhouette in the bar's foreground colour** (`TopBarSettings.ForegroundColor`,
`#FFFFFF` by default — "white" means the bar foreground, so it follows that setting like every other mark on the
bar). A custom image's own colours are never shown: its shape becomes a mask, filled with the foreground brush.

Mask rule for custom images (decided, see Risks for the alternative):

- **Images with real transparency** (at least one pixel with alpha < 255): the alpha channel is the mask; RGB is
  ignored.
- **Opaque images** (JPEG, BMP, opaque PNGs): **inverted luminance** is the mask (255 − luma, BT.601 weights), so a
  dark mark on a light background becomes a solid silhouette and antialiased edges survive. The row subtitle and
  README document that opaque images should be dark-on-light.

This pixel rule is pure logic and lives in Core (`LogoMaskRule`, see Design), with the app layer only decoding and
freezing the result.

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
tile (`Popups/QuickSettingsCard.xaml`, tooltip "Settings"). Repointing would silently change that surface; adding
members keeps the change inside the logo menu. `Popups/LogoMenuCard.xaml`'s "System Settings…" row is relabelled
"Windows Settings…" and retagged `WindowsSettings`; a "Control Panel…" row is added below it; everything else keeps
its tag.

**Trap the implementation must not fall into:** `TopBarActions.Execute`'s `default:` routes every unhandled action
to `ExecutePowerAction`, whose own switch silently ignores anything that isn't a power action. New enum members
without explicit cases therefore compile clean, pass `-warnaserror`, and do nothing when clicked. The change must
add explicit `case TopBarAction.WindowsSettings:` and `case TopBarAction.ControlPanel:` to `Execute`'s switch; ACs 1
and 2 catch this by clicking through the real menu.

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
- New `TopBar/LogoMaskRule.cs`: the alpha-vs-luminance pixel decision as pure logic over raw pixels — mode
  selection (any pixel with alpha < 255 → alpha mask; otherwise 255 − luma with BT.601 weights), Bgra32 input →
  Gray8 mask bytes, and the decode target dimension as a named constant (`MaxDimension = 256`, used by the app
  layer's decode too). No WPF types: it takes pixel buffers and dimensions, so the whole mask rule is testable.
- Tests in `tests/WinGnome.Core.Tests/TopBar/LogoSelectionTests.cs`,
  `tests/WinGnome.Core.Tests/TopBar/LogoMaskRuleTests.cs` and the existing settings tests: every rule above,
  `Normalize` clamps, old-file compatibility (a literal pre-0021 `settings.json` fragment deserialises to
  `Windows` + `""`), and the mask rule against literal expected bytes for a small alpha image, a fully opaque
  image and the one-transparent-pixel degenerate case. Each test is mutation-checked by name: swap two geometry
  keys, flip the luma coefficients, invert the mode switch, off-by-one the alpha scan.

### Rendering (app layer)

- **`Theme/LogoMarks.xaml`** — new frozen `StreamGeometry` dictionary (merged like `SymbolicIcons.xaml`) with
  `LogoFoot`, `LogoStar`, `LogoTerminal`. Original artwork for this project (the same rule `SymbolicIcons.xaml`
  states: not copied from Adwaita), drawn on the 16-unit grid with solid fills, corners on whole units so
  pixel snapping keeps them sharp.
- **`Features/TopBar/Controls/LogoGlyph.cs`** — one new `FrameworkElement` replacing `WindowsLogo` (which is
  deleted; both of its call sites, `TopBarWindow.xaml` and the Top Bar preview, switch to `LogoGlyph`). In the same
  change, the `<see cref="WindowsLogo"/>` in `Controls/SymbolicIcon.cs`'s doc comment (line 11) must be repointed to
  `LogoGlyph`: an unresolvable cref is CS1574, and the build runs `-warnaserror`. DPs:
  `Size`, `Fill` (unchanged semantics), plus the resolved selection (`Kind`, `GeometryKey`, `Mask` ImageSource).
  `OnRender` branches: `WindowsMark` keeps `WindowsLogo`'s device-pixel pane arithmetic verbatim; `Geometry`
  resolves the key with `TryFindResource` and draws it with `SymbolicIcon`'s snap pipeline (extract that `Snap`
  method into a shared internal helper both controls use, with its existing cached-snapped-shape behaviour);
  `Image` draws `Fill` through the mask (`DrawRectangle` with an `ImageBrush` of the mask as opacity mask, or
  equivalent), Uniform-fitted inside the snapped square, `HighQuality` scaling. `MeasureOverride` stays
  `new Size(Size, Size)`, `OnDpiChanged` invalidates — the bar re-renders only on settings changes, as today.
- **`Features/TopBar/Controls/LogoMask.cs`** — thin app-layer glue: decode → `LogoMaskRule.Apply` → freeze. Decode
  through a `BitmapImage` over a `FileStream` with `BitmapCacheOption.OnLoad` (the file is read once, then let go —
  never locked), `BitmapCreateOptions.IgnoreColorProfile`, and `DecodePixelWidth`/`DecodePixelHeight` set to
  `LogoMaskRule.MaxDimension` so WIC downscales **during** the decode, preserving aspect and bounding transient
  memory. (Not `BitmapDecoder` with a `Transform` — it has none — and not a full-frame `Decode()`, which
  materialises the entire image, hundreds of MB for a large photo, before any downscale.) Then `CopyPixels` to
  Bgra32, run the pixels through `LogoMaskRule.Apply`, and emit a frozen Gray8 `BitmapSource` (≤ 64 KB). A > 10 MB
  file-size pre-check rejects before decoding (it bounds the compressed file; the decode-pixel cap bounds the
  transient). ICO uses `Frames[0]` (ICO sub-images are stored largest-first in practice); GIF its first frame.
  Any exception → failure return, and the caller logs once. No `System.Drawing`, no new dependency.
- **`Features/TopBar/TopBarServices.cs`** — a small `LogoProvider` owned by the shared services (one decode
  however many bars there are). `ApplySettings` resolves via `LogoSelection` and, only when the target path
  changed, decodes on the thread pool with a generation counter (the `AppearancePanelViewModel.LoadWallpapers`
  pattern), then posts the frozen mask to the dispatcher and raises `Changed`. Every successful decode writes one
  structured log line, `LogoProvider: decoded <path> → <w×h> Gray8`, so QA can verify the decode-once rule from
  `wingnome.log` rather than by code inspection alone. Failures log once and publish `WindowsMark`.
  `TopBarServices.Dispose()` disposes the provider: a generation bump discards any in-flight decode, so no
  callback can touch a disposed service.
- **`TopBarWindow`** — gets the provider the way it already gets `PopupHost` (through `TopBarInstance`);
  `ApplySettings` sets `LogoGlyph`'s selection, and a `Changed` subscription updates the mask when the async
  decode lands. `ApplySizes` keeps sizing `Logo` exactly as now. `Shutdown()` unsubscribes the `Changed` handler
  alongside its existing event teardown. Until a mask arrives the bar shows the Windows mark (first apply) or
  keeps the previous logo (later changes) — no flicker to blank.
- **Settings UI** — `TopBarPageViewModel` gains the choice handling and exposes the resolved selection + mask for
  the preview (its own `Task.Run`/generation load through `LogoMask`, since `TopBarServices` is not reachable from
  the settings window); the view model bumps its generation when the page closes, mirroring
  `AppearancePanelViewModel.Close()`. The drop-down's revert-on-cancel has **no precedent in the codebase** — every
  existing `ChoiceSetting` is a pure value binding, and the wallpaper panel only proves the dialog half — so the
  mechanism is specified here: **"Custom image…" is a display-only entry, not a persisted value.** Selecting it
  opens the picker without writing anything through; on success the view model sets `Logo = Custom` and
  `LogoImagePath`; on cancel it restores the previous persisted selection and raises `PropertyChanged` so the
  `ComboBox` snaps back. `TopBarPage.xaml`: the new row in the Contents group and `<tb:LogoGlyph>` replacing
  `<tb:WindowsLogo>` in the preview. `VisitAllPages` (self-test) builds the page; the picker only opens on an
  explicit selection, so the self-test never shows a dialog.

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

Core-tested criteria ship with the mutation checks named in Design; each is seen red first.

1. The logo menu shows "Windows Settings…" with the "Win+I" hint and opens the Windows Settings home page.
   (Manual QA — clicking through the real menu proves `Execute` has the explicit case, not the silent
   `default:`/`ExecutePowerAction` no-op.)
2. "Control Panel…" sits directly below it and opens the classic Control Panel. (Manual QA, same trap.)
3. "WinGnome Settings…" and every other row are unchanged. (Manual QA.)
4. The quick-settings gear still opens WinGnome's settings window. (Manual QA — regression guard for keeping
   `TopBarAction.Settings`.)
5. A pre-0021 `settings.json` (no `Logo`/`LogoImagePath`) loads with `Logo == Windows`, `LogoImagePath == ""`, and
   the bar shows the Windows mark. (Core test.)
6. `Normalize()`: unknown `Logo` name/number → `Windows`; null/whitespace `LogoImagePath` → `""`; > 260 chars →
   `""`; `Custom` + empty path is preserved. (Core tests, one per rule.)
7. `LogoSelection.Resolve` returns the right `LogoTarget` for all five enum values, a missing custom file, and an
   empty custom path; the geometry keys are exactly `LogoFoot`/`LogoStar`/`LogoTerminal`. (Core test; mutation:
   swap two keys.)
8. `LogoMaskRule`: a one-transparent-pixel image takes the alpha path; a fully opaque image takes the
   inverted-luma path; both produce the literal expected Gray8 bytes; `MaxDimension` is 256. (Core tests;
   mutations: flip the luma coefficients, invert the mode switch, off-by-one the alpha scan.)
9. Choosing Foot/Star/Terminal updates the bar and the Top Bar preview immediately; each mark is solid, in the bar
   foreground colour, with straight edges sharp at 100 %, 125 % and 150 % DPI. (Manual QA.)
10. "Custom image…" picks a PNG with transparency: the bar shows its silhouette in the bar foreground colour —
    never the image's own colours — fitted without distortion; an opaque JPEG renders dark-mark-on-light as a
    silhouette. Cancelling the picker leaves the drop-down on the previously persisted choice. (Manual QA with one
    alpha PNG and one JPEG.)
11. A custom path that is deleted before the next start renders the Windows mark, logs one warning, and shows no
    dialog. (Manual QA: pick an image, delete it, restart with the same `--settings-dir`.)
12. Changing the bar's text colour recolours the custom silhouette. (Manual QA.)
13. The image file is decoded once per change: two monitors show the same logo, and `wingnome.log` carries exactly
    one `LogoProvider: decoded <path> → <w×h> Gray8` line per path change and none on reopening the menu, hovering
    or reapplying unchanged settings. (Manual QA against the log, plus code review of `LogoProvider`.)
14. Teardown: quitting WinGnome while a decode is in flight (large image on a slow disk) logs no decode line or
    callback afterwards, and the settings page's preview load stops the same way when the page closes. (Manual QA
    + code review of the three generation/unsubscribe points in Design.)
15. `dotnet build -c Release -warnaserror` and `dotnet test` pass — this also proves the `SymbolicIcon` cref was
    repointed (CS1574 would fail the build); `--selftest --safe` exits 0 (it visits the Top Bar page with the new
    row). (CI/local.)
16. Idle CPU and memory are unchanged with the default logo; with a custom logo, working-set growth is ≈ the mask
    size (≤ 64 KB). (Manual: Task Manager before/after.)
17. Docs updated: README logo-menu bullet and top-bar settings; the `TopBarPage.xaml` "Logo menu" row subtitle
    reworded generically ("A logo at the far left…" — "A Windows logo…" goes stale once a custom mark is chosen);
    PLAN.md Core API table (`LogoSelection`, `LogoMaskRule` in the TopBar row) and module map (`LogoGlyph`,
    `LogoMarks.xaml`); KNOWN_ISSUES entry **KI-103** (S4) written, covering the three points listed in Risks.
    (Review.)

## Risks and open questions

- **Foot mark IP**: the GNOME foot is associated with the GNOME Foundation. The mark must be an original drawing
  "in that spirit", not a trace of Adwaita's `start-here` icon — the same policy `SymbolicIcons.xaml` already
  follows. The Windows mark itself already ships (`WindowsLogo`); this spec doesn't change that exposure.
- **Opaque images with a light mark on a dark background** invert badly under the luminance rule (the background
  becomes the silhouette). Alternatives considered: a hard threshold (aliases badly on antialiased edges) and
  "darkest-colour-is-ink" heuristics (unpredictable). Decision: inverted luminance, documented as
  dark-mark-on-light. **KI-103** (S4, next free ID per KNOWN_ISSUES.md; entry written at implementation time)
  records this plus two neighbours: (a) the degenerate alpha case — a JPEG-like opaque image with a *single*
  stray transparent pixel switches to the alpha rule and yields a near-full-square mask; (b) the size guards bound
  the *compressed* file (10 MB) and the *decoded transient* (256 px cap), not a huge file's decompressed size in
  between. Alpha images — the normal case for logos — are unaffected.
- **Tiny source images** (< the ~21 DIP logo at high DPI) upscale softly; acceptable, no guard beyond the 256 px
  decode cap.
- **Path lifetime**: referencing the file instead of copying it means moving it silently falls back to the
  Windows mark (logged). Accepted; matches the wallpaper precedent.
- Open question (resolved by scope discipline, reopen only on request): should the quick-settings gear also target
  Windows Settings? Left as-is.

---

Advisor review (deepseek-v4-pro-0813, 2026-10-10): implementable after six must-fix drafting changes; all six
plus nice-to-haves folded in; no re-design required.
