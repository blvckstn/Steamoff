# Phase 0 Research: ScriptFile Strategy, Strategy Mode & Self-Test

No `NEEDS CLARIFICATION` markers remained from the Technical Context (informed defaults were already documented in
spec.md's Assumptions). This document records the research the user explicitly asked for — *"Перед этим обязательно
в PowerShell надо отправить команду 'Set-ExecutionPolicy -Scope Process Bypass' и подтвердить ее запуск найди как
правильно это сделать"* — plus the supporting design research needed before Phase 1.

## R1. The correct, safe, non-interactive way to run `Set-ExecutionPolicy ... -Scope Process Bypass`

**Decision**: The generated script's first executable statement is exactly:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force | Out-Null
```

wrapped in its own `try/catch` that swallows any failure and continues (see rationale below), **and** the parent
process additionally launches the script with `-ExecutionPolicy Bypass` on the command line:

```text
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "<scriptPath>" ...
```

**Rationale**:
- `-Scope Process` makes the policy change apply **only to the current PowerShell process** — it is stored in the
  `__PSExecutionPolicyCount` environment variable of that process and **automatically discarded the moment the
  process exits**. It never touches `HKCU`/`HKLM` registry-backed scopes (`CurrentUser`/`LocalMachine`/
  `MachinePolicy`/`UserPolicy`), so it satisfies FR-004's "without permanently changing any machine-wide or
  user-wide script-execution setting" outright — there is nothing to revert.
- `-Force` suppresses the "Do you want to change the execution policy?" confirmation prompt — required for a fully
  non-interactive child process (a prompt would hang forever with `-NonInteractive` and no attached console input).
- Because `-Scope Process` writes to an in-memory/process-environment value rather than the registry, it requires
  **no administrative rights of its own** to succeed — but running it is still useful belt-and-suspenders insurance
  against a machine-wide `MachinePolicy`/`UserPolicy` (set via Group Policy) that would otherwise override even the
  `-File` launch flag's `-ExecutionPolicy Bypass`. `Set-ExecutionPolicy -Scope Process` cannot override a
  `MachinePolicy`/`UserPolicy` GPO-backed restriction (Microsoft's own precedence rules place those above `Process`
  scope) — so it is wrapped in `try/catch` and never treated as fatal: if it fails, the `-File ... -ExecutionPolicy
  Bypass` launch-flag path (which *does* still execute the script's content regardless of the stored policy value,
  per documented `powershell.exe -ExecutionPolicy` behavior for that single invocation) remains the operative
  safeguard. Belt AND suspenders — neither alone is assumed sufficient, matching the user's explicit ask.
- Doing this **inside** the script (as its first statement) rather than only as a launch flag closes the gap where
  something (security software, a modified shortcut, a future code path) might invoke the same `.ps1` file without
  passing `-ExecutionPolicy Bypass` — the script remains self-sufficient and "forces" its own way through, directly
  answering *"сделать так, чтобы правила firewall форсировались и очень агрессивно ставились"*.

**Alternatives considered**:
- `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Unrestricted` — rejected: `Bypass` is the documented
  "nothing is blocked, no warnings or prompts" level; `Unrestricted` still warns for files downloaded from the
  internet (zone-identifier ADS) and our generated file, written fresh to a local path with no zone-identifier
  stream, would not normally trigger that anyway — but `Bypass` is strictly the more deterministic, prompt-free
  choice and is what the user explicitly named.
- `-Scope CurrentUser`/`-Scope LocalMachine` — rejected outright: these persist in the registry beyond the process
  lifetime, directly violating FR-004 ("without permanently changing... settings") and constitution principle IV
  ("Respect the Administrator Boundary" — an app must not quietly widen what is allowed to run on the machine after
  it exits).
- Relying solely on the `-File ... -ExecutionPolicy Bypass` launch flag without the in-script statement — rejected
  because it is the single point of failure the user is explicitly trying to eliminate (their framing: "форсировать
  и агрессивно" — defense in depth, not a single fragile flag).

## R2. Elevation inheritance — launching the `.ps1` file without a second UAC prompt

**Decision**: Launch via `System.Diagnostics.Process` with `UseShellExecute = false` and `Verb` left unset/empty
(exactly the pattern `ProcessPowerShellCommandRunner` already uses for the inline-`-Command` strategy) — **never**
`UseShellExecute = true` with `Verb = "runas"`.

**Rationale**: A child process created with `UseShellExecute = false` inherits the parent's access token verbatim.
Since the Steamoff app already requires and runs with an elevated (administrator) token (constitution principle IV
precondition, established in prior features), the spawned `powershell.exe -File ...` process is elevated from the
moment it starts — Windows performs no additional UAC consent-prompt negotiation for token-inherited child
processes. Setting `Verb = "runas"` (or `UseShellExecute = true` generally) is what triggers a *fresh* UAC
elevation request through the shell (`ShellExecuteEx`) — exactly the double-elevation UX problem the user's design
intent (and FR-004) rules out.

**Alternatives considered**: `ShellExecute` with `runas` — rejected (redundant prompt). PowerShell's own
`Start-Process -Verb RunAs` from within a script — not applicable here since the *parent* (the .NET app) is already
elevated; reaching for `RunAs` would be solving a problem that does not exist in this codebase.

## R3. Passing per-rule data into the script file safely

**Decision**: Reuse the exact environment-variable contract `PowerShellRuleInvoker` already proves reliable —
`STEAMOFF_DISPLAY_NAME`, `STEAMOFF_RULE_GROUP`, `STEAMOFF_RULE_DIRECTION`, `STEAMOFF_PROGRAM`,
`STEAMOFF_RULE_DESCRIPTION` (plus an operation-selector variable, e.g. `STEAMOFF_OPERATION` =
`Apply`/`Remove`/`Query`, since one script file now needs to serve all three `IFirewallService` operations rather
than the single upsert the inline strategy issues per call) — set on `ProcessStartInfo.Environment`, read inside
the script via `$env:STEAMOFF_*`.

**Rationale**: This is the pattern the user's own summary identified as "explicitly the proven safe pattern to
mirror" — it sidesteps all argument-quoting hazards with paths containing spaces and parentheses
(`C:\Program Files (x86)\Steam\steam.exe`), which is precisely the class of bug `PowerShellRuleInvoker` was built
to avoid. Passing the same five (plus operation-selector) values through the same channel means the script-file
strategy's *data plane* is identical to the inline strategy's already-proven one — the only thing that changes is
*how* the PowerShell code reaches the interpreter (a file on disk vs. an inline string), which is exactly the one
variable the user wants isolated and tested (their hypothesis: file-based execution is what survives their
machine's security-software interference).

**Alternatives considered**: Positional/named script parameters (`param($DisplayName, $ProgramPath, ...)`) passed
via `-File script.ps1 -DisplayName "..." -ProgramPath "..."` — rejected as the *primary* channel: argument-array
quoting for paths with spaces/parentheses is exactly the historically fragile area `PowerShellRuleInvoker`'s
env-var design was chosen to route around, and reusing a second, different data-passing convention for the third
strategy would needlessly fragment the codebase's "proven patterns" surface.

## R4. Script-file lifecycle: where it lives and how it is kept fresh without accumulating stale copies

**Decision**: One fixed file path — `<applicationBaseDirectory>\Scripts\steamoff-firewall.ps1` (a dedicated
subfolder beside the executable, created if missing) — is treated as a managed, content-addressed artifact: before
each use, the writer computes a stable hash (e.g. SHA-256) of the script content the *current build* expects, and
compares it to a hash recorded in a small sidecar marker (or simply re-hashes the on-disk file and compares to the
expected constant embedded in the assembly). If the file is missing, unreadable, or its hash differs from what this
build expects, it is rewritten atomically (write to a temp file in the same folder, then `File.Move` with overwrite)
— otherwise it is left untouched. There is exactly one canonical path; nothing is ever duplicated, versioned-by-
suffix, or left behind across app updates.

**Rationale**: Directly satisfies FR-005 ("created if missing, safely refreshed/replaced if its expected content has
changed... never accumulate multiple conflicting copies") and Edge Case "script file deleted/corrupted/modified
between runs" / "app-update script refresh". A hash-of-expected-content check is simpler and more robust than a
version-number comment inside the file (which a hostile or buggy external modification could forge or corrupt
without changing the hash check's outcome) and avoids any need for a separate persisted "script version" setting.
Atomic write-then-move avoids ever leaving a half-written file behind if the process is interrupted mid-write —
mirroring the atomic-write convention already established for `AppSettings` persistence (features 002-004).

**Alternatives considered**: Regenerating the script on every single use — rejected as wasteful I/O for a file that
changes only across app *builds*, not across runs; embedding the script as a string resource and writing it fresh
each app *launch* (not each operation) — close, but strictly inferior to the hash-gated approach because it cannot
detect/repair the "modified by something else mid-session" edge case the spec calls out.

## R5. Designing the first-run self-test as genuinely safe and non-destructive

**Decision**: The self-test creates a single temporary firewall rule per strategy under test — using a name and
**group that is deliberately *not* `FirewallConstants.RuleGroup`** (e.g. a private constant such as
`"Steamoff-SelfTest-Probe"`), targeting a harmless, guaranteed-present, inert path (the Steamoff executable's own
path, with `Action = Block` and a direction that has no practical effect since the app makes no outbound network
calls — principle I — or, more conservatively, a deliberately non-existent dummy path that Windows Firewall accepts
syntactically but that can never match real traffic), then immediately queries for it and immediately removes it —
all within the same strategy's own `ApplyBlockAsync`-equivalent / `RemoveOrDisableAsync`-equivalent surface so the
"does this mechanism work end-to-end" question is answered with full fidelity (create → verify → remove → verify
gone), wrapped in a `try/finally` that guarantees removal is attempted even if an earlier step throws.

**Rationale**: Satisfies FR-011 and Acceptance Scenario US3.2/US3.4 — a separate rule group means
`IsManagedBySteamoff`/`StatusEvaluator`/the dashboard never see or count the probe rules even if cleanup were
somehow interrupted (defense in depth beyond the `try/finally`), and a non-Steam, non-functional target path means
even a worst-case "rule briefly exists" window has zero observable effect on Steam or anything else the user runs.
Testing the full create→verify→remove cycle (not just "did `New-NetFirewallRule` not throw") is what makes the
result trustworthy enough to seed "Авто" mode's remembered preference — a strategy that can create but not reliably
remove rules would be a poor first choice for real operations.

**Alternatives considered**: Testing only "can this strategy read current state" (a pure `GetCurrentStateAsync`
probe, no writes) — rejected as insufficient signal: the user's entire premise is that *write*-side operations
(`New-NetFirewallRule` through different execution surfaces) are what differs across strategies on their machine;
a read-only probe would not actually distinguish the three paths. Using the real `Steamoff` rule group with instant
cleanup — rejected: any timing window in which a `Steamoff`-grouped probe rule is visible would corrupt the
dashboard's coverage calculation and could confuse `FallbackAwareFirewallService`'s own enrichment/verification
logic, which is precisely the class of subtle cross-contamination this design must avoid.

## R6. Mapping "Вариант 1/2/3" to concrete strategy instances stably

**Decision**: A small `FirewallStrategyVariant` enum (`Primary` = COM = "Вариант 1", `Secondary` =
NetSecurity-inline = "Вариант 2", `ScriptFile` = "Вариант 3") is the single source of truth for the mapping, used
identically by: the `AppSettings`-persisted mode/remembered-strategy fields (serialized by enum *name*, not numeric
value, so a future reordering of the underlying enum declaration cannot silently corrupt persisted user choices —
mirroring how the codebase already treats `LogEventKey` per the existing contract note "enum is used as an
identifier, not a serializable numeric value for external storage"), the `SettingsWindow` UI choice, the self-test
record, and the orchestrator's internal cascade-order logic.

**Rationale**: Directly operationalizes spec.md's Assumption "'Вариант 1/2/3' map directly and stably to the three
strategies in the order they were introduced... this mapping is treated as a stable identity for the lifetime of
the feature". Name-based serialization is the same defensive choice already implicitly relied upon for
`LogEventKey` consistency, keeping the codebase's persistence conventions uniform.

**Alternatives considered**: Persisting a raw `int` (ordinal) — rejected: fragile against any future enum-ordering
change and inconsistent with the codebase's existing name-based-enum-persistence posture for user-facing choices
(`DesiredState`, `EnforcementMode`, `RuleCleanupMode` are all persisted via `System.Text.Json`'s default — which is
also ordinal by default for plain enums, **but** this is exactly the kind of silent footgun worth explicitly
guarding against for a *brand-new* persisted field where we have the chance to be deliberate; using
`JsonStringEnumConverter` semantics for the new fields, or storing the variant as its `string` name explicitly,
removes the risk at zero cost).
