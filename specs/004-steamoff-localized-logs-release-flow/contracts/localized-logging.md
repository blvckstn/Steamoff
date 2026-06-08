# Contract: Localized Logging

## `LogEventKey` → localization key → level → example RU/EN
All localization keys live under `log.event.*`. Levels map to
`ILogService.Write` levels `"INFO"` / `"WARN"` / `"ERROR"`.

| `LogEventKey` | Localization key | Level | RU example | EN example |
|---|---|---|---|---|
| `AppStarted` | `log.event.appStarted` | Info | Приложение запущено | Application started |
| `AppClosed` | `log.event.appClosed` | Info | Приложение закрыто | Application closed |
| `SettingsOpened` | `log.event.settingsOpened` | Info | Открыты настройки | Settings opened |
| `SettingsApplied` | `log.event.settingsApplied` | Info | Настройки применены | Settings applied |
| `SettingsSaved` | `log.event.settingsSaved` | Info | Настройки сохранены | Settings saved |
| `SettingsCancelled` | `log.event.settingsCancelled` | Info | Изменения настроек отменены | Settings changes cancelled |
| `LanguageChangedRestartRequired` | `log.event.languageChangedRestartRequired` | Warning | Выбран новый язык. Для полного применения требуется перезапуск | New language selected. Restart is required to fully apply it |
| `RestartRequested` | `log.event.restartRequested` | Info | Запрошен перезапуск приложения | Application restart requested |
| `RestartFailed` | `log.event.restartFailed` | Error | Не удалось перезапустить приложение: {0} | Failed to restart the application: {0} |
| `SteamAutoSearchStarted` | `log.event.steamAutoSearchStarted` | Info | Запущен автопоиск Steam | Steam auto-search started |
| `SteamAutoSearchSucceeded` | `log.event.steamAutoSearchSucceeded` | Info | Steam найден автоматически: {0} | Steam found automatically: {0} |
| `SteamAutoSearchFailed` | `log.event.steamAutoSearchFailed` | Error | Автопоиск Steam не дал результата | Steam auto-search did not find anything |
| `SteamPathNormalized` | `log.event.steamPathNormalized` | Info | Путь к Steam нормализован: {0} → {1} | Steam path normalized: {0} → {1} |
| `SteamPathInvalid` | `log.event.steamPathInvalid` | Warning | Указанный путь к Steam недействителен: {0} | The specified Steam path is invalid: {0} |
| `FolderAdded` | `log.event.folderAdded` | Info | Добавлена папка: {0} | Folder added: {0} |
| `FolderRemoved` | `log.event.folderRemoved` | Info | Удалена папка: {0} | Folder removed: {0} |
| `ExeAdded` | `log.event.exeAdded` | Info | Добавлен исполняемый файл: {0} | Executable added: {0} |
| `ExeRemoved` | `log.event.exeRemoved` | Info | Удалён исполняемый файл: {0} | Executable removed: {0} |
| `FirewallBlockStarted` | `log.event.firewallBlockStarted` | Info | Начата блокировка через брандмауэр | Firewall block started |
| `FirewallBlockCompleted` | `log.event.firewallBlockCompleted` | Info | Блокировка через брандмауэр завершена | Firewall block completed |
| `FirewallBlockFailed` | `log.event.firewallBlockFailed` | Error | Не удалось выполнить блокировку через брандмауэр: {0} | Firewall block failed: {0} |
| `FirewallUnblockStarted` | `log.event.firewallUnblockStarted` | Info | Начата отмена блокировки через брандмауэр | Firewall unblock started |
| `FirewallUnblockCompleted` | `log.event.firewallUnblockCompleted` | Info | Отмена блокировки через брандмауэр завершена | Firewall unblock completed |
| `FirewallUnblockFailed` | `log.event.firewallUnblockFailed` | Error | Не удалось отменить блокировку через брандмауэр: {0} | Firewall unblock failed: {0} |
| `DriftDetected` | `log.event.driftDetected` | Warning | Обнаружено расхождение между ожидаемым и фактическим состоянием правил | Drift detected between expected and actual rule state |
| `AutostartCreated` | `log.event.autostartCreated` | Info | Включён автозапуск | Autostart enabled |
| `AutostartRemoved` | `log.event.autostartRemoved` | Info | Отключён автозапуск | Autostart disabled |
| `DiagnosticsCopied` | `log.event.diagnosticsCopied` | Info | Отчёт диагностики скопирован в буфер обмена | Diagnostics report copied to clipboard |
| `ReleaseBuildStarted` | `log.event.releaseBuildStarted` | Info | Начата сборка релиза | Release build started |
| `ReleaseBuildCompleted` | `log.event.releaseBuildCompleted` | Info | Сборка релиза завершена | Release build completed |
| `ReleaseBuildFailed` | `log.event.releaseBuildFailed` | Error | Сборка релиза завершилась с ошибкой: {0} | Release build failed: {0} |

