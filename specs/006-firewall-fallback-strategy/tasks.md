---

description: "Task list for feature 006: dual-strategy firewall fallback (NetSecurity-based fallback for ComFirewallService)"
---

# Tasks: Резервная стратегия применения правил брандмауэра (dual-strategy firewall enforcement)

**Input**: Design documents from `/specs/006-firewall-fallback-strategy/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/firewall-fallback.md](./contracts/firewall-fallback.md), [quickstart.md](./quickstart.md)

**Tests**: Included — Constitution Principle V (Test-First for Core Logic) mandates fakes-based unit coverage for the new orchestration logic, and the spec's success criteria (SC-001/SC-004) are only verifiable through tests that simulate primary-failure → fallback transitions.

**Organization**: Tasks are grouped by user story (P1/P2/P3 from spec.md) to enable independent implementation and validation of each.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 — maps to spec.md user stories
- File paths are exact and relative to the repository root (`c:\Users\adm\Desktop\13\vibe\Steamoff`)

## Path Conventions

Single-project layered .NET solution (per plan.md Structure Decision):
- `src/Steamoff.Core/` — interfaces, models, logging enums/templates, localization JSON
- `src/Steamoff.Infrastructure/Firewall/` — firewall service implementations
- `src/Steamoff.App/` — DI wiring (`AppServices.cs`)
- `tests/Steamoff.Tests/` — xUnit tests + fakes

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare the new source location and confirm the build is green before any new code lands.

- [ ] T001 Confirm `src/Steamoff.Infrastructure/Firewall/` exists (it already hosts `ComFirewallService.cs`) and is the target directory for the two new services in this feature; no new project/folder is created (per plan.md Structure Decision)
- [ ] T002 Run `dotnet build` on the full solution and `dotnet test tests/Steamoff.Tests/Steamoff.Tests.csproj` to capture the current green baseline before changes begin (so regressions introduced by this feature are unambiguous)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared types, log infrastructure, and test fakes that ALL three user stories depend on. No user-story work can start until this phase is complete — every story needs the strategy/outcome types (data-model.md), the new log keys (contracts C5), and the orchestrator skeleton to exist.

**⚠️ CRITICAL**: Phases 3, 4, and 5 all build on `FallbackAwareFirewallService` and the shared enums — none of them is independently startable before this phase finishes.

- [ ] T003 [P] Add `FirewallStrategyKind` enum (`Primary`, `Fallback`) and `StrategyFailureReason` enum (`Exception`, `NoRulesProduced`) to a new file `src/Steamoff.Core/Models/FirewallStrategyModels.cs`, per data-model.md
- [ ] T004 [P] Add two new values to `LogEventKey` enum in `src/Steamoff.Core/Logging/LogEventKey.cs`: `FirewallStrategyFallbackUsed`, `FirewallBothStrategiesFailed` (appended after the existing `FirewallUnblockFailed`, preserving existing member order/values per contracts/firewall-fallback.md C5)
- [ ] T005 Add corresponding entries to `src/Steamoff.Core/Logging/LogEventTemplates.cs`: `[LogEventKey.FirewallStrategyFallbackUsed] = ("log.event.firewallStrategyFallbackUsed", LogLevel.Warning)` and `[LogEventKey.FirewallBothStrategiesFailed] = ("log.event.firewallBothStrategiesFailed", LogLevel.Error)` (depends on T004)
- [ ] T006 [P] Add `log.event.firewallStrategyFallbackUsed` and `log.event.firewallBothStrategiesFailed` localized strings (with the same parameter placeholders/tone as existing `log.event.firewallBlock*`/`firewallUnblock*` entries) to all 9 locale files under `src/Steamoff.Core/Resources/Localization/` (`ru.json`, `en.json`, `de.json`, `es.json`, `fr.json`, `it.json`, `pl.json`, `pt.json`, `zh.json`)
- [ ] T007 Run the existing localization-parity test suite (`tests/Steamoff.Tests/.../Localization`) and fix any reported gaps from T006 before proceeding (depends on T006)
- [ ] T008 [P] Add minimal scriptable fake `IFirewallService` implementations to `tests/Steamoff.Tests/TestSupport/` (e.g. `ScriptedFirewallService.cs`) that can be configured per-test to: throw on `ApplyBlockAsync`/`RemoveOrDisableAsync`, succeed but report an empty `ActualFirewallState` from `GetCurrentStateAsync`, or succeed with the expected non-empty rule set — reusable across all three user stories' tests
- [X] T009 Create the skeleton class `FallbackAwareFirewallService : IFirewallService` in `src/Steamoff.Infrastructure/Firewall/FallbackAwareFirewallService.cs` with constructor `(IFirewallService primary, IFirewallService secondary, ILogService log, ILocalizedLogService localizedLog)`, delegating `GetCurrentStateAsync`/`IsManagedBySteamoff` straight to `primary` (per contracts/firewall-fallback.md C3), and `ApplyBlockAsync`/`RemoveOrDisableAsync` initially delegating to `primary` only (no fallback logic yet — that's built out per-story below) (depends on T003)

**Checkpoint**: Shared enums, log keys (+ localization), test fakes, and orchestrator skeleton exist and compile/pass — user story implementation can now proceed.

---

## Phase 3: User Story 1 - Автономный режим включается даже когда основной способ заблокирован средой пользователя (Priority: P1) 🎯 MVP

**Goal**: When the primary (COM-based) strategy fails to actually produce the expected `Steamoff`-group rules — whether by throwing or by silently producing nothing — the app automatically retries through a working `NetSecurity`-cmdlet-based fallback strategy, so the user ends up with real, functioning firewall rules without any manual intervention.

**Independent Test**: On a machine where `ComFirewallService` is known to fail (per the session's diagnosed COMODO-affected machine), toggle blocking on; verify via `netsh advfirewall firewall show rule name=all | findstr /I "Steamoff"` that rules now exist, named `Steamoff - Block - <Target> - <Direction>`, and that Steam's network access is actually blocked — exactly Quickstart Scenario B.

### Tests for User Story 1 ⚠️

> Write these first; they must fail (red) before the corresponding implementation task turns them green.

- [ ] T010 [P] [US1] Test in `tests/Steamoff.Tests/Infrastructure/NetSecurityFirewallServiceTests.cs`: given a list of `FirewallTarget`s and `DirectionMode`, `ApplyBlockAsync` builds `New-NetFirewallRule` invocations whose `-DisplayName`/`-Group` exactly match `FirewallRuleNameBuilder.Build(...)`/`FirewallConstants.RuleGroup` (assert against the constructed argument list / invocation request, not a live firewall — inject a fake process/command runner)
- [ ] T011 [P] [US1] Test in `tests/Steamoff.Tests/Infrastructure/NetSecurityFirewallServiceTests.cs`: a single target whose invocation fails (simulated non-zero exit / thrown error) is logged as a warning and does not prevent the remaining targets from being processed (mirrors `ComFirewallService`'s per-target try/catch — FR-009)
- [ ] T012 [P] [US1] Test in `tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs` using `ScriptedFirewallService` (T008): primary throws during `ApplyBlockAsync` → orchestrator invokes `secondary.ApplyBlockAsync` with the same `targets`/`directionMode`, and the overall call completes successfully when `secondary` succeeds
- [ ] T013 [P] [US1] Test in `tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs`: primary completes without throwing, but post-operation verification via `GetCurrentStateAsync` shows zero/insufficient `Steamoff`-group rules for the requested targets → orchestrator still invokes `secondary` (covers the "silent no-op" case that exception-only detection would miss — this is the actual bug being fixed)
- [ ] T014 [P] [US1] Test in `tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs`: primary succeeds AND verification confirms expected rules exist → `secondary` is never invoked (asserts zero calls on the fake) — guards FR-010 (no overhead on the happy path)
- [ ] T015 [P] [US1] Test in `tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs`: same fallback decision logic (throw-detection AND empty-verification-detection) applies symmetrically to `RemoveOrDisableAsync`, not just `ApplyBlockAsync`

### Implementation for User Story 1

- [ ] T016 [US1] Implement `PowerShellRuleInvoker` helper in `src/Steamoff.Infrastructure/Firewall/PowerShellRuleInvoker.cs`: launches elevated `powershell.exe -NoProfile -NonInteractive` with `ProcessStartInfo.ArgumentList` (never string-concatenated commands), passing `New-NetFirewallRule`/`Get-NetFirewallRule`/`Remove-NetFirewallRule`/`Set-NetFirewallRule` invocations built from already-validated, strongly-typed values (`FirewallConstants`, `FirewallRuleNameBuilder.Build(...)`, `FirewallTarget.ExecutablePath`); captures stdout/stderr/exit code and surfaces structured failures (per research.md R1 and contracts C2's command-injection-safety requirement)
- [ ] T017 [US1] Implement `NetSecurityFirewallService : IFirewallService` in `src/Steamoff.Infrastructure/Firewall/NetSecurityFirewallService.cs`: `ApplyBlockAsync`/`RemoveOrDisableAsync` iterate `targets` with the SAME per-target try/catch resilience as `ComFirewallService.ApplyBlockAsync` (warn-and-continue on a single target's failure, FR-009), building each rule exclusively via `FirewallConstants.RuleGroup`/`FirewallRuleNameBuilder.Build(displayName, direction)` and invoking `PowerShellRuleInvoker` (T016); `GetCurrentStateAsync` parses `Get-NetFirewallRule -Group "Steamoff"` output into the same `ActualFirewallState`/`FirewallRuleState` shape `ComFirewallService` produces; `IsManagedBySteamoff` reuses the same name/group recognition criterion (depends on T016)
- [ ] T018 [US1] In `FallbackAwareFirewallService.ApplyBlockAsync` (`src/Steamoff.Infrastructure/Firewall/FallbackAwareFirewallService.cs`), implement the decision flow from data-model.md's state diagram: run `primary`, classify any thrown exception as `StrategyFailureReason.Exception`, otherwise verify via `GetCurrentStateAsync()` and classify an empty/insufficient result as `StrategyFailureReason.NoRulesProduced`; on either failure, invoke `secondary` with the same `targets`/`directionMode`/`ct` (depends on T009, T017)
- [ ] T019 [US1] Mirror T018's decision flow in `FallbackAwareFirewallService.RemoveOrDisableAsync` for `RuleCleanupMode` operations (depends on T018)
- [ ] T020 [US1] Wire the orchestrator into DI: in `src/Steamoff.App/AppServices.cs`, replace `Firewall = new ComFirewallService(Log)` with `Firewall = new FallbackAwareFirewallService(new ComFirewallService(Log), new NetSecurityFirewallService(Log), Log, LocalizedLog)` (per contracts/firewall-fallback.md C4) — confirm `LocalizedLog`/`ILocalizedLogService` is already constructed at that point in `AppServices`'s initialization order (depends on T009, T017)

**Checkpoint**: User Story 1 is independently functional — toggling blocking on a COMODO-affected machine now produces real `Steamoff`-group rules via the fallback path, with zero changes to the happy-path behavior elsewhere. This is the MVP; it can be validated and shipped on its own (logging detail from US2 will already be partially present as a side effect of T018/T019, but is refined in Phase 4).

---

## Phase 4: User Story 2 - Понятная диагностика: каким способом реально была выполнена операция (Priority: P2)

**Goal**: Every block/unblock operation clearly records, in both the technical log and the localized user-facing journal, which strategy actually performed the work — and why a fallback occurred, if one did — without adding noise to the common case where the primary strategy works fine.

**Independent Test**: Trigger a primary-strategy failure (US1's scenario) and confirm `steamoff.log` contains a `FirewallStrategyFallbackUsed`-derived entry naming the failure reason (`Exception` or `NoRulesProduced`) and the affected target count, AND the in-app localized journal shows a short, user-readable summary in the active UI language — while a normal successful run on an unaffected machine shows neither, exactly as before this feature (Quickstart Scenarios A & B, steps verifying `steamoff.log`/journal).

### Tests for User Story 2 ⚠️

- [ ] T021 [P] [US2] Test in `tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs` using a `FakeLogService`/`FakeLocalizationService` pair: when primary fails and fallback succeeds, the orchestrator calls `ILogService` with a message identifying `FirewallStrategyKind.Fallback` and the `StrategyFailureReason`, AND calls `ILocalizedLogService.LogAsync(LogEventKey.FirewallStrategyFallbackUsed, ...)` exactly once with arguments that round-trip through `LogEventTemplates`
- [ ] T022 [P] [US2] Test in `tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs`: when primary succeeds (no fallback), `ILocalizedLogService.LogAsync` is never called with `FirewallStrategyFallbackUsed`/`FirewallBothStrategiesFailed` — only the existing `FirewallBlockCompleted`/`FirewallUnblockCompleted` events fire, proving the happy path's journal output is unchanged (guards Principle VI / FR-007)
- [ ] T023 [P] [US2] Test in `tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs`: when BOTH `primary` and `secondary` fail (throw or empty-verify), the orchestrator logs `FirewallBothStrategiesFailed` exactly once (not two separate/contradictory warnings) to both `ILogService` and `ILocalizedLogService`, and propagates a single clear exception to the caller (FR-008, Edge Case "both strategies fail")

### Implementation for User Story 2

- [ ] T024 [US2] In `FallbackAwareFirewallService` (both `ApplyBlockAsync` and `RemoveOrDisableAsync`), on successful fallback completion, emit one `ILogService.Warning(...)` entry naming `FirewallStrategyKind.Fallback`, the `StrategyFailureReason`, and `AffectedTargetCount`, plus one `ILocalizedLogService.LogAsync(LogEventKey.FirewallStrategyFallbackUsed, ...)` call with the localized-summary arguments (depends on T018, T019, T021)
- [ ] T025 [US2] In `FallbackAwareFirewallService`, on total failure (both strategies failed), emit exactly one `ILogService.Error(...)` and one `ILocalizedLogService.LogAsync(LogEventKey.FirewallBothStrategiesFailed, ...)` call, then throw a single descriptive exception to the caller — replacing what would otherwise be two separate per-strategy failure logs (depends on T018, T019, T023)
- [ ] T026 [US2] Confirm (and adjust if needed) that the happy-path branch of `FallbackAwareFirewallService` performs NO additional logging beyond what `primary` already emits via the existing `FirewallBlockStarted`/`FirewallBlockCompleted`/`FirewallUnblockStarted`/`FirewallUnblockCompleted` events — i.e. the orchestrator adds zero journal noise when no fallback is needed (depends on T022, T024, T025)

**Checkpoint**: User Stories 1 AND 2 both work independently — fallback not only happens automatically (US1) but is now clearly explained in both logs (US2), while the silent/unaffected happy path stays exactly as quiet as before.

---

## Phase 5: User Story 3 - Резервный способ не ломает существующие правила и соглашения именования (Priority: P3)

**Goal**: Whichever strategy ends up creating/removing rules, the resulting rule set uses the exact same `Steamoff` group and `Steamoff - Block - <Target> - <Direction>` naming convention — never the old prototype's `SteamOfflineToggle`/`Steam Offline IN/OUT - <exe>` convention — and rules created by one strategy are correctly recognized, reused, and cleaned up by the other (no duplicates, no orphaned foreign-looking rules).

**Independent Test**: Force a fallback run (US1 scenario), then toggle blocking off and back on again — possibly forcing the OTHER strategy to run the second time (e.g. by simulating recovery of the primary) — and confirm via `Get-NetFirewallRule -Group Steamoff` that there is exactly one rule per `(target, direction)` pair, all named per the `FirewallRuleNameBuilder` convention, with no duplicates and no leftover rules from the strategy that ran first (Quickstart Scenario B step 4 + spec.md FR-011/Edge Case "cross-strategy rule reuse").

### Tests for User Story 3 ⚠️

- [ ] T027 [P] [US3] Test in `tests/Steamoff.Tests/Infrastructure/NetSecurityFirewallServiceTests.cs`: for a representative set of `FirewallTarget`s and both `DirectionMode` values, the rule names/group produced by `NetSecurityFirewallService` are byte-for-byte identical to those `ComFirewallService` would produce for the same inputs (drive both through `FirewallRuleNameBuilder.Build` and assert equality — proves no naming drift, FR-004/FR-005)
- [ ] T028 [P] [US3] Test in `tests/Steamoff.Tests/Infrastructure/NetSecurityFirewallServiceTests.cs`: `NetSecurityFirewallService.IsManagedBySteamoff` returns `true` for rules following the `Steamoff - Block - <Target> - <Direction>` / `Steamoff` group convention and `false` for rules following the OLD `SteamOfflineToggle`/`Steam Offline IN/OUT - <exe>` convention (proves the fallback never mistakes foreign/legacy rules for its own — Constitution Principle II "Rules without this signature belong to someone else")
- [ ] T029 [P] [US3] Test in `tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs`: simulate a scenario where `secondary` previously created the `Steamoff`-group rules and `primary` now succeeds on a later run — `GetCurrentStateAsync` (delegated to `primary`) recognizes the existing rules as Steamoff-managed (via `IsManagedBySteamoff`) so the operation updates/reuses them rather than creating duplicates (FR-011, Edge Case "cross-strategy rule reuse without duplication")

### Implementation for User Story 3

- [ ] T030 [US3] Audit `NetSecurityFirewallService` (T017) to ensure every `New-NetFirewallRule`/`Set-NetFirewallRule`/`Remove-NetFirewallRule -Group` argument is sourced exclusively from `FirewallConstants.RuleGroup` and every `-DisplayName` from `FirewallRuleNameBuilder.Build(...)` — zero string literals duplicating the convention — fixing any drift found by T027/T028 (depends on T017, T027, T028)
- [ ] T031 [US3] Verify (and if needed, adjust `NetSecurityFirewallService.GetCurrentStateAsync`/rule-update logic) that re-running `ApplyBlockAsync` against an existing `Steamoff`-group rule set (created by either strategy) updates/reuses matching rules rather than creating duplicates — mirrors `ComFirewallService.UpsertRule`'s upsert semantics (depends on T017, T029)

**Checkpoint**: All three user stories are independently functional — the fallback activates automatically (US1), explains itself clearly in logs (US2), and never drifts from the established naming/grouping contract or creates duplicate/orphaned rules across strategy switches (US3).

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation across all stories, using the prepared quickstart scenarios and the project's existing quality gates.

- [ ] T032 [P] Run the full `dotnet test tests/Steamoff.Tests/Steamoff.Tests.csproj` suite (excluding `RequiresAdmin`) and confirm all tests — old and new (T010-T029) — pass
- [ ] T033 [P] Add a `RequiresAdmin`-categorized integration test (skipped by default, matching the project's existing convention) in `tests/Steamoff.Tests/Infrastructure/NetSecurityFirewallServiceAdminTests.cs` that exercises `NetSecurityFirewallService` against the live Windows Firewall (create → verify via `Get-NetFirewallRule` → clean up `Steamoff`-group test rules it created), per quickstart.md's "Юнит-тестовая проверка" section
- [ ] T034 Rebuild the full solution (`dotnet build`) and execute Quickstart Scenario A (specs/006-firewall-fallback-strategy/quickstart.md) on a machine where the primary strategy already works — confirm zero behavioral/log changes vs. pre-feature baseline (FR-010, SC-002)
- [ ] T035 Execute Quickstart Scenario B on the COMODO-affected machine (with the user) — confirm rules now appear via fallback, `steamoff.log` and the localized journal both clearly explain which strategy ran and why, and Steam's network access is actually blocked (SC-001, SC-003)
- [ ] T036 Execute Quickstart Scenario C (simulate/verify the both-strategies-fail path, e.g. via the T023 unit test plus a manual check of the resulting user-facing message) — confirm a single, clear, actionable error is shown rather than silence or contradictory messages (SC-004)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories (provides shared enums T003, log keys T004-T007, test fakes T008, and the orchestrator skeleton T009 that every story extends)
- **User Story 1 (Phase 3)**: Depends on Foundational — delivers the MVP (automatic fallback)
- **User Story 2 (Phase 4)**: Depends on Foundational AND on US1's orchestrator decision flow existing (T018/T019) — it refines the *logging* of decisions US1 already makes; cannot be meaningfully tested without US1's fallback-triggering logic in place
- **User Story 3 (Phase 5)**: Depends on Foundational AND on US1's `NetSecurityFirewallService` existing (T017) — it audits/hardens that service's naming fidelity and cross-strategy recognition; logically a quality gate on US1's output, not a new capability
- **Polish (Phase 6)**: Depends on US1, US2, and US3 all being complete

### User Story Dependencies

- **US1 (P1)**: Can start immediately after Foundational — no dependency on US2/US3
- **US2 (P2)**: Builds on US1's decision flow (T018/T019) to attach richer logging — sequentially follows US1 in practice, even though its *user value* (clear diagnostics) is conceptually independent
- **US3 (P3)**: Audits/hardens US1's `NetSecurityFirewallService` (T017) for naming fidelity — sequentially follows US1's implementation, even though its *user value* (no convention drift) is conceptually independent

> Note: Unlike a typical web-app feature where stories touch disjoint endpoints, these three stories all describe different *qualities* of the SAME underlying mechanism (the fallback). US1 makes it work; US2 makes it explainable; US3 makes it safe/non-duplicating. They are independently *testable* (each has its own checkpoint and quickstart scenario) but P2/P3 implementation tasks naturally layer on top of P1's artifacts rather than touching disjoint files.

### Within Each User Story

- Tests (T010-T015, T021-T023, T027-T029) MUST be written first and FAIL before their corresponding implementation tasks
- `PowerShellRuleInvoker` (T016) before `NetSecurityFirewallService` (T017)
- `NetSecurityFirewallService` (T017) before orchestrator wiring that depends on it (T018, T020)
- Orchestrator decision flow for `ApplyBlockAsync` (T018) before `RemoveOrDisableAsync` (T019) — same pattern, written second to mirror the first
- DI wiring (T020) last within US1 — only after both strategies are real implementations, not skeletons

### Parallel Opportunities

- T003, T004, T006, T008 (Phase 2) touch disjoint files and can run in parallel
- T010-T015 (US1 tests) target two different test files and largely disjoint scenarios — can be written in parallel
- T021-T023 (US2 tests) and T027-T029 (US3 tests) are each internally parallelizable
- T032 and T033 (Phase 6) are independent and can run in parallel

---

## Parallel Example: Phase 2 (Foundational)

```bash
# Launch independent foundational tasks together:
Task: "Add FirewallStrategyKind/StrategyFailureReason enums in src/Steamoff.Core/Models/FirewallStrategyModels.cs"
Task: "Add FirewallStrategyFallbackUsed/FirewallBothStrategiesFailed to LogEventKey enum in src/Steamoff.Core/Logging/LogEventKey.cs"
Task: "Add localized log strings for both new keys to all 9 locale JSON files under src/Steamoff.Core/Resources/Localization/"
Task: "Add ScriptedFirewallService fake to tests/Steamoff.Tests/TestSupport/"
```

## Parallel Example: User Story 1 Tests

```bash
# Launch all US1 test-writing tasks together (red phase, before implementation):
Task: "NetSecurityFirewallService rule-naming test in tests/Steamoff.Tests/Infrastructure/NetSecurityFirewallServiceTests.cs"
Task: "NetSecurityFirewallService per-target resilience test in tests/Steamoff.Tests/Infrastructure/NetSecurityFirewallServiceTests.cs"
Task: "FallbackAwareFirewallService exception-triggers-fallback test in tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs"
Task: "FallbackAwareFirewallService empty-verification-triggers-fallback test in tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs"
Task: "FallbackAwareFirewallService no-fallback-on-success test in tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs"
Task: "FallbackAwareFirewallService RemoveOrDisableAsync symmetry test in tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T002)
2. Complete Phase 2: Foundational (T003-T009) — CRITICAL, blocks everything else
3. Complete Phase 3: User Story 1 (T010-T020)
4. **STOP and VALIDATE**: Run Quickstart Scenario B with the user on the COMODO-affected machine — confirm rules now actually appear
5. At this point the core problem ("toggle does nothing, silently") is fixed — US2/US3 refine explainability and safety on top of a working mechanism

