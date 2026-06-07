# Tasks: Localized Logs, Restart-on-Language-Change & Release Flow

Status legend: [x] done · [ ] pending

## A. Core layer
- [x] A1. `LanguageRestartState.IsRestartRequired(selected, runtime)` pure
      static helper (`Steamoff.Core/Localization/LanguageRestartState.cs`)
- [x] A2. `LogEventKey` enum (`Steamoff.Core/Logging/LogEventKey.cs`, ~33 members)
- [x] A3. `LogLevel` enum + `LogEventTemplates` lookup table
      (`Steamoff.Core/Logging/LogEventTemplates.cs`)
- [x] A4. `ILocalizedLogService` interface (`Steamoff.Core/Interfaces/ILocalizedLogService.cs`)
- [x] A5. `DiagnosticsSnapshot` record (`Steamoff.Core/Models/DiagnosticsModels.cs`,
      ~18 fields) + `IDiagnosticsService.BuildSnapshotAsync`/`BuildExtendedReportAsync`

## B. Infrastructure layer
- [x] B1. `LocalizedLogService : ILocalizedLogService`
      (`Steamoff.Infrastructure/Logging/LocalizedLogService.cs` — composes
      `ILogService` + `ILocalizationService`, format-and-write per R3/data-model §4)
- [x] B2. `DiagnosticsService`: inject `ILocalizationService`; replace every
      hardcoded-Russian `Ok/Warning/Error(name, message)` call with
      localized `diagnostics.check.*` templates
- [x] B3. `DiagnosticsService.BuildSnapshotAsync` + `BuildExtendedReportAsync`
      (fully localized text report incl. pending-restart notice, US5)

## C. App layer — restart flow
- [x] C1. `SettingsViewModel`: remove live `_services.Localization.SetLanguage(...)`
      calls from `SelectedLanguage` setter and `Cancel()`; remove unused
      `_languageOnEntry`; add `RuntimeLanguage`/`SelectedLanguage`/
      `IsRestartRequired` derived properties + `OnPropertyChanged` wiring
- [x] C2. `SettingsViewModel`: `RestartNowCommand` (`IAsyncRelayCommand`,
      `CanExecute ⇔ IsRestartRequired`) + `RestartRequested` event
- [x] C3. `App.xaml.cs`: `RestartApplication()` (resolve `ProcessPath`,
      relaunch with same args, graceful teardown; log `RestartRequested`/
      `RestartFailed`; UI-Kit error notification on failure) + wire
      `RestartRequested` event from `SettingsWindow`/`SettingsViewModel`
      (mirrors existing `SettingsCommitted`/`Closed` wiring)
- [x] C4. `SettingsWindow.xaml`: restart warning banner + "Перезапустить
      сейчас"/"Restart now" button (UI Kit styled, bound to
      `IsRestartRequired`/`RestartNowCommand`)
- [x] C5. `CompactViewModel`/`MainWindow.xaml`: "restart required to change
      language" banner derived the same way (`AppSettings.Language` vs
      `CurrentLanguage.Code`), shown after `OnSettingsCommitted`

## D. App layer — localized logging call sites
- [x] D1. Wire `ILocalizedLogService` into `AppServices` (new `LocalizedLog`
      property, constructed inline like every other service)
- [x] D2. `App.xaml.cs`: `AppStarted`/`AppClosed` via `LocalizedLog`
- [x] D3. `SettingsViewModel`: `SettingsOpened` (construction),
      `LanguageChangedRestartRequired` (picker commit),
      `SettingsApplied`/`SettingsSaved`/`SettingsCancelled` (Commit/Cancel),
      `FolderAdded`/`FolderRemoved`, `ExeAdded`/`ExeRemoved`,
      `SteamPathNormalized`/`SteamPathInvalid`, `AutostartCreated`/`AutostartRemoved`,
      diagnostics-outcome lines (rows 11-13 of the logging contract)
- [x] D4. `CompactViewModel`/firewall toggle path: route
      `FirewallBlock/Unblock Started/Completed/Failed` and `DriftDetected`
      through `ILocalizedLogService` (replacing raw `ILogService.Write` calls)
- [x] D5. `SteamDiscoveryService`/auto-find path: `SteamAutoSearchStarted/
      Succeeded/Failed` through `ILocalizedLogService`
- [x] D6. `CopyDiagnosticsCommand` (Compact + Settings journal): `DiagnosticsCopied`

