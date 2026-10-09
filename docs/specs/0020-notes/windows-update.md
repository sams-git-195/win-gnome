# Spec 0020 WP8 (Windows Update): notes for WP9

## KNOWN_ISSUES rows

Merge into KI-092 (shared with the Region & Language note; spec 0020 owns KI-085..KI-092 and KI-093 on belongs to
spec 0017). KI-092 becomes "settings left to Windows Settings by design":

- KI-092 S4 Settings: the Windows Update panel shows the Windows Update Agent's cached state only (offline search,
  `Online = false`): updates Windows hasn't found yet, or that were found since the last check, are not listed until
  Windows checks. It can differ from Windows Settings' list, which also merges Microsoft Store and driver sources.
  The panel never scans or installs (By design).
- KI-092 (cont.): a standard user may be refused by policy on managed machines; the panel then shows the error with the
  Windows Settings link.

## PLAN.md rows

Core API:

| Type | Purpose |
|---|---|
| `UpdateStatus` / `UpdateStatusLines` | Cached Windows Update state and the words the panel shows |
| `UpdateStatusText.Build(status, now, culture)` / `When` / `ErrorMessage` | Relative times ("Today at 14:05", "3 days ago", "Never"), update counts, HRESULT messages, `TimedOut` |

Module map: `Features/Settings/Panels/WindowsUpdate/` (view model, `UpdateStatusService`, view),
`Interop/WindowsUpdateApi.cs` (WUA ProgIDs and the `ISearchCompletedCallback` interface).

## Spec deviations

- WUA is called late-bound (`dynamic` over the IDispatch objects, by ProgID) instead of hand-written `[ComImport]`
  copies of nine interfaces. Only `ISearchCompletedCallback` is declared. Same documented API, far less interop code.
- `UpdateStatusText.Build` takes an `UpdateStatus` record and a culture rather than six loose arguments.
- WUA returns UTC dates (checked against the WindowsUpdateClient event log); the service converts to local time.

## Checked live

Read took about 2.7 s offline on this machine and listed the 3 driver updates the agent has cached; the same read with
a 50 ms timeout aborted the search and returned the timeout code in 0.45 s with nothing left running.
