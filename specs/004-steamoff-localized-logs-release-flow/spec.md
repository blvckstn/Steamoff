# Feature Spec: Localized Logs, Restart-on-Language-Change & Release Flow

**Feature branch**: `004-steamoff-localized-logs-release-flow`
**Status**: Draft → Implemented
**Input**: Russian-language task brief (see `IMPLEMENTATION_LOG.md` "Feature 004" for the verbatim governing instruction)

## Summary
Four user-facing changes plus one operational tool:
1. Switching the UI language inside Settings now requires an app **restart**
   to take effect (replacing the previous instant-redraw behavior) — so that
   logs and diagnostics never mix two languages mid-session.
2. The event log is now **localized**: every log line written at runtime uses
   the language that was active when the app started (`RuntimeLanguage`), via
   templated, parameterized messages translated into all 9 shipped languages.
3. The Settings window gains a **Log/Journal panel** (last 200 lines, filter
   by level, refresh, open log folder, copy diagnostics, clear display) —
   mirroring and extending the Compact View's mini-log.
4. **Settings actions are logged**: opening Settings, changing language,
   Apply/Save/Cancel, adding/removing folders and EXEs, changing the Steam
   path, and running diagnostics all produce localized log entries.
5. **Diagnostics report in the runtime language**, including a new
   "selected-language pending restart" notice, plus a documented, repeatable
   **release build flow** (`build-release.ps1`) that always produces two
   publish variants (self-contained / framework-dependent) into
   `src/Steamoff.App/release/` with a manifest and build log.

## User Stories

### US1 — Predictable language switching (Priority: P1)
As a user, when I change the interface language in Settings, I want the app
to clearly tell me a restart is needed and offer to do it for me, so that I
never end up with a UI/log that mixes two languages.

**Acceptance scenarios**
1. **Given** Settings is open and the current runtime language is Russian,
   **when** I pick English in the language picker, **then** a warning
   "Для полного применения языка перезапустите Steamoff." appears and the
   "Restart now" button becomes enabled — but the UI text does **not**
   change yet (no live preview).
2. **Given** I picked a new language, **when** I click **Apply**, **then**
   settings are persisted, the Settings window stays open, and a status
   message "Настройки применены. Для смены языка требуется перезапуск."
   is shown.
3. **Given** I picked a new language, **when** I click **Save**, **then**
   settings are persisted, the window closes, and the Compact View shows a
   compact "Restart required to change language" banner.
4. **Given** I picked a new language but have not saved, **when** I click
   **Cancel**, **then** the draft is discarded, the picker reverts to the
   previously *persisted* language, and the restart warning disappears
   (unless an *earlier* Apply/Save already left a persisted language that
   differs from the running one — that pending restart still shows).
5. **Given** the restart warning is visible, **when** I click
   **"Restart now"**, **then** Steamoff saves pending changes, shuts the
   tray down cleanly, relaunches itself with the same arguments, and the new
   process starts in the newly selected language.
6. **Given** the relaunch fails (e.g. the executable path can't be
   resolved), **when** I click "Restart now", **then** a UI-Kit-styled error
   notification appears and the failure is written to the log.

### US2 — First-launch language selection needs no restart (Priority: P1)
As a first-time user, when I pick my language in the first-launch dialog, I
want the app to use it immediately — not ask me to restart something I
haven't even seen yet.

**Acceptance scenarios**
1. **Given** this is the very first launch, **when** I confirm a language in
   the picker, **then** `RuntimeLanguage` becomes that language immediately,
   `IsFirstLaunchCompleted` is persisted as `true`, and no restart prompt
   ever appears for this choice.

### US3 — Logs and diagnostics speak the runtime language (Priority: P1)
As a user reading Steamoff's log file or copying a diagnostics report, I
want every line to be in one consistent, correct language — the language the
app is actually running in right now.

