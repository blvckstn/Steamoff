# Implementation Plan: Steamoff — Localization & Settings Experience

**Spec**: [spec.md](spec.md) | **Constitution**: [.specify/memory/constitution.md](../../.specify/memory/constitution.md)

## Summary
Layer a 9-language localization system onto the existing 4-project solution
without touching any firewall logic: a `LanguageManager` + `LocalizedStringProvider`
+ `LocalizationService` triad in `Steamoff.Core` backed by embedded JSON
resources, a WPF-friendly `LocalizationProxy` indexer in `Steamoff.App` for
instant binding refresh, a first-launch picker dialog, and a two-view UI
(Compact Switch + Settings) where Settings edits a cloned
`SettingsEditSession.Draft` through an Apply/Save/Cancel/Test/Status workflow.

## Technical Context
- **Language/Runtime**: C# 12, .NET 8 (`net8.0-windows`), WPF, MVVM — same
  hand-rolled `ObservableObject`/`RelayCommand`/`AsyncRelayCommand` base from
  feature 001, no third-party MVVM or localization framework.
- **Translation storage**: flat `key → string` JSON tables, one per language,
  embedded as resources (`Resources/Localization/{code}.json`, logical name
  `Steamoff.Core.Resources.Localization.{code}.json`) so the published
  single-file EXE stays self-contained — see [research.md](research.md) R1
  for why JSON-over-`.resx` was chosen.
- **Live redraw**: `LocalizationProxy : INotifyPropertyChanged` exposes
  `string this[string key]`, raising `PropertyChanged(Binding.IndexerName)`
  ("Item[]") on every `LanguageChanged`; XAML binds via
  `{Binding [key], Source={StaticResource Loc}}` — see
  [research.md](research.md) R2.
- **Settings editing**: `SettingsEditSession` deep-clones `AppSettings` via a
  `System.Text.Json` serialize/deserialize round trip (camelCase + enum
  string converter) to produce independent `Original`/`Draft` instances;
  `HasChanges` is a structural diff of their serialized forms.
- **Persistence/migration**: `JsonSettingsService` (from feature 001) gains
  two new `AppSettings` fields (`Language`, `IsFirstLaunchCompleted`); the
  existing `MigrateIfNeeded` bumps `Version` to 2 and lets
  `System.Text.Json` fill the new fields with model defaults (`"ru"` /
  `false`) for pre-existing files — no custom migration code needed.
- **Testing**: xUnit + `FakeLogService`/`FakeLocalizationService` doubles
  (`tests/Steamoff.Tests/TestSupport`); `InternalsVisibleTo` exposes
  `JsonSettingsService`'s temp-directory test seam.
- **Packaging**: unchanged — `dotnet publish -r win-x64 --self-contained true
  /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true`.

## Constitution Check
| Principle | How the plan satisfies it |
|---|---|
| I. Local-only/no telemetry | Translation tables are embedded resources; no network access added anywhere |
| II. Firewall-only enforcement | Untouched — this feature adds zero new firewall mutation paths |
| III. Honest state | `SettingsViewModel.StatusSummaryText`/`LastRunText` always re-render the latest real `DiagnosticsReport`, in the active language, on both report changes and language switches |
| IV. Admin boundary | Untouched — `UserContextInfo`/read-only gating flows through unchanged |
| V. Test-first core | `LocalizationServiceTests`, `SettingsEditSessionTests`, `JsonSettingsServiceTests`, `LocalizationProxyTests`, `LanguageSelectionViewModelTests` cover the new surface via fakes/temp dirs — see [quickstart.md](quickstart.md) |
| VI. Calm UI | First-launch picker and both views use the existing `Themes/DarkOrange.xaml` neumorphic styles, custom `WindowChromeHelper` dark title bars, and zero `MessageBox.Show` calls |

No violations requiring justification.

## Project Structure (delta from feature 001)
```
src/Steamoff.Core/
  Localization/{LanguageManager,LocalizationService,LocalizedStringProvider}.cs
  Models/{AppLanguage,SettingsEditSession}.cs
  Resources/Localization/{ru,en,de,fr,es,it,pt,pl,zh}.json   (embedded)
  Interfaces/ILocalizationService.cs

src/Steamoff.App/
  Localization/LocalizationProxy.cs
  ViewModels/{LanguageSelectionViewModel,CompactViewModel,SettingsViewModel}.cs
  Views/{LanguageSelectionWindow,MainWindow,SettingsWindow}.xaml(.cs)
  Converters/Converters.cs   (LanguageEqualityConverter, etc.)

tests/Steamoff.Tests/
  TestSupport/{FakeLogService,FakeLocalizationService}.cs
  Localization/LocalizationServiceTests.cs
  Models/SettingsEditSessionTests.cs
  Settings/JsonSettingsServiceTests.cs
  App/{LocalizationProxyTests,LanguageSelectionViewModelTests}.cs
```

## Phases
1. **Model & service layer** — `AppLanguage`, `LanguageManager` (fixed
   9-language catalogue, fallback = ru), `LocalizedStringProvider` (embedded
   JSON loader + cache), `LocalizationService` (lookup chain + missing-key
   logging + `LanguageChanged`), `SettingsEditSession` (clone/diff/commit/discard).
2. **Persistence** — add `Language`/`IsFirstLaunchCompleted` to `AppSettings`,
   bump `CurrentVersion`, confirm `MigrateIfNeeded` handles v1 → v2 with no
   data loss for `AdditionalFolders`/`AdditionalExecutables`.
3. **WPF bridge** — `LocalizationProxy` indexer + `Loc` app resource;
   `IMultiValueConverter`-based `LanguageEqualityConverter` for card highlight
   (needed because `AppLanguage` has no `INotifyPropertyChanged`).
4. **Dialogs & views** — `LanguageSelectionWindow` (first-launch),
   `MainWindow` (Compact Switch View), `SettingsWindow` (language bar +
   sectioned settings + Test/Status/Apply/Save/Cancel bar).
5. **Tray** — `TrayService` rebuilds its menu and re-renders its tooltip from
   cached state on every `LanguageChanged`.
6. **Wiring & instant-redraw audit** — `App.xaml.cs` startup orchestration,
   first-launch flow; explicit audit of every ViewModel's computed
   `Loc[...]`-derived properties to ensure each subscribes to
   `LanguageChanged` and raises its own `PropertyChanged` (caught and fixed a
   real gap in `CompactViewModel`/`SettingsViewModel` during this phase — see
   [../../IMPLEMENTATION_LOG.md](../../IMPLEMENTATION_LOG.md)).
7. **Tests & docs** — unit tests for every layer above, this SpecKit set,
   README/FINAL_REPORT/ASSUMPTIONS updates.
