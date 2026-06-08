# Feature Specification: Steamoff — Settings Paths & UI Fixes

**Feature branch**: `003-steamoff-settings-paths-ui-fixes`
**Status**: Draft → Implemented (v0.1.0)
**Input**: Make the Settings screen's Additional Folders / EXE Files lists
fully usable (add/remove/rescan/open/check), give the Steam path field real
normalization + validation + auto-discovery + drag&drop, fix the broken
Settings-opening buttons, restyle the big Block/Unblock button as a card, and
add a mini-log panel to the Compact view — all exclusively via Microsoft
Defender Firewall rules, no DRM hacks, no telemetry.

## User Scenarios & Testing

### Primary user story
A user opens Settings, clicks "+ Добавить папку" / "+ Добавить EXE", picks a
folder or EXE (or drags one onto the list), and immediately sees it appear as
a styled row with an enable toggle, status, EXE count (folders) and per-row
actions (rescan / open in Explorer / delete for folders; check status / open
location / delete for EXEs). The Steam path field shows a colored indicator
(green/red/yellow/gray) reflecting whether the path resolves to a valid
`steam.exe`, accepts pasted/dropped folder paths, `.lnk` shortcuts, quoted
paths and `steam.exe` paths (normalizing to the containing folder), and offers
"Найти автоматически" / "Выбрать папку" buttons. Both the gear icon and the
bottom-left "Settings" button open the same single Settings window. The big
Block/Unblock button reads as a soft rounded card, not a pill. The Compact
view shows a small scrolling, color-coded log of the last lines with buttons
to expand, open the full log file, and copy a diagnostics report.

### Acceptance Scenarios
1. **Given** Settings is open with no additional folders, **When** the
   Folders card renders, **Then** it shows the empty-state title/subtitle and
   a "+ Добавить папку" button in the section header.
2. **Given** the user clicks "+ Добавить папку", **When** they pick a valid
   directory, **Then** it is normalized, scanned, added to the draft and
   appears as a row with name, path, EXE count, status and an enabled toggle.
3. **Given** a folder row, **When** the user clicks Rescan / Open-in-Explorer
   / Delete, **Then** the corresponding `RescanFolderCommand` /
   `OpenFolderLocationCommand` / `RemoveFolderCommand` runs against that row's
   `FolderBlockTarget` (via `CommandParameter="{Binding}"`).
4. **Given** Settings is open with no standalone EXEs, **When** the EXE Files
   card renders, **Then** it shows its own empty-state title/subtitle and a
   "+ Добавить EXE" button; adding one validates the extension, de-duplicates
   by normalized path, and surfaces `settings.dialog.notAnExe` for non-`.exe`
   targets.
5. **Given** the user drags a folder, `.exe`, or `.lnk` onto the respective
   drop zone, **Then** `AddFolderFromPathAsync` / `AddExeFromPathAsync` /
   `ApplySteamPathCandidate` resolves the shortcut (via the injectable
   resolver), normalizes the path, and adds/validates it exactly like the
   dialog-driven flow.
6. **Given** the Steam path field is empty or invalid at startup or whenever
   Settings opens, **When** `AutoFindSteamCommand` (or the automatic discovery
   on open) runs, **Then** `ISteamDiscoveryService` is queried, a green
   "Steam найден" indicator + path is shown on success, or a warning + folder
   picker affordance ("Выбрать папку") on failure.
7. **Given** any candidate Steam path (typed, pasted, dropped, or
   auto-discovered), **When** it is validated, **Then**
   `ISteamPathValidator.Validate` normalizes it (trim, strip quotes, expand
   env vars, fix slashes/duplicates), resolves `.lnk` shortcuts, accepts both
   a `steam.exe` file path and its containing folder (saving the **folder**),
   and reports one of `Valid` (green) / `Unchecked` (yellow) / `Empty` (gray) /
   `PathNotFound` / `SteamExeNotFound` / `WrongExe` / `ShortcutUnresolved`
   (red), each with a localized status-message key.
8. **Given** the user clicks the gear icon or the bottom-left "Settings"
   button, **When** either is clicked, **Then** exactly one Settings window
   opens via the shared `OpenSettingsCommand` → `SettingsRequested` →
   `App.OpenSettings()` path — never two.
9. **Given** the Compact view, **When** it renders, **Then** the
   Block/Unblock button uses `BigToggleButtonStyle` with `CornerRadius="16"`,
   `Height="58"`, a soft shadow at rest, a glow on hover, and a pressed/
   disabled visual state — a rounded card, not a pill.
