# Implementation Plan: Localized Logs, Restart-on-Language-Change & Release Flow

**Spec**: [spec.md](spec.md) | **Branch**: `004-steamoff-localized-logs-release-flow`

## Summary
Implement restart-required language switching by *removing* the live
`SetLanguage` call from `SettingsViewModel` and deriving `IsRestartRequired`
from `SelectedLanguage.Code != CurrentLanguage.Code` (no new mutable state,
no architecture change). Add a small localized-logging facility
(`LogEventKey` enum + `ILocalizedLogService`) that sits on top of the
existing `ILogService`/`ILocalizationService` and is called from the places
listed in the brief. Extend `SettingsViewModel`/`SettingsWindow.xaml` with a
journal panel that reuses the `CompactViewModel` mini-log pattern. Localize
`DiagnosticsService`'s check messages and add an extended, fully localized
text-report builder. Write `build-release.ps1` plus the SpecKit docs, ~65
new localization keys × 9 languages, and the required tests.

## Technical Context
- **Language/Runtime**: C# 12 / .NET 8 (unchanged)
- **UI**: WPF + MVVM, hand-rolled `ObservableObject`/`RelayCommand` (unchanged)
- **Persistence**: `JsonSettingsService` / `SettingsEditSession` clone-then-diff
  (unchanged — `AppSettings.Language` already exists and is persisted)
- **Localization**: `ILocalizationService`/`LocalizationService`/
  `LanguageManager`/`LocalizedStringProvider`, 9 embedded JSON tables
  (unchanged mechanism — we stop calling `SetLanguage` mid-session, we don't
  remove it; the first-launch dialog and tests still rely on live switching)
- **Logging**: `ILogService`/`FileLogService`, append-only text file with
  rotation (unchanged on-disk format: `{timestamp} [{level}] {message}` —
  only the *message* content becomes localized/templated)
- **Testing**: xUnit, `DOTNET_ROLL_FORWARD*` env vars for `dotnet test`
- **Release tooling**: PowerShell 5.1 script (`build-release.ps1`) at the
  repo root, `dotnet publish` ×2 + `Get-FileHash` for the manifest

## Constitution Check
- ✅ Preserves the `App`/`Core`/`Infrastructure`/`Tests` layering — new types
  land in the same projects as their siblings (`LogEventKey`/
  `ILocalizedLogService` in `Core`/`Infrastructure`, journal UI in `App`).
- ✅ No rewrite — `SettingsViewModel`/`DiagnosticsService`/`FileLogService`/
  `CompactViewModel` are *extended*, not replaced.
- ✅ Zero new third-party dependencies; `build-release.ps1` uses only
  built-in PowerShell cmdlets and the `dotnet` CLI already in use.
- ✅ All blocking remains exclusively MS Defender Firewall (untouched);
  release-flow process termination is scoped to Steamoff's own executables
  only (never `steam.exe`/`steamwebhelper.exe`/third-party processes — see
  `contracts/release-build-flow.md` "Process safety").
- ✅ No telemetry/cloud APIs/Electron/secrets introduced.

