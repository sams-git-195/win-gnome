# 0018 — Overview workspaces, app folders, dock drag and file search

Status: Agreed — user decisions 2026-10-09; advisor review (Fable) applied 2026-10-09. Ready for implementation after
spec 0010; spike first.

## Problem
Four GNOME habits are missing from WinGnome's overview and dock (README roadmap items 2, 5 and 6):
- The overview shows only the current virtual desktop's windows. GNOME 40+ shows a strip of workspace thumbnails, lets
  you switch by clicking one, and lets you drag a window onto another workspace (or a new one).
- The app grid is one flat alphabetical list of 150+ apps. GNOME has folders (drop an app on another to make one, a
  default *Utilities* folder) and a user-defined order.
- The dock already reorders pins by dragging (`DockWindow.StartPinDrag`, `DockPins.Reorder`), but a running unpinned
  app can't be pinned by dragging it among the pins, and a pin can't be removed by dragging it off the dock.
- Typing in the overview finds apps and windows, never files. GNOME's search also lists files from the user's folders.

## Behaviour
### Workspace thumbnails (decision 1)
- In window mode the overview shows a **workspace strip** between the search pill and the window grid: one thumbnail
  per Windows virtual desktop, in Explorer's order, the current one with an accent-coloured ring. Thumbnails are GNOME's
  size (at most 1/10 of the primary monitor's size each), shrinking to fit when there are many.
- Each thumbnail shows the desktop's wallpaper (the current wallpaper image, downscaled and cached) with its windows
  as rounded rectangles at their scaled positions on the primary monitor, back to front, each with the app icon centred.
  No live content in the strip (see Footprint); the main grid keeps live DWM thumbnails of the current desktop's windows.
- Hover shows the desktop's name (Explorer's `Desktops\{GUID}\Name`, else "Desktop N") as a tooltip.
- **Click a thumbnail** → the overview closes and Windows switches to that desktop through the existing
  `VirtualDesktopMonitor.SwitchTo` path (Ctrl+Win+arrows, KI-003). Clicking the current one closes the overview.
  (Decided by the user 2026-10-09: clicking a workspace closes the overview.)
