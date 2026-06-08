# Tasks: ScriptFile Firewall Strategy, Strategy Mode Selection & First-Run Self-Test

**Input**: Design documents from `/specs/007-scriptfile-strategy-mode-selftest/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/scriptfile-strategy-and-orchestration.md, quickstart.md

**Tests**: Included — constitution principle V ("Test-First for Core Logic") and the user's explicit requirement to
keep the existing 124/124 suite green while adding coverage for every new behavior.

**Organization**: Grouped by user story (US1 = P1 third strategy, US2 = P2 mode selection, US3 = P3 self-test) so
each can be implemented, tested, and demoed independently, in priority order.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Maps the task to US1/US2/US3 from spec.md
- All paths are repo-relative from `c:\Users\adm\Desktop\13\vibe\Steamoff\`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New shared types every later phase depends on — no behavior yet, purely additive declarations.

- [X] T001 [P] Add `FirewallStrategyVariant` enum (`Primary`/`Secondary`/`ScriptFile`) to `src/Steamoff.Core/Enums/Enums.cs`, per data-model.md E1 — doc-comment naming each to its "Вариант N"
- [X] T002 [P] Add `FirewallStrategyMode` enum (`Auto`/`ForcePrimary`/`ForceSecondary`/`ForceScriptFile`) to `src/Steamoff.Core/Enums/Enums.cs`, per data-model.md E2
- [X] T003 [P] Add `FirewallSelfTestOutcome` enum (`NotYetRun`/`CompletedWithResult`/`Inconclusive`) to `src/Steamoff.Core/Enums/Enums.cs`, per data-model.md E4
- [X] T004 [P] Add `FirewallSelfTestRecord` sealed class (`Outcome`, `WorkingStrategies: List<FirewallStrategyVariant>`, `CompletedAt: DateTimeOffset?`) to `src/Steamoff.Core/Models/AppSettings.cs` (or a new `src/Steamoff.Core/Models/FirewallSelfTestRecord.cs` if that keeps `AppSettings.cs` focused — match the file's existing convention for nested settings types like `UiSettings`), per data-model.md E4
- [X] T005 [P] Add `FirewallAllStrategiesFailed`, `FirewallForcedStrategyFailed`, `FirewallSelfTestCompleted`, `FirewallSelfTestInconclusive` to the end of `enum LogEventKey` in `src/Steamoff.Core/Logging/LogEventKey.cs`, and matching entries (`log.event.firewallAllStrategiesFailed` Error, `log.event.firewallForcedStrategyFailed` Error, `log.event.firewallSelfTestCompleted` Info, `log.event.firewallSelfTestInconclusive` Warning) in `src/Steamoff.Core/Logging/LogEventTemplates.cs`, per contracts C7

**Checkpoint**: New enums/records/log keys compile; nothing references them yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Settings persistence, localization strings, and the script-file writer — every user story phase below
needs these to exist first.

**⚠️ CRITICAL**: No user story implementation can begin until this phase is complete and the solution builds.

- [X] T006 Add `FirewallStrategyMode FirewallStrategyMode { get; set; } = FirewallStrategyMode.Auto`, `FirewallStrategyVariant? LastSuccessfulFirewallStrategy { get; set; }`, and `FirewallSelfTestRecord FirewallSelfTest { get; set; } = new()` to `AppSettings` in `src/Steamoff.Core/Models/AppSettings.cs`; bump `CurrentVersion` from `2` to `3`; extend whatever existing version-migration path upgrades a loaded settings file (find it via the `Version`/`CurrentVersion` usage near features 002-004) so a v2-or-earlier file gets these three defaults on load — per data-model.md E6
- [X] T007 [P] Add the four new localization keys (`log.event.firewallAllStrategiesFailed`, `log.event.firewallForcedStrategyFailed`, `log.event.firewallSelfTestCompleted`, `log.event.firewallSelfTestInconclusive`) with translated strings to all 9 locale files alongside the existing `log.event.firewallStrategyFallbackUsed`/`firewallBothStrategiesFailed` entries (find them via `grep -rl "firewallBothStrategiesFailed"` under the localization resources directory — `ru`, `en`, `de`, `es`, `fr`, `it`, `pl`, `pt`, `zh`), matching tone/format; this keeps the existing localization-parity test green (per contracts C7)
- [X] T008 [P] Create `IFirewallScriptFileWriter` interface and `FirewallScriptFileWriter` implementation in `src/Steamoff.Infrastructure/Firewall/FirewallScriptFileWriter.cs`: `EnsureUpToDateAsync(ct)` resolves the canonical path `<applicationBaseDirectory>\Scripts\steamoff-firewall.ps1`, computes SHA-256 of an embedded script-content constant, compares against the on-disk file, and atomically (re)writes (temp file + `File.Move` overwrite) when missing/unreadable/hash-mismatched — per contracts C3 and research.md R4. The embedded script content MUST: (a) start with `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force` wrapped in `try/catch` (research.md R1), (b) read all per-operation data from `STEAMOFF_OPERATION`/`STEAMOFF_DISPLAY_NAME`/`STEAMOFF_RULE_GROUP`/`STEAMOFF_RULE_DIRECTION`/`STEAMOFF_PROGRAM`/`STEAMOFF_RULE_DESCRIPTION` env vars (research.md R3, mirroring `PowerShellRuleInvoker`'s proven script preamble — read it first), (c) implement `Apply`/`Remove`/`Query` branches using `New-NetFirewallRule`/`Get-NetFirewallRule`/`Remove-NetFirewallRule`/`Set-NetFirewallRule` adapted from `steamOff.ps1`'s `Block-Steam`/`Allow-Steam`/`Get-SteamStatus` mechanics (read `steamOff.ps1` for the proven cmdlet sequences) but naming/grouping rules EXCLUSIVELY via the values passed in `STEAMOFF_RULE_GROUP`/a name built the same way `FirewallRuleNameBuilder.Build` does — never the prototype's `"SteamOfflineToggle"`/`"Steam Offline IN/OUT - <exe>"` convention, and (d) emit query results as structured (e.g. JSON via `ConvertTo-Json`) stdout that the .NET side can parse into `FirewallRuleState`/`ActualFirewallState`

**Checkpoint**: Settings persist the three new fields with correct defaults/migration, localization parity test passes, and `FirewallScriptFileWriter.EnsureUpToDateAsync()` reliably produces a valid, idempotent script file on disk (verifiable by a quick standalone run/test before strategy work begins).

---

## Phase 3: User Story 1 - Третья стратегия — рабочий запасной путь (Priority: P1) 🎯 MVP

**Goal**: A working `ScriptFileFirewallService` ("Вариант 3") that implements `IFirewallService` by writing/running
the adapted script file, producing rules indistinguishable from the other two strategies.

**Independent Test**: Force the orchestrator to "Вариант 3" only (achievable even before US2's UI exists, by directly
constructing `FallbackAwareFirewallService` with `currentModeProvider = () => FirewallStrategyMode.ForceScriptFile`
in a test, or by temporarily wiring it as the sole strategy), trigger block/unblock, and verify correct
`Steamoff`-named/grouped rules appear and disappear — per quickstart.md US1.

### Tests for User Story 1 ⚠️

> Write these first; they must fail (no production type yet) before implementation.

- [X] T009 [P] [US1] `FirewallScriptFileWriterTests` in `tests/Steamoff.Tests/Infrastructure/FirewallScriptFileWriterTests.cs` — covers: writes file when missing; leaves up-to-date file untouched; rewrites when hash mismatches (simulate external modification); never leaves a half-written temp file behind on a simulated interruption; always resolves to the one canonical path (no duplicates across repeated calls)
- [X] T010 [P] [US1] `ScriptFileFirewallServiceTests` in `tests/Steamoff.Tests/Infrastructure/ScriptFileFirewallServiceTests.cs` — using a scripted `IPowerShellCommandRunner` test double (mirror the one `NetSecurityFirewallService`'s tests already use, or create an equivalent `ScriptedPowerShellCommandRunner`): asserts the launch uses `-File <scriptPath>` arguments (never `-Command`, never `Verb=runas`/`UseShellExecute=true`), asserts all six `STEAMOFF_*` environment variables are set correctly per operation (including the new `STEAMOFF_OPERATION` selector), asserts produced rule names/groups exactly match `FirewallRuleNameBuilder.Build`/`FirewallConstants.RuleGroup`, asserts per-target resilience (one target's failure is caught/logged/skipped without aborting the rest — mirror `NetSecurityFirewallServiceTests`' equivalent test if one exists), asserts `IsManagedBySteamoff` matches the shared criterion

### Implementation for User Story 1

- [X] T011 [US1] Implement `ScriptFileFirewallService : IFirewallService` in `src/Steamoff.Infrastructure/Firewall/ScriptFileFirewallService.cs` — constructor `(IFirewallScriptFileWriter scriptWriter, IPowerShellCommandRunner runner, ILogService log)`; `GetCurrentStateAsync`/`ApplyBlockAsync`/`RemoveOrDisableAsync`/`IsManagedBySteamoff` each call `scriptWriter.EnsureUpToDateAsync()` then build a `PowerShellInvocation` with `FileName = "powershell.exe"`, `Arguments = ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath]` and the `STEAMOFF_*` environment dictionary (including `STEAMOFF_OPERATION = "Apply"|"Remove"|"Query"`), run it via `runner.RunAsync`, parse the JSON stdout for `Query`/state-returning calls into `ActualFirewallState`/`FirewallRuleState` (depends on T008/T011's script JSON-output contract), and wrap each per-target call in its own try/catch exactly like `NetSecurityFirewallService` does (read it for the exact pattern to mirror) — per contracts C2 (depends on T008, T009, T010)
- [X] T012 [US1] Wire `ScriptFileFirewallService` into `Steamoff.App/AppServices.cs`: construct `new ScriptFileFirewallService(new FirewallScriptFileWriter(), new ProcessPowerShellCommandRunner(), Log)` alongside the existing `primary`/`secondary` instances (do not yet change `Firewall`'s assembly — that is US2's job; for now just instantiate and hold the reference so US1 is independently verifiable, e.g. via a temporary direct-construction test or an internal property) — per contracts C6 (depends on T011)

**Checkpoint**: User Story 1 is independently functional — `ScriptFileFirewallService` reliably blocks/unblocks via
an actual elevated `.ps1` file with zero naming drift from "Вариант 1"/"Вариант 2", and the on-disk script never
accumulates stale copies. This alone is the MVP the affected user needs (a working escape hatch).

---

## Phase 4: User Story 2 - Выбор и доверие к стратегии через Settings (Priority: P2)

**Goal**: `FallbackAwareFirewallService` becomes a true mode-aware 3-way cascade; users can force a specific
"Вариант N" or trust "Авто" to remember and prefer whatever last worked, all from Settings, live (no restart).

**Independent Test**: Switch the strategy mode through all four options, trigger block/unblock under each, and
confirm forced modes never silently fall back while "Авто" demonstrably prefers the last successful strategy —
per quickstart.md US2.

### Tests for User Story 2 ⚠️

- [X] T013 [P] [US2] Extend `tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs` with: `ApplyBlockAsync_AutoMode_NoRememberedStrategy_TriesCanonicalOrder` (Primary→Secondary→ScriptFile, preserves feature-006 default behavior — FR-015), `ApplyBlockAsync_AutoMode_RememberedScriptFile_TriesItFirst`, `ApplyBlockAsync_AutoMode_RememberedStrategyFailsThisTime_FallsThroughAndUpdatesMemory` (self-healing — asserts `rememberSuccessAsync` is called with the NEW winner), `ApplyBlockAsync_AutoMode_AllThreeFail_LogsFirewallAllStrategiesFailedAndThrows_DoesNotOverwriteMemory`, `ApplyBlockAsync_ForcedMode_UsesOnlyThatStrategy_NeverInvokesOthers` (parametrized/triplicated for `ForcePrimary`/`ForceSecondary`/`ForceScriptFile`), `ApplyBlockAsync_ForcedModeFails_LogsFirewallForcedStrategyFailed_NoSilentFallback`, `ApplyBlockAsync_ForcedModeSucceeds_StillUpdatesRememberedStrategy`, `ApplyBlockAsync_ModeCapturedOncePerOperation_MidOperationModeChangeDoesNotAffectInFlightCall` (FR-014 — use a scripted service whose operation awaits a controllable `TaskCompletionSource` while the mode-provider delegate's return value is changed mid-flight)

### Implementation for User Story 2

- [X] T014 [US2] Promote `FallbackAwareFirewallService` in `src/Steamoff.Infrastructure/Firewall/FallbackAwareFirewallService.cs` to the mode-aware 3-way orchestrator per contracts C4: new constructor `(IFirewallService primary, IFirewallService secondary, IFirewallService scriptFile, Func<FirewallStrategyMode> currentModeProvider, Func<FirewallStrategyVariant?, Task> rememberSuccessAsync, ILogService log, ILocalizedLogService localizedLog)`; capture `currentModeProvider()` once at the top of `ExecuteWithFallbackAsync`; build the `Auto`-mode try-order as `[remembered ?? canonical-first, ...remaining canonical without dupes]`; implement forced-mode single-strategy execution with `FirewallForcedStrategyFailed` (no fallback) vs `Auto`'s `FirewallAllStrategiesFailed` (after exhausting all three); call `rememberSuccessAsync` on every success (forced or auto) and never on a full failure; preserve the existing `GetCurrentStateAsync` cross-strategy `ApplicationName` enrichment and `IsManagedBySteamoff` delegation unchanged (depends on T005, T013)
- [X] T015 [US2] Update `Steamoff.App/AppServices.cs` wiring per contracts C6: assemble `Firewall = new FallbackAwareFirewallService(primary, secondary, scriptFile, () => Settings.Current.FirewallStrategyMode, variant => Settings.UpdateAsync(s => s.LastSuccessfulFirewallStrategy = variant), Log, LocalizedLog)` (adapt the exact `Settings`/persistence accessor names to whatever `AppServices` actually exposes — read the file first) (depends on T006, T012, T014)
- [X] T016 [US2] Add the four-option "Стратегия применения правил брандмауэра" control group to `src/Steamoff.App/Views/SettingsWindow.xaml` (+ code-behind in `SettingsWindow.xaml.cs`), styled with the existing Dark Orange Neumorphic radio-choice convention (find and mirror an existing single-choice settings group in the same window), each option labeled "Авто"/"Вариант 1"/"Вариант 2"/"Вариант 3" with a short explanatory caption, two-way-bound to `AppSettings.FirewallStrategyMode`, applying immediately on selection (no restart, no save-and-reload — match how other live-applied settings in this window already work) — per contracts C8 / FR-007 / Acceptance Scenario US2.4 (depends on T006)

**Checkpoint**: User Stories 1 AND 2 both work independently — users can see, choose, force, and trust any of the
three strategies, and "Авто" demonstrably converges on whatever worked last.

---

## Phase 5: User Story 3 - Самотестирование при первом запуске (Priority: P3)

**Goal**: A one-time, fully invisible probe of all three strategies on first launch that seeds "Авто"'s memory and
clearly logs what it found — so "Авто" never has to learn by failure.

**Independent Test**: Reset to a "never run" state, launch, and confirm the probe runs exactly once, leaves the live
`Steamoff` rule set provably untouched, logs a clear summary, and that "Авто"'s first real operation immediately
prefers the strategy the probe found working — per quickstart.md US3.

### Tests for User Story 3 ⚠️

- [X] T017 [P] [US3] `FirewallSelfTestRunnerTests` in `tests/Steamoff.Tests/Infrastructure/FirewallSelfTestRunnerTests.cs` — covers: `RunIfNeededAsync_Outcome_NotYetRun_ProbesAllThreeAndRecordsCompletedWithResult`; `RunIfNeededAsync_Outcome_AlreadyTerminal_IsNoOp` (both `CompletedWithResult` and `Inconclusive` — never re-probes); `RunIfNeededAsync_ProbeUsesDedicatedGroup_NeverTouchesSteamoffManagedRules` (assert the probe rule's group is `"Steamoff-SelfTest-Probe"`, never `FirewallConstants.RuleGroup`, and that pre-existing `Steamoff`-managed rules in a scripted state are unchanged before/after); `RunIfNeededAsync_ProbeAlwaysCleansUpEvenWhenAStepThrows` (try/finally removal guarantee — scripted strategy throws mid-probe, assert removal was still attempted); `RunIfNeededAsync_SeedsLastSuccessfulStrategy_FirstWorkingInCanonicalOrder`; `RunIfNeededAsync_NoneWorking_RecordsCompletedWithResult_EmptyList_DoesNotSeedMemory`; `RunIfNeededAsync_Interrupted_RecordsInconclusive_DistinctFromNotYetRun_NeverRetried`; `RunIfNeededAsync_LogsFirewallSelfTestCompletedOrInconclusive_ToBothLogs`

### Implementation for User Story 3

- [X] T018 [US3] Implement `IFirewallSelfTestRunner`/`FirewallSelfTestRunner` in `src/Steamoff.Infrastructure/Firewall/FirewallSelfTestRunner.cs` per contracts C5: constructor takes the SAME three `IFirewallService` instances the orchestrator uses (not separate probe implementations — research.md R5) plus settings-read/persist accessors and `ILogService`/`ILocalizedLogService`; `RunIfNeededAsync` no-ops unless `Outcome == NotYetRun`; for each strategy in canonical order, runs a create→verify→remove→verify cycle against group `"Steamoff-SelfTest-Probe"` (a private constant, deliberately distinct from `FirewallConstants.RuleGroup`) targeting a harmless inert path, wrapped in `try/finally` guaranteeing removal-attempt; aggregates the 0-3 working variants, persists `FirewallSelfTestRecord{ Outcome = CompletedWithResult, WorkingStrategies, CompletedAt }` (or `Inconclusive` on interruption) atomically via the existing `AppSettings` persistence path, seeds `LastSuccessfulFirewallStrategy` with the first working variant in canonical order when the list is non-empty, and logs `FirewallSelfTestCompleted`/`FirewallSelfTestInconclusive` with a human-readable per-strategy summary to both `ILogService` and `ILocalizedLogService` (depends on T005, T012, T017)
- [X] T019 [US3] Wire `FirewallSelfTestRunner` into `Steamoff.App/AppServices.cs` (new `SelfTestRunner` property/composition, constructed with `primary`/`secondary`/`scriptFile`) and invoke `await AppServices.SelfTestRunner.RunIfNeededAsync()` once during startup in `src/Steamoff.App/App.xaml.cs` — placed after `AppServices`/`Firewall` initialization but before the main window first applies any real block/unblock operation, fully awaited/non-blocking-to-UI (use the same async-startup pattern already established for other one-time startup checks — read `App.xaml.cs` first to match it) (depends on T015, T018)

**Checkpoint**: All three user stories are independently functional. "Авто" mode now has a head start from the very
first launch, with zero observable side effects.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final verification that the whole feature is correct, cohesive, and additive-only.

- [X] T020 [P] Run `dotnet test` from the repo root and confirm the full suite (the pre-existing 124 plus every new test from T009/T010/T013/T017) is green with `--filter "Category!=RequiresAdmin"`; fix any regressions before proceeding
- [X] T021 Walk through every scenario in `specs/007-scriptfile-strategy-mode-selftest/quickstart.md` (US1, US2, US3, and the "ничего не сломано" checkpoint) on a real elevated Windows session, confirming observable behavior matches each "Ожидаемо" — this is the closest the team can get to `RequiresAdmin`-level validation without a live CI agent
- [X] T022 Rebuild the release via `.\build-release.ps1` (mirrors the rebuild done for feature 006) and record the new `Steamoff.exe` SHA-256 hashes for both `Steamoff-with-dotnet-runtime` and `Steamoff-without-dotnet-runtime`, confirming the build includes all of this feature's changes

**Checkpoint**: Feature complete, fully tested, release artifacts refreshed.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — T001-T005 can all run in parallel (different declarations in shared files;
  coordinate edits to `Enums.cs`/`LogEventKey.cs`/`LogEventTemplates.cs` to avoid merge collisions if run by
  multiple agents simultaneously, but conceptually independent)
- **Foundational (Phase 2)**: Depends on Phase 1 (needs the new enums/record/log keys to exist) — BLOCKS all user
  stories. T006/T007/T008 can proceed in parallel once Phase 1 lands.
- **User Story 1 (Phase 3)**: Depends on Foundational (needs `FirewallScriptFileWriter` from T008, `LogEventKey`
  entries are not directly used by US1 itself but the shared infra must compile). Independently testable and
  deliverable as the MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational (T006 settings fields, T007 log strings) AND on User Story 1
  (the orchestrator's third leg is `ScriptFileFirewallService` from T011/T012 — cannot build a real 3-way cascade
  without it). This is the one real cross-story dependency in this feature; US2 cannot be meaningfully demoed
  without US1 existing first (though its orchestration logic could theoretically be coded against a stub).
- **User Story 3 (Phase 5)**: Depends on Foundational AND User Story 1 (the self-test probes the same three
  `IFirewallService` instances, including `ScriptFileFirewallService`) AND benefits from User Story 2's
  `AppServices` wiring shape (T015) for where `LastSuccessfulFirewallStrategy`/mode plumbing lives — implement last.
- **Polish (Phase 6)**: Depends on all three user stories being complete.

### Recommended order

T001-T005 → T006-T008 → [T009,T010] → T011 → T012 → [T013] → T014 → T015 → T016 → [T017] → T018 → T019 → T020 → T021 → T022

(US1's Phase 3 is the MVP checkpoint — stop and validate there if you want an intermediate demo before continuing.)

---

## Parallel Example: Phase 1 (Setup)

```text
Task: "Add FirewallStrategyVariant enum to src/Steamoff.Core/Enums/Enums.cs"
Task: "Add FirewallStrategyMode enum to src/Steamoff.Core/Enums/Enums.cs"
Task: "Add FirewallSelfTestOutcome enum to src/Steamoff.Core/Enums/Enums.cs"
Task: "Add FirewallSelfTestRecord class to src/Steamoff.Core/Models/AppSettings.cs"
Task: "Add 4 new LogEventKey entries + templates"
```//(coordinate to a single edit pass on shared files — these are conceptually parallel but file-colliding)

## Parallel Example: User Story 1 tests

```text
Task: "FirewallScriptFileWriterTests in tests/Steamoff.Tests/Infrastructure/FirewallScriptFileWriterTests.cs"
Task: "ScriptFileFirewallServiceTests in tests/Steamoff.Tests/Infrastructure/ScriptFileFirewallServiceTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T005)
2. Complete Phase 2: Foundational (T006-T008) — CRITICAL, blocks everything
3. Complete Phase 3: User Story 1 (T009-T012)
4. **STOP and VALIDATE**: force "Вариант 3" (even via direct test-level construction before US2's UI exists) and
   confirm block/unblock works end-to-end with correct rule names — the affected user now has a working escape
   hatch, which is the entire point of P1
5. Continue to US2, then US3

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. User Story 1 → validate independently → MVP delivered (a working third strategy)
3. User Story 2 → validate independently → users can see/choose/trust/force any strategy
4. User Story 3 → validate independently → "Авто" never has to learn by failure again
5. Polish → full regression + release rebuild

---

## Notes

- [P] tasks touch different files (or, where noted, the same shared file in a way that's conceptually independent —
  coordinate to avoid edit collisions if parallelized across agents)
- Every new `IFirewallService`-shaped type follows the exact constructor-injection/test-double pattern already
  proven by `NetSecurityFirewallService`/`PowerShellRuleInvoker`/`FallbackAwareFirewallServiceTests` — read those
  first before writing the new ones
- `FirewallConstants`/`FirewallRuleNameBuilder`/`ComFirewallService`/`NetSecurityFirewallService` are NEVER modified
  by this feature — if a task seems to require touching them, stop and re-read contracts C1/C2/C4
- Commit after each task or logical group; stop at each phase checkpoint to validate independently
- Run `dotnet test --filter "Category!=RequiresAdmin"` after every implementation task that touches existing test
  files, to catch regressions immediately rather than at the end
