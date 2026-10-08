# 0003 — GNOME look tweaks

Status: Implemented

## Problem
WinGnome moves the shell, window buttons and overview towards GNOME, but the rest of Windows keeps its own
look: a random or wallpaper-derived accent colour, accent-coloured title bars, and a desktop full of icons.
GNOME's defaults are calm and neutral: Adwaita blue is the one accent, window chrome is neutral, and the
desktop is empty. The Streamline page already has reversible HKCU tweaks; this adds a short, coherent group
of them that make stock Windows read as GNOME without any injection into other processes.

## Behaviour
A new Streamline group, **GNOME look**, with four opt-in tweaks (all off by default, each reversible alone):

| Id | Title | What it does |
|---|---|---|
| `gnome-accent` | Adwaita blue accent | Sets the Windows accent colour to Adwaita blue `#3584E4` and turns off "pick an accent from my wallpaper". Controls, selections, toggles and focus rings use it. |
| `neutral-chrome` | Neutral title bars, Start and taskbar | Turns off "show accent colour on title bars and window borders" and "on Start and taskbar", so window chrome and shell surfaces stay neutral like GNOME's. |
| `hide-desktop-icons` | Empty desktop | Hides all desktop icons (`Explorer\Advanced\HideIcons`). Needs an Explorer restart. |
| `hide-spotlight-icon` | Hide "Learn about this picture" | Hides the Windows Spotlight desktop icon. Needs an Explorer restart. |

No new settings; enabled tweaks are tracked like every other tweak (`EnabledTweaks` and `tweaks-backup.json`).
Theme-affecting tweaks (`gnome-accent`, `neutral-chrome`) broadcast `WM_SETTINGCHANGE` "ImmersiveColorSet" on
apply and on revert, using the existing `BroadcastThemeChange` flag.

Registry values (all HKCU, no admin):

- `gnome-accent`
  - `Software\Microsoft\Windows\CurrentVersion\Explorer\Accent`: `AccentColorMenu` DWORD `0xFFE48435` (ABGR),
    `StartColorMenu` DWORD (the darker shade, ABGR), `AccentPalette` 32-byte binary (8 RGBA swatches: three
    tints, the base, three shades, and Windows' fixed eighth swatch `88 17 98 00`).
  - `Software\Microsoft\Windows\DWM`: `AccentColor` DWORD `0xFFE48435`, `ColorizationColor` DWORD `0xC43584E4`.
  - `Control Panel\Desktop`: `AutoColorization` DWORD `0` (otherwise Windows replaces the accent from the
    wallpaper at the next theme refresh).
- `neutral-chrome`: `Software\Microsoft\Windows\DWM\ColorPrevalence` = 0 and
  `Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\ColorPrevalence` = 0.
- `hide-desktop-icons`: `Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\HideIcons` = 1.
- `hide-spotlight-icon`: `Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel`
  value `{2cc5ca98-6485-489a-920e-b3e88a6ccce3}` = 1.

## Non-goals
- Transparency off (a strong global change that also affects Mica/acrylic in every app).
- Accent on Start/taskbar (GNOME's shell is neutral, so the tweak does the opposite).
- Changing the dark/light mode (the existing *Dark mode* tweak) or the wallpaper.
- Reproducing Windows' exact internal palette algorithm: the shades are a documented approximation.

## Design
- `WinGnome.Core/Tweaks/AccentColorChanges.cs` (new): pure function from an sRGB colour to the list of
  `RegistryChange`s above, including the palette bytes. Tested with literal expected bytes.
- `TweakCatalog`: four new definitions after `dark-mode`; `TweakCategory.GnomeLook` added at the end of the enum.
- `StreamlinePageViewModel`: title/description for the new category. Nothing else in the app layer changes:
  `TweakService` already broadcasts after both apply and revert, and offers the Explorer-restart button.
- Catalogue test that every sub key starts with `Software\` is widened to also allow `Control Panel\`
  (only `AutoColorization`).

## Safety and recovery
HKCU only. The engine records each original value (including "did not exist") and persists the backup
before writing; revert restores exactly, deleting values that were absent. A value Windows rewrote
meanwhile is left alone only if it no longer equals ours (existing engine behaviour). `--safe` and
`--selftest` use the in-memory registry and skip the broadcast.

## Footprint
None at idle: registry writes on click, one broadcast on a worker thread.

## Acceptance criteria
1. The catalogue lists the four tweaks, in order, with the values above.
2. `AccentColorChanges` produces the exact ABGR/ARGB values and 32-byte palette for `#3584E4`.
3. Apply writes every value; IsApplied is true only when all are present.
4. Revert restores prior values, and deletes values that did not exist (including `AutoColorization` and
   the `HideDesktopIcons` value), for each of the four tweaks.
5. Backup JSON round-trip restores the accent tweak's binary and DWORD values.
6. Only `gnome-accent`, `neutral-chrome` and `dark-mode` broadcast theme changes; only the two desktop tweaks
   require an Explorer restart.
7. Build with `-warnaserror`, `dotnet test` and `--selftest --safe` pass.

## Risks and open questions
- Accent shades are approximated; Windows may normalise `ColorizationColor` or the palette, which would make
  the toggle read as off until reapplied.
- Live refresh of accent / chrome relies on the same `ImmersiveColorSet` broadcast the Settings app triggers;
  some surfaces (Start, taskbar) may only pick the change up after Explorer restarts or a sign-out.
- `hide-spotlight-icon` is community-documented, not Microsoft-documented.
