# Implementation Plan: Резервная стратегия применения правил брандмауэра (dual-strategy firewall enforcement)

**Branch**: `006-firewall-fallback-strategy` | **Date**: 2026-06-08 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/006-firewall-fallback-strategy/spec.md`

## Summary

`ComFirewallService` (the existing late-bound `INetFwPolicy2`/`HNetCfg.FWRule` COM implementation of `IFirewallService`) is being silently intercepted on at least one real machine by third-party security software (COMODO Internet Security HIPS/Defense+), which masks every `Rules.Add()` call as `FileNotFoundException` (`HRESULT 0x80070002`). The user independently confirmed that the `NetSecurity` PowerShell module (`New-NetFirewallRule`/`Get-NetFirewallRule`/`Remove-NetFirewallRule`, which the project's old `steamOff.ps1` prototype used) is **not** intercepted on the same machine and still creates working rules.

This plan adds a second `IFirewallService` implementation (`NetSecurityFirewallService`) that performs the exact same operations through the `NetSecurity` PowerShell cmdlets (run via an elevated, argument-array `powershell.exe` invocation — never string-interpolated/concatenated commands), reusing `FirewallConstants`/`FirewallRuleNameBuilder` so its rules are byte-for-byte indistinguishable from the COM strategy's. A new orchestrator, `FallbackAwareFirewallService`, wraps both: it always tries the existing `ComFirewallService` first, verifies the result actually changed the rule set as expected, and — only on detected failure — re-runs the operation through `NetSecurityFirewallService`, logging (both technically and via the localized user journal) which strategy actually did the work and why a fallback occurred, if one did. `AppServices` swaps its single `ComFirewallService` registration for the orchestrator; nothing else in the app changes.

This is exactly the fallback chain Constitution Principle II already names as acceptable ("`INetFwPolicy2` COM API, with `netsh advfirewall` / PowerShell `New-NetFirewallRule` as documented fallbacks using safely-escaped arguments") — this feature is its first concrete implementation.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (net8.0-windows)

**Primary Dependencies**: WPF/MVVM app shell (`Steamoff.App`), `Steamoff.Infrastructure` (late-bound COM interop via `System.Runtime.InteropServices`, process invocation via `System.Diagnostics.Process`), `Steamoff.Core` (interfaces/models/constants — `IFirewallService`, `FirewallConstants`, `FirewallRuleNameBuilder`, `LogEventKey`/`LogEventTemplates`)

**Storage**: N/A (no new persisted data; rules live in Windows Defender Firewall, journal entries in the existing local log files)

**Testing**: xUnit (`Steamoff.Tests`), fakes-based unit tests (`FakeLogService`, `FakeLocalizationService`, a new `FakeFirewallService`-style strategy fakes for orchestrator tests); a `RequiresAdmin`-categorized integration suite (skipped by default) for the real `NetSecurityFirewallService` against the live firewall, mirroring the project's existing test-tier convention

**Target Platform**: Windows 10/11 desktop, elevated (administrator) process

**Project Type**: Desktop app (single Windows solution: Core/Infrastructure/App/Tests layers)

**Performance Goals**: No perceptible slowdown of the toggle operation on machines where the primary (COM) strategy already works — the fallback path must add zero overhead unless actually invoked (FR-010); when invoked, completing within the same order of magnitude as the manual `steamOff.ps1` run (a few seconds for ~25 targets × 2 directions)

**Constraints**: Must run elevated; must never shell out with string-concatenated/interpolated PowerShell commands (argument-array invocation + `FirewallRuleNameBuilder`-sanitized names only — command-injection safety); must not introduce any new naming/grouping convention (FR-005); must not weaken or alter `ComFirewallService` (FR-001); must preserve per-target resilience for both strategies (FR-009)

**Scale/Scope**: Single new infrastructure service + one orchestrator decorator + DI wiring change + new `LogEventKey` entries (×9 locales) + unit/integration tests; ~typical per-toggle workload is ~25 executables × up to 2 directions = up to ~50 rule operations

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| I. Local-Only, No Cloud, No Telemetry | Fallback strategy only invokes a local, already-elevated `powershell.exe` against local `NetSecurity` cmdlets — no network calls, no telemetry. | PASS |
| II. Firewall-Only Enforcement (NON-NEGOTIABLE) | This feature is the literal implementation of the constitution's own named fallback ("PowerShell `New-NetFirewallRule` as documented fallback using safely-escaped arguments"). Both strategies route every name through `FirewallRuleNameBuilder` and every group through `FirewallConstants.RuleGroup` — single source of truth preserved; foreign rules (incl. the old `SteamOfflineToggle` group) are never touched by either strategy. | PASS |
| III. Honest State (No Lying Toggles) | The orchestrator's whole purpose is to close the "toggle says on, but nothing happened" honesty gap (FR-002, FR-008, SC-004) — it verifies actual rule-set changes rather than trusting a strategy's "no exception" return. | PASS — *this feature directly strengthens this principle* |
| IV. Respect the Administrator Boundary | No change to elevation handling; the fallback strategy assumes the same already-verified elevated context as the primary. If neither strategy can act (e.g. firewall service disabled), the orchestrator surfaces one clear error rather than degrading silently (FR-008, Edge Cases). | PASS |
| V. Test-First for Core Logic | New orchestration/detection logic (which is exactly "core logic": deciding when to fall back, what counts as success) gets fakes-based unit coverage; the real `NetSecurityFirewallService` against the live firewall is `RequiresAdmin`-tagged and skipped by default, matching the existing convention. | PASS |
| VI. Calm, Cohesive UI | No new UI surfaces — this is a pure backend resilience change. Existing localized-log UI (already shipped in feature 004) is the only user-facing surface touched, via new `LogEventKey` entries following the established `log.event.*` convention. | PASS |

No violations requiring justification — Complexity Tracking section is omitted.

## Project Structure

### Documentation (this feature)

```text
specs/006-firewall-fallback-strategy/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── firewall-fallback.md
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
src/Steamoff.Core/
├── Interfaces/
│   └── IFirewallService.cs          # unchanged — both strategies + orchestrator implement it
├── Logging/
│   ├── LogEventKey.cs               # + FirewallStrategyFallback, FirewallBothStrategiesFailed (and similar)
│   └── LogEventTemplates.cs         # + entries for the new keys
└── Models/ (FirewallConstants.cs, FirewallTarget, ...)   # unchanged — reused, not duplicated

