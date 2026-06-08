# Data Model

## Enums
- **`DesiredState`**: `Blocked`, `Unblocked`
- **`EnforcementMode`**: `ManualToggle`, `AlwaysBlock`, `AlwaysUnblock`, `PauseMonitoring`
- **`RuleCleanupMode`**: `DisableRules`, `DeleteRules`
- **`DirectionMode`**: `OutboundOnly`, `OutboundAndInbound`
- **`HealthLevel`**: `Ok`, `Warning`, `Error`, `Disabled`, `Unknown`, `ReadOnly`
- **`OverallStatus`** (status evaluator output): `FullyBlocked`, `FullyUnblocked`,
  `PartiallyBlocked`, `DriftDetected`, `Error`, `NotConfigured`, `ReadOnlyNoAdmin`
- **`TargetKind`**: `SteamCore`, `Folder`, `StandaloneExe`
- **`FolderStatus`**: `OkBlocked`, `OkUnblocked`, `Partial`, `MissingRules`,
  `PathNotFound`, `ScanError`, `Disabled`
- **`ExeStatus`**: `Blocked`, `Unblocked`, `MissingRule`, `FileNotFound`,
  `Disabled`, `Error`
- **`AppMode`**: mirrors `EnforcementMode` for UI binding purposes (one model)

## Core Models

### `AppSettings`
| Field | Type | Notes |
|---|---|---|
| `Version` | int | for migrations |
| `DesiredState` | `DesiredState` | persisted toggle intent |
| `EnforcementMode` | `EnforcementMode` | |
| `SteamPath` | string? | resolved/overridden Steam install dir |
| `BlockInbound` | bool | maps to `DirectionMode` |
| `RuleCleanupMode` | `RuleCleanupMode` | |
| `CheckIntervalSeconds` | int | default 15 |
| `StartWithWindows` | bool | |
| `StartMinimizedToTray` | bool | |
| `ApplySavedStateOnStartup` | bool | |
| `WarnBeforeUnblock` | bool | |
| `AutoRestoreWhenAlwaysBlock` | bool | |
| `BlockAllExecutablesInSteamFolder` | bool | |
| `AdditionalFolders` | `List<FolderBlockTarget>` | |
| `AdditionalExecutables` | `List<ExeBlockTarget>` | |
| `Ui` | `UiSettings { Theme, CloseToTray }` | |

### `SteamInstallation`
`Path`, `SteamExePath`, `IsValid`, `DiscoverySource` (Registry/Process/DefaultPath/Shortcut/Manual)

### `FirewallTarget`
`Id`, `DisplayName`, `ExecutablePath`, `Kind` (`TargetKind`), `GroupName`
— the canonical unit the status evaluator and rule builder operate on.

### `TargetGroup`
`Name`, `Targets: IReadOnlyList<FirewallTarget>`, `ExpectedCount`,
`CoveredCount` — used for "Steam Core 3/3" style displays.

### `FolderBlockTarget`
`Id (Guid)`, `Name`, `Path`, `Enabled`, `Recursive`, `DiscoveredExeCount`,
`ActiveRuleCount`, `Status (FolderStatus)`

### `ExeBlockTarget`
`Id (Guid)`, `Name`, `Path`, `Enabled`, `AddedAt (DateTimeOffset)`,
`LastSeenAt (DateTimeOffset?)`, `Status (ExeStatus)`

### `FirewallRuleState`
`RuleName`, `GroupName`, `Direction (Inbound/Outbound)`, `Action (Block/Allow)`,
`Enabled`, `ApplicationName`, `Profiles` — a read-back snapshot of one rule.

### `DesiredFirewallState`
`State (DesiredState)`, `Targets: IReadOnlyList<FirewallTarget>`, `DirectionMode`

### `ActualFirewallState`
`Rules: IReadOnlyList<FirewallRuleState>`, `CapturedAt (DateTimeOffset)`

### `DriftReport`
`HasDrift (bool)`, `MissingTargets`, `UnexpectedlyActiveTargets`,
`DisabledTargets`, `Summary (string)`

### `HealthStatus`
`Level (HealthLevel)`, `Overall (OverallStatus)`, `Message`, `CoveragePercent`,
`SteamCoreCoverage (n/total)`, `FolderCoverage (n/total)`, `ExeCoverage (n/total)`,
`LastCheckedAt`, `Drift (DriftReport?)`

### `UserContextInfo`
`UserName`, `Domain`, `Sid`, `IsAdministrator`, `IsElevated`,
`HasFirewallAccess`, `IsInteractiveSession`, `Warning (string?)`

## Relationships
```
AppSettings 1──* FolderBlockTarget
AppSettings 1──* ExeBlockTarget
SteamInstallation 1──* FirewallTarget (Kind = SteamCore)
FolderBlockTarget 1──* FirewallTarget (Kind = Folder, derived at scan time)
ExeBlockTarget 1──1 FirewallTarget (Kind = StandaloneExe)
DesiredFirewallState + ActualFirewallState ──> IStatusEvaluator ──> HealthStatus + DriftReport
```

## Validation Rules
- `ExeBlockTarget.Path`: must exist, must end in `.exe`, must not be a URL
  (`Uri.TryCreate(..., UriKind.Absolute)` + scheme check rejects `http(s)://`),
  must not be empty/whitespace.
- `FolderBlockTarget.Path`: must be an existing directory; scan results capped
  by a depth constant (`MaxScanDepth = 6`) and a count guard to avoid runaway
  scans of huge trees.
- `FirewallRuleNameBuilder` output must always start with `"Steamoff - Block - "`
  and the group must always be `"Steamoff"` — `IFirewallService` implementations
  assert this before mutating, and filter all reads/deletes by both.

## State Transitions (enforcement)
```
ManualToggle:    user action -> DesiredState change -> apply once -> verify
AlwaysBlock:     startup -> force DesiredState=Blocked -> apply -> on drift: restore
AlwaysUnblock:   startup -> force DesiredState=Unblocked -> apply -> on drift: disable/delete
PauseMonitoring: no automatic apply; only HealthStatus is recomputed and shown
```