## Project Structure (additions/changes only)
```
src/Steamoff.Core/
  Logging/
    LogEventKey.cs              (new — enum + per-key localization-key/level table)
  Interfaces/
    ILocalizedLogService.cs     (new)
    IDiagnosticsService.cs      (extended — BuildExtendedReportAsync, DiagnosticsSnapshot)
  Models/
    DiagnosticsModels.cs        (extended — DiagnosticsSnapshot record)

src/Steamoff.Infrastructure/
  Logging/
    LocalizedLogService.cs      (new — ILocalizedLogService over ILogService+ILocalizationService)
  Diagnostics/
    DiagnosticsService.cs       (modified — localized check messages + BuildExtendedReportAsync)

src/Steamoff.App/
  ViewModels/
    SettingsViewModel.cs        (modified — IsRestartRequired, RestartNowCommand, journal panel
                                  state/commands, settings-action logging calls)
    CompactViewModel.cs         (modified — IsLanguageRestartPending banner, AppStarted/Closed,
                                  FirewallBlock*/Unblock*, DriftDetected logging)
  Views/
    SettingsWindow.xaml(.cs)    (modified — restart banner + button, journal panel/tab)
    MainWindow.xaml             (modified — compact "restart required" banner)
  App.xaml.cs                   (modified — RestartApplication(), AppStarted/AppClosed logging,
                                  first-launch immediate-apply unchanged)
  AppServices.cs                (modified — wire LocalizedLogService)

src/Steamoff.Core/Resources/Localization/{ru,en,de,fr,es,it,pt,pl,zh}.json
                                (modified — ~65 new keys: log.event.*, settings.language.*,
                                 settings.journal.*, diagnostics.*, compact.languageRestart.*)

build-release.ps1               (new, repo root)
src/Steamoff.App/release/       (new — release-flow output, gitignored except via README mention)

tests/Steamoff.Tests/
  Logging/LogEventKeyTests.cs / LocalizedLogServiceTests.cs   (new)
  Settings/SettingsViewModelLanguageRestartTests.cs           (new, if AppServices seam allows —
                                                                else documented as deferred, A16-style)
  Diagnostics/DiagnosticsSnapshotTests.cs                     (new)
  Localization/LocalizationServiceTests.cs                    (parity — already covers new keys)

specs/004-steamoff-localized-logs-release-flow/  (this directory)
```

## Phases
1. **Phase 0 — Research** (`research.md`): confirm the derive-don't-store
   approach for `IsRestartRequired`/`RuntimeLanguage`/`SelectedLanguage`;
   confirm log-template severity mapping; confirm release-folder process
   safety rules. Record as `ASSUMPTIONS.md` A17+.
2. **Phase 1 — Design** (`data-model.md`, `contracts/*.md`): formalize
   `LogEventKey`, `DiagnosticsSnapshot`, the restart state machine, and the
   release-manifest schema.
3. **Phase 2 — Core/Infrastructure**: `LogEventKey`, `ILocalizedLogService`/
   `LocalizedLogService`, `DiagnosticsService` localization + extended
   report, wire into `AppServices`.
4. **Phase 3 — App layer**: `SettingsViewModel` restart derivation + journal
   panel + action logging; `CompactViewModel` banner + lifecycle/firewall
   logging; `App.xaml.cs` restart relaunch + startup/shutdown logging;
   XAML for the restart banner/button and journal panel.
5. **Phase 4 — Localization**: add ~65 keys × 9 languages (RU high-quality,
   EN solid, others basic-but-complete — same tiering as A15).
6. **Phase 5 — Release tooling**: `build-release.ps1`, manifest/log/READMEs.
7. **Phase 6 — Tests & pipeline**: new unit tests; `dotnet build/test`;
   `build-release.ps1` end-to-end run.
8. **Phase 7 — Docs & ship**: `README`/`FINAL_REPORT`/`IMPLEMENTATION_LOG`/
   `KNOWN_LIMITATIONS`/`ASSUMPTIONS`; commit; push.

## Risks / Mitigations
- **Restart relaunch can fail in dev environments** (`Environment.ProcessPath`
  null under `dotnet run`) → caught, logged as `RestartFailed`, surfaced via
  a balloon-notification-styled error (existing `INotificationService`),
  never throws to the UI thread.
- **Volume of new localization keys** (~65 × 9 ≈ 585 strings) → generated
  programmatically (PowerShell + a structured translation table) the same
  way `compact.miniLog.collapse` was patched in during feature 003, then
  verified by the existing parity test — avoids hand-editing 9 files 65 times.
- **`AppServices` remains untestable** (per A16) → `SettingsViewModel`
  language-restart logic is therefore tested at the *derivable-state* level
  via a minimal seam (see `research.md` R4) or, if that proves infeasible
  without an architecture change, documented as deferred exactly like A16/H3.
