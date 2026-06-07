# Tasks: Steamoff — Localization & Settings Experience

Derived from [plan.md](plan.md). Core → App → Tests order enforced by
compile-time dependency order; the App layer was brought to a compiling
baseline first since the previous session had left it mid-scaffold.

## T0 — Baseline
- [x] T001 `dotnet restore` / `dotnet build -c Release` / `dotnet test` baseline
      run before touching any feature code; fixed pre-existing App-layer
      compile gaps (namespace ambiguity from `UseWPF`+`UseWindowsForms`,
      missing `using System.IO;`, `Application`/`Binding`/`Brush` aliases) —
      logged in [../../IMPLEMENTATION_LOG.md](../../IMPLEMENTATION_LOG.md)

## T1 — Core localization layer (`Steamoff.Core`)
- [x] T101 `AppLanguage` model (`Code`, `DisplayCode`, `NativeName`, `FlagEmoji`)
- [x] T102 `LanguageManager` — fixed 9-language catalogue (no Ukrainian),
      `Current`/`Resolve`/`SetLanguage`/`LanguageChanged`, `Fallback = ru`
- [x] T103 `LocalizedStringProvider` — embedded-JSON loader + per-language cache
- [x] T104 `ILocalizationService` / `LocalizationService` — facade, lookup
      chain (current → ru → key), missing-key logging (once per key)
- [x] T105 9 embedded translation tables `Resources/Localization/{ru,en,de,fr,es,it,pt,pl,zh}.json`
      with full key parity (`app.*`, `language.dialog.*`, `compact.*`,
      `settings.*`, `tray.*`, `status.*`, `notification.*`)
- [x] T106 `SettingsEditSession` (`Original`/`Draft`/`HasChanges`/`CommitDraft`/`DiscardDraft`)

## T2 — Persistence (`Steamoff.Infrastructure`)
- [x] T201 `AppSettings.Language` (default `"ru"`) and `IsFirstLaunchCompleted`
      (default `false`); `CurrentVersion` bumped `1` → `2`
- [x] T202 Confirmed `JsonSettingsService.MigrateIfNeeded` bumps `Version` and
      lets `System.Text.Json` fill the new fields with model defaults for
      pre-existing files — no custom value-migration code required
- [x] T203 `InternalsVisibleTo("Steamoff.Tests")` (`AssemblyInfo.cs`) to expose
      the temp-directory test seam constructor

## T3 — WPF bridge & first-launch dialog (`Steamoff.App`)
- [x] T301 `LocalizationProxy` (indexer + `GetFormatted` + `Item[]` refresh on `LanguageChanged`)
- [x] T302 `LanguageEqualityConverter` rewritten as `IMultiValueConverter`
      (compares two dynamically-bound `AppLanguage`s for card highlight)
- [x] T303 `LanguageSelectionViewModel` (live preview on select, `Confirmed` event)
- [x] T304 `LanguageSelectionWindow` — neumorphic first-launch "Your language"
      dialog, flag/code/name cards, orange highlight, dismiss → fallback (ru)

## T4 — Two-view UI (`Steamoff.App`)
- [x] T401 `MainWindow` (Compact Steam Switch View) + `CompactViewModel`
      (status pill, big toggle, mode/admin/version labels, settings entry)
- [x] T402 `SettingsWindow` + `SettingsViewModel` — topmost language bar,
      sectioned settings (modes/path/folders/exes/autostart/testing),
      Test/Status/Apply/Save/Cancel bar, `SettingsEditSession`-backed editing
- [x] T403 `App.xaml.cs` startup orchestration — single-instance mutex, load
      settings, set language, run first-launch dialog when needed, build tray,
      show Compact view, wire Settings open/commit/cancel round trip

## T5 — Tray localization
- [x] T501 `TrayService` rebuilt to take `ILocalizationService`; menu/tooltip
      built from `tray.*`/`status.*`/`app.title` keys; `RefreshForLanguageChange()`
      rebuilds the menu and re-renders the cached tooltip on `LanguageChanged`

## T6 — Instant-redraw audit (gap found & fixed)
- [x] T601 Audited every ViewModel exposing computed `Loc[...]`-derived
      properties for a `LanguageChanged` subscription. Found and fixed two
      real gaps: `CompactViewModel` (`StatusText`/`ToggleButtonText`/`ModeText`/
      `AdminStatusText`/`VersionText` were never refreshed on language switch)
      and `SettingsViewModel` (`StatusSummaryText`/`LastRunText`). Both now
      subscribe in their constructor, re-raise the affected `PropertyChanged`s
      via a small handler, and unsubscribe on disposal
      (`SettingsViewModel` gained `IDisposable`; `SettingsWindow` calls
      `viewModel.Dispose()` on close). See
      [../../IMPLEMENTATION_LOG.md](../../IMPLEMENTATION_LOG.md) for the
      before/after and [research.md](research.md) R2 for why the proxy alone
      can't cover computed properties.

## T7 — Tests (`Steamoff.Tests`)
- [x] T701 `TestSupport/{FakeLogService,FakeLocalizationService}` doubles
- [x] T702 `LocalizationServiceTests` — language catalogue (no Ukrainian, EN
      DisplayCode, fallback = ru), lookup chain, missing-key logging
      (once-only), key-set parity across all 9 shipped tables,
      `LanguageChanged` raise/no-raise/unknown-code semantics
- [x] T703 `SettingsEditSessionTests` — clone independence, `HasChanges`
      detection, `CommitDraft`/`DiscardDraft` semantics including
      post-commit rollback baseline and language rollback
- [x] T704 `JsonSettingsServiceTests` — fresh-install defaults (`ru` /
      `IsFirstLaunchCompleted = false`), save/load round trip of the new
      fields, v1 → v2 migration (version bump + field defaults), no
      unrelated-field rewrite on already-current files
- [x] T705 `LocalizationProxyTests` — indexer read-through, `GetFormatted`,
      `Item[]` `PropertyChanged` on language switch, immediate value change
- [x] T706 `LanguageSelectionViewModelTests` — initial selection, language
      list, live preview on select (calls `SetLanguage` immediately),
      no-op on reselecting the same language, `Confirmed` carries the
      selected language
- [x] T707 `dotnet test` run — 33/33 passing (via `DOTNET_ROLL_FORWARD`
      env-var workaround for the local `Microsoft.WindowsDesktop.App`
      version mismatch — see [../../IMPLEMENTATION_LOG.md](../../IMPLEMENTATION_LOG.md))

## T8 — Docs & ship
- [x] T801 SpecKit docs for this feature (this tree)
- [x] T802 `README.md` / `FINAL_REPORT.md` / `ASSUMPTIONS.md` (A11-A15) created/updated
- [x] T803 `IMPLEMENTATION_LOG.md` — every build error and fix from this session logged
- [x] T804 `dotnet restore/build/test/publish` full run — 33/33 tests passing,
      `Steamoff.App.exe` (~162 MB) published to
      `src/Steamoff.App/bin/Release/net8.0-windows/win-x64/publish/`
- [x] T805 `KNOWN_LIMITATIONS.md` created; git commit + push attempted and
      documented in `FINAL_REPORT.md` §9