**Acceptance scenarios**
1. **Given** the app is running in Russian, **when** any tracked event occurs
   (app started, settings applied, folder added, …), **then** the log line is
   written in Russian using the localized template for that event.
2. **Given** I changed the selected language to English but have not yet
   restarted, **when** new events occur, **then** they are still logged in
   Russian (the current `RuntimeLanguage`).
3. **Given** I restart after selecting English, **when** new events occur,
   **then** they are logged in English.
4. **Given** a log template key is missing for the runtime language,
   **when** the event is logged, **then** the fallback chain
   (current → `ru` → raw key) from `ILocalizationService.GetString` applies,
   exactly as it does for UI strings — the app never crashes or skips the
   write.

### US4 — Journal inside Settings (Priority: P2)
As a user troubleshooting an issue, I want to read recent log activity
without leaving the Settings window, filter it by severity, and grab a full
diagnostics report in one click.

**Acceptance scenarios**
1. **Given** Settings is open, **when** I open the "Журнал"/"Log" section,
   **then** I see the last 200 log lines in a dark, monospace, scrollable
   card, color-coded by level (errors soft-red, warnings yellow, info
   neutral), refreshing automatically.
2. **Given** the journal is showing, **when** I pick a filter ("All" /
   "Errors" / "Warnings" / "Info"), **then** only matching lines are shown
   (client-side filter over the cached tail — the file itself is untouched).
3. **Given** the journal is showing, **when** I click "Clear display",
   **then** the on-screen list empties but the log file on disk is
   untouched, and the next auto-refresh repopulates it.
4. **Given** the journal is showing, **when** I click "Open log folder" /
   "Copy diagnostics", **then** the OS file-explorer opens at the log
   directory / a localized diagnostics report is copied to the clipboard.

### US5 — Localized, complete diagnostics (Priority: P2)
As a user, I want the diagnostics report to be readable in my language and
to tell me clearly whether a pending language change is waiting for a
restart.

**Acceptance scenarios**
1. **Given** the runtime language is Russian, **when** I view or copy
   diagnostics, **then** every check message and every report field
   (version, language, user, paths, Steam status, firewall state, drift,
   autostart, last test, last release build path, …) is in Russian.
2. **Given** I selected English but have not restarted, **when** I view
   diagnostics, **then** it shows "Выбран новый язык: EN. Он будет применён
   после перезапуска." (or the English equivalent if the runtime language is
   English and a third language is pending).

### US6 — Repeatable two-variant release builds (Priority: P2)
As the maintainer, I want one command that always produces a clean,
known-good pair of release builds in a fixed location, with a manifest I can
hand to testers.

**Acceptance scenarios**
1. **Given** Steamoff is currently running, **when** I run
   `.\build-release.ps1`, **then** the script gracefully closes it (without
   touching Steam or any other process), cleans
   `src/Steamoff.App/release/`, runs restore/build/test, publishes both a
   self-contained (`Steamoff-with-dotnet-runtime`) and a framework-dependent
   (`Steamoff-without-dotnet-runtime`) build, writes `release-manifest.json`
   (with SHA-256 hashes and sizes) and `release-log.txt`, and prints the
   final paths.
2. **Given** any step fails, **when** the script runs, **then** it stops,
   prints a clear error, appends the failure to `release-log.txt`, and exits
   with a non-zero code — without leaving the release folder half-cleaned in
   a way that hides the failure.

## Edge Cases
- User changes the language, applies it, reopens Settings, and changes it
  back to the original *before* restarting → on Cancel/Apply/Save the
  "restart required" state must reflect the **persisted-vs-runtime**
  comparison, not just "did the user touch the picker."
- Log file is mid-rotation or momentarily locked when the journal panel
  refreshes → swallow the transient `IOException`, keep the previous tail.
- `Environment.ProcessPath` is null/empty (e.g. running from `dotnet run`)
  → restart must fail gracefully with a localized error, not throw.
