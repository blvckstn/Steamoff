# Tasks: Steamoff — Smart Firewall Switch

Derived from [plan.md](plan.md). Executed roughly top-to-bottom; Core before
Infrastructure before App before Tests is enforced by compile-time dependency
order (App → Infrastructure → Core; Tests → all).

## T0 — Project & Solution Scaffolding
- [x] T001 `git init`, add `origin` remote, `.gitignore`
- [x] T002 SpecKit init (`.specify`, `CLAUDE.md`, this `specs/` tree)
- [x] T003 `Steamoff.sln` + 4 project skeletons (`Core`, `Infrastructure`, `App`, `Tests`) with correct references and `net8.0-windows` TFMs
- [x] T004 `app.manifest` with `requestedExecutionLevel="requireAdministrator"` wired into `Steamoff.App.csproj`

## T1 — Core Domain (`Steamoff.Core`)
- [x] T101 Enums (`DesiredState`, `EnforcementMode`, `RuleCleanupMode`, `DirectionMode`, `HealthLevel`, `OverallStatus`, `TargetKind`, `FolderStatus`, `ExeStatus`)
- [x] T102 Models (`AppSettings` + nested, `SteamInstallation`, `FirewallTarget`, `TargetGroup`, `FolderBlockTarget`, `ExeBlockTarget`, `FirewallRuleState`, `DesiredFirewallState`, `ActualFirewallState`, `DriftReport`, `HealthStatus`, `UserContextInfo`)
- [x] T103 Interfaces (`IFirewallService`, `ISteamDiscoveryService`, `ITargetScanner`, `ISettingsService`, `IStatusEvaluator`, `IAutostartService`, `INotificationService`, `IUserContextService`, `IElevationService`, `ILogService`, `ITrayService`, `IExeTargetService`, `IFolderTargetService`)
- [x] T104 `FirewallRuleNameBuilder` (stable, safe naming per contract)
- [x] T105 `StatusEvaluator` (desired vs actual → `HealthStatus`/`DriftReport`/`OverallStatus`)
- [x] T106 Minimal MVVM base (`ObservableObject`, `RelayCommand`, `AsyncRelayCommand`)
- [x] T107 Exceptions (`FirewallAccessDeniedException`, `FirewallOperationException`, `SettingsPersistenceException`)

## T2 — Infrastructure (`Steamoff.Infrastructure`)
- [x] T201 `ComFirewallService` (late-bound `INetFwPolicy2` COM) + `NetshFirewallBackend` fallback
- [x] T202 `SteamDiscoveryService` (registry, process, default paths, shortcuts)
- [x] T203 `TargetScanner` + `FolderTargetService` + `ExeTargetService` (depth-limited, cancellable, validating)
- [x] T204 `JsonSettingsService` (`%ProgramData%`/`%AppData%` fallback, atomic write, backup-on-corruption, migrations)
- [x] T205 `TaskSchedulerAutostartService` (`schtasks.exe` create/delete/query, drift checks)
- [x] T206 `UserContextService` + `ElevationService` (`WindowsIdentity`/`WindowsPrincipal`, relaunch via `runas`)
- [x] T207 `FileLogService` (rolling text log, last-N read, diagnostics report)
- [x] T208 `BalloonNotificationService` (tray balloon notifications)

## T3 — App / WPF (`Steamoff.App`)
- [x] T301 `App.xaml`/`App.xaml.cs` startup orchestration (single-instance mutex, elevation gate, `--tray` arg, DI wiring)
- [x] T302 `Themes/DarkOrange.xaml` (palette, neumorphic styles, toggle, cards, buttons)
- [x] T303 Custom window chrome (`MainWindow.xaml` titlebar, nav rail, status pill)
- [x] T304 `MainViewModel` + navigation (`Dashboard/Steam/Folders/ExeFiles/FirewallRules/Settings/Logs`)
- [x] T305 Dashboard view + VM (big toggle, coverage ring, info cards)
- [x] T306 Steam / Folders / EXE Files views + VMs
- [x] T307 Firewall Rules / Settings / Logs views + VMs
- [x] T308 Custom dialog host + 8 dialogs (`AdminRequired`, `UacDenied`, `ConfirmUnblock`, `DriftDetected`, `SelectSteamFolder`, `AddFolder`, `AddExe`, `Error`)
- [x] T309 `TrayService` (NotifyIcon, status icons, context menu, balloon)
- [x] T310 `app.manifest` + `AssemblyInfo`/csproj version metadata (`Steamoff v0.1.0`)

## T4 — Tests (`Steamoff.Tests`)
- [x] T401 `SettingsServiceTests`
- [x] T402 `SteamDiscoveryServiceTests`
- [x] T403 `TargetScannerTests`
- [x] T404 `ExeTargetServiceTests`
- [x] T405 `StatusEvaluatorTests`
- [x] T406 `FirewallRuleNameBuilderTests`
- [x] T407 `UserContextServiceTests`
- [x] T408 `AutostartServiceTests`
- [x] T409 `RequiresAdmin` integration test category (skipped by default, self-cleaning)

## T5 — Docs & Ship
- [x] T501 `README.md` (RU)
- [x] T502 `KNOWN_LIMITATIONS.md`
- [x] T503 `IMPLEMENTATION_LOG.md`
- [x] T504 `dotnet restore/build/test/publish` run + results captured
- [x] T505 git commit(s), push attempt, `FINAL_REPORT.md`