> Note (R5): the three `ReleaseBuild*` keys are translated for parity/future
> use, but `build-release.ps1` writes its own plain-text `release-log.txt`
> rather than calling `ILocalizedLogService` (the script has no running
> `RuntimeLanguage`).

## `ILocalizedLogService` usage pattern
```csharp
await _services.LocalizedLog.LogAsync(LogEventKey.FolderAdded, folder.Path);
await _services.LocalizedLog.LogAsync(LogEventKey.RestartFailed, ex.Message);
await _services.LocalizedLog.LogAsync(LogEventKey.AppStarted); // no args
```
Internally: `GetString(key)` → `string.Format(template, args)` (skipped when
`args.Length == 0` to tolerate templates with stray `{`/`}` safely) →
`ILogService.Write(level, message)`. The fallback chain
(current → `ru` → raw key) is inherited unchanged from `LocalizationService`.

## Settings → Journal: the 14 logged actions (FR-007)
| # | User action | `LogEventKey` | Notes |
|---|---|---|---|
| 1 | Opened Settings | `SettingsOpened` | on `SettingsViewModel` construction / window shown |
| 2 | Changed language (in picker, before Apply) | `LanguageChangedRestartRequired` | fired once per *distinct new selection* that differs from `RuntimeLanguage` (not on every keystroke — on commit of the picker value) |
| 3 | Applied | `SettingsApplied` | in `CommitAsync(closeAfter:false)` |
| 4 | Saved | `SettingsSaved` | in `CommitAsync(closeAfter:true)` |
| 5 | Cancelled | `SettingsCancelled` | in `Cancel()` |
| 6 | Added folder | `FolderAdded` | `AddFolderFromPathAsync` success path |
| 7 | Removed folder | `FolderRemoved` | folder remove command |
| 8 | Added EXE | `ExeAdded` | `AddExeFromPathAsync` success path |
| 9 | Removed EXE | `ExeRemoved` | exe remove command |
| 10 | Changed Steam path | `SteamPathNormalized` (valid) / `SteamPathInvalid` (invalid) | `ApplySteamPathCandidate`/`RevalidateSteamPath` |
| 11 | Ran testing | reuse existing test-run log line (already present in `RunTestAsync`) — no new key; only the *outcome* lines below are new |
| 12 | Testing passed | `SettingsApplied`-sibling — modeled via existing `TestOutcome.Success` branch logging a localized "diagnostics passed" line (`diagnostics.outcome.success` through `ILocalizedLogService`-style formatting, reusing `DiagnosticsCopied`'s severity tier: Info) |
| 13 | Testing finished with warning/error | same `RunTestAsync` outcome branch, `Warning`/`Error` level, localized via `diagnostics.outcome.warning`/`diagnostics.outcome.error` |
| 14 | Changed autostart | `AutostartCreated` / `AutostartRemoved` | in `PersistAsync`'s autostart install/uninstall branch |
| — | Changed firewall mode | `FirewallBlockStarted/Completed` or `FirewallUnblockStarted/Completed` | already-existing toggle path in `CompactViewModel`/`MainViewModel` — wired through `ILocalizedLogService` instead of raw `ILogService.Write` |

(Rows 11–13 reuse the existing `RunTestAsync`/`TestOutcome` plumbing and add
two new diagnostics-outcome localization keys rather than new `LogEventKey`
members — the brief's list of ~30 keys does not include "test passed/failed"
as named events, and inventing parallel `LogEventKey` members for them would
duplicate `TestOutcome`'s existing role. This keeps one source of truth for
"what happened during a test run.")

## Journal panel (Settings) — filter → level mapping
Reuses the exact `[INFO]`/`[WARN]`/`[ERROR]` substring convention already
encoded in `LogLineContainsConverter` (feature 003, `E4`/`C3`):
| Filter | Matches lines containing |
|---|---|
| Все / All | (no filter — show all 200 cached lines) |
| Ошибки / Errors | `[ERROR]` |
| Предупреждения / Warnings | `[WARN]` |
| Информация / Info | `[INFO]` |

Source data: `ILogService.ReadLastLinesAsync(200)` on open + on each
auto-refresh tick (reusing `CompactViewModel`'s existing `DispatcherTimer`
cadence — 5 s); "Clear display" only clears the bound `JournalLines`
collection (UI state), never touches the file or re-reads until the next
timer tick or manual "Refresh".
