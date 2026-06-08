# Implementation Plan: ScriptFile Firewall Strategy, Strategy Mode Selection & First-Run Self-Test

**Branch**: `007-scriptfile-strategy-mode-selftest` | **Date**: 2026-06-08 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/007-scriptfile-strategy-mode-selftest/spec.md`

## Summary

Add a third `IFirewallService` strategy ("Вариант 3" / `ScriptFileFirewallService`) that writes an actual
adapted-from-`steamOff.ps1` `.ps1` file to disk and runs it as an elevated file (`powershell.exe -NoProfile
-ExecutionPolicy Bypass -File <path> ...`, with `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
-Force` as the script's first statement) — using the exact same `FirewallConstants.RuleGroup`/
`FirewallRuleNameBuilder` identity as the existing COM (`ComFirewallService`, "Вариант 1") and inline-PowerShell
(`NetSecurityFirewallService`, "Вариант 2") strategies, and the same env-var data-passing pattern proven by
`PowerShellRuleInvoker`. Promote `FallbackAwareFirewallService` from a 2-way to a mode-aware 3-way orchestrator:
in "Авто" it cascades COM → NetSecurity-inline → ScriptFile but tries the persisted "last successful strategy"
first and self-heals its memory; in a forced "Вариант N" mode it calls only that one strategy and reports its
own success/failure without silent fallback. Add a four-option strategy-mode setting to `AppSettings`/
`SettingsWindow` ("Авто"/"Вариант 1"/"Вариант 2"/"Вариант 3"), persisted last-successful-strategy state, and a
one-time first-run self-test that safely probes all three strategies (create-then-immediately-remove a
clearly-temporary, non-`Steamoff`-grouped probe rule) to pre-seed the remembered strategy and log a clear
technical+localized summary — without ever touching real `Steamoff`-managed rules or interrupting Steam.

## Technical Context

**Language/Version**: .NET 8 (LTS) / C# 12, WPF (per constitution Technology Constraints — unchanged from features 002-006)

**Primary Dependencies**: Existing `Steamoff.Core`/`Steamoff.Infrastructure` firewall abstraction (`IFirewallService`,
`FirewallConstants`, `FirewallRuleNameBuilder`, `FirewallRuleState`/`ActualFirewallState`), `System.Diagnostics.Process`
for elevated `powershell.exe` child invocation (mirroring `PowerShellRuleInvoker`/`ProcessPowerShellCommandRunner`),
existing `ILogService`/`ILocalizedLogService`/`LogEventKey` logging-and-localization layer, existing `AppSettings`
JSON persistence and `SettingsWindow` MVVM/UI conventions (Dark Orange Neumorphic per constitution principle VI)

**Storage**: JSON settings file under `%ProgramData%\Steamoff` with `%AppData%\Steamoff` fallback (existing
`AppSettings` convention, atomic writes, `Version`-gated migrations) — extended with the new strategy-mode choice,
remembered-last-successful-strategy, and first-run-self-test-record fields; the generated `.ps1` script file itself
is a plain text file written next to the application executable (or a dedicated subfolder beside it)

**Testing**: xUnit with injectable fakes/scripted test doubles (`ScriptedFirewallService`, `FakeLogService`,
`FakeLocalizedLogService`, `IPowerShellCommandRunner` test doubles — established in feature 006's
`FallbackAwareFirewallServiceTests`), plus a `RequiresAdmin`-categorized subset (constitution principle V) for any
test that would need a real elevated `powershell.exe`/live firewall — skipped by default, must self-clean

**Target Platform**: Windows 10/11 desktop, win-x64, self-contained single-file publish (existing constraint)

**Project Type**: desktop-app (layered: `Steamoff.Core` / `Steamoff.Infrastructure` / `Steamoff.App` / `Steamoff.Tests`)

**Performance Goals**: No new perf-sensitive paths — the new strategy and self-test are operations that already run
infrequently (on toggle / once on first launch); script-file write/refresh is a small text-file operation gated by a
content/hash check so it only happens when actually stale

**Constraints**: Local-only/no telemetry (constitution principle I — the self-test must never phone home); must never
trigger a second UAC elevation prompt (the app already runs elevated, the child `powershell.exe` inherits the token);
must never permanently change machine-wide or user-wide `ExecutionPolicy`; must leave zero observable trace in the
live firewall rule set after the first-run self-test; must keep `ComFirewallService`/`NetSecurityFirewallService`/
`FirewallConstants`/`FirewallRuleNameBuilder` functionally unchanged (purely additive); must keep the existing
124/124 test suite green

**Scale/Scope**: Single new `IFirewallService` implementation + one script-file writer/refresher helper, promotion of
`FallbackAwareFirewallService` from 2-way to 3-way mode-aware orchestration, ~3 new `AppSettings` fields, one new
`SettingsWindow` control group (4-option choice), one new first-run self-test runner invoked from app startup,
~4-6 new `LogEventKey` entries (mirrored across all 9 localization files)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Local-Only / No Cloud / No Telemetry** — PASS. The self-test runs entirely on-machine against the local
  firewall API surface; its outcome is persisted only in the existing local `AppSettings` JSON and written only to
  the existing local log files. No network calls are introduced.
- **II. Firewall-Only Enforcement (NON-NEGOTIABLE)** — PASS. The new strategy is exactly the kind of "documented
  fallback" the principle anticipates ("PowerShell `New-NetFirewallRule` ... using safely-escaped arguments"),
  taken one step further into an actual script file rather than an inline `-Command` string — still the same
  `New-NetFirewallRule`/`Get-NetFirewallRule`/`Remove-NetFirewallRule`/`Set-NetFirewallRule` surface. FR-001/FR-002
  lock in that it MUST reuse `FirewallConstants.RuleGroup = "Steamoff"` and
  `FirewallRuleNameBuilder.Build(displayName, direction)` identically — this plan's design carries that requirement
  through verbatim (see contracts).
- **III. Honest State (No Lying Toggles)** — PASS. The orchestrator's existing verify-after-apply discipline
  (`TryStrategyAsync` re-reading `GetCurrentStateAsync` before declaring success) is preserved and extended to the
  third strategy and to forced-single-variant mode, which must "clearly report success/failure... without silent
  fallback" (FR-008) — i.e. it never claims success it cannot verify.
- **IV. Respect the Administrator Boundary** — PASS. FR-004 explicitly requires running "without prompting the user
  for elevation again" and "without permanently changing any machine-wide or user-wide script-execution setting" —
  `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force` is process-scoped and reverts when the child
  process exits (see research.md R1 for the verified mechanics).
- **V. Test-First for Core Logic** — PASS. The new strategy, the 3-way cascade, forced-mode behavior, Auto
  remember-and-self-heal, and the self-test all get unit tests against injectable fakes (mirroring
  `FallbackAwareFirewallServiceTests`/`ScriptedFirewallService`); anything that would need a real elevated process or
  live firewall is isolated into `RequiresAdmin`-categorized tests, skipped by default, self-cleaning.
- **VI. Calm Cohesive UI** — PASS. The new strategy-mode choice is added to the existing `SettingsWindow` using its
  established Dark Orange Neumorphic controls and custom-dialog conventions (no `MessageBox`, no new top-level
  surface — FR-007/Assumption "presented as a single choice in the existing Settings surface").

No violations requiring justification — proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/007-scriptfile-strategy-mode-selftest/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── scriptfile-strategy-and-orchestration.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── Steamoff.Core/
│   ├── Enums/Enums.cs                                  # + FirewallStrategyMode, FirewallStrategyVariant enums
│   ├── Models/AppSettings.cs                           # + StrategyMode, LastSuccessfulStrategy, SelfTest record fields
│   └── Logging/LogEventKey.cs, LogEventTemplates.cs    # + new ScriptFile/cascade/self-test log event keys
│
├── Steamoff.Infrastructure/
│   └── Firewall/
│       ├── ScriptFileFirewallService.cs                # NEW — third IFirewallService strategy ("Вариант 3")
│       ├── FirewallScriptFileWriter.cs                 # NEW — writes/refreshes the adapted .ps1 on disk
│       ├── PowerShellRuleInvoker.cs                    # unchanged — env-var pattern mirrored, not modified
│       ├── ComFirewallService.cs                       # unchanged ("Вариант 1")
│       ├── NetSecurityFirewallService.cs               # unchanged ("Вариант 2")
│       ├── FallbackAwareFirewallService.cs             # promoted to mode-aware 3-way cascade orchestrator
│       └── FirewallSelfTestRunner.cs                   # NEW — first-run probe of all three strategies
│
└── Steamoff.App/
    ├── AppServices.cs                                  # wires the third strategy + self-test into DI/composition
    ├── Views/SettingsWindow.xaml(.cs)                  # + 4-option strategy-mode control group
    └── App.xaml.cs                                     # + first-run self-test trigger on startup

tests/Steamoff.Tests/
├── Infrastructure/
│   ├── ScriptFileFirewallServiceTests.cs               # NEW
│   ├── FirewallScriptFileWriterTests.cs                # NEW
│   ├── FallbackAwareFirewallServiceTests.cs            # extended: 3-way cascade, forced modes, Auto remember/self-heal
│   └── FirewallSelfTestRunnerTests.cs                  # NEW
└── TestSupport/                                        # existing fakes extended (ScriptedFirewallService, etc.)
```

**Structure Decision**: Single-project layered desktop-app structure (matches the existing solution exactly — no new
projects, no Option 2/3 web/mobile layouts apply). All new production code lands in the existing
`Steamoff.Core`/`Steamoff.Infrastructure`/`Steamoff.App` layers at the paths shown above; all new tests land beside
their feature-006 siblings in `tests/Steamoff.Tests/Infrastructure/`, reusing `tests/Steamoff.Tests/TestSupport/`.

## Complexity Tracking

*No Constitution Check violations — this section is intentionally empty.*