## E. App layer — Journal panel (Settings)
- [x] E1. `SettingsViewModel`: `JournalLines`/`JournalFilter`/`HasJournalLines`
      + `RefreshJournalCommand`/`OpenLogFolderCommand`/`ClearJournalDisplayCommand`
      (mirrors `CompactViewModel` mini-log; 200-line window + level filter)
- [x] E2. `SettingsWindow.xaml`: "Журнал"/"Log" card — dark, monospace,
      ScrollViewer, color-coded rows (reuse `LogLineContainsConverter`),
      filter `ComboBox` (Все/Ошибки/Предупреждения/Информация), action row
      (Обновить / Открыть папку логов / Скопировать диагностику / Очистить отображение)
- [x] E3. Auto-refresh timer for the journal (reuse `CompactViewModel`'s
      `DispatcherTimer` cadence/pattern, scoped to when Settings is open)

## F. Localization
- [x] F1. Generate & insert ~33 `log.event.*` keys × 9 languages (table in
      `contracts/localized-logging.md`) via PowerShell+translation-table script
- [x] F2. Generate & insert `settings.language.restartWarning`,
      `settings.toast.appliedRestartPending`, `settings.toast.savedRestartPending`,
      `settings.toast.restartFailed`, `compact.languageRestart.banner`,
      `settings.journal.*` (title/filters/actions/empty-state),
      `diagnostics.field.*` (~18), `diagnostics.languagePendingRestart`,
      `diagnostics.outcome.{success,warning,error}`, `diagnostics.check.*`
      (migrated from hardcoded RU) × 9 languages
- [x] F3. Verify parity via existing
      `LocalizationServiceTests.EveryShippedLanguage_HasNonEmptyTable_WithSameKeySetAsRussian`

## G. SpecKit documentation
- [x] G1. `spec.md`, `plan.md`, `research.md`, `data-model.md`, `quickstart.md`, `tasks.md`
- [x] G2. `contracts/language-restart.md`, `contracts/localized-logging.md`,
      `contracts/release-build-flow.md`

## H. Release tooling
- [x] H1. `build-release.ps1` (repo root) — full 12-step pipeline per
      `contracts/release-build-flow.md` (verify root → restore/build/test →
      close Steamoff → clean/recreate release\ → publish ×2 → rename to
      `Steamoff.exe` → `README-RUN.txt` ×2 → `release-manifest.json` →
      `release-log.txt` → print paths → exit code)
- [x] H2. Add `.\build-release.ps1` usage section to `README.md`

## I. Tests
- [x] I1. `LanguageRestartStateTests` — 5 scenarios from
      `contracts/language-restart.md` state machine (pure static helper, no seam needed)
- [x] I2. `LogEventTemplatesTests`/`LocalizedLogServiceTests` — 4 cases
      (key resolution, level mapping, formatting with/without args, fallback chain)
- [x] I3. Settings-action logging tests — 7 cases (one per representative
      action category: open/apply-save-cancel/folder/exe/steam-path/
      autostart/test-outcome) — via `FakeLogService`+`FakeLocalizationService`
      where the seam allows, else documented as deferred (A16-style)
- [x] I4. `DiagnosticsSnapshotTests`/extended-report tests — 3 cases
      (field completeness, localized rendering, pending-restart notice)
- [x] I5. Release-script tests — 4 cases: manifest JSON shape/round-trip,
      README-RUN.txt content presence, process-safety path-matching predicate
      (pure function extracted & tested), exit-code-on-failure contract
      (script-level smoke, run in isolation)
- [x] I6. Localization-keys parity test — 3 cases covered by existing
      parity test + spot-checks for `log.event.*`/`diagnostics.*`/`settings.journal.*`
      group completeness

## J. Pipeline & docs
- [x] J1. `dotnet restore` / `build -c Release` — 0 errors
- [x] J2. `dotnet test` (with `DOTNET_ROLL_FORWARD*`) — all green
- [x] J3. `.\build-release.ps1` — end-to-end run, verify both EXEs + manifest + log
- [x] J4. Update `README.md`, `FINAL_REPORT.md`, `IMPLEMENTATION_LOG.md`,
      `KNOWN_LIMITATIONS.md`, `ASSUMPTIONS.md` (A17-A20+)
- [ ] J5. `git add` / commit / push (no force-push)
- [x] J6. Final summary report (brief §15, 12 points) appended to `FINAL_REPORT.md`
