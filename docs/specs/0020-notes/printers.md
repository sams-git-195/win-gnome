# Spec 0020 WP2 (Printers): notes for WP9

## KNOWN_ISSUES rows

Merge into KI-091 (shared with the AutoPlay note):

- KI-091 S4 Settings: `LegacyDefaultPrinterMode` (HKCU `Software\Microsoft\Windows NT\CurrentVersion\Windows`,
  DWORD, 1 = the user manages the default printer, 0 or absent = Windows does) is undocumented but stable since
  Windows 10 1511. Isolated in `PrinterService`; the panel shows what it reads back.

New rows (WP9 picks the numbers):

- S4 Settings: the Printers list is one `EnumPrinters` (level 2, local and connections) per open, action and Refresh.
  There is no change notification, so a printer added, removed or going offline elsewhere shows on the next
  Refresh. A print server that doesn't answer keeps the spinner-less list empty until its RPC times out (the read runs
  on its own thread, so the window stays responsive).
- S4 Settings: when the print spooler service is stopped or disabled the list reads as empty with the "couldn't read
  the printer list" banner; the *Let Windows manage my default printer* switch still works (it is a registry value).
- S4 Settings: *Print queue*, *Printer properties* and *Printing preferences* start `rundll32.exe printui.dll,
  PrintUIEntry /o|/p|/e /n "<name>"`; a printer whose name contains a double quote can't be passed on (PrintUIEntry
  has no escape) and the panel points to Windows Settings instead.

## PLAN.md Core API rows

| Type | Where | Purpose |
|---|---|---|
| `PrinterStatusText.Describe(status, attributes, jobs)` | `ControlCenter/PrinterStatusText.cs` | "Ready", "Offline", "Paper jam", "2 jobs" or "Printing, 2 jobs" from `PRINTER_INFO_2` bits (most serious bit wins; `WORK_OFFLINE` attribute counts as offline) |
| `PrintUiCommand.Build(PrintUiAction, name)` | `ControlCenter/PrintUiCommand.cs` | rundll32 `PrintUIEntry` arguments (`/o` queue, `/p` properties, `/e` preferences) with the name quoted; null for an empty name or one containing `"` |

App layer: `Features/Settings/Panels/Printers/` (`PrinterService`, `PrintersPanelViewModel`, `PrintersPanel`),
`Interop/NativeMethods.Printing.cs` (winspool: `EnumPrintersW`, `GetDefaultPrinterW`, `SetDefaultPrinterW`).
