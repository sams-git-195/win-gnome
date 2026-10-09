# Spec 0020 WP4 — Accessibility: notes for WP9

Hand-over text for WP9 to fold into KNOWN_ISSUES.md and PLAN.md (WP4 doesn't edit the shared tables).

## KNOWN_ISSUES rows

| ID | Sev | Area | Issue | Status |
|---|---|---|---|---|
| KI-087 | S4 | Settings | Accessibility: *Cursor size* writes the undocumented `HKCU\Software\Microsoft\Accessibility\CursorSize` (1–15) and `HKCU\Control Panel\Cursors\CursorBaseSize` (32 + 16 per step), then `SPI_SETCURSORS` (`CursorSizeStore`). The row is disabled with a link to Windows Settings for black, inverted and custom-colour pointers (`CursorType` ≠ 0) and for a user cursor scheme (`Scheme Source` = 1), because Windows regenerates those cursor files through a private API. Pointer colour, text size (`TextScaleFactor`, shown read-only) and the text cursor indicator stay in Windows Settings (by design). *High contrast* is a **read-only row with a link** to `ms-settings:easeofaccess-highcontrast` until the spike below passes (spec: spike first, link if unproven); it shows the theme Windows reports (Aquatic = "High Contrast Black"/`hcblack.theme`, Desert = "High Contrast White"/`hcwhite.theme`, Dusk = "High Contrast #1"/`hc1.theme`, Night sky = "High Contrast #2"/`hc2.theme`, matched by the files' colours; the read accepts the Windows 11 name, the legacy name, the file name or a path). The write returns once the spike passes (see "Restoring the high-contrast write"). Cursor size was not live-tested either (this machine has a custom-colour pointer, so the row is correctly disabled). | Open |
| KI-088 | S4 | Settings | Slow keys and bounce keys can't both be on (Windows' `FILTERKEYS`: a bounce time needs the acceptance delay and repeat timings at 0); turning one on turns the other off and the panel says so. Turning bounce keys on also turns Windows' repeat keys off (`iDelayMSec`/`iRepeatMSec` 0, as Windows requires) and switching back to slow keys leaves them off; Windows Settings' filter keys page turns them back on. WinGnome's own surfaces don't follow high contrast (separate work). | By design |

## PLAN.md Core API rows

| Namespace | Type | Purpose |
|---|---|---|
| ControlCenter | `StickyKeysFlags`, `FilterKeysState`, `FilterKeysPlan` | Accessibility keyboard switches: read-modify-write of `STICKYKEYS`/`FILTERKEYS` that changes only the on bit (and filter keys' timings), keeps every other flag, falls back to Windows' default flags for a read without `AVAILABLE`, keeps slow and bounce keys exclusive and clamps their timings to Windows Settings' ranges |
| ControlCenter | `CursorSizeScale`, `CursorSizeAvailability`, `CaretWidth`, `ContrastThemes`, `ContrastTheme` | Pointer size step ↔ pixels and when WinGnome may change it, text cursor thickness range, Windows 11 contrast themes ↔ theme file ↔ scheme name and the High contrast row's choices |

Module map: `Features/Settings/Panels/Accessibility/` (`AccessibilityPanelViewModel`, `AccessibilityService`,
`CursorSizeStore`, view and resources), `Interop/NativeMethods.Accessibility.cs` (`STICKYKEYS`, `FILTERKEYS`,
`HIGHCONTRASTW`, the SPI constants and struct overloads of `SystemParametersInfoW`).

## Live checks done (2026-10-09, 25H2, standard account)

Baseline before: `STICKYKEYS` 0x1F2 (shortcut off by the user), `FILTERKEYS` 0x7E/1000/1000/500/0, caret 1 px,
client-area animation on, high contrast off, `CursorType` 3 (custom colour). Each change went through the panel
(UI Automation on a test instance with its own profile), was read back with a separate SPI/registry probe, and
the row showed the read-back value:

- Sticky keys on → 0x1F3, off → 0x1F2 (and `Control Panel\Accessibility\StickyKeys\Flags` 498): original bit for bit.
- Slow keys on → 0x7F/1000/1000/500/0; bounce keys on → 0x7F/0/0/0/500 (slow keys switched off); bounce keys off →
  0x7E/0/0/0/500. The original 0x7E/1000/1000/500/0 was then restored with a direct `SPI_SETFILTERKEYS` and
  confirmed (SPI and `Keyboard Response` registry values, including the `Last Valid *` values, all as before).
- Text cursor thickness 1 → 3 → 1 px; reduce animation on → off (`UserPreferencesMask` back to its original bytes).
- *Start the on-screen keyboard* started `osk.exe` (closed again). Magnifier and Narrator were not started (they
  take over the screen and audio); they use the same `ShellLaunch.SystemTool` path.
- `--safe`: every switch and slider disabled (the high-contrast row is read-only anyway), launch buttons enabled, a toggle attempt
  wrote nothing. Light and dark theme checked.

## Review notes (Opus review of WP4, S4 items)

- **FilterKeysPlan, repeat keys after bounce:** turning bounce keys on zeroes `iDelayMSec`/`iRepeatMSec` (Windows
  requires it), and switching back to slow keys leaves repeat keys off. Recorded in KI-088; Windows Settings' filter
  keys page turns them back on. Not changed: WinGnome has no repeat-keys control to restore them from.
- **CursorSizeStore, two writes:** `CursorSize` and `CursorBaseSize` are two separate registry writes before
  `SPI_SETCURSORS`; if the second fails the first stays written (the read-back then shows the step from
  `CursorSize`, and the problem banner shows). Accepted for an undocumented, disabled-on-this-machine row (KI-087).
- **AccessibilityService caret/animation read fallback:** fixed. The text cursor thickness and animation reads now
  fail the whole read (problem banner) instead of showing a fallback value Windows didn't confirm.

## Restoring the high-contrast write

The write was removed from the UI (no dead code) and comes back once the spike below passes. What it was:
`SPI_SETHIGHCONTRAST` (0x0043) with `uiParam = cbSize = Marshal.SizeOf<HIGHCONTRASTW>()` (16 on x64) and
`SPIF_UPDATEINIFILE | SPIF_SENDCHANGE`, on the settings writer thread, read-modify-write from a fresh
`SPI_GETHIGHCONTRAST`: keep every flag, set or clear `HCF_HIGHCONTRASTON` (0x1), and point `lpszDefaultScheme` at a
`Marshal.StringToHGlobalUni` copy of the theme's `Scheme` (or, for off, of the scheme Windows reported), freed after
the call. The row stays busy until the call returns (several seconds, behind Windows' "Please wait") and then
re-reads (verified-set). On this machine high contrast is off with flags 0x7E and an empty scheme;
`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\LastHighContrastTheme` holds the path of the last contrast
theme file applied (`hcblack.theme`), which suggests Windows 11 applies contrast themes by file.

## High-contrast spike (for the user)

Turns the whole desktop to a contrast theme for a few seconds at a time. Save work first. Recovery at any point:
Left Alt + Left Shift + Print Screen, or Windows Settings → Accessibility → Contrast themes → None.

1. Note the current theme (Windows Settings → Personalisation → Themes).
2. Put the write back (see above) in a local build. Start a test instance with every other feature off:
   `dotnet run --project src/WinGnome -c Release -- --settings-dir "$env:TEMP\wingnome-a11y" --settings-panel accessibility`
   with `%TEMP%\wingnome-a11y\settings.json` =
   `{"General":{"HideWindowsTaskbar":false},"TopBar":{"Enabled":false},"Dock":{"Enabled":false},"WindowButtons":{"Enabled":false}}`.
3. High contrast → **Aquatic**. Expect Windows' "Please wait", then a #202020 background with cyan links. Check:
   Windows Settings → Accessibility → Contrast themes shows *Aquatic*; the WinGnome settings window still responds;
   the row shows *Aquatic* once the "Applying the change in Windows…" line goes; the test log has
   `Settings: turn high contrast on with Aquatic`.
4. Switch directly to **Desert** (cream background), **Dusk** (#2D3236, teal links) and **Night sky** (black, violet
   links), checking the same each time.
5. Choose **None**: the theme from step 1 comes back (wallpaper, accent, light/dark mode).
6. If a theme comes out with the wrong colours or Windows Settings shows a different name, change that theme's
   `Scheme` in `ContrastThemes` to its file path (`%WINDIR%\Resources\Ease of Access Themes\<file>`) and repeat. If
   neither form applies cleanly, the row becomes a link to `ms-settings:easeofaccess-highcontrast`; record the
   result in KI-087 either way.
