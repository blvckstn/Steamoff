# Contract: Settings UI Actions & Compact-View Mini-Log

## Settings → Steam Path
| Command/Property | Behavior |
|---|---|
| `SteamPath` (string, two-way) | Raw text bound to the path `TextBox`; every change re-derives `SteamPathCheck` via `ApplySteamPathCandidate` (debounced by `LostFocus` for full revalidation, live for the indicator). |
| `SteamPathCheck` (`SteamPathCheckResult`) | Result of the latest `ISteamPathValidator.Validate` / `FromInstallation` call. |
| `SteamPathStatus` (`PathCheckStatus`) | `SteamPathCheck.Status`, bound through `PathCheckStatusToBrushConverter` to the indicator `Ellipse.Fill`. |
| `SteamPathStatusText` (string) | `Loc[SteamPathCheck.StatusMessageKey]`. |
| `IsSteamPathValid` (bool) | `SteamPathCheck.IsValid`. |
| `IsDiscoveringSteamPath` (bool) | `true` while `AutoFindSteamCommand` is running; disables the button and drives a busy indicator. |
| `AutoFindSteamCommand` | Runs `ISteamDiscoveryService.DiscoverAsync`; on success calls `ApplySteamPathCandidate`/adopts `FromInstallation`; on failure shows a localized warning, leaves the manual picker available. |
| `BrowseSteamFolderCommand` | `IDialogService.PickFolder` → `ApplySteamPathCandidate(picked)`. |
| `ApplySteamPathCandidate(string? rawValue)` | Public — shared by typing, paste, drop, and discovery. Normalizes, validates, updates `SteamPath`/`SteamPathCheck`/draft when valid. |
| `RevalidateSteamPath()` | Public — re-runs `Validate` against the current `SteamPath` (e.g. on `LostFocus`) without changing the text. |

Auto-discovery trigger: runs once when the `SettingsViewModel` is constructed
(Settings opened) **and** whenever `SteamPath` is empty or `SteamPathCheck`
is not `Valid` at that moment — matches spec §3 ("on startup AND
settings-open, auto-discover if path empty/invalid").

## Settings → Additional Folders
| Command | `CommandParameter` | Effect |
|---|---|---|
| `AddFolderCommand` | — | `IDialogService.PickFolder` → normalize → scan → add to `Folders`/draft (skips duplicates by normalized path, toasts on missing dir). |
| `AddFolderFromPathAsync(string)` | n/a (public method) | Same pipeline, entry point for drag&drop. |
| `RescanFolderCommand` | `FolderBlockTarget` | `IFolderTargetService.RescanAsync`, replaces the row in-place to refresh `DiscoveredExeCount`/`Status`. |
| `OpenFolderLocationCommand` | `FolderBlockTarget` | `Process.Start` (shell execute) on the folder path. |
| `RemoveFolderCommand` | `FolderBlockTarget` | Removes from `Folders`, draft, and `IFolderTargetService`. |
| `Enabled` (two-way on each row) | — | Directly mutates the bound `FolderBlockTarget.Enabled` (draft-tracked via the session's structural diff). |

Empty state: a centered `title`+`subtitle` block, visible exactly when
`Folders.Count == 0` (via `BoolToVisibilityConverter ConverterParameter=Invert`
bound to `Folders.Count`).

## Settings → EXE Files
| Command | `CommandParameter` | Effect |
|---|---|---|
| `AddExeCommand` | — | `IDialogService.PickExecutableFile` → `AddExeFromPathAsync`. |
| `AddExeFromPathAsync(string)` | n/a (public method) | Normalizes, resolves `.lnk`, validates `.exe` extension, de-duplicates, adds to `Executables`/draft. Entry point for both the dialog and drag&drop. |
| `OpenExeLocationCommand` | `ExeBlockTarget` | `explorer.exe /select,"<path>"`. |
| `CheckExeStatusCommand` | `ExeBlockTarget` | Re-evaluates and updates the row's `Status`/firewall-rule indicator. |
| `RemoveExeCommand` | `ExeBlockTarget` | Removes from `Executables` and draft. |
| `Enabled` (two-way on each row) | — | Directly mutates `ExeBlockTarget.Enabled`. |

Empty state mirrors Folders, gated on `Executables.Count == 0`.

## Settings window opening (single-instance)
```
Gear icon Button.Command ──┐
                           ├──► CompactViewModel.OpenSettingsCommand
Bottom-left Button.Command ┘           │
                                        ▼
                          SettingsRequested?.Invoke()
                                        │
                                        ▼
                         App.xaml.cs: _mainWindow.ViewModel.SettingsRequested
                                  += OpenSettings   (subscribed once, in App.xaml.cs)
                                        │
                                        ▼
                       App.OpenSettings(): reuses an existing SettingsWindow
                              instance if one is open, else creates one
```
Contract: **exactly one** `SettingsWindow` instance may exist at a time,
regardless of which trigger (gear or footer button) is used, any number of
times in any order.

## Compact view mini-log
| Member | Behavior |
|---|---|
| `RecentLogLines` (`ObservableCollection<string>`) | Populated by `RefreshRecentLogLinesAsync` from `ILogService.ReadLastLinesAsync(30, ct)`, on construction and every 5s via a `DispatcherTimer`. |
| `HasRecentLogLines` (bool) | `RecentLogLines.Count > 0` — drives the empty-state vs. list `Visibility`. |
| `IsLogExpanded` / `ExpandLogCommand` | Toggles visibility of the action-button row; `ExpandLogButtonText` swaps between `compact.miniLog.expand`/`compact.miniLog.collapse`. |
| `OpenFullLogCommand` | Shell-executes `ILogService.LogFilePath`. |
| `CopyDiagnosticsCommand` | `await ILogService.BuildDiagnosticsReportAsync()` → `Clipboard.SetText`, then a balloon notification confirms via `compact.miniLog.copied`. |
| Color coding | `LogLineContainsConverter` (substring match on `[ERROR]`/`[WARNING]`/`[INFO]`) drives `DataTrigger`s on each row's `TextBlock.Foreground`. |

## Test obligations (see tasks.md §10)
- `SettingsViewModel`: Add/Remove Folder & EXE mutate both the
  `ObservableCollection` and the draft; `OpenSettingsCommand` raises
  `SettingsRequested` exactly once per click; Steam-path indicator updates on
  `ApplySteamPathCandidate`/`RevalidateSteamPath`; `AutoFindSteamCommand`
  adopts a discovered installation and surfaces failure.
- `CompactViewModel`: mini-log populates from a fake `ILogService`, empty
  state toggles correctly, `CopyDiagnosticsCommand` writes to the clipboard
  abstraction, `ExpandLogCommand` flips `IsLogExpanded`/button text.
- UI smoke: opening Settings from both buttons yields one window; empty
  states render when collections are empty and hide once populated.