- The strip is hidden when there is one desktop, except during a window drag (below), when it appears with the
  *new workspace* slot. Windows shown on all desktops (defined in Design, *Other desktops' windows*) are drawn on every
  thumbnail.
- Setting: **`Activities.ShowWorkspaceThumbnails`** (bool, default `true`), Settings → WinGnome → Activities,
  "Workspace thumbnails".

### Move a window to another workspace (decision 2)
- Drag a window thumbnail from the grid (past the system drag distance) and it follows the pointer at half size. Drop it
  on a workspace thumbnail to move the window there; the grid and strip refresh. Drop it on the trailing **"+" slot**
  (shown only while dragging) to create a new desktop at the end and move the window there. Esc or a drop elsewhere
  cancels; the thumbnail glides back.
- Refused, with a short notice in the overview ("Can't move this window"/reason), when: the mover is unavailable (unknown
  Windows build or update, `--safe`, `--selftest`, shell mode, a failed fingerprint check, a crash recorded on this
  build, or the COM objects failed), the window is elevated, the window is shown on all desktops, or the window has gone
  or its handle now belongs to another process. On an unsupported build the drag still starts but the strip slots show
  the no-drop state and the notice names the version: "Moving windows isn't supported on this Windows build
  (26100.4351)".
- Spec 0016's Super+Shift+Page Up/Down uses the same mover service (`IWindowDesktopMover`, below); this spec owns it.

### App grid folders and order (decision 3)
- **Drag to reorder** any app or folder in the grid; others make room as you drag. The order is saved. Until the first
  reorder the grid stays alphabetical; afterwards new apps are appended at the end (GNOME).
- **Drop an app on another app** (pointer over the target's centre half for 400 ms, the target grows to show the folder
  preview) → a folder named "Unnamed Folder" is created in the target's place containing both, and its popup opens with
  the name ready to edit.
- **Folder icon**: a rounded square with a 2×2 grid of the first four apps' icons, the name below. Click (or Enter) opens
  the **folder popup** over the grid: name at the top with a pencil button to rename (Enter saves, Esc cancels, empty
  name is refused), the folder's apps in a grid, reorderable by drag. Esc or a click outside the popup closes it first;
  a second Esc closes the overview.
- **Drag an app onto a folder** adds it. **Drag an app out of the popup** (past its edge) closes the popup and drops the
  app into the main grid at the pointer position.
- A folder is **deleted when its last app leaves** (GNOME's rule: `FolderView.removeApp` deletes at zero apps; a
  one-app folder stays until that app is dragged out). Decided to follow GNOME; the user may still prefer deleting at
  fewer than two (decided by the user 2026-10-09: deleted when empty).
- A default **Utilities** folder is created once per profile, filled by a built-in rule that matches Windows' system
  tools (rule in Design). Like GNOME's category folders, newly installed matching tools join it; an app dragged out is
  excluded from the rule so it doesn't come back. Renaming or emptying it is allowed; it is never re-created except by
  *Reset*.
- Search results still include apps that live in folders. Right-click menus (pin, run as administrator) work inside the
  popup too.
- Settings → WinGnome → Activities gains a **Reset application grid** button (confirmation dialog): alphabetical order,
  only the default Utilities folder.
- New settings section **`AppGrid`** (persisted, old files load with defaults):
  - `AppGrid.Order` (`List<string>`, default empty = alphabetical): launch ids and `folder:<id>` entries in grid order.
    Ids no longer in the catalogue are pruned whenever an edit is saved.
  - `AppGrid.Folders` (`List<AppFolder>`, default empty): `Id` (string), `Name` (string), `Apps` (`List<string>`
    launch ids, folder order), `Rules` (`List<string>`, built-in rule ids, e.g. `"WindowsTools"`), `ExcludedApps`
    (`List<string>`).
  - `AppGrid.DefaultFoldersAdded` (bool, default `false`): set once Utilities has been created.

### Dock drag (decision 4)
- Existing: dragging a pin among pins reorders and persists (`DockSettings.PinnedApps` order). Unchanged.
- New: **drag a running unpinned app** into the pinned part of the dock → it becomes a pin at the drop position (with the
  catalogue name and launch id, as *Pin to Dock* does). Dropped elsewhere on the dock, nothing changes. Apps with no
  launch id (no catalogue entry and no executable path) can't be dragged.
- New: **drag a pin off the dock** and release it outside → the pin is removed (GNOME). While the pointer is outside the
  dock (by about one cell, see Design) the item's slot collapses, so the user sees the result before releasing. Esc
  cancels (pin kept). If the app has windows it stays in the dock as a running app.
- No new settings.

### File search (decision 5)
- When the query has at least 2 characters, a **Files** section lists up to **5** files from the Windows Search index,
  after **Windows** (order decided by the user 2026-10-09: Applications, Windows, Files). Each
  row: shell icon, file name, folder shown relative to the profile (`~\Documents\Reports`).
- Matches file names (not contents), every typed word as a prefix, ranked by the indexer's rank. Scope: the user's
  Desktop, Documents, Downloads, Pictures, Music and Videos (their real paths, including OneDrive or other redirection),
  subfolders included.
- Results update as you type: 150 ms debounce after the last keystroke, a newer query supersedes any queued one, stale
  results are dropped. The rest of the results show immediately; Files fills in when ready (no layout jump: the section
  appears below Applications with its header only once results arrive).
- Arrow keys move through Files like other results. **Enter** opens the selected file with its default app (in-process
  `ShellExecuteEx`, never elevated; Ctrl+Shift is ignored for files). Enter with nothing moved still opens the top hit.
- If Windows Search is off, ADO is missing, the index is unreachable or a query fails, the Files section stays hidden
  (logged once per overview open).
- Setting: **`Activities.SearchFiles`** (bool, default `true`), Settings → WinGnome → Activities, "Search files".

## Non-goals
- Live DWM content in the workspace strip; per-desktop wallpapers; renaming, removing or reordering desktops; keyboard
  workspace shortcuts (spec 0016); workspaces in shell mode (spec 0013 phase 3).
- Showing other desktops' windows in the main grid, or windows from non-primary monitors in the strip.
- Dragging apps from the grid to the dock or the desktop, nested folders, paged grid, keyboard reordering.
- Content (full-text) file search, non-indexed folders, other libraries, network locations, "open file location".

## Design
### Shared workspace service (prerequisite for the strip, the mover and spec 0016)
- `VirtualDesktopMonitor` moves from `Features/TopBar/Services/` to **`Services/Workspaces/VirtualDesktopService.cs`**,
  created once in `App` and exposed as `ShellContext.Workspaces` (features never reference each other; the top bar's
  `WorkspacesViewModel` uses it from the context). It keeps the registry watchers and `DesktopSwitchPlanner`. The move
  happens **once**, in whichever of spec 0016 and this spec ships first; the other builds on it. Spec 0016 refers to
  this location (its Workspaces section says `Services/`; the path above is the agreed one).
- Core `Workspaces/VirtualDesktopState` keeps `Count`/`CurrentIndex` (equality used for change detection) and gains a
  static `ParseIds(byte[]?) -> IReadOnlyList<Guid>`; a new Core `VirtualDesktopList` record (ordered ids, current id,
  names) is built from the same reads. Tests: malformed, empty, one, many, unknown current id.
- `Interop/VirtualDesktopManager.cs` (new): documented `IVirtualDesktopManager` (`IsWindowOnCurrentVirtualDesktop`,
  `GetWindowDesktopId`; `MoveWindowToDesktop` is declared but not used — it returns E_ACCESSDENIED for other processes'
  windows, which is why decision 2 needs the internal API).
- **Other desktops' windows**: `WindowTracker` filters out cloaked windows, and other desktops' windows are cloaked by
  the shell (`DWM_CLOAKED_SHELL`). `WindowTracker.SnapshotAllDesktops()` (new, on demand only) enumerates top-level
  windows once, reads each one's `DWMWA_CLOAKED` flags, and keeps those passing a new Core rule
  `WindowFilter.IsTaskWindowOnAnyDesktop(WindowInfo info, int cloakFlags)`: the task-switcher rule
  (`IsTaskSwitcherWindow`), except that a window cloaked *only* by the shell counts. The cloak flags are passed
  explicitly; `WindowInfo` is unchanged. Tests: uncloaked, shell-only, app-cloaked, shell+app, inherited.
- **"Shown on all desktops"**: a window that is *not* shell-cloaked while `GetWindowDesktopId` returns an id other than
  the current desktop's (typically `Guid.Empty` or a pinned-view id) is pinned to all desktops. Core
  `WorkspaceAssignment` applies this rule from (desktop id, shell-cloaked, current id). Spike item (b) confirms the ids
  returned before WP E starts.
- **No blocking**: `GetWindowDesktopId` calls into Explorer. All `IVirtualDesktopManager` and mover calls run on one
  background MTA worker thread (`Services/Workspaces/VirtualDesktopWorker.cs`, a queue of work items each with a `Task`
  result). It starts on the first overview open, stays for the session, and releases its COM objects and exits in
  `Dispose()`; it does no work and wakes for nothing at idle. The overview opens with the tracker's windows at once; the
  strip is filled when the worker answers.
- **Stuck worker**: every work item has a 500 ms budget. On a timeout the caller gets a timeout result (strip hidden for
  this open, move refused, warning logged with the item), the worker is marked stuck and abandoned (never joined, never
  aborted), and a fresh worker is started for later items. At most **one** abandoned thread is allowed: if the
  replacement also gets stuck, the service refuses all further work for the session (strip hidden, mover `Failed`) and
  logs it.
- **Explorer restart**: on `RPC_E_DISCONNECTED`, `RPC_S_SERVER_UNAVAILABLE`, `CO_E_OBJNOTCONNECTED` or `E_FAIL` the
  worker releases its proxies, re-creates them **once** and retries the item; a second failure fails the item. Desktop
  GUIDs survive the restart. Fast user switching: per-session registry key, as today.

### Workspace strip (app: `Features/Overview/Workspaces/`)
- Core `Overview/WorkspaceStripLayout`: slot rects (DIPs) for N desktops given the primary monitor size, the available
  width and GNOME's 1/10 scale cap, plus the trailing "+" slot when requested. Tests: 1, 2, 9, 20 desktops; narrow width;
  scale > 100 %; zero/negative sizes.
- Core `Overview/WorkspaceMiniature`: maps window `PixelRect`s (physical) into a slot, clipped to the monitor; assigns
  each window to the monitor with the largest overlap and keeps only primary-monitor windows; z-order preserved. Tests:
  negative coordinates, partly off-screen, mixed DPI (primary 150 %, secondary 100 % on the left at negative x),
  maximised, minimised (drawn at its restore rect from `WINDOWPLACEMENT`, which the app supplies).
- Core `Workspaces/WorkspaceAssignment`: groups snapshot windows by desktop id against the registry list; pinned windows
  (rule above) go to every desktop; ids neither current-list nor pinned are logged and dropped. Tests with literal
  GUIDs.
- App: `WorkspaceStripView` (WPF, part of the overview window; no extra HWND), `WorkspaceThumbnailView` (wallpaper
  `ImageBrush` from one frozen downscaled bitmap shared by all slots; window rects with icons from `IIconProvider`).
  Window bounds come from the snapshot (physical pixels; the overview is on the primary monitor, Per-Monitor-V2).
- **Wallpaper**: decoded off the UI thread (thread pool, not the Explorer-bound worker) with `DecodePixelWidth` set to
  the slot width in pixels, frozen, and cached by (path, last-write time); the cache holds one bitmap and is replaced
  when either changes.
- `OverviewWindow.ThumbnailArea()` moves its top down by the strip height when the strip is shown.

### Window mover (decision 2; safety-critical, undocumented)
- `Services/Workspaces/IWindowDesktopMover.cs`: `MoverAvailability Availability { get; }` (`Available`, `SafeMode`,
  `ShellMode`, `UnsupportedBuild`, `Failed`), `Task<MoveResult> MoveAsync(nint hwnd, WindowIdentity expected, Guid
  desktopId)`, `Task<MoveResult> MoveToNewDesktopAsync(nint hwnd, WindowIdentity expected)`. Created in `App`, exposed
  on `ShellContext`; spec 0016 calls the same instance.
- `Services/Workspaces/NullWindowDesktopMover.cs`: refuses every move with a fixed availability. `App` constructs it
  with `SafeMode` in `--safe`/`--selftest` and `ShellMode` in shell mode, so callers never see a null service; WP F
  uses it with `UnsupportedBuild` as its stub until WP E lands.
- `Interop/VirtualDesktopInternal.cs` is the **only** file with the undocumented interfaces: `CLSID_ImmersiveShell` →
  `IServiceProvider.QueryService` for `IVirtualDesktopManagerInternal` and `IApplicationViewCollection`;
  `GetCount`, `GetCurrentDesktop`, `IVirtualDesktop.GetID`, `GetViewForHwnd`, `FindDesktop(Guid)`,
  `MoveViewToDesktop`, `CreateDesktop`. The vtable layouts and IIDs differ per build and within build lines, so it
  declares one interface set per verified range.
- Core `Workspaces/VirtualDesktopInterfaceTable`: maps **(build, UBR)** to an interface-set id using closed ranges
  `[build, fromUbr..toUbr]`, each verified on a real machine before it's added. Known breakpoints within lines (e.g.
  22621.2215, 22631.3085, and changes during 26100) are why the build alone isn't enough. Anything outside a verified
  range → `UnsupportedBuild`. The build and UBR come from `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`
  (`CurrentBuildNumber`, `UBR`), the same values `SystemInfoReader` already reads for About. No IID probing: a matching
  IID with a different vtable would be an uncatchable access violation. Tests: each range's first and last UBR, one
  either side, a neighbouring build, 0, a future build.
