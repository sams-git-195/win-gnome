# Spec 0020 WP6 — Privacy: notes for WP9

Handed over for the integration package (not merged into the shared docs by WP6).

## KNOWN_ISSUES row

| KI-086 | S4 | Settings | Privacy switches write the undocumented `CapabilityAccessManager\ConsentStore` values (HKCU `Value`, `NonPackaged\Value`, per-package `Value`); the device-wide switches (HKLM) are read-only and need an administrator. A per-app value of `Prompt` is shown off and turning it on stores `Allow`; a value other than Allow/Deny/Prompt is shown off and left unchangeable. Desktop apps have no per-app switch (Windows has none) and show only *In use* / *Last used*. Packaged apps the app catalogue can't name (system packages) are hidden. | Open |

Live check (this machine): microphone "Let apps access" off then on, "Let desktop apps access" off then on, and one
packaged app (Snipping Tool, which stored `Prompt`) on then off, each through the panel; the registry value and the
switch both matched after every change, and all values were restored (the app back to `Prompt`). That the Camera app
then reports no access without sign-out (AC 7) was not exercised.

## PLAN.md Core API rows (ControlCenter)

| Type | Purpose |
|---|---|
| `ConsentValue.Parse/IsAllowed/ToText` (`ConsentState`) | The consent store's REG_SZ values: absent counts as allowed, `Prompt` and unknown text as off |
| `ConsentEffective.For` / `ForMaster` | What a per-app or master switch shows and whether it can change, from the device-wide, user and app values |
| `ConsentAppList.Build(keys, packagedName, clock)`, `LastUsedText` | Consent keys to the sorted app list (packaged first, desktop named after their file, in-use and last-used text from FILETIMEs and an injected `ConsentClock`) |

Module map: `Features/Settings/Panels/Privacy/` holds `ConsentStore` (the only class touching the keys),
`PrivacyPanelViewModel` (with the per-capability and per-app view models) and the views; the verified-set switch is the
shared `Panels/VerifiedSwitch` (now with `CanChange`).
