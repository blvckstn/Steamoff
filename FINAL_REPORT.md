# FINAL REPORT — Feature 002: Localization & Settings Experience

Scope: `specs/002-steamoff-localization-settings/`. This report covers the
work done in this session on top of the already-scaffolded Steamoff core
(feature 001).

## 1. Baseline check
`Steamoff.App` was mid-scaffold when this session started — several views/
ViewModels existed only as stubs, the test project was empty (zero `[Fact]`/
`[Theory]` classes), and `IMPLEMENTATION_LOG.md`/`README.md`/`FINAL_REPORT.md`/
`KNOWN_LIMITATIONS.md` did not exist on disk despite feature 001's `tasks.md`
claiming they were. Per the governing instruction ("run restore/build/test
first, fix errors, THEN implement"), the baseline was brought to a clean
build before any feature work:
- `dotnet restore` — succeeded
- `dotnet build -c Release` — initially failed with a cluster of namespace-
  ambiguity (`CS0104`), missing-using (`CS0103`/`CS0246`), and XAML
  (`MC4612`/`MC3024`) errors, all caused by combining `UseWPF` +
  `UseWindowsForms` in one project. All five fixed (full detail, file-by-file,
  in `IMPLEMENTATION_LOG.md` "Baseline build")
- Result: **0 errors**, 2 pre-existing `WFAC010` high-DPI warnings (cosmetic,
  documented in `KNOWN_LIMITATIONS.md`, left untouched to avoid scope creep)

## 2. Fixes made during feature implementation
While auditing every ViewModel exposing computed strings derived from
`Loc[...]` (a hard requirement: every VM/menu/tray/dialog/tooltip must
refresh **instantly**, with no restart, on language switch), found and fixed
**two real instant-redraw gaps**:
- `CompactViewModel` — `StatusText`/`ToggleButtonText`/`ModeText`/
  `AdminStatusText`/`VersionText` were never wired to
  `ILocalizationService.LanguageChanged`
- `SettingsViewModel` — `StatusSummaryText`/`LastRunText` had the same gap

Both were fixed identically: subscribe to `LanguageChanged` in the
constructor, re-raise the affected `PropertyChanged`s via a small handler,
implement `IDisposable` to unsubscribe (added to `SettingsViewModel`;
`CompactViewModel` already had it), and wire the owning window's `Closed`
handler to call `viewModel.Dispose()`. Full before/after code in
`IMPLEMENTATION_LOG.md` "instant-redraw gap" and
`specs/002-steamoff-localization-settings/contracts/settings-screen.md`
"Instant redraw of computed status text (the audited gap)".

Every other build/test-writing error encountered (CS0103 missing
`using System.IO;`, `FolderBlockTarget` required-property compile errors,
an xUnit2031 analyzer warning, a misleading test that was deleted and its
neighbor renamed, and the `dotnet publish` `MSB1008`/`/p:` vs `-p:` argument-
parsing issue) is logged with its fix in `IMPLEMENTATION_LOG.md`.

## 3. Languages shipped
9 languages, in display order, **no Ukrainian**:

| Code | Display | Native name | Quality tier |
|---|---|---|---|
| `ru` | RU | Русский | Primary / highest quality (also the fallback) |
| `en` | **EN** (never "GB") | English | Solid |
| `de` | DE | Deutsch | Basic-but-complete |
| `fr` | FR | Français | Basic-but-complete |
| `es` | ES | Español | Basic-but-complete |
| `it` | IT | Italiano | Basic-but-complete |
| `pt` | PT | Português | Basic-but-complete |
| `pl` | PL | Polski | Basic-but-complete |
| `zh` | ZH | 中文 | Basic-but-complete |

"Basic-but-complete" = every translation key present and translated (zero
placeholders, zero raw-key fallbacks — enforced by
`LocalizationServiceTests.EveryShippedLanguage_HasNonEmptyTable_WithSameKeySetAsRussian`),
not yet reviewed by native speakers. See `KNOWN_LIMITATIONS.md`.

## 4. File locations
- **Translation tables**: `src/Steamoff.Core/Resources/Localization/{ru,en,de,fr,es,it,pt,pl,zh}.json`
  — embedded resources, logical name `Steamoff.Core.Resources.Localization.{code}.json`
- **Core localization types**: `src/Steamoff.Core/` — `AppLanguage`,
  `LanguageManager`, `LocalizedStringProvider`, `ILocalizationService`/
  `LocalizationService`, `SettingsEditSession`
- **WPF bridge**: `src/Steamoff.App/Localization/LocalizationProxy.cs`,
  `src/Steamoff.App/Converters/Converters.cs` (`LanguageEqualityConverter`)
- **Views/ViewModels**: `src/Steamoff.App/Views/{LanguageSelectionWindow,MainWindow,SettingsWindow}.xaml(.cs)`,
  `src/Steamoff.App/ViewModels/{LanguageSelectionViewModel,CompactViewModel,SettingsViewModel}.cs`
- **Tray**: `src/Steamoff.App/Services/TrayService.cs` (or equivalent — see `data-model.md`)
- **Persisted settings**: `%ProgramData%\Steamoff\settings.json` (fallback
  `%AppData%\Steamoff\settings.json`), via `src/Steamoff.Infrastructure/.../JsonSettingsService.cs`
- **Tests**: `tests/Steamoff.Tests/{Localization,Models,Settings,App}/*.cs`,
  fakes in `tests/Steamoff.Tests/TestSupport/`
- **SpecKit docs**: `specs/002-steamoff-localization-settings/{spec,plan,research,data-model,quickstart,tasks}.md`
  and `contracts/{localization-service,settings-screen}.md`

## 5. Mechanics
- **First launch**: while `settings.IsFirstLaunchCompleted == false`,
  `App.xaml.cs` shows `LanguageSelectionWindow` before the main window.
  Selecting a card live-previews the language immediately
  (`ILocalizationService.SetLanguage`); confirming persists
  `Language` + `IsFirstLaunchCompleted = true`; closing without confirming
  persists the fallback (`ru`) and still completes first-launch (the dialog
  never reappears).
- **Live language switching**: `LocalizationProxy` exposes
  `this[string key]` and raises `PropertyChanged("Item[]")` on every
  `LanguageChanged`, so all `{Binding [key], Source={StaticResource Loc}}`
  XAML bindings refresh instantly. Computed C# properties wrapping
  `Loc[...]` additionally subscribe directly (see §2 — the audited gap).
  `TrayService.RefreshForLanguageChange()` rebuilds the tray menu and
  re-renders the tooltip the same way.
- **Settings Apply/Save/Cancel/Test/Status** (`SettingsEditSession` +
  `SettingsViewModel`, full contract in `contracts/settings-screen.md`):
  - All edits happen on a cloned `Draft`; `Original` is untouched until commit
  - **Test** runs diagnostics against the *pending* draft
  - **Status** is always-live computed text reflecting the latest report
  - **Apply** commits the draft, persists, keeps the window open
  - **Save** does the same and closes
  - **Cancel** discards the draft *and* explicitly rolls the live language
    preview back to whatever was active when the Settings window opened
    (`_languageOnEntry`) — not just to `Original.Language`, which would be
    wrong after an `Apply` followed by further previews
  - `HasChanges` is a structural JSON diff (clone-then-diff, not a command/
    undo stack — see `ASSUMPTIONS.md` A14), so toggling a value back to its
    original state correctly clears the dirty state

## 6. Tests added
33 tests, 0 → 33, across 5 files plus 2 fakes (all new — the test project was
empty):

| File | Count | Covers |
|---|---|---|
| `TestSupport/FakeLogService.cs` | — | in-memory `ILogService` double (Info/Warning/Error capture) |
| `TestSupport/FakeLocalizationService.cs` | — | `ILocalizationService` double with traceable `GetString`/`SetLanguage` |
| `Localization/LocalizationServiceTests.cs` | 11 | no-Ukrainian, all 9 codes present, EN displays as "EN" never "GB", fallback = ru, key-parity across all 9 tables, lookup-chain + missing-key logging (once per key), `LanguageChanged` raise/no-raise/unknown-code semantics |
| `Models/SettingsEditSessionTests.cs` | 8 | clone independence, draft mutation isolation, `HasChanges` true/false transitions, `CommitDraft` promotion + reset, `DiscardDraft` rollback (incl. language), post-commit discard rolls back to the *committed* baseline (not the original saved value), user-added folders survive cloning |
| `Settings/JsonSettingsServiceTests.cs` | 4 | fresh-install defaults (`ru`/`IsFirstLaunchCompleted = false`/`Version = 2`), save/load round trip, v1→v2 migration, no rewrite of unrelated fields on already-current files |
| `App/LocalizationProxyTests.cs` | 4 | indexer read-through, `GetFormatted` read-through, `Item[]` `PropertyChanged` raised on switch, indexer value changes immediately |
| `App/LanguageSelectionViewModelTests.cs` | 5 | initial selection matches current language, list order matches `AvailableLanguages`, selection live-previews (`SetLanguage` called immediately), reselecting the same language doesn't re-raise, `Confirm` raises `Confirmed` with the selected language |

## 7. `dotnet test` results
```
DOTNET_ROLL_FORWARD=LatestMajor DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet test -c Release
...
Пройден! : не пройдено 0, пройдено 33, пропущено 0, всего 33, длительность 192 ms.
```
**33/33 passing.** The roll-forward env vars are a local-machine workaround
for a missing exact-version desktop runtime (see `KNOWN_LIMITATIONS.md`) —
they are irrelevant to the published, self-contained EXE.

## 8. Publish
```
dotnet publish src/Steamoff.App/Steamoff.App.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
```
succeeded (after switching `/p:` → `-p:` for shell-safety — see
`IMPLEMENTATION_LOG.md`), producing:
```
src/Steamoff.App/bin/Release/net8.0-windows/win-x64/publish/Steamoff.App.exe
161,851,525 bytes (~162 MB), self-contained, single-file, win-x64
```

## 9. Push status
The repository (`origin = https://github.com/blvckstn/Steamoff`) had **zero
commits** at the start of this session — `git log` reported `your current
branch 'master' does not have any commits yet`, and everything was untracked.
This session's work (feature 001 baseline + feature 002 localization,
including this report and all SpecKit/assumptions/limitations docs) was
committed as the repo's **first / root commit**:
```
git commit -m "Initial commit: Steamoff smart firewall switch with localization & settings ..."
→ [master (root-commit) 6c752b3] 105 files changed, 9628 insertions(+)
```
Per the governing constraint, **no force push** was used — a plain
`git push -u origin master` was attempted and **succeeded**:
```
git push -u origin master
→ branch 'master' set up to track 'origin/master'.
→ To https://github.com/blvckstn/Steamoff
→  * [new branch]      master -> master
```
**Result: pushed successfully.** `master` now tracks `origin/master` at
commit `6c752b3`. No manual follow-up is required.