src/Steamoff.Infrastructure/Firewall/
├── ComFirewallService.cs            # unchanged (primary strategy, already hardened with per-target try/catch)
├── NetSecurityFirewallService.cs    # NEW — secondary strategy, NetSecurity-cmdlet-based, mirrors steamOff.ps1 logic
├── FallbackAwareFirewallService.cs  # NEW — orchestrating IFirewallService: try primary, verify, fall back, log which ran
└── PowerShellRuleInvoker.cs         # NEW — small helper: safe argument-array powershell.exe invocation + structured result parsing

src/Steamoff.App/
└── AppServices.cs                   # CHANGED — wrap ComFirewallService in FallbackAwareFirewallService(comService, netSecurityService, log, localizedLog)

src/Steamoff.Core/Resources/Localization/{ru,en,de,es,fr,it,pl,pt,zh}.json
                                       # + log.event.* strings for the new LogEventKey entries (parity across all 9)

tests/Steamoff.Tests/
├── Infrastructure/
│   └── FallbackAwareFirewallServiceTests.cs   # NEW — fakes-based: primary succeeds (no fallback), primary fails (fallback runs + is logged), both fail (single clear error)
├── TestSupport/
│   └── (extend or add minimal fake IFirewallService strategies for orchestrator tests; reuse FakeLogService/FakeLocalizationService)
└── Localization/ (existing parity-test suite — must keep passing 49+N/9 with new keys)
```

**Structure Decision**: Single-project layered solution (already established: `Core` → `Infrastructure` → `App` → `Tests`). New code lands entirely in `Steamoff.Infrastructure/Firewall/` (two new services + one helper) plus a one-line DI swap in `Steamoff.App/AppServices.cs`, exactly mirroring how `ComFirewallService` itself is structured and wired today. No new projects, no new top-level folders.

## Complexity Tracking

*No constitution violations — table omitted.*
