# Spec 0020 WP5 (Region & Language): notes for WP9

## KNOWN_ISSUES rows

- KI-092 S4 Settings: the format locale and display language are changed in Windows Settings (By design). The panel
  shows them read-only with links. Region, the five formats and the first day of the week are written here.
  KI-092 also takes the Windows Update panel's cached-only, read-only status (see windows-update.md).
- Related to KI-063: the Region writes (`SetLocaleInfoW`, `SetUserGeoName`) were checked live for the first day of the
  week and the short date (changed, read back, restored exactly). Region (`SetUserGeoName`), long date, short/long time
  and *Reset to defaults* use the same code path but were not changed live.
- New finding (add to KI-092 text): .NET keeps the user's formats on the `CurrentCulture` instance it already built, so
  `ClearCachedData()` alone does not make the clock and calendar follow a format change; `SettingsFeature` replaces
  the culture on `WM_SETTINGCHANGE "intl"` (see PLAN note below).

## PLAN.md rows

Core API:

| Type | Purpose |
|---|---|
| `RegionFormatChoices.Build(patterns, current)` | Distinct date/time patterns in locale order, the current one first if it isn't listed |
| `FirstDayOfWeek` | Windows `LOCALE_IFIRSTDAYOFWEEK` (0 = Monday .. 6 = Sunday) to and from `DayOfWeek`; `All` is Monday first |
| `GeoList` / `GeoEntry` | Country list: sort, search and find by name |

Module map: `Features/Settings/Panels/RegionLanguage/` (view model, `RegionService`, view), `Interop/NativeMethods.Locale.cs`.

Culture refresh (PLAN.md, app layer): `SettingsFeature` listens to `SystemEvents.UserPreferenceChanged` (`Locale`
category, i.e. `WM_SETTINGCHANGE "intl"`), and on the dispatcher calls `ClearCachedData()` then sets
`CultureInfo.CurrentCulture` and `DefaultThreadCurrentCulture` to a new read-only
`CultureInfo(name, useUserOverride: true)`, where `name` is `GetUserDefaultLocaleName` (so a format-locale change in
Windows Settings is followed too).
Verified live: before the change a clear alone left the old short date in the process for 1.5 s+; with the new
instance the date, the first day of the week and pool threads follow in both directions.

## Spec corrections

- The spec's "`ClearCachedData()`, which suffices" is wrong (see above).
- Live tests must run outside the agent sandbox (launch through `explorer.exe`): from a sandboxed shell the HKCU
  writes are virtualised and Windows' locale code never sees them.