### Incremental Delivery

1. Setup + Foundational → shared types/log-keys/fakes/orchestrator-skeleton ready
2. Add US1 → fallback actually works → validate with Quickstart Scenario B → this is the MVP fix the user is waiting for
3. Add US2 → fallback now clearly explains itself in both logs → validate with Quickstart Scenarios A & B (log inspection steps)
4. Add US3 → naming/grouping convention audited and cross-strategy reuse hardened → validate with Quickstart Scenario B step 4 + repeated toggle cycles
5. Polish (Phase 6) → full regression pass + all three quickstart scenarios run end-to-end with the user

### Suggested Single-Session Strategy

Given the size (one orchestrator + one new service + log/localization plumbing), all three stories can realistically be implemented in one continuous pass — Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6 — without stopping at intermediate "deploy" checkpoints, since this is a single-developer desktop app, not a multi-team service. The checkpoints remain useful as validation gates (run the relevant quickstart scenario) rather than as deployment boundaries.

---

## Notes

- [P] tasks touch different files and have no inter-task dependency within their phase
- [Story] labels (US1/US2/US3) trace every implementation/test task back to spec.md's prioritized user stories
- Per Constitution Principle V: write each story's tests first, confirm they fail, then implement until green
- `ComFirewallService.cs` is NOT modified by any task in this list (FR-001 / user's explicit "не удаляй то, что сделал" — the existing per-target try/catch fix stays exactly as shipped)
- Commit after each completed task or logical group (per the project's existing workflow with `/speckit-git-commit`)
- Avoid: introducing any new rule-naming/grouping convention, touching foreign (non-`Steamoff`) firewall rules, or adding journal noise to the happy path
