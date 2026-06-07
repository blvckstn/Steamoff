# IMPLEMENTATION LOG

Errors hit and how they were fixed, in chronological order. This file covers
the localization/settings feature session (`specs/002-steamoff-localization-settings`);
the underlying firewall-switch core (`specs/001-steamoff-smart-firewall-switch`)
was already in place when this session started.

## Baseline build (before feature work)
The `Steamoff.App` layer was mid-scaffold (several views/ViewModels existed
only as stubs or were missing entirely). Bringing it to a compiling baseline
surfaced a cluster of related errors, all caused by combining
`<UseWPF>true</UseWPF>` with `<UseWindowsForms>true</UseWindowsForms>` in one
project (required for `System.Windows.Forms.NotifyIcon` tray support):

1. **`CS0104` — `Application`/`Binding`/`Brush`/`Brushes`/`MessageBox` ambiguous
   between `System.Windows.*` and `System.Windows.Forms`/`System.Drawing`**.
   The SDK auto-generates global usings for both namespace families when both
   UI flags are on. Fix: explicit type aliases at the top of affected files —
   `using Application = System.Windows.Application;`,
   `using Binding = System.Windows.Data.Binding;`,
   `using Brush = System.Windows.Media.Brush;`,
   `using Brushes = System.Windows.Media.Brushes;` — or full qualification
   (`System.Windows.MessageBox.Show(...)`) for one-off uses. Applied in
   `Converters/Converters.cs`, `Localization/LocalizationProxy.cs`, `App.xaml.cs`.
2. **`CS0246`/`CS0103` — `IOException`, `Path`, `Directory` not found**.
   `System.IO` is *not* a global using for this project type (only
   `System.Drawing`/`System.Windows.Forms`/WPF namespaces are auto-included).
   Fix: added explicit `using System.IO;` to `TargetBuilder.cs`,
   `CompactViewModel.cs`, `App.xaml.cs`, and the new test file
   `Settings/JsonSettingsServiceTests.cs`.
3. **`CS0246` — `ILocalizationService` not found** in
   `LanguageSelectionViewModel.cs`. Fix: added `using Steamoff.Core.Interfaces;`.
4. **`MC4612` — prefix "d" not defined**. New window XAML files
   (`LanguageSelectionWindow`, `MainWindow`, `SettingsWindow`) declared
   `mc:Ignorable="d"` before the `xmlns:mc`/`xmlns:d` namespace declarations.
   Fix: reordered so `xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"`
   and `xmlns:d="http://schemas.microsoft.com/expression/blend/2008"` precede
   `mc:Ignorable="d"`.
5. **`MC3024` — `Border.Style` already set**. `LanguageSelectionWindow.xaml`'s
   card template had both a `Style="{StaticResource CardStyle}"` attribute and
   a `<Border.Style>` element with `BasedOn="{StaticResource CardStyle}"`.
   Fix: removed the attribute, kept the element form (the only one that can
   carry triggers).

After these fixes: `dotnet build -c Release` → **0 errors**, 2 pre-existing
`WFAC010` high-DPI warnings (unrelated to this feature — `app.manifest` vs.
`ApplicationHighDpiMode`, present before this session and left as-is to avoid
scope creep; documented in `KNOWN_LIMITATIONS.md`).