- **Crash marker** (same pattern as `display-revert.json`): before the first undocumented call of each operation
  (fingerprint included) the worker writes `workspace-move-inflight.json` (build, UBR, hwnd) to the settings directory
  atomically (temp file + move), and deletes it after the last call returns. If it can't be written, the operation is
  not attempted (logged). At start-up, if the file exists: log it, and if its (build, UBR) equals the running OS the
  mover is `Failed` and the file is left in place, so it stays off on later starts too; once the OS version differs,
  the file is deleted and the table decides again. Core `Workspaces/MoverCrashGuard.Decide(record?, build, ubr)` →
  `Clear | DisabledByCrash | DeleteStale`, with tests (none, same, newer UBR, newer build, malformed). KI-079.
- **Read-only fingerprint** before any mutating call in an operation: `GetCount` (vtable slot 3) must equal the
  registry desktop count (`VirtualDesktopIDs` length / 16), and `GetCurrentDesktop` → `GetID` must equal the registry's
  `CurrentVirtualDesktop`. A mismatch sets `Failed` for the session (logged with both values, build and UBR) and nothing
  else is called. Core `Workspaces/InterfaceFingerprint.Matches(...)` with tests.
- **HWND reuse**: at drag start the app captures a `WindowIdentity` for the dragged hwnd from the hwnd itself:
  `GetWindowThreadProcessId(hwnd)` (for UWP frames this is ApplicationFrameHost's pid, not `WindowInfo.ProcessId`,
  which `WindowTracker` sets to the CoreWindow's process) plus that process's start time (as `ProcessFacts.StartTime`).
  Before each move the worker re-checks `IsWindow`, that the hwnd's own pid and start time still match, and that the
  process isn't elevated. Any difference → `MoveResult.WindowGone`.
- Core `Workspaces/WorkspaceMovePlanner`: decides `Move`, `MoveToNew`, `NoOp` (same desktop) or `Refuse(reason)` from the
  availability, the window's elevation, the pinned state, liveness results and the drop target. Tests per row.
- Every HRESULT failure is logged with the hwnd, build and UBR; two consecutive failures set `Failed` for the session.

### App grid folders (app: `Features/Overview/AppGrid/`)
- Core `Overview/AppGridModel.Build(apps, AppGridSettings, IFolderRules)`: ordered items (`AppItem` | `FolderItem` with
  its visible apps). Explicit folder members win; a rule folder takes matching apps not in another folder or its
  `ExcludedApps`; an app shows at top level only when in no folder; launch ids not installed are hidden but kept in
  folders; no order = alphabetical (current culture passed in); order set = stored order, then new apps alphabetically.
- Core `Overview/AppGridEdits` (pure, returns new `AppGridSettings`): `Move`, `CreateFolder(dragged, target,
  newFolderId)` (id injected), `AddToFolder`, `RemoveFromFolder(appId, insertIndex)` (adds to `ExcludedApps` when a rule
  matched it; deletes the folder at zero members), `MoveWithinFolder`, `Rename` (trimmed, empty refused), `Reset`. Each
  edit takes the installed id set and prunes `Order` entries that are neither installed apps nor existing folders
  (folder members are kept). Edits happen only after the catalogue has loaded.
- Core `Overview/UtilitiesFolderRules` (`"WindowsTools"`): matches when the app's resolved target is under the Windows
  directory (`%windir%`, passed in; Program Files never matches) or is a `.msc` file; or its parsing name / AUMID is one
  of `Microsoft.Windows.ControlPanel`, `Microsoft.Windows.AdministrativeTools` (Windows Tools),
  `Microsoft.Windows.Shell.RunDialog`, `Microsoft.Windows.RemoteDesktop`. Inputs: parsing name, AUMID, target path,
  using `KnownFolderPath` for `{GUID}\…` parsing names. Spike item (d) checks what this matches in this machine's
  AppsFolder before WP B. Tests: a target under `%windir%` and in a subfolder, a same-named exe under Program Files,
  a `.msc` anywhere, each AUMID, case, `{GUID}` paths, a missing target.
- Seeding: when the catalogue first loads and `DefaultFoldersAdded` is false, `AppGridEdits.AddDefaultFolders` adds
  Utilities (id `utilities`, rule `WindowsTools`) and sets the flag, through `SettingsService.Update` — once, idempotent.
- App: `AppGridView` (the grid ItemsControl moves here from `OverviewWindow.xaml` in WP 0; virtualisation unchanged),
  `AppFolderIcon`, `AppFolderPopup` (a WPF overlay inside the overview window, not a new window), `GridDragController`
  (mouse-capture drag inside the overview; no OLE, so no nested modal loop and no `Deactivated` surprises). Hit-testing
  and drop-target decisions (reorder slot vs. "make folder" centre zone vs. folder) are Core
  `Overview/GridDropTarget` with tests on tile geometry. Every edit is one `SettingsService.Update` on drop, not per move.

### Dock drag (app: `Features/Dock/`)
- `DockWindow.OnItemMouseMove` also starts a drag for `DockAppEntry { IsPinned: false }` with a launch id.
- **Outside detection** is not based on `DragLeave`/`DragEnter` (they fire when crossing child elements and gaps).
  The `QueryContinueDrag` handler records Esc (`DRAGDROP_S_CANCEL`) and, on each call, reads `GetCursorPos` and asks Core
  `Dock/DockDragBounds.IsOutside(cursor, bodyRect, cellSize, wasOutside)`: outside means beyond the dock body's rect
  (physical pixels) by more than about one cell, and returning inside needs the pointer back within the rect
  (hysteresis, so the slot doesn't flicker at the edge). A change collapses or restores the dragged pin's slot (view
  model `PreviewRemove`/`PreviewInsert`). Tests: inside, at the edge, just under and over one cell, re-entry, negative
  coordinates.
- Core `Dock/DockDragOutcome.Decide(isPinnedSource, outside, escapePressed, pinIndex)` →
  `Reorder | Pin(index) | Unpin | Cancel`. Tests for every combination.
- `DockActions` gains `PinAt(DockApp, int)` and reuses `Unpin`; `DockFeature.OnPinDragFinished` maps the outcome. The
  dock stays revealed during the drag (`SetInteracting`), as today.

### File search (app: `Features/Overview/Search/`)
- Evaluation: WinRT `Windows.Storage.Search` works unpackaged but loads the projection (~15–30 MB, spec 0015), queries
  per folder and creates a brokered `StorageFile` per result. `System.Data.OleDb` is a NuGet package. **Chosen:** SQL
  built in Core and executed through **ADO** (`ADODB.Connection`/`Recordset`, part of Windows' data access components),
  late-bound with `Type.GetTypeFromProgID("ADODB.Connection")` and `dynamic` (IDispatch), provider
  `Search.CollatorDSO` — Microsoft's documented way to query the index from script/ADO. No package, no `searchapi.h`
  interfaces, no raw OLE DB. If the ProgID doesn't resolve (no ADO), the Files section is hidden and it's logged.
- Core `Search/FileSearchSql.Build(query, scopes)` → SQL string or null (no tokens, or no scopes):
  `SELECT TOP 5 System.ItemPathDisplay, System.ItemNameDisplay FROM SystemIndex WHERE
  CONTAINS(System.ItemNameDisplay, '"tok1*" AND "tok2*"') AND (SCOPE='file:C:/…' OR …) ORDER BY System.Search.Rank DESC`.
  Tokenising splits on whitespace, drops `"` and other characters CONTAINS treats as syntax, and drops empty tokens;
  `'` is doubled everywhere it appears inside a SQL literal; scopes use forward slashes, no trailing slash, duplicates
  and folders inside another scope removed. Tests: one and several words, apostrophes, double quotes, only punctuation
  (→ null), leading/trailing spaces, trailing slashes, duplicate and nested scopes, empty scope list (→ null).
- Core `Search/FileSearchScheduler` (injected clock): `TextChanged(text, now)` → deadline; `Due(now)` → `(query,
  generation)` or none (under 2 chars, setting off, same as last); `Completed(generation)` → accept or drop. Builds on
  `DeadlineSchedule`. Tests: debounce boundary (149/150 ms), supersede, stale drop, clear.
- Core `Search/FileResultText`: display name and `~\`-relative folder (profile path passed in). Tests.
- App `FileSearchService`: one MTA worker thread, started on the first query, holding one pending slot (the newest
  query; older pending ones are replaced). ADO `Execute` is synchronous and can't be cancelled, so: the UI thread never
  waits on the worker; a running query is allowed to finish and its results are dropped if a newer generation exists
  (supersede-and-drop); the recordset and connection are closed and released (`Marshal.FinalReleaseComObject`) on the
  worker thread that created them. `CommandTimeout = 2 s` is set, but is honoured only if `Search.CollatorDSO` supports
  `DBPROP_COMMANDTIMEOUT` (spike item (e)); the design doesn't rely on it. The worker exits when its slot is empty and
  the overview has closed; it is never joined. Results are marshalled with `Dispatcher.BeginInvoke`. Known folder paths
  come from `NativeMethods.GetKnownFolderPath`.
- `FileResult : SelectableItem` joins `SearchItems.cs`; the results list and `SelectionNavigator` order is Applications,
  Files, Windows. Opening uses `IAppLauncher.Launch(path)`.

### Threading, DPI, monitors, elevation, hostile cases
- UI work stays on the dispatcher; Explorer-bound COM and the index query run on background MTA threads; the UI never
  waits on either.
- Overview geometry is DIPs on the primary monitor; window rects are physical `PixelRect`s converted with the overview's
  scale. Monitors at negative coordinates and mixed DPI are covered by `WorkspaceMiniature` tests.
- Elevated windows: shown in the strip, never moved, never dragged. Search never elevates.
- Display change, DPI change, sleep/resume: the overview already closes or rebuilds on open; the strip is rebuilt per
  open. A desktop added or removed while open (registry watcher) refreshes the strip; a drop onto a desktop that no
  longer exists is refused (`FindDesktop` fails → `MoveResult.DesktopGone`).

### Spike (before the WPs it gates)
Run on this machine (26200) and every build/UBR to be listed, recorded in the PR:
- (a) DWM thumbnails of shell-cloaked windows render (only matters if live previews are added later; gates nothing).
- (b) what `GetWindowDesktopId` returns for pinned windows and pinned apps — **before WP E**.
- (c) whether `MoveViewToDesktop` works for UWP `ApplicationFrameWindow`s — **before WP E**.
- (d) which system tools AppsFolder lists on Windows 11 and what the `WindowsTools` rule matches — **before WP B**.
- (e) whether `Search.CollatorDSO` honours `CommandTimeout` — before WP C (informational; the design doesn't depend
  on it).
- The vtable layout and IIDs for each (build, UBR) range added to `VirtualDesktopInterfaceTable` — before WP E merges.

### Work packages
| WP | Owns | Depends on | Parallel | Safety |
|---|---|---|---|---|
| 0. Settings and overview split | `Core/Settings/AppSettings.cs` (+`AppGridSettings`, `AppFolder`, two Activities bools, Normalize, old-file tests); Activities page stubs; split **only** the app grid into `AppGridView` and add an empty `WorkspaceStripView` host in `OverviewWindow.xaml`, behaviour unchanged | — | First, alone (small) | Settings contract |
| A. Dock drag | `Features/Dock/DockWindow.xaml.cs`, `DockViewModel.cs`, `DockFeature.cs`, `DockActions.cs`, `Core/Dock/DockDragOutcome.cs`, `DockDragBounds.cs` | — (no settings change) | From the start | — |
| B. Folders and order | `Features/Overview/AppGrid/*`, `Core/Overview/AppGridModel.cs`, `AppGridEdits.cs`, `GridDropTarget.cs`, `UtilitiesFolderRules.cs`, Activities settings page button; its `OverviewWindow.xaml(.cs)` edits after C's | 0, spike (d) | With A, D, E | Settings writes |
| C. File search | `Features/Overview/Search/*`, a Files `ItemsControl` in the existing results XAML of `OverviewWindow.xaml`, `SearchItems.cs`, `Core/Search/FileSearchSql.cs`, `FileSearchScheduler.cs`, `FileResultText.cs`, Activities settings toggle | 0 | With A, D, E | — |
| D. Workspace service | `Services/Workspaces/VirtualDesktopService.cs` (the move, unless 0016 did it), `VirtualDesktopWorker.cs`, `IWindowDesktopMover.cs`, `NullWindowDesktopMover.cs`, `Interop/VirtualDesktopManager.cs`, `WindowTracker.SnapshotAllDesktops`, `Core/Workspaces/*` parsing and assignment, `WindowFilter.IsTaskWindowOnAnyDesktop`, `ShellContext`, TopBar `WorkspacesViewModel` | — | With A, B, C | Explorer-bound calls |
| E. Mover | `Interop/VirtualDesktopInternal.cs`, `Services/Workspaces/WindowDesktopMover.cs`, `Core/Workspaces/VirtualDesktopInterfaceTable.cs`, `MoverCrashGuard.cs`, `InterfaceFingerprint.cs`, `WorkspaceMovePlanner.cs`, crash-marker file | D, spike (b)(c) | With B, C, F | **Safety-critical** (undocumented, crash risk) |
| F. Strip and window drag | `Features/Overview/Workspaces/*`, `ThumbnailLayer.cs`, `WindowSlotView.*`, `Core/Overview/WorkspaceStripLayout.cs`, `WorkspaceMiniature.cs`; its `OverviewWindow.xaml(.cs)` edits after B's | 0, D | With E, against `NullWindowDesktopMover(UnsupportedBuild)` | Uses E |
| G. Docs | README, PLAN.md tables and module map, KNOWN_ISSUES, this spec | all | Last | — |

- **`OverviewWindow.xaml` / `OverviewWindow.xaml.cs` ownership:** one WP at a time — **C first, then B, then F**. Each
  may build its Core and own-folder parts in parallel, but lands its edits to these two files only after the previous
  one has merged, rebasing on it.
- Settings page edits (B and C both add rows to `ActivitiesPageViewModel` and its view) are done by WP 0 as stubs bound
  to the new settings, so B and C don't edit the same files. `ShellContext.cs` changes only in D.
- The `VirtualDesktopMonitor` move is done once, by D or by spec 0016's WP 5, whichever lands first.

## Safety and recovery
- **No persistent system state is changed.** Moving a window between desktops and creating a desktop are user actions,
  like doing it in Task View; nothing is recorded or undone on exit, crash or next start. Settings writes go through
  `SettingsService` as today.
- **Undocumented code** (the mover) is in one class, behind a (build, UBR) range table, guarded by a crash marker and a
  read-only fingerprint, off in `--safe`, `--selftest` and shell mode (`NullWindowDesktopMover`), never probed on
  unverified builds, and disabled for the session after a fingerprint mismatch or repeated failures.
- **Access violation**: a wrong vtable would raise an AV, which .NET can't catch, so the process dies without running
  `EmergencyRestore` (taskbar, title-bar colours, focus-follows-mouse are not restored at that moment). Recovery is the
  existing next-start restore from the recorded session state, which AGENTS §4.5's `taskkill /f` QA already covers. The
  crash marker then keeps the mover off on that build/UBR so it can't happen twice.
- **Settings contract**: all new fields have defaults; a settings file without `AppGrid` or the new Activities bools
  loads unchanged (`SchemaVersion` stays 1; additive). Normalize repairs null lists, empty or duplicate folder ids, an app
  in two folders (first wins), and `Order` duplicates. Tests load the pre-0018 shape.
- `--safe`: strip, folders, dock drag and file search work (no system changes); the drop onto a workspace is refused
  with the safe-mode notice. `--selftest` creates no real mover (the null one) and no search worker.
- A crash mid-drag loses only the drag; a crash between creating a desktop and moving the window leaves an empty desktop
  (harmless, user can close it).

## Footprint
- No CPU at idle: no timers, hooks or polling. The workspace worker is started on the first overview open and then
  stays for the session, blocked on its queue (one idle thread, its COM proxies); it's released in `Dispose()`. The
  search worker exists only while queries are pending. ADO and the search provider are loaded only while searching.
- Strip: no DWM thumbnails beyond today's (one per current-desktop window); miniatures are WPF rectangles and cached
  icons; the wallpaper is decoded once per (path, mtime) at strip size (~200×110 px, ~100 KB cached). The all-desktop
  snapshot runs once per open and after a move, not on every window event.
- App grid: folders reduce visible tiles; drag work is per mouse move during a drag only. Settings written once per drop.
  Dock drag: one `GetCursorPos` per `QueryContinueDrag` during a drag only.
- Expected idle memory change: one parked thread and its proxies after the first overview open; while the overview is
  open a few MB for ADO during searches. Check per AGENTS §4.6.

## Acceptance criteria
1. Old settings files (no `AppGrid`, no new Activities fields) load with defaults; malformed folder data is repaired —
   Core tests on `AppSettings`.
2. `VirtualDesktopState.ParseIds`, `VirtualDesktopList`, `WorkspaceAssignment` (including the pinned rule) and
   `WindowFilter.IsTaskWindowOnAnyDesktop` — Core tests (malformed, one, many, unknown and pinned ids, cloak flag
   combinations).
3. `WorkspaceStripLayout` and `WorkspaceMiniature` produce exact rects for the listed cases, including negative
   coordinates and mixed DPI — Core tests.
4. `VirtualDesktopInterfaceTable` returns a set only inside the listed (build, UBR) ranges; `MoverCrashGuard`,
   `InterfaceFingerprint` and `WorkspaceMovePlanner` (refuses elevated, pinned, gone/reused windows, unsupported builds,
   safe mode, failed) — Core tests.
5. Manual: with 3 desktops the strip shows 3 thumbnails in Task View's order with the current one ringed and windows in
   the right places; a pinned window appears on all three; with 1 desktop it's hidden; turning the setting off hides it.
6. Manual: clicking a thumbnail closes the overview and switches to that desktop.
7. Manual (supported build/UBR): dragging a Notepad thumbnail onto desktop 2 moves it there; onto "+" creates desktop 4
   and moves it; a UWP app (Settings) moves (or is refused, per spike (c)); Esc cancels; an elevated window can't be
   moved; on `--safe` the drop is refused with the notice.
8. Manual: on an unsupported build/UBR (temporarily remove the table row in a local build) the drop is refused with the
   version notice and nothing is called on the internal interfaces (log).
9. Manual: kill Explorer (`taskkill /im explorer.exe /f`, restart it) with the overview closed; the next open shows the
   strip and a move works (proxies re-created once).
10. Manual: leave a `workspace-move-inflight.json` with the current build/UBR in the profile → the mover reports
    `Failed` (log, notice) on this and the next start; edit its UBR to an older value → it's deleted and moves work.
    Temporarily make the fingerprint compare against a wrong count → `Failed`, and the log shows no mutating call.
11. Manual: make Explorer unresponsive to the worker (debugger break on explorer.exe, test VM) → the overview opens
    without the strip, a warning is logged, a later open uses a fresh worker; a second hang refuses for the session.
12. `AppGridModel`, `AppGridEdits` (including `Order` pruning), `GridDropTarget`, `UtilitiesFolderRules` — Core tests:
    create folder, add, remove to zero deletes, rule exclusions stick, rename rules, order with new and uninstalled apps,
    the `%windir%`/`.msc`/AUMID rule.
13. Manual: drop app on app → folder created and rename field active; rename persists after restart; drag an app out of
    the popup lands at the pointer; reorder persists after restart; Utilities appears once with the system tools;
    *Reset application grid* restores alphabetical order and Utilities.
14. `DockDragOutcome` and `DockDragBounds` — Core tests for every combination and the hysteresis boundaries.
15. Manual: drag a running unpinned app between pins → pinned there and persisted; drag a pin off the dock → slot
    collapses about one cell out without flicker at the edge, release removes it (stays as running app if it has
    windows); Esc → kept; reorder still works.
16. `FileSearchSql`, `FileSearchScheduler`, `FileResultText` — Core tests (tokenising, escaping, scopes, debounce
    boundary, superseded and stale results).
17. Manual: typing "rep" shows up to 5 files from Documents/Downloads under Applications within ~300 ms; typing fast
    never freezes the UI (no dropped keystrokes); Enter on a file opens it; with the Windows Search service stopped the
    Files section stays hidden and the overview works; the setting turns it off.
18. Footprint: idle CPU unchanged before and after (Task Manager, 5 min idle, after one overview open); idle memory
    change limited to the parked workspace worker.
19. Definition of done in AGENTS §4, light and dark, 100 % and 150 %, and a secondary monitor at negative coordinates.

## Risks and open questions
- **KNOWN_ISSUES entries** (placeholders reserved for this spec):
  - KI-077 (S4, Workspaces, Open): moving windows between desktops uses undocumented `IVirtualDesktopManagerInternal`;
    supported only inside verified (build, UBR) ranges, so a cumulative update outside the range turns it off until the
    table is updated.
  - KI-078 (S4, Overview, By design): the workspace strip shows window outlines and icons, not live previews.
  - KI-079 (S3, Workspaces, Open): a crash during an undocumented call leaves `workspace-move-inflight.json`, which keeps
    the mover off on that build/UBR until the OS version changes; the process died without `EmergencyRestore`, recovery
    was at next start. A stuck call abandoned at exit also leaves the marker (conservative false positive).
  - KI-080 (S4, Overview, By design): file search needs the Windows Search service, indexed folders and ADO; names only.
    App grid reordering and folders are mouse-only (same entry, or split when G writes them).
- **Uncatchable crash** if a vtable changes without an IID change inside a listed range. Mitigated by exact (build,
  UBR) ranges verified per machine, the read-only fingerprint (catches a shifted vtable before any mutating call in most
  cases, but the fingerprint calls themselves could fault) and the crash marker (it can't happen twice on one build).
  Still the main risk, hence the reviewer focus.
- **Narrow ranges mean frequent off-states**: each monthly update past `toUbr` disables moving until verified. Accepted
  trade-off (safety over availability).
- **Fingerprint race**: a desktop switch or add between the registry read and `GetCount`/`GetCurrentDesktop` could
  produce a spurious mismatch and disable the mover for the session. Rare (the reads are back to back on the worker);
  revisit if seen in logs.
- **Code correction**: the previous draft said the build came from `RtlGetVersion` "already used by About"; About
  (`SystemInfoReader`) actually reads `CurrentBuildNumber` and `UBR` from the registry, which this spec now uses.
- **User decisions 2026-10-09:** clicking a workspace closes the overview; results order Applications, Windows, Files;
  a folder is deleted when empty; window moving is enabled only on verified (build, UBR) ranges, refused elsewhere.
- Dragging a pin off the dock removes it without undo (GNOME behaviour); intellihide keeps the dock shown during drags.
- ADO is late-bound; a stripped-down Windows image without ADO just loses the Files section (logged).
- Shell mode (spec 0013) has no Explorer, so no virtual desktops: the strip is off and the mover is the null one.
