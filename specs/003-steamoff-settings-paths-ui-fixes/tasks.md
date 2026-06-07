# Tasks: Steamoff — Settings Paths & UI Fixes

Status legend: [x] done · [ ] pending

## A. Core layer
- [x] A1. `PathCheckStatus` enum (`Steamoff.Core/Enums/Enums.cs`)
- [x] A2. `SteamPathCheckResult` model + `Empty` singleton (`TargetModels.cs`)
- [x] A3. `IPathNormalizationService` / `ISteamPathValidator`
      (`Interfaces/IPathServices.cs`)

## B. Infrastructure layer
- [x] B1. `PathNormalizationService` (trim/de-quote/env-expand/slash-fix/
      duplicate-collapse, UNC-aware)
- [x] B2. `SteamPathValidator` (normalize → `.lnk` → file/folder resolution
      chain, injectable shortcut-resolver delegate)

## C. App layer — services & converters
- [x] C1. `IDialogService` / `WpfDialogService` (aliased
      `Microsoft.Win32.OpenFolderDialog`/`OpenFileDialog`)
- [x] C2. `PathCheckStatusToBrushConverter` (green/red/yellow/gray)
- [x] C3. `LogLineContainsConverter` (mini-log level color-coding)
- [x] C4. Wire `PathNormalization`/`SteamPathValidator`/`Dialogs` into
      `AppServices`

## D. App layer — Settings
- [x] D1. `SettingsViewModel`: `Folders`/`Executables` as
      `ObservableCollection<T>` synced with the draft
- [x] D2. Folder commands: Add (dialog), `AddFolderFromPathAsync` (shared
      drag&drop entry), Remove, Rescan, OpenLocation
- [x] D3. EXE commands: Add (dialog), `AddExeFromPathAsync` (shared
      drag&drop entry), Remove, OpenLocation, CheckStatus
- [x] D4. Steam path: `SteamPathCheck`/`Status`/`StatusText`/`IsValid`/
      `IsDiscovering`, `ApplySteamPathCandidate`, `RevalidateSteamPath`,
      `AutoFindSteamCommand`, `BrowseSteamFolderCommand`, auto-discovery on
      construction when path empty/invalid
- [x] D5. `SettingsWindow.xaml`: Steam-path card (drop zone, indicator,
      auto-find/browse buttons), Folders card (header `+`, per-row actions,
      empty state), EXE Files card (same pattern)
- [x] D6. `SettingsWindow.xaml.cs`: drag&drop handlers for all three drop
      zones + `LostFocus` → `RevalidateSteamPath`

## E. App layer — Compact view & shell
- [x] E1. Fix `SettingsRequested` wiring in `App.xaml.cs` (single
      `OpenSettings()` for gear + footer button)
- [x] E2. Restyle `BigToggleButtonStyle` as a rounded card
      (`CornerRadius=16`, `Height=58`, soft shadow / glow / pressed states)
- [x] E3. Add `AddItemButtonStyle`/`RowActionButtonStyle`/`ListRowCardStyle`
      to `DarkOrange.xaml`
- [x] E4. `CompactViewModel`: `RecentLogLines`, `HasRecentLogLines`,
      `IsLogExpanded`/`ExpandLogCommand`, `OpenFullLogCommand`,
      `CopyDiagnosticsCommand`, 5s refresh `DispatcherTimer`
- [x] E5. `MainWindow.xaml`: mini-log card (title, scrollable color-coded
      list, empty state, action row), window resized to fit

## F. Localization
- [x] F1. Add ~33 new keys (`settings.steamPath.*`, `settings.button.*`,
      `settings.folders.empty.*`, `settings.exe.empty.*`,
      `compact.miniLog.*`, …) to all 9 language JSON files
- [x] F2. Add `compact.miniLog.collapse` (discovered missing during E4 wiring)

## G. SpecKit documentation
- [x] G1. `spec.md`, `plan.md`, `research.md`, `data-model.md`,
      `quickstart.md`, `tasks.md`
- [x] G2. `contracts/path-normalization.md`,
      `contracts/settings-ui-actions.md`

## H. Tests
- [x] H1. `PathNormalizationServiceTests`: one case per normalization-table
      row in `contracts/path-normalization.md` + idempotency (8 tests)
- [x] H2. `SteamPathValidatorTests`: folder / `steam.exe` path / wrong-exe /
      missing-exe-in-folder / missing-path / `.lnk` via fake resolver
      (success, wrong target, `null`) / `FromInstallation` (valid + empty)
      (12 tests, real temp-directory tree, no registry/COM access)
- [x] H3/H4. `SettingsViewModelTests`/`CompactViewModelTests` — **not
      written**; see `ASSUMPTIONS.md` A16 and `KNOWN_LIMITATIONS.md`. Both
      view models require a real `AppServices` (a concrete `sealed` type with
      a parameterless constructor that eagerly builds `FileLogService`,
      `JsonSettingsService`, `ComFirewallService`, `TrayService`/`NotifyIcon`,
      etc. — no fakeable seam exists, and the existing suite already has zero
      `AppServices`-dependent view-model tests for the same reason). Adding
      one would mean either writing real files to the user's AppData/registry
      from a unit test, or threading ~18 constructor parameters through a new
      internal test-only `AppServices` constructor — both rejected as
      out-of-scope architecture changes for a UI-fix feature ("fix the
      current implementation, preserve architecture").
- [x] H5. Localization parity already covered end-to-end by the existing
      `LocalizationServiceTests.EveryShippedLanguage_HasNonEmptyTable_WithSameKeySetAsRussian`
      (set-equality against `ru` keys — automatically exercises the ~33 new
      keys added in F1/F2 across all 9 languages)
- [ ] H6. UI smoke: gear + footer button both yield exactly one
      `SettingsWindow`; empty states render/hide correctly — **deferred**,
      same `AppServices`-construction blocker as H3/H4 (UI smoke tests in
      this codebase would need a live `MainWindow`/`SettingsWindow`, both
      requiring `AppServices`)

## I. Pipeline & docs
- [x] I1. `dotnet restore` / `build` (Debug + Release) — 0 errors (no
      `Steamoff.sln` exists, only `Steamoff.slnx`; built `.csproj` directly,
      same as feature 002)
- [x] I2. `dotnet test` (with `DOTNET_ROLL_FORWARD*` env vars) — **53/53
      passing** (33 pre-existing + 20 new: 8 `PathNormalizationServiceTests`
      + 12 `SteamPathValidatorTests`)
- [x] I3. `dotnet publish` (win-x64 self-contained single-file) — succeeded,
      `Steamoff.App.exe` ~122 MB (worked around a running-instance file lock
      via `-o publish-output`, then cleaned up the temp directory — see
      `IMPLEMENTATION_LOG.md`)
- [x] I4. Updated `README.md`, `FINAL_REPORT.md`, `IMPLEMENTATION_LOG.md`,
      `KNOWN_LIMITATIONS.md`, `ASSUMPTIONS.md` (added **A16**)
- [x] I5. `git add` / commit / push (no force-push)
- [x] I6. Final summary report (spec §13, 11 points) — appended to
      `FINAL_REPORT.md` as "Feature 003" section
