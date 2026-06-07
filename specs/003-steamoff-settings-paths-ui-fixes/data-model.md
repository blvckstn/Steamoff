# Data Model: Steamoff — Settings Paths & UI Fixes

## New types

### `PathCheckStatus` (enum, `Steamoff.Core.Enums`)
| Value | Meaning | Indicator color |
|---|---|---|
| `Empty` | No candidate path entered yet | Gray |
| `Unchecked` | Candidate present but not yet (re)validated | Yellow |
| `Valid` | Resolves to a folder containing `steam.exe` | Green |
| `PathNotFound` | Neither file nor directory exists at the path | Red |
| `SteamExeNotFound` | Directory exists but has no `steam.exe` | Red |
| `WrongExe` | File exists but is not named `steam.exe` | Red |
| `ShortcutUnresolved` | `.lnk` could not be resolved to a target | Red |

### `SteamPathCheckResult` (sealed class, `Steamoff.Core.Models`)
| Member | Type | Notes |
|---|---|---|
| `NormalizedFolderPath` | `string?` | The folder to persist when `IsValid` |
| `SteamExePath` | `string?` | Full path to the resolved `steam.exe`, if any |
| `Status` | `PathCheckStatus` (required) | Drives indicator color + message |
| `StatusMessageKey` | `string` (required) | Localization key for status text |
| `IsValid` | `bool` (computed) | `Status == Valid` |
| `Empty` | `static SteamPathCheckResult` | Singleton for the unset state |

Relationships: produced exclusively by `ISteamPathValidator`; consumed by
`SettingsViewModel.SteamPathCheck` (bound through `PathCheckStatusToBrushConverter`
for the indicator `Ellipse.Fill` and `Loc[StatusMessageKey]` for status text).

## New interfaces

### `IPathNormalizationService` (`Steamoff.Core.Interfaces`)
```csharp
string NormalizeRawPath(string rawPath);
```
Pure function: trim → de-quote → expand env vars → slash-normalize →
collapse duplicate separators (UNC-aware). No filesystem access — safe to
call on every keystroke/paste/drop.

### `ISteamPathValidator` (`Steamoff.Core.Interfaces`)
```csharp
SteamPathCheckResult Validate(string candidatePath);
SteamPathCheckResult FromInstallation(SteamInstallation installation);
```
`Validate` runs the full normalize → resolve-shortcut → file-or-folder
resolution chain (see research.md §2). `FromInstallation` adapts a
`SteamInstallation` returned by `ISteamDiscoveryService` into the same result
shape, so discovered and manually-entered paths render identically.

### `IDialogService` (`Steamoff.App.Services`)
```csharp
string? PickFolder(string title, string? initialDirectory = null);
string? PickExecutableFile(string title, string? initialDirectory = null);
```
Abstraction over `Microsoft.Win32.OpenFolderDialog`/`OpenFileDialog`
(implemented by `WpfDialogService`), injected into `SettingsViewModel` so
Add-Folder/Add-EXE/Browse-Steam-folder commands are unit-testable without a
real file-picker UI.

## Modified types

### `FolderBlockTarget` / `ExeBlockTarget` (existing, `Steamoff.Core.Models`)
No shape changes. `SettingsViewModel.Folders` / `.Executables` now expose
them as `ObservableCollection<T>` (synced 1:1 with
`_session.Draft.AdditionalFolders` / `AdditionalExecutables`, which remain
plain `List<T>`) so per-row mutation (add/remove/rescan/replace-on-rescan)
raises the correct collection-changed notifications for the `ItemsControl`
rows and empty-state visibility bindings.

## State flow (Steam path)
```
user input / drop / discovery
        │
        ▼
NormalizeRawPath  ──────────────►  raw candidate string
        │
        ▼
ISteamPathValidator.Validate ───►  SteamPathCheckResult
        │                                   │
        │                                   ├─ IsValid=true → SteamPath = NormalizedFolderPath, draft updated
        ▼                                   │
SteamPathCheck (bound)  ◄───────────────────┘
        │
        ├─► SteamPathStatus → PathCheckStatusToBrushConverter → Ellipse.Fill
        └─► SteamPathStatusText → Loc[StatusMessageKey]
```