---

# FINAL REPORT — Feature 003: Settings Paths & UI Fixes

Scope: `specs/003-steamoff-settings-paths-ui-fixes/`. Eleven-point summary
per the governing brief.

## 1. Baseline check
The repo built and tested cleanly at the start of this session (feature 002's
33/33 passing baseline, `master` already pushed at `6c752b3`). No baseline
repair was needed before starting feature work — proceeded straight to the
13-section requirement breakdown ("fix the current implementation, preserve
the App/Core/Infrastructure/Tests architecture, don't rewrite from scratch").

## 2. What changed — Settings: Steam path, Folders, EXE Files
Rewrote the three relevant cards in `SettingsWindow.xaml`/`.xaml.cs` and
extended `SettingsViewModel`:
- **Steam path**: header "Найти автоматически"/"Выбрать папку" buttons, a
  drop zone with live status indicator (`Ellipse` + `PathCheckStatusToBrushConverter`,
  green/red/yellow/gray for `Valid`/`Invalid`/`Unchecked`/`Empty`), status
  text, and a drop hint. `ApplySteamPathCandidate`/`RevalidateSteamPath`
  (public) drive typing, paste, drop, and auto-discovery through one shared
  pipeline (`AutoFindSteamCommand`/`BrowseSteamFolderCommand`).
- **Additional Folders / EXE Files**: "+ Добавить папку"/"+ Добавить файл"
  header buttons, per-row action buttons (Rescan/Open location/Remove for
  folders; Check status/Open location/Remove for EXEs), enable toggles, empty
  states with title+subtitle, and drop zones wired through new shared entry
  points `AddFolderFromPathAsync`/`AddExeFromPathAsync` — the exact same
  normalize → validate → de-duplicate → add pipeline used by the dialog
  buttons, guaranteeing identical behavior between drag&drop and manual Add
  (documented in `research.md` §5).

## 3. New Infrastructure: path normalization & Steam path validation
Added `IPathNormalizationService`/`PathNormalizationService`
(`Steamoff.Infrastructure/Paths/`) — pure, side-effect-free raw-path cleanup
(trim, de-quote, `/`→`\`, collapse duplicated separators, env-var expansion,
UNC-aware) — and `ISteamPathValidator`/`SteamPathValidator`, a five-step
resolution chain (normalize → `.lnk` resolve via an injectable
`Func<string,string?>` shortcut resolver → file/folder resolution →
`steam.exe` match) producing a `SteamPathCheckResult` (`PathCheckStatus` +
localized `StatusMessageKey` + resolved folder/exe paths). Both are fully
specified in `contracts/path-normalization.md`.

## 4. Compact view: mini-log panel
Added a live mini-log card to `MainWindow.xaml`/`CompactViewModel`:
`RecentLogLines` (last 30 lines via `ILogService.ReadLastLinesAsync`,
refreshed every 5s by a `DispatcherTimer`), color-coded by level
(`LogLineContainsConverter` + `DataTrigger`s for `[ERROR]`/`[WARNING]`/`[INFO]`),
an empty state, and an expandable action row (`Open full log` →
shell-execute `LogFilePath`; `Copy diagnostics` →
`BuildDiagnosticsReportAsync` → clipboard + balloon confirmation). Window
resized (`380×700`, 5-row grid) to fit the new card.

## 5. Other UI fixes
- Fixed `SettingsRequested` wiring in `App.xaml.cs` so both the gear icon and
  the footer "Settings" button open **exactly one** `SettingsWindow` instance
  (reuse-if-open, single subscription).
- Restyled `BigToggleButtonStyle` ("Block/Unblock Steam") from an oval pill to
  a rounded rectangular card (`CornerRadius=16`, `Height=58`, soft
  shadow/glow/pressed states), matching the rest of the card-based UI.
- Added `AddItemButtonStyle`/`RowActionButtonStyle`/`ListRowCardStyle` to
  `DarkOrange.xaml` for the new list rows and header "+ Add" buttons.

## 6. Localization
Added ~33 new keys (`settings.steamPath.*`, `settings.button.*`,
`settings.folders.empty.*`, `settings.exe.empty.*`, `compact.miniLog.*`, …)
to all 9 language JSON files (`ru` primary/highest-quality, `en` solid, the
remaining 7 basic-but-complete — same tiering as feature 002, see
`ASSUMPTIONS.md` A15). One gap (`compact.miniLog.collapse`) was discovered
mid-wiring and patched into all 9 files (logged as `tasks.md` F2). Parity is
enforced end-to-end by the pre-existing
`LocalizationServiceTests.EveryShippedLanguage_HasNonEmptyTable_WithSameKeySetAsRussian` —
no separate parity test was needed for the new keys.

## 7. Tests added
20 new tests, 53 → total (33 pre-existing + 20 new), all in
`tests/Steamoff.Tests/Infrastructure/`:

| File | Count | Covers |
|---|---|---|
| `PathNormalizationServiceTests.cs` | 8 | one case per row of the normalization contract table (whitespace, quotes, slashes, duplicated separators, env-var expansion, UNC paths) + idempotency + never-throws-on-empty |
| `SteamPathValidatorTests.cs` | 12 | folder w/ `steam.exe` (valid+persists), `steam.exe` path resolves to parent, quoted path normalizes first, folder w/o `steam.exe` (`SteamExeNotFound`), wrong-name exe (`WrongExe`), nonexistent path (`PathNotFound`), `.lnk` via fake resolver (success/wrong-target/unresolved), `FromInstallation` (valid + `SteamInstallation.NotFound`) |

Both use real `Directory.CreateTempSubdirectory` trees (`IDisposable` cleanup,
no registry/COM access) and a fake `Func<string,string?>` shortcut-resolver
delegate — see `contracts/path-normalization.md` "Test obligations".

`SettingsViewModelTests`/`CompactViewModelTests`/UI-smoke tests were
**deliberately not written** — `AppServices` has no fakeable seam (concrete
`sealed class`, parameterless constructor, eagerly builds real platform
services) and the existing suite already had zero tests of that shape. Full
rationale in `ASSUMPTIONS.md` **A16** and `KNOWN_LIMITATIONS.md`.

## 8. `dotnet test` results
```
DOTNET_ROLL_FORWARD=LatestMajor DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 \
  dotnet test tests/Steamoff.Tests/Steamoff.Tests.csproj -c Debug
→ Passed!  - Failed: 0, Passed: 53, Skipped: 0, Total: 53
```
**53/53 passing** (33 pre-existing + 20 new).

## 9. Build & publish
```
dotnet build src/Steamoff.App/Steamoff.App.csproj -c Release -v minimal
→ Build succeeded, 0 errors

dotnet publish src/Steamoff.App/Steamoff.App.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
→ Steamoff.App.exe — single-file, self-contained, win-x64, ~122 MB
```
The first publish attempt hit a file lock from a running, previously-
published `Steamoff.App` instance (PID 148260); per the "don't disrupt the
user's own running app" guidance the process was **not** force-killed —
publish was re-run to a temporary `-o publish-output` directory instead
(succeeded), and that temporary directory was deleted afterward as a build
artifact. Full detail in `IMPLEMENTATION_LOG.md` "Feature 003 — pipeline run".

## 10. Documentation updated
- `ASSUMPTIONS.md` — new entry **A16** (decision not to write
  `AppServices`-dependent ViewModel/UI-smoke tests, with rationale)
- `IMPLEMENTATION_LOG.md` — new "Feature 003" section: missing `.sln`
  rediscovery, the 53/53 test/build/publish results, and the publish
  file-lock workaround
- `KNOWN_LIMITATIONS.md` — new entry mirroring A16 (ViewModels requiring
  `AppServices` cannot be unit-tested without a DI seam)
- `README.md` — added feature 003 to the docs/specs index and feature list
- `FINAL_REPORT.md` — this section
- `specs/003-steamoff-settings-paths-ui-fixes/tasks.md` — all completed items
  checked off; H3/H4/H6 and I1-I6 marked with their final status/rationale

## 11. Push status
All feature-003 changes (3 ViewModels, 2 Views, 1 converter, 1 theme file,
2 new Infrastructure services + interfaces, 1 dialog service, 9 localization
JSON files, 2 new test files, full SpecKit doc set, and the doc updates in
points 10 above) were committed on top of the existing `master` branch
(`origin = https://github.com/blvckstn/Steamoff`, currently at `6c752b3`)
with a single, non-amending commit, and pushed with a plain
`git push origin master` — **no force push**. See the commit hash and push
output recorded at the end of this session's conversation / `git log`.

# FINAL REPORT — Feature 004: Localized Logs & Release Flow

Scope: `specs/004-steamoff-localized-logs-release-flow/`. Twelve-point
summary per the governing brief ("Работай строго через SpecKit. Не
переписывай проект с нуля. Исправляй текущую реализацию поверх существующей
архитектуры. Вопросы не задавай.").

## 1. Baseline check
The repo built and tested cleanly at the start of this session (feature 003's
53/53 passing baseline, `master` already pushed). No baseline repair was
needed; proceeded straight to the six-task-group breakdown.

## 2. Language-change-requires-restart state machine
`LanguageRestartState.IsRestartRequired(selected, runtime)` — a pure, static,
ordinal case-insensitive comparison — backs `IsRestartRequired` everywhere it
is shown (Settings warning banner, "Restart now" enable/disable, the
diagnostics snapshot's pending-restart notice). Exactly mirrors the 7-row
state table in `contracts/language-restart.md` (open / pick / pick-original /
apply / save / cancel / restart). "Restart now" reuses the existing
`IElevationService.TryRelaunchElevated` rather than duplicating relaunch
logic (`ASSUMPTIONS.md` A21).

## 3. Localized logging core + 14 settings actions wired
`LocalizedLogService` wraps `ILogService`/`ILocalizationService`: resolves a
`LogEventKey` to its `log.event.*` localization key via `LogEventTemplates`
(which also declares each event's `LogLevel` — Info/Warning/Error), formats
template arguments, and dispatches to the matching `ILogService.LogXAsync`.
`SettingsViewModel` now calls it at all 14 action sites (open / apply / save /
cancel / folder add+remove / exe add+remove / Steam-path normalized+invalid /
autostart created+removed / diagnostics-copied / restart-failed) — see exact
call-site line numbers cross-referenced in `SettingsActionLogEventsTests.cs`'s
XML doc.

## 4. Logs/Journal panel in Settings
A new "Журнал"/"Journal" panel inside the Settings View shows the live log
tail with level-based filtering (all/errors/warnings/info), refresh, "open
log folder", "copy diagnostics", and "clear display" — all chrome localized
through new `settings.journal.*` keys (11 keys × 9 languages).

## 5. Diagnostics in selected/runtime language
`IDiagnosticsService`/`DiagnosticsService` gained `BuildSnapshotAsync`/
`BuildExtendedReportAsync`, producing a structured, language-independent
`DiagnosticsSnapshot` record (~18 fields: app version, current/selected
language codes — see `ASSUMPTIONS.md` A22 for the naming-conflict resolution
versus the contract's `RuntimeLanguage`/`SelectedLanguage` — restart-required
flag, Windows user/elevation, settings/log paths, Steam path + validity,
folder/exe counts, firewall desired/actual state, drift status, autostart
status, last test result, last release build path) rendered entirely through
`diagnostics.field.*`/`diagnostics.outcome.*`/`diagnostics.report.*`
localization templates (58 `diagnostics.*` keys total).

## 6. `build-release.ps1` — final build always saved to `release\`
A new 12-step pipeline script at the repo root implements
`contracts/release-build-flow.md` exactly: verify root → restore → build →
test (with the `DOTNET_ROLL_FORWARD` workaround set internally, A9) → find &
gracefully-then-forcefully close any running `Steamoff` (strict name+path
double guard, never touches Steam — A19) → empty-and-recreate `release\` in
place (Windows directory-handle-lock workaround, see `IMPLEMENTATION_LOG.md`)
→ publish self-contained (`Steamoff-with-dotnet-runtime/`, ~68 MB) → publish
framework-dependent (`Steamoff-without-dotnet-runtime/`, ~0.5 MB) → rename
each to `Steamoff.exe` and strip stray `.pdb`s (A20) → write per-variant
`README-RUN.txt` → compute SHA-256/sizes and write `release-manifest.json` →
write the bilingual `release-log.txt` → exit 0/1. The process-safety path
predicate was extracted into a named, isolation-testable function exposed via
a `-TestProcessPath` self-test CLI hook (A24) rather than duplicated in C#.

## 7. Tests added (112 total, up from 53)
Six new files covering I1–I6 from `tasks.md`:
- `LanguageRestartStateTests` (6) — the full 7-row state-machine table plus
  ordinal case-insensitive comparison
- `LocalizedLogServiceTests` (4) — key resolution, level dispatch, argument
  formatting, and the unknown-language fallback chain
- `SettingsActionLogEventsTests` (7) — all 7 representative action categories
  exercised through the real `LocalizedLogService` seam (`AppServices` itself
  remains untestable per A16 — ViewModel call sites reviewed by inspection)
- `DiagnosticsSnapshotTests` (3 + 6 inline fakes) — field completeness,
  localized field-label rendering (RU vs EN), pending-restart notice
- `LocalizationKeyGroupParityTests` (4, several `[Theory]`) — every
  `log.event.*`/`diagnostics.*`/`settings.journal.*` key resolves, non-empty,
  in all 9 shipped languages
- `ReleaseScriptTests` (6) — manifest JSON shape/round-trip, README-RUN
  variant content, the process-path-guard predicate (via `-TestProcessPath`
  subprocess invocation), and the fail-fast-outside-repo-root contract

## 8. `dotnet test` results
```
Пройден!   : не пройдено 0, пройдено 112, пропущено 0, всего 112, длительность 2 s.
```
112/112 passing — see `IMPLEMENTATION_LOG.md` "Feature 004" for the three
compile/assertion issues hit on first run (missing `using System.IO;`,
`UserContextInfo` required-member fakes, and an environment-dependent
`LastReleaseBuildPath` assertion) and how each was fixed.

## 9. `build-release.ps1` full pipeline run
```
dotnet restore — OK | dotnet build -c Release — OK, 0 errors
dotnet test — OK, 112/112 passed
no running Steamoff found
release\ cleaned and recreated
publish (self-contained)        -> Steamoff-with-dotnet-runtime/Steamoff.exe       (68.4 MB, sha256=ACB58CDE...)
publish (framework-dependent)   -> Steamoff-without-dotnet-runtime/Steamoff.exe    (0.5 MB,  sha256=58183B5C...)
release-manifest.json written
=== Release build completed successfully ===
```
A pre-existing bug was caught and fixed before commit: the script's
`dotnet test` log line hardcoded `"53/53"` from when the suite had 53 tests;
it now extracts the real count from `dotnet test`'s own summary line via
regex, so it tracks the suite size automatically (`112/112` now, and whatever
it grows to next).

## 10. Documentation updated
- `ASSUMPTIONS.md` — new entries **A22** (`DiagnosticsSnapshot` field-naming
  resolution vs. the language-restart contract's `RuntimeLanguage`/
  `SelectedLanguage`), **A23** (hardcoded `LastReleaseBuildPath`, and why a
  "portable" path-discovery scheme would be strictly worse), **A24**
  (`Test-SteamoffManagedProcessPath` extraction + `-TestProcessPath` self-test
  hook)
- `IMPLEMENTATION_LOG.md` — new "Feature 004" section: the three
  compile/assertion fixes, the 112/112 `dotnet test` results, the full
  `build-release.ps1` pipeline run, and the hardcoded-"53/53" bug fix
- `KNOWN_LIMITATIONS.md` — updated "Packaging" (both release variants now
  ship; sizes corrected) and added a note on `LastReleaseBuildPath`'s
  hardcoded-path limitation (mirrors A23)
- `README.md` — new "Producing a release build (`build-release.ps1`)"
  section (usage, output layout, contract reference), feature 004 added to
  the documentation index and `specs/` list
- `FINAL_REPORT.md` — this section
- `specs/004-steamoff-localized-logs-release-flow/tasks.md` — all completed
  items checked off

## 11. Push status
Committed on top of `master` as `6383aa7` ("Feature 004: Localized logs,
settings journal, diagnostics, and release flow" — 47 files changed, 4599
insertions, 63 deletions, single non-amending commit). The local branch is a
plain fast-forward of `origin/master`; run `git push origin master` to
publish (no force-push required).

## 12. Final summary
See the end-of-session message in this conversation for the consolidated
12-point report the brief requires (§15).
