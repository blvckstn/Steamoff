# Feature Specification: Steamoff — Smart Firewall Switch for Steam

**Feature branch**: `001-steamoff-smart-firewall-switch`
**Status**: Draft → Implemented (v0.1.0)
**Input**: Build a Windows desktop app that toggles Steam's network access via
Microsoft Defender Firewall, with tray presence, persisted settings, real
firewall-state verification, and support for blocking extra folders/EXEs.

## User Scenarios & Testing

### Primary user story
A Windows user wants to play single-player Steam games or pause multiplayer
without Steam nagging about being offline / re-downloading, or wants to cut a
specific game's network access. They open Steamoff, see whether Steam is
currently blocked or not (verified against the real firewall, not a cached
guess), and flip one big switch. Steamoff creates/enables (or disables) a set
of Microsoft Defender Firewall rules scoped to Steam's executables. The app
keeps living in the tray, periodically re-checks that the rules still match
what the user wants, and tells the user honestly when something doesn't match
(rules removed externally, Steam reinstalled to a new path, a new
`steamwebhelper.exe` appeared, etc).

### Acceptance Scenarios
1. **Given** Steam is installed and currently allowed through the firewall,
   **When** the user presses the big "Заблокировать Steam" toggle, **Then**
   Steamoff creates `Steamoff - Block - <exe> - Outbound` (and `Inbound` if
   enabled) rules for each Steam Core target, the toggle flips to
   "Разблокировать Steam", the dashboard turns green, and the tray icon turns
   green.
2. **Given** Steam is blocked by Steamoff, **When** the user presses
   "Разблокировать Steam" (and `warnBeforeUnblock` is on), **Then** a custom
   confirmation dialog appears; on confirmation the rules are disabled (or
   deleted, per `ruleCleanupMode`), the dashboard turns red, and the tray
   tooltip reads "Steamoff: Steam разблокирован".
3. **Given** the desired state is `Blocked` and `enforcementMode = AlwaysBlock`,
   **When** the periodic check finds the Steamoff rules missing or disabled,
   **Then** Steamoff recreates/re-enables them automatically, logs the
   restoration, and shows a notification.
4. **Given** the desired state is `Unblocked` and `enforcementMode = AlwaysUnblock`,
   **When** the periodic check finds active Steamoff blocking rules, **Then**
   Steamoff disables/removes them per `ruleCleanupMode` and logs the action.
5. **Given** the app is launched without administrator rights, **When** it
   starts, **Then** it shows a custom "Нужны права администратора" dialog with
   options to relaunch elevated, continue read-only, or exit; read-only mode
   disables all firewall-mutating controls but keeps Settings/Logs/Diagnostics
   browsable.
6. **Given** the user adds a folder under "Папки", **When** the scan completes,
   **Then** every discovered `.exe` becomes a coverage target, the folder shows
   a status (`OK blocked` / `OK unblocked` / `partial` / `missing rules` /
   `path not found` / `scan error` / `disabled`), and the dashboard's overall
   coverage percentage is recomputed across Steam Core + folders + standalone
   EXEs.
7. **Given** the user adds a standalone `.exe`, **When** validation runs,
   **Then** non-existent paths, non-`.exe` extensions, and URL-like strings
   are rejected with a friendly inline error, and the app never executes the
   added file.

### Edge Cases
- Steam path changes (reinstall to a new drive) → discovery re-runs, drift is
  reported if old rules point at a now-missing executable.
- Multiple `steamwebhelper.exe` copies in different subfolders → all are
  discovered and each gets its own rule.
- User manually deletes Steamoff's rules from Windows Firewall → next periodic
  check reports `DriftDetected`/`PartiallyBlocked`, auto-restored only in
  `AlwaysBlock` mode.
- `%ProgramData%` not writable → fallback to `%AppData%\Steamoff`, warning
  surfaced in Settings and logged.
- UAC prompt cancelled → app continues in read-only mode, does not crash, logs
  `UAC denied`.
- Folder scan on a huge/slow tree → cancellable, depth-limited, never blocks UI
  thread, logs access-denied subpaths and continues.

## Requirements

### Functional Requirements
- **FR-001**: System MUST discover the Steam installation via registry keys,
  the running `steam.exe` process, well-known default paths, and shortcuts (in
  that priority order), and let the user override the path manually.
