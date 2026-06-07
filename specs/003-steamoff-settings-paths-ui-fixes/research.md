# Research: Steamoff — Settings Paths & UI Fixes

## 1. Path normalization edge cases
**Decision**: `PathNormalizationService.NormalizeRawPath` performs, in order:
trim whitespace → strip a single matching pair of surrounding quotes →
`Environment.ExpandEnvironmentVariables` → convert `/` to `\` → collapse
duplicated `\` (special-casing a leading `\\` so UNC paths like
`\\server\share` survive collapsing).
**Rationale**: covers every raw-input shape named in the spec (quoted paths,
env vars, mixed slashes, doubled separators, incidental whitespace) with one
deterministic pipeline reused by every entry point (typed, pasted, dropped,
auto-discovered).
**Alternatives considered**: `Path.GetFullPath` — rejected, it requires the
path to be syntactically well-formed *before* quote-stripping/env-expansion
and throws on the very inputs we need to clean up first.

## 2. Steam path resolution chain (file vs. folder vs. shortcut)
**Decision**: `SteamPathValidator.Validate`:
1. normalize
2. if `.lnk` → resolve via injected `Func<string,string?>` (default
   `ShortcutResolver.TryResolveTarget`), then re-enter resolution on the
   resolved target
3. if the candidate is an existing **file**: it must be named `steam.exe`
   (case-insensitive) → folder = its parent, else `WrongExe`
4. if the candidate is an existing **directory**: it must contain
   `steam.exe` → else `SteamExeNotFound`
5. otherwise → `PathNotFound`
**Rationale**: the spec requires saving the *folder* even when the user
supplies `...\Steam\steam.exe` — doing the file/folder branch explicitly
means both inputs converge on the same persisted value and the same `Valid`
status, while still being able to report *why* something failed.
**Alternatives considered**: always treating the input as a folder and
failing on file paths — rejected, breaks the explicit "handle steam.exe
paths" requirement (§4).

## 3. Injectable shortcut resolver for testability
**Decision**: `SteamPathValidator(IPathNormalizationService, Func<string,
string?>? shortcutResolver = null)`, defaulting to
`ShortcutResolver.TryResolveTarget`.
**Rationale**: `ShortcutResolver` is `internal` to `Steamoff.Infrastructure`
and uses COM (`IShellLinkW`/`IPersistFile`), which is awkward and slow to
exercise in unit tests with real `.lnk` files. A delegate seam lets tests
substitute a fake resolver — directly satisfying spec §10 ("`.lnk` via fake
resolver") — while production code keeps the real COM-backed resolution, and
`SteamPathValidator` can stay in the same assembly as `ShortcutResolver`
(required because of its `internal` visibility) without exposing it publicly.

## 4. WPF Open*Dialog ambiguity (`Microsoft.Win32` vs `System.Windows.Forms`)
**Decision**: alias both dialog types explicitly in `WpfDialogService.cs`
(`using OpenFolderDialog = Microsoft.Win32.OpenFolderDialog;` /
`OpenFileDialog = Microsoft.Win32.OpenFileDialog;`), and likewise alias
`DragEventArgs`, `DataFormats`, `DragDropEffects` to their `System.Windows`
forms in `SettingsWindow.xaml.cs`.
**Rationale**: `Steamoff.App.GlobalUsings.g.cs` contains a confirmed global
`using System.Windows.Forms;`, which collides (CS0104) with `Microsoft.Win32`
dialog types and several `System.Windows` drag&drop types. This is the same
class of bug already documented in `IMPLEMENTATION_LOG.md` ("Baseline
build") — explicit aliasing at the point of use is the smallest, most local
fix and avoids removing the global using (which other files may depend on).

## 5. Drag&drop reuse strategy
**Decision**: one `GetFirstDroppedPath(DragEventArgs)` helper extracts
`DataFormats.FileDrop` → `string[]` → first entry; each drop zone's handler
then calls the *same* public pipeline method the dialog-driven Add command
uses (`ApplySteamPathCandidate` / `AddFolderFromPathAsync` /
`AddExeFromPathAsync`).
**Rationale**: guarantees drag&drop and dialog-driven adds behave identically
(same normalization, validation, de-duplication, `.lnk` handling, toasts) —
single source of truth, per spec §1/§2/§4 "incl. `.lnk` resolution".
**Alternatives considered**: separate drop-specific add logic — rejected as
dupliceted logic that would drift from the dialog path over time.

## 6. Mini-log refresh cadence and rendering
**Decision**: a dedicated `DispatcherTimer` ticking every 5 seconds calls
`ILogService.ReadLastLinesAsync(30, ct)` into an `ObservableCollection<string>`
bound through an `ItemsControl`; per-line foreground color is chosen via a
small `LogLineContainsConverter` (`IValueConverter` checking for `[ERROR]` /
`[WARNING]` / `[INFO]` substrings — matching the documented log line format
`{timestamp} [{level}] {message}`) wired as `DataTrigger`s in the item
template's style.
**Rationale**: 5s is responsive enough for a "live" feel without adding
filesystem pressure beyond the existing health-status refresh cadence
(`CheckIntervalSeconds`, minimum 10s); reusing `IValueConverter` + triggers
keeps the color logic declarative and testable in isolation, consistent with
the existing `HealthLevelToBrushConverter`/`TestOutcomeToBrushConverter`
pattern rather than introducing a new per-line view-model wrapper type.
**Alternatives considered**: a `LogLineViewModel` wrapper exposing a
`Brush`/`Level` property — rejected as unnecessary indirection; the raw
string already carries the level, and the spec only requires "color-coded by
substring", not structured parsing.