- `build-release.ps1` runs while a *different* Steamoff instance (e.g. a
  manually-launched copy) is locking the output folder → soft-close, then
  force-close only processes whose image path matches Steamoff's own
  build/publish/release output trees — never Steam.

## Functional Requirements
- **FR-001**: Selecting a different language in Settings MUST NOT change
  `ILocalizationService.CurrentLanguage` (no live preview); it MUST only
  update the draft's `Language` field.
- **FR-002**: The Settings View MUST expose a derived `IsRestartRequired`
  flag — true whenever the draft's selected language differs from
  `ILocalizationService.CurrentLanguage.Code` — and show a localized warning
  plus an enabled "Restart now" button while it's true.
- **FR-003**: "Restart now" MUST persist pending changes, log
  `RestartRequested`, relaunch the same executable with the same arguments,
  and shut the current instance down cleanly; failure MUST be logged
  (`RestartFailed`) and surfaced via a UI-Kit-styled notification.
- **FR-004**: The first-launch language dialog MUST continue to apply its
  choice immediately (no restart prompt) and MUST set
  `IsFirstLaunchCompleted = true`.
- **FR-005**: A new localized-logging facility MUST translate ~30 named
  event templates (see `contracts/localized-logging.md`) through
  `ILocalizationService.GetString`, write them at the appropriate severity,
  and MUST be added to all 9 shipped language tables (no Ukrainian, EN
  displays as "EN").
- **FR-006**: The Settings View MUST show the last 200 log lines in a
  filterable, auto-refreshing, UI-Kit-styled journal panel with Refresh /
  Open folder / Copy diagnostics / Clear-display actions.
- **FR-007**: The 14 settings actions listed in
  `contracts/localized-logging.md` "Settings → Journal" MUST each produce
  exactly one localized log entry.
- **FR-008**: `IDiagnosticsService` MUST localize every check message via
  `ILocalizationService` and MUST expose an extended, fully localized text
  report containing the ~18 fields enumerated in `data-model.md`
  `DiagnosticsSnapshot`, including a "selected language pending restart"
  notice when applicable.
- **FR-009**: `build-release.ps1` MUST: verify it runs from the repo root;
  run restore/build/test; gracefully close any running Steamoff instance
  (never Steam); clean and recreate `src/Steamoff.App/release/`; publish
  both variants to the documented paths; write `README-RUN.txt` in each;
  write `release-manifest.json` (schema in `contracts/release-build-flow.md`)
  with SHA-256 + size; write `release-log.txt`; print final paths; and
  return a non-zero exit code on any failure.
- **FR-010**: All new localization keys MUST exist, non-empty, in all 9
  language files, verified by the existing
  `LocalizationServiceTests.EveryShippedLanguage_HasNonEmptyTable_WithSameKeySetAsRussian`
  parity test.

## Key Entities
- **`IsRestartRequired`** (derived `bool` on `SettingsViewModel`) — see
  `contracts/language-restart.md`.
- **`LogEventKey`** (enum, `Steamoff.Core.Logging`) — the ~30 named,
  localizable, leveled log events; see `contracts/localized-logging.md`.
- **`ILocalizedLogService`** — translates `LogEventKey` + args into a
  localized message and writes it via `ILogService` at the mapped severity.
- **`DiagnosticsSnapshot`** — the ~18-field structured diagnostics payload
  behind the localized extended report; see `data-model.md`.
- **`release-manifest.json`** / **`release-log.txt`** — release-flow
  artifacts; see `contracts/release-build-flow.md`.

## Review Checklist
- [x] User stories are independently testable and prioritized
- [x] Acceptance scenarios are concrete and observable
- [x] Edge cases address the trickiest state-derivation and process concerns
- [x] Functional requirements map 1:1 onto the governing brief's 24
      acceptance criteria (§14)
- [x] No speculative scope beyond the brief (no new languages, no telemetry,
      no firewall-logic changes)