10. **Given** the Compact view, **When** the mini-log card renders, **Then**
    it shows up to the last 30 log lines (auto-refreshed every 5s via
    `ILogService.ReadLastLinesAsync`), color-coded by `[ERROR]`/`[WARNING]`/
    `[INFO]`, an empty-state message when there are none, and
    Expand/Collapse, "Открыть полный лог" (`ILogService.LogFilePath` via shell
    execute) and "Скопировать диагностику"
    (`ILogService.BuildDiagnosticsReportAsync` → clipboard) buttons.
11. **Given** any of the above strings, **When** the active language changes,
    **Then** every new string redraws instantly via the existing
    `LocalizationProxy` indexer/`LanguageChanged` mechanism, in all 9
    supported languages (parity-tested).

### Edge Cases
- Steam path candidate is a `steam.exe` file path → validator resolves to its
  parent folder and reports `Valid` with a "resolved from exe" message key.
- Steam path candidate is a `.lnk` shortcut → resolved via the injectable
  `Func<string,string?>` shortcut resolver (defaults to `ShortcutResolver`,
  swappable with a fake in tests) before re-running folder/exe resolution.
- Steam path candidate is a folder containing `steam.exe` vs. one that
  doesn't → `Valid` vs. `SteamExeNotFound`.
- Steam path candidate points at an existing file that is *not* `steam.exe`
  → `WrongExe`.
- Raw input has surrounding quotes, `%ENV%` variables, forward slashes,
  duplicated backslashes, or leading/trailing whitespace → normalized before
  any filesystem check (UNC `\\server\share` paths preserved).
- Folder/EXE add: duplicate normalized path → silently ignored (no duplicate
  row); nonexistent path → localized toast (`settings.dialog.folderNotFound` /
  `settings.dialog.notAnExe`).
- Drag&drop of multiple files → only the first dropped path is used (matches
  the single-target add commands).
- Auto-discovery fails (Steam not installed/found) → warning shown, manual
  "Выбрать папку" remains available; nothing is silently overwritten.

## Requirements

### Functional Requirements
- **FR-001**: Settings MUST allow adding, enabling/disabling, rescanning,
  opening-in-Explorer, and removing additional folders, each action wired to
  a real command operating on the clicked row's model via `CommandParameter`.
- **FR-002**: Settings MUST allow adding, enabling/disabling, checking status,
  opening-location, and removing standalone EXE targets, with extension
  validation and path-based de-duplication.
- **FR-003**: Both lists MUST show localized empty states (title + subtitle)
  when empty, and a header "+"/Add button styled per the UI kit
  (`AddItemButtonStyle`).
- **FR-004**: The Steam path MUST be normalized and validated through
  `IPathNormalizationService` / `ISteamPathValidator`, with a colored
  indicator (`PathCheckStatusToBrushConverter`: green=valid, red=invalid,
  yellow=unchecked, gray=empty) and localized status text.
- **FR-005**: Steam discovery MUST run automatically when the path is empty
  or invalid (on app startup and on Settings open), showing a found/
  not-found indicator and persisting a found path to the draft.
- **FR-006**: Drag&drop MUST be supported on the Steam-path field, the
  Folders list, and the EXE list, including `.lnk` resolution, reusing the
  same normalize/validate/add code paths as the dialog-driven flows.
- **FR-007**: The gear icon and the bottom-left Settings button MUST both
  route through one `OpenSettingsCommand` → one `SettingsRequested` event →
  the app's single-instance `OpenSettings()` — never opening duplicates.
- **FR-008**: The Block/Unblock button MUST be restyled as a rounded
  rectangular card (radius 14–18, height 52–64) with soft shadow at rest,
  glow on hover, and distinct pressed/disabled states.
- **FR-009**: The Compact view MUST show a mini-log panel: title, last
  20–50 auto-refreshed lines, monospace, color-coded by level, empty state,
  and Expand/Open-full-log/Copy-diagnostics actions.
- **FR-010**: All new user-facing strings MUST exist in all 9 localization
  files and pass the existing localization-parity test pattern.

### Key Entities
- **`SteamPathCheckResult`**: `NormalizedFolderPath`, `SteamExePath`,
  `Status` (`PathCheckStatus`), `StatusMessageKey`, `IsValid`.
- **`PathCheckStatus`**: `Empty | Unchecked | Valid | PathNotFound |
  SteamExeNotFound | WrongExe | ShortcutUnresolved`.
- **`FolderBlockTarget` / `ExeBlockTarget`**: existing draft-bound models
  (now exposed as `ObservableCollection<T>` for live row mutation).

## Review & Acceptance Checklist
- [x] No DRM hacks, telemetry, cloud APIs, or non-firewall blocking introduced
- [x] All blocking remains exclusively MS Defender Firewall rule based
- [x] Architecture (App/Core/Infrastructure/Tests) preserved, not rewritten
- [x] New services are interface-first and unit-testable (injectable dialog
      service, injectable shortcut resolver)
- [x] Localization parity maintained across all 9 languages