## Feature implementation — instant-redraw gap (found during self-review)
While auditing every ViewModel that exposes computed strings derived from
`Loc[...]` (a requirement: "all VMs/menus/tray/dialogs/tooltips refresh
instantly" on language switch), found that
`CompactViewModel.RaiseLanguageDependentChanges()` — which re-raises
`PropertyChanged` for `StatusText`, `ToggleButtonText`, `ModeText`,
`AdminStatusText`, `VersionText` — existed but was **never wired** to
`ILocalizationService.LanguageChanged`. The `LocalizationProxy`'s `Item[]`
refresh covers *direct* XAML indexer bindings, but not C# computed properties
that wrap the indexer — those need their own subscription (see
[specs/002-steamoff-localization-settings/research.md](specs/002-steamoff-localization-settings/research.md) R2).

**Fix applied to `CompactViewModel`**:
- Constructor: `_services.Localization.LanguageChanged += OnLanguageChanged;`
- New handler: `private void OnLanguageChanged(object? sender, AppLanguage language) => RaiseLanguageDependentChanges();`
- `Dispose()`: added `_services.Localization.LanguageChanged -= OnLanguageChanged;`

**Same audit found the identical gap in `SettingsViewModel`** —
`StatusSummaryText`/`LastRunText` only refreshed via the `LastReport` setter,
not on a pure language switch (e.g. switching languages *after* diagnostics
had already run would leave the status summary in the old language until the
next test run). Fixed identically:
- Made `SettingsViewModel : ObservableObject, IDisposable`
- Constructor: `_services.Localization.LanguageChanged += OnLanguageChanged;`
- New handler re-raises `PropertyChanged` for `StatusSummaryText`/`LastRunText`
- New `Dispose()` unsubscribes
- `SettingsWindow`'s `Closed` handler now also calls `viewModel.Dispose()`

`dotnet build -c Release` re-run after both fixes → still **0 errors,
2 pre-existing warnings**.

## Test project was empty
`find tests -iname "*.cs"` (excluding `obj/`) returned nothing — zero
`[Fact]`/`[Theory]` classes existed despite the test project compiling and
referencing `Steamoff.Core`/`Steamoff.Infrastructure`. Wrote 33 tests from
scratch across 5 files plus 2 fakes (`FakeLogService`, `FakeLocalizationService`
in `TestSupport/`) — see `FINAL_REPORT.md` "Tests added" for the full
breakdown. Also added a `ProjectReference` to `Steamoff.App` (plus
`<UseWPF>true</UseWPF>`/`<UseWindowsForms>true</UseWindowsForms>` on the test
project) to unit-test `LocalizationProxy` and `LanguageSelectionViewModel`
directly — both depend only on lightweight types
(`INotifyPropertyChanged`/`ILocalizationService`), not on any rendered window.

## `dotnet test` runtime mismatch (environment limitation, not a code issue)
`dotnet test` initially failed with a framework-resolution error: the exact
`Microsoft.WindowsDesktop.App` 8.0.0 (x64) was not installed on this machine
— only 6.0.16/10.0.8 (x64) and 8.0.14 (x86) were present. **Workaround**:
`DOTNET_ROLL_FORWARD=LatestMajor DOTNET_ROLL_FORWARD_TO_PRERELEASE=1` env vars
let the test host roll forward to 10.0.8 and run normally. This only affects
local `dotnet test` invocations in this dev environment — the published
self-contained `win-x64` EXE bundles its own runtime and is unaffected.
Documented in `specs/002-steamoff-localization-settings/quickstart.md`.

Final `dotnet test -c Release` (with the env vars):
```
Пройден! : не пройдено 0, пройдено 33, пропущено 0, всего 33, длительность 192 ms.
```

## `CS2012` file lock on re-run (stale build server, not a code issue)
On a later verification run, `dotnet build -c Release` failed with
`CS2012: Не удается открыть "...\obj\Release\net8.0-windows\refint\Steamoff.App.dll"
для записи` ("being used by another process"). No `Steamoff.App.exe` was
running (`Get-Process` showed only `dotnet`/`VBCSCompiler` background
processes); a leftover Roslyn/MSBuild build-server from a prior session held
the reference assembly open. Fix: `dotnet build-server shutdown` released the
lock, and the rebuild then succeeded cleanly (0 errors, 1 pre-existing
`WFAC010` warning). Re-running `dotnet test`/`dotnet publish` afterward
reconfirmed **33/33 tests passing** and a successful single-file `win-x64`
publish (`Steamoff.App.exe`, 161,852,037 bytes).

## `dotnet publish` argument quoting (shell quirk, not a project issue)
`/p:PublishSingleFile=true` was being mis-parsed by the POSIX-style shell as
a second project path (`MSB1008: можно указать только один проект`) because
the leading `/` was stripped before reaching MSBuild. Fix: used the
equivalent `-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`
syntax instead — functionally identical, just shell-safe. Publish then
succeeded:
```
Steamoff.App -> .../bin/Release/net8.0-windows/win-x64/publish/
```
producing a single ~162 MB self-contained `Steamoff.App.exe`.
