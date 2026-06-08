# Research & Decisions: Feature 004

## R1. `IsRestartRequired`/`RuntimeLanguage`/`SelectedLanguage` — derive, don't store
**Decision**: Do not introduce new mutable fields. Define:
- `RuntimeLanguage` ≡ `ILocalizationService.CurrentLanguage.Code` — the
  language the process actually started in. It no longer changes mid-session
  once `SettingsViewModel` stops calling `SetLanguage` live (R2), so it is
  stable for the whole runtime lifetime — exactly the semantics the brief
  describes ("до перезапуска активным языком считается старый runtime
  language").
- `SelectedLanguage` ≡ `_session.Draft.Language` (already exists, already
  persisted as `AppSettings.Language`).
- `IsRestartRequired` ≡ `!string.Equals(SelectedLanguage, RuntimeLanguage, OrdinalIgnoreCase)`
  — a single derived boolean.

**Why this satisfies every described transition without extra state**:
| Action | What changes | `IsRestartRequired` after |
|---|---|---|
| Pick a different language in the picker | `_session.Draft.Language` | **true** immediately (live warning, matches "UI показывает предупреждение" on selection, before Apply) |
| Apply / Save | `_session.Original.Language` persisted to disk; `_session.Draft` becomes the new baseline | still **true** (persisted ≠ runtime) |
| Cancel | `_session.Draft` re-cloned from `_session.Original` (the *last persisted* value) | **true** only if a *previous* Apply/Save already left a persisted language ≠ runtime — i.e. exactly "false, если других сохранённых pending изменений нет" |
| Restart | new process starts with `CurrentLanguage = persisted Language` | **false** (they're equal again) |

This is the textbook "single source of truth" fix — both cheaper and more
correct than mirroring `Original`/`Draft`/`Runtime` into three independent
booleans that could drift out of sync (e.g. if a future code path forgets to
update one of them). Recorded as `ASSUMPTIONS.md` **A17**.

## R2. Stop calling `SetLanguage` from `SettingsViewModel`
**Decision**: Delete the `_services.Localization.SetLanguage(value.Code)`
call from the `SelectedLanguage` setter and the `_services.Localization.SetLanguage(_languageOnEntry.Code)`
rollback from `Cancel()`. `_languageOnEntry` becomes unused and is removed.//
The `LanguageChanged` event and `SetLanguage` method stay on
`ILocalizationService`/`LanguageManager` — they're still used by the
first-launch `LanguageSelectionViewModel` (which *should* keep live preview,
per the brief's §2 "можно сразу использовать выбранный язык") and by
`LocalizationProxy`'s indexer-refresh mechanism for any future live-switch
needs. Removing them would be a bigger, riskier change than just not calling
them from one place.

## R3. Log-template severity mapping
**Decision**: Each `LogEventKey` maps to exactly one `(localizationKey,
LogLevel)` pair in a static lookup table (`LogEventTemplates`). Levels:
- **Error**: `RestartFailed`, `SteamAutoSearchFailed`, `FirewallBlockFailed`*,
  `ReleaseBuildFailed` — failures the user should notice in red.
- **Warning**: `LanguageChangedRestartRequired`, `DriftDetected`,
  `SteamPathInvalid` — states that need attention but aren't failures.
- **Info**: everything else (lifecycle, settings actions, successful
  operations).

(*`FirewallBlockFailed`/`FirewallUnblockFailed` are not in the brief's list
but are the natural error-path siblings of `FirewallBlockStarted/Completed`
— added for completeness of the start/complete pairs already present in the
firewall apply/remove code paths, see `data-model.md`.)

This keeps the mapping in one place, type-checked, and trivially unit
testable (`LogEventTemplates.LevelFor(key)` / `.LocalizationKeyFor(key)`).

## R4. Testing the language-restart derivation without `AppServices`
**Decision**: `IsRestartRequired` depends only on two strings
(`_session.Draft.Language`, `_services.Localization.CurrentLanguage.Code`)
and pure comparison logic — it does **not** need a live `AppServices`. We
extract the comparison into a tiny, pure, internally-visible static helper
`LanguageRestartState.IsRestartRequired(string selected, string runtime)`
in `Steamoff.Core` and unit-test *that* directly (no seam needed), while the
`SettingsViewModel` property becomes a one-line wrapper around it. This adds
real, fast, isolated coverage of the exact state-machine table in R1 without
touching the `AppServices`-construction blocker documented in `ASSUMPTIONS.md`
A16 — the wrapper itself remains untested for the same reason as every other
`AppServices`-dependent computed property (`StatusSummaryText`, etc).

## R5. Where `ReleaseBuild*` log events are written
**Decision**: `ReleaseBuildStarted/Completed/Failed` localization keys are
added to all 9 language tables (the brief explicitly lists them among the
~30 templates to translate), but they are **not** written through
`ILocalizedLogService` — `build-release.ps1` runs as a standalone PowerShell
process, outside the app's runtime and its `ILocalizationService` instance
("RuntimeLanguage" is a property of a *running Steamoff process*, and a
build script has none). The script writes its own `release-log.txt` in
plain, operator-facing bilingual (RU/EN) text — consistent with how
`IMPLEMENTATION_LOG.md`/`ASSUMPTIONS.md` themselves are bilingual-leaning
maintainer documents, not end-user UI. The localization keys remain
available (and parity-tested) for a hypothetical future in-app "trigger a
release build" feature, which is out of scope here. Recorded as `ASSUMPTIONS.md`
**A18**.

## R6. Generating ~65 keys × 9 languages without hand-editing 9 files by hand
**Decision**: Reuse the exact mechanism from feature 003's
`compact.miniLog.collapse` patch — a single PowerShell-invoked script
(Python, since `python3` works via the `PowerShell` tool though not via
`Bash` per session notes) holding one `{key: {lang: text}}` translation
table, inserted into each of the 9 JSON files at a stable anchor point,
preserving key order and UTF-8/no-BOM encoding. Verified afterwards by the
existing `LocalizationServiceTests.EveryShippedLanguage_HasNonEmptyTable_WithSameKeySetAsRussian`
parity test — no new parity test needed (same as F1/F2/H5 in feature 003).

## R7. Process-safety rules for `build-release.ps1`
**Decision**: Only ever target processes whose `MainModule.FileName` resolves
to a path *inside* one of: `src/Steamoff.App/bin/**`,
`src/Steamoff.App/release/**`, or any prior `publish*`/`publish-output`
directory under the repo — i.e. Steamoff's own build outputs — **and** whose
process name starts with `Steamoff`. `steam.exe`/`steamwebhelper.exe`/etc are
never in those paths, so the path check alone is a safe, redundant-with-name
double guard. Soft-close (`CloseMainWindow` + 3–5 s wait) before any
`Stop-Process -Force`, and log every step to `release-log.txt`. This mirrors
(at script level) the same "never touch Steam, never force-kill blindly"
constraint that governed the human-in-the-loop decision in feature 003's
`IMPLEMENTATION_LOG.md` (PID 148260 was *not* force-killed during publish —
here the script *is* allowed to, but only after a soft-close attempt and only
within the path/name guard, because the user explicitly asked for this
automation in §7 of the brief). Recorded as `ASSUMPTIONS.md` **A19**.
