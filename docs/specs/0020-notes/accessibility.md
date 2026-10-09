# Spec 0020 WP4 — Accessibility: notes for WP9

Hand-over text for WP9 to fold into KNOWN_ISSUES.md and PLAN.md (WP4 doesn't edit the shared tables).

## KNOWN_ISSUES rows

| ID | Sev | Area | Issue | Status |
|---|---|---|---|---|
| KI-087 | S4 | Settings | Accessibility: *Cursor size* writes the undocumented `HKCU\Software\Microsoft\Accessibility\CursorSize` (1–15) and `HKCU\Control Panel\Cursors\CursorBaseSize` (32 + 16 per step), then `SPI_SETCURSORS` (`CursorSizeStore`). The row is disabled with a link to Windows Settings for black, inverted and custom-colour pointers (`CursorType` ≠ 0) and for a user cursor scheme (`Scheme Source` = 1), because Windows regenerates those cursor files through a private API. Pointer colour, text size (`TextScaleFactor`, shown read-only) and the text cursor indicator stay in Windows Settings (by design). *High contrast* writes `SPI_SETHIGHCONTRAST` with the theme's legacy scheme name (Aquatic = "High Contrast Black"/`hcblack.theme`, Desert = "High Contrast White"/`hcwhite.theme`, Dusk = "High Contrast #1"/`hc1.theme`, Night sky = "High Contrast #2"/`hc2.theme`, matched by the files' colours); the read accepts the Windows 11 name, the legacy name, the file name or a path. **Spike pending** (WP4 step 1 was not run live: it flips the whole desktop): see "High-contrast spike" below. Until it passes, the row is unverified on 25H2; the legacy scheme names are English and may not apply on a localised Windows. Cursor size was not live-tested either (this machine has a custom-colour pointer, so the row is correctly disabled). | Open |
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
- `--safe`: every switch, slider and the high-contrast drop-down disabled, launch buttons enabled, a toggle attempt
  wrote nothing. Light and dark theme checked.

## High-contrast spike (for the user)

Turns the whole desktop to a contrast theme for a few seconds at a time. Save work first. Recovery at any point:
Left Alt + Left Shift + Print Screen, or Windows Settings → Accessibility → Contrast themes → None.

1. Note the current theme (Windows Settings → Personalisation → Themes).
2. Quit nothing; start a test instance with every other feature off:
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
