# Implementation Plan: Steamoff — Smart Firewall Switch for Steam

**Spec**: [spec.md](spec.md) | **Constitution**: [.specify/memory/constitution.md](../../.specify/memory/constitution.md)

## Summary
Build a 4-project .NET 8 / WPF / MVVM solution that lets a user toggle Steam's
network access through Microsoft Defender Firewall rules, verifies actual
firewall state against desired state on a timer, manages three target
collections (Steam Core, Folders, Standalone EXEs), lives in the tray, and
ships as a single self-contained `win-x64` EXE requiring administrator rights
(with runtime elevation detection and read-only degradation).

## Technical Context
- **Language/Runtime**: C# 12, .NET 8 (`net8.0-windows`), WPF, MVVM (no
  external MVVM framework needed — minimal `ObservableObject`/`RelayCommand`
  base classes in `Steamoff.Core` keep the dependency surface small).
- **Firewall**: COM interop `HNetCfg.FwPolicy2` / `INetFwPolicy2` /
  `INetFwRule` (primary), `netsh advfirewall` argument-list fallback
  (documented, isolated).
- **Persistence**: JSON via `System.Text.Json`, atomic write
  (write-to-temp + replace), `%ProgramData%\Steamoff` → `%AppData%\Steamoff`.
- **Scheduler**: `schtasks.exe` via `ProcessStartInfo.ArgumentList`.
- **Testing**: xUnit + fakes for `IFirewallService`, file system, registry,
  process; `RequiresAdmin` trait category for live-firewall integration tests.
- **Packaging**: `dotnet publish -r win-x64 --self-contained true
  /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true`.

## Constitution Check
| Principle | How the plan satisfies it |
|---|---|
| I. Local-only/no telemetry | No HTTP clients anywhere in the solution; settings/logs are local files only |
| II. Firewall-only enforcement | `IFirewallService` is the single mutation point; `FirewallRuleNameBuilder` enforces the `Steamoff - Block - <Target> - <Direction>` / `Steamoff` group convention; all read/delete operations filter by group+prefix |
| III. Honest state | `IStatusEvaluator` always re-derives `ActualFirewallState` from `IFirewallService.GetRuleStateAsync` and diffs against `DesiredFirewallState`; UI binds to evaluator output, not to a cached toggle flag |
| IV. Admin boundary | `IUserContextService`/`IElevationService` run on startup and on demand; `AdminRequiredDialog`/`UacDeniedDialog` + read-only `IsReadOnly` flag gates all mutating `RelayCommand.CanExecute` |
| V. Test-first core | `Steamoff.Tests` covers settings, discovery, scanner, exe targets, status evaluator, rule-name builder, user context, autostart — all via interfaces/fakes |
| VI. Calm UI | `Themes/DarkOrange.xaml` resource dictionary + custom `Window` chrome (`WindowChrome`) + dialog overlay host; no `MessageBox.Show` in `Steamoff.App` |

No violations requiring justification — the architecture maps directly onto
the constitution's required interfaces and conventions.

## Project Structure
```
Steamoff.sln
src/
  Steamoff.Core/            # models, enums, interfaces, use-cases, status evaluation
  Steamoff.Infrastructure/  # firewall, discovery, scanner, settings, autostart, logging, elevation, tray notification
  Steamoff.App/             # WPF UI: App.xaml, MainWindow, Views, ViewModels, Themes, Tray, Dialogs, app.manifest
tests/
  Steamoff.Tests/           # xUnit unit tests + RequiresAdmin integration category
specs/001-steamoff-smart-firewall-switch/
  spec.md, plan.md, tasks.md, research.md, data-model.md, quickstart.md
  contracts/firewall-service.md, contracts/config-schema.json
```

## Phased Approach

### Phase 0 — Research (`research.md`)
Resolve unknowns: COM interop approach for `INetFwPolicy2` from C# without a
type library reference (late-bound `dynamic` over `Type.GetTypeFromProgID`),
TFM availability (.NET 8 ref packs via SDK 10), Task Scheduler invocation
without extra NuGet deps, toast/balloon notification approach.

### Phase 1 — Design (`data-model.md`, `contracts/`, `quickstart.md`)
Define all entities/enums precisely (matching spec §Key Entities), the
`IFirewallService` contract (method signatures, naming convention, safety
invariants), and the `settings.json` schema (JSON Schema in
`contracts/config-schema.json`). Write `quickstart.md` as the smoke-test
script a reviewer runs after publish.

### Phase 2 — Core domain (`Steamoff.Core`)
Enums, models, interfaces, `FirewallRuleNameBuilder`, `StatusEvaluator`,
minimal MVVM base (`ObservableObject`, `RelayCommand`/`AsyncRelayCommand`),
`AppSettings` + child models with `System.Text.Json` source-generated context.

### Phase 3 — Infrastructure
`ComFirewallService` (+ `NetshFirewallBackend` fallback behind the same
interface), `SteamDiscoveryService`, `TargetScanner`/`FolderTargetService`/
`ExeTargetService`, `JsonSettingsService`, `TaskSchedulerAutostartService`,
`UserContextService`/`ElevationService`, `FileLogService`,
`BalloonNotificationService`.

### Phase 4 — App (WPF)
`App.xaml`/startup orchestration (single-instance mutex, elevation check,
mode `--tray`), `MainWindow` custom chrome + navigation, Dashboard/Steam/
Folders/ExeFiles/FirewallRules/Settings/Logs views + ViewModels, dialog host +
8 custom dialogs, `Themes/DarkOrange.xaml`, `TrayService` (NotifyIcon, colored
icons generated at runtime via `DrawingVisual`→`RenderTargetBitmap`→`.ico`,
or pre-baked resource icons), `app.manifest` with
`requestedExecutionLevel="requireAdministrator"`.

### Phase 5 — Tests
Unit tests per `spec.md` §Testing list; `RequiresAdmin` category skipped by
default (`dotnet test --filter "Category!=RequiresAdmin"`).

### Phase 6 — Build/Publish/Docs/Ship
`dotnet restore/build/test/publish`, `README.md`, `KNOWN_LIMITATIONS.md`,
`IMPLEMENTATION_LOG.md`, commit, push, `FINAL_REPORT.md`.

## Risks & Mitigations
- **.NET 8 desktop ref packs unavailable offline** → documented fallback to
  bump TFM to `net10.0-windows` (single-line change); see ASSUMPTIONS A1.
- **COM interop brittleness** → wrap all `INetFwPolicy2` calls in a thin
  adapter with explicit `Marshal.ReleaseComObject` and typed exceptions so the
  rest of the app never touches `dynamic` directly.
- **Long folder scans freezing UI** → `Task.Run` + `CancellationToken` +
  `IProgress<T>` reporting, depth limit constant, try/catch per directory.
- **Cannot fully verify on a live elevated Windows session in this run** →
  call out explicitly in `FINAL_REPORT.md` as required manual verification.
