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