- **FR-002**: System MUST identify "Steam Core" targets (`steam.exe`,
  `steamservice.exe`, all `steamwebhelper.exe` copies) by scanning the Steam
  root, `bin/`, `package/`, and bounded-depth subfolders, excluding
  `steamapps\common`.
- **FR-003**: System MUST create/enable/disable/delete Microsoft Defender
  Firewall rules exclusively through the `Steamoff` rule group, named
  `Steamoff - Block - <TargetName> - <Direction>`, scoped to
  Domain/Private/Public profiles, Outbound by default (Inbound optional).
- **FR-004**: System MUST never create, modify, or delete a firewall rule that
  does not carry the `Steamoff -` name prefix and `Steamoff` group.
- **FR-005**: System MUST persist `desiredState` and re-validate it against the
  actual firewall rule set on every check cycle (`IStatusEvaluator`), producing
  one of: `FullyBlocked`, `FullyUnblocked`, `PartiallyBlocked`,
  `DriftDetected`, `Error`, `NotConfigured`, `ReadOnlyNoAdmin`.
- **FR-006**: System MUST support four enforcement modes — Manual Toggle,
  Always Block, Always Unblock, Pause Monitoring — each governing whether/how
  drift is auto-corrected.
- **FR-007**: System MUST let users manage three equally-weighted target
  collections: Steam Core (auto-discovered), Additional Folders (with
  recursive scan, enable/disable, status, exe count, rule coverage), and
  Standalone EXEs (validated, never executed by Steamoff).
- **FR-008**: System MUST compute an overall firewall coverage percentage as
  `covered targets / expected enabled targets * 100` across all three
  collections, respecting `DirectionMode` (Outbound-only vs Outbound+Inbound
  coverage definitions).
- **FR-009**: System MUST detect its own admin/elevation context at runtime
  (not rely solely on the manifest), expose `IUserContextService`/
  `IElevationService`, and gracefully offer self-relaunch via UAC or degrade to
  read-only.
- **FR-010**: System MUST persist settings as JSON under `%ProgramData%\Steamoff`
  (fallback `%AppData%\Steamoff`) with atomic writes, corrupted-file backup,
  and version migration; never silently drop user-added targets.
- **FR-011**: System MUST run a background periodic check (configurable
  interval, default 15s) that re-evaluates real firewall state and, depending
  on enforcement mode, restores or removes rules and raises notifications.
- **FR-012**: System MUST provide an autostart mechanism via Windows Task
  Scheduler (`Steamoff` task, logon trigger, highest privileges, `--tray`
  argument), with create/remove/verify operations and drift detection
  (wrong path, wrong privilege level, wrong user).
- **FR-013**: System MUST live in the system tray with a status-colored icon
  (green/red/orange/gray/read-only-blue-gray), a tooltip reflecting the
  honest current status, and a context menu exposing all primary actions;
  closing the main window minimizes to tray, "Выход" exits.
- **FR-014**: System MUST present all key user decisions (admin required, UAC
  denied, confirm unblock, drift detected, folder/exe pickers, errors) through
  custom-styled dialogs consistent with the dark-orange neumorphic design
  system — never default `MessageBox`.
- **FR-015**: System MUST log structured events (startup, user/SID/elevation,
  discovery, scans, rule mutations, drift, autostart, errors, UAC denial,
  read-only transitions) to `%ProgramData%\Steamoff\logs\steamoff.log`
  (fallback `%AppData%`), with a Logs tab (last 200 lines, open folder, copy
  diagnostics).

### Key Entities
`AppSettings`, `SteamInstallation`, `FirewallTarget`, `TargetGroup`,
`FolderBlockTarget`, `ExeBlockTarget`, `FirewallRuleState`,
`DesiredFirewallState`, `ActualFirewallState`, `AppMode`, `DriftReport`,
`HealthStatus`, `UserContextInfo` — see [data-model.md](data-model.md).

## Review & Acceptance Checklist
- [x] No cloud APIs, no telemetry, no DRM/Steam-file modification (Constitution I/II)
- [x] All firewall mutation scoped to `Steamoff` group/prefix (Constitution II, FR-003/004)
- [x] Desired vs actual state always reconciled and surfaced (Constitution III, FR-005)
- [x] Runtime elevation check + graceful degradation (Constitution IV, FR-009)
- [x] Custom dialog system, no MessageBox for key decisions (Constitution VI, FR-014)
- [x] Unit-testable core via interfaces/fakes; RequiresAdmin tests isolated (Constitution V)
