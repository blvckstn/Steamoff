# Contract: Restart-Required Language Switching

## Properties (read-only, derived)
| Property | Type | Definition |
|---|---|---|
| `RuntimeLanguage` | `string` | `_services.Localization.CurrentLanguage.Code` |
| `SelectedLanguage` | `string` | `_session.Draft.Language` |
| `IsRestartRequired` | `bool` | `LanguageRestartState.IsRestartRequired(SelectedLanguage, RuntimeLanguage)` ⇔ `SelectedLanguage != RuntimeLanguage` (ordinal, case-insensitive) |

No mutable backing fields are introduced. `RuntimeLanguage` is effectively
constant for the process lifetime once `SettingsViewModel` stops calling
`SetLanguage` (see FR-001/R2) — it only changes via `App.xaml.cs`'s
one-time startup `SetLanguage(settings.Language)` call and the first-launch
dialog (which only runs before any Settings session can exist).

## State machine
| # | Trigger | Effect on `_session.Draft.Language` | Effect on `CurrentLanguage` | `IsRestartRequired` becomes |
|---|---|---|---|---|
| 1 | Settings opens | unchanged (= persisted value) | unchanged | `Draft == Runtime` → **false** (typical case) |
| 2 | User picks language X in picker | `Draft.Language = X` | **unchanged** (no live preview, FR-001) | `X != Runtime` → **true**, warning shown, "Restart now" enabled |
| 3 | User picks the original/runtime language again | `Draft.Language = Runtime` | unchanged | **false** again — warning hides (purely reactive) |
| 4 | Apply | `_session.CommitDraft()` persists `Draft` → new `Original` baseline; `Draft` stays as committed | unchanged | `Committed != Runtime` → **true** if a different language was chosen; window stays open, status `"Настройки применены. Для смены языка требуется перезапуск."` |
| 5 | Save | same persistence as Apply, then window closes | unchanged | reflected in Compact View as `"Требуется перезапуск для смены языка."` banner when **true** |
| 6 | Cancel | `_session.DiscardDraft()` → `Draft = clone(Original)` (the **last persisted** value, which may itself differ from `Runtime` if a *previous* Apply left it that way) | unchanged | **true** only if `Original.Language != Runtime` — i.e. "false, если других сохранённых pending изменений нет", exactly as specified |
| 7 | "Restart now" clicked | settings persisted (if dirty) | new process starts with `CurrentLanguage = persisted Language` | new process: `Selected == Runtime` → **false** |

## Commands & UI
- **`RestartNowCommand`** (`IAsyncRelayCommand`, `CanExecute ⇔ IsRestartRequired`):
  1. `await CommitAsync(closeAfter: false)` if there are unsaved draft changes
     (reuses the existing Apply path — guarantees the new language is persisted
     before relaunch).
  2. Raise `RestartRequested` event (same wiring pattern as `SettingsCommitted`/
     `CloseRequested` in `App.xaml.cs`).
  3. `App.xaml.cs.RestartApplication()`:
     - Resolve `Environment.ProcessPath`; if null/empty → log `RestartFailed`
       with reason `"process-path-unavailable"`, show a UI-Kit-styled error
       notification via `INotificationService`, **do not** tear down the app.
     - `Process.Start(new ProcessStartInfo(processPath, Environment.GetCommandLineArgs().Skip(1)) { UseShellExecute = true })`.
     - On success: log `RestartRequested`, gracefully tear down the tray
       (`TrayService.Dispose()`/existing `ExitApplication` cleanup path) and
       call `Shutdown()`.
     - On any exception starting the new process: log `RestartFailed` with
       the exception message, show the same UI-Kit error notification, leave
       the current instance running untouched.
- **Warning banner** (Settings): visible iff `IsRestartRequired`; localized
  text `settings.language.restartWarning` =
  RU "Для полного применения языка перезапустите Steamoff." /
  EN "Restart Steamoff to fully apply the language change."
- **Status messages** (after Apply/Save while `IsRestartRequired`):
  `settings.toast.appliedRestartPending` =
  RU "Настройки применены. Для смены языка требуется перезапуск." /
  EN "Settings applied. A restart is required to change the language.";
  `settings.toast.savedRestartPending` =
  RU "Требуется перезапуск для смены языка." /
  EN "A restart is required to change the language."
- **Compact View banner**: `compact.languageRestart.banner`, shown iff the
  freshly-loaded `AppSettings.Language` (after `SettingsCommitted`) differs
  from `_services.Localization.CurrentLanguage.Code` — i.e. the same
  `IsRestartRequired` derivation, computed from `AppServices` directly
  (no `SettingsEditSession` in `CompactViewModel`).

## First-launch exception (US2 / FR-004)
`LanguageSelectionViewModel`/`RunFirstLaunchDialogAsync` is **not** changed:
it keeps calling `_services.Localization.SetLanguage(code)` immediately and
persists `IsFirstLaunchCompleted = true`. At that moment there is no
`SettingsEditSession`, no Settings window, and `RuntimeLanguage` simply
*becomes* the chosen language — so `IsRestartRequired` is trivially `false`
the instant Settings might later open. No special-casing needed in
`SettingsViewModel`; the derivation already produces the right answer.

## Failure surfacing contract
`RestartFailed` notifications reuse the existing UI-Kit notification/toast
mechanism (`INotificationService`/`Toast` pattern already used for
`settings.toast.*`); the message is
`settings.toast.restartFailed` = RU "Не удалось перезапустить Steamoff. Подробности в журнале." /
EN "Couldn't restart Steamoff. See the log for details." — directing the
user to the very journal panel this feature adds (US4).
