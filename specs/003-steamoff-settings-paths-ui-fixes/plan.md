# Implementation Plan: Steamoff — Settings Paths & UI Fixes

**Branch**: `003-steamoff-settings-paths-ui-fixes` | **Spec**: [spec.md](./spec.md)

## Summary
Fix and extend the existing Settings screen (Additional Folders, EXE Files,
Steam Path) and Compact view without rewriting the App/Core/Infrastructure/
Tests architecture: add real per-row commands and empty states, a path
normalization/validation pipeline with an injectable shortcut resolver, drag
&drop reuse of the same add pipelines, a single unified `OpenSettingsCommand`
wiring, a card-style restyle of the big toggle button, and a mini-log panel
driven by the existing `ILogService`.

## Technical Context
- **Language/Runtime**: C# 12 / .NET 8, WPF (net8.0-windows)
- **Architecture**: `Steamoff.Core` (models/interfaces/enums/mvvm),
  `Steamoff.Infrastructure` (platform services incl. firewall/steam/paths),
  `Steamoff.App` (views/viewmodels/services/themes), `Steamoff.Tests`
- **MVVM primitives**: hand-rolled `ObservableObject` / `RelayCommand` /
  `AsyncRelayCommand` (no third-party MVVM framework, no DI container)
- **Persistence**: `SettingsEditSession` clone-then-diff over `AppSettings`
  (JSON round-trip, camelCase, `JsonStringEnumConverter`)
- **Localization**: `LocalizationProxy` indexer binding +
  `LanguageChanged`-driven instant redraw, 9 language JSON resources
- **New dependencies**: none (no NuGet additions; reuse `Microsoft.Win32`
  dialogs already available in net8.0-windows WPF)

## Constitution Check
- Firewall-only blocking: unaffected — no new blocking mechanism introduced;
  this feature only touches Settings UI/UX and path handling. ✅
- No DRM hacks / telemetry / cloud APIs / Electron: none added. ✅
- Architecture preserved: new types added to existing layers along existing
  seams (`Steamoff.Core.Interfaces`, `Steamoff.Infrastructure.Paths`,
  `Steamoff.App.Services`, `Steamoff.App.ViewModels`). ✅
- Testability: `SteamPathValidator` takes an injectable shortcut-resolver
  delegate; `SettingsViewModel` takes an injectable `IDialogService` — both
  satisfy the spec's "fake resolver"/"no real file dialogs in tests"
  constraints. ✅

## Project Structure (touched areas only)
```
src/Steamoff.Core/
  Enums/Enums.cs                       (+ PathCheckStatus)
  Models/TargetModels.cs               (+ SteamPathCheckResult)
  Interfaces/IPathServices.cs          (new)
  Resources/Localization/*.json        (+ ~33 keys × 9 languages)

src/Steamoff.Infrastructure/
  Paths/PathNormalizationService.cs    (new)
  Paths/SteamPathValidator.cs          (new)

src/Steamoff.App/
  Services/IDialogService.cs           (new)
  Services/WpfDialogService.cs         (new)
  Converters/Converters.cs             (+ PathCheckStatusToBrushConverter,
                                          + LogLineContainsConverter)
  ViewModels/SettingsViewModel.cs      (rewritten: ObservableCollections,
                                          add/remove/rescan/open commands,
                                          Steam-path discover/browse/validate)
  ViewModels/CompactViewModel.cs       (+ mini-log: RecentLogLines,
                                          Expand/OpenFullLog/CopyDiagnostics)
  Views/SettingsWindow.xaml(.cs)       (rewritten cards + drag&drop handlers)
  Views/MainWindow.xaml                (+ mini-log card, taller window)
  Themes/DarkOrange.xaml               (BigToggleButtonStyle restyle,
                                          + AddItemButtonStyle,
                                          RowActionButtonStyle, ListRowCardStyle)
  App.xaml.cs                          (SettingsRequested wiring fix)

specs/003-steamoff-settings-paths-ui-fixes/  (this SpecKit doc set)
tests/Steamoff.Tests/...                     (new test files, see tasks.md)
```

## Phase 0: Research
See [research.md](./research.md) — path-normalization edge cases, WPF dialog
type-alias pitfalls, drag&drop `DataFormats.FileDrop` handling, mini-log
refresh cadence choice.

## Phase 1: Design
See [data-model.md](./data-model.md) and `contracts/`:
- [contracts/path-normalization.md](./contracts/path-normalization.md)
- [contracts/settings-ui-actions.md](./contracts/settings-ui-actions.md)

## Phase 2: Task Breakdown
See [tasks.md](./tasks.md).

## Complexity Tracking
No constitution deviations — no new third-party packages, no architectural
layer changes, no telemetry/cloud surfaces introduced.
