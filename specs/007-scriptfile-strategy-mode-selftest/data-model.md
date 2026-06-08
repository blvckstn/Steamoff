# Phase 1 Data Model: ScriptFile Strategy, Strategy Mode & Self-Test

Maps spec.md's four Key Entities onto concrete `Steamoff.Core` types and `AppSettings` fields. No existing entity
(`FirewallTarget`, `FirewallRuleState`, `ActualFirewallState`, `DesiredFirewallState`, `DriftReport`,
`FirewallConstants`, `FirewallRuleNameBuilder`) changes — this feature is purely additive (FR-015).

## E1. Firewall Strategy Variant *(new — `Steamoff.Core.Enums`)*

The stable identity referenced by every other entity below (research.md R6).

```csharp
/// <summary>Stable identity for one of the three concrete IFirewallService implementations — "Вариант 1/2/3".</summary>
public enum FirewallStrategyVariant
{
    Primary,    // "Вариант 1" — ComFirewallService (COM/INetFwPolicy2)
    Secondary,  // "Вариант 2" — NetSecurityFirewallService (inline -Command)
    ScriptFile  // "Вариант 3" — ScriptFileFirewallService (elevated .ps1 file)
}
```

Persisted (where applicable) by **name**, not ordinal — see R6.

## E2. Firewall Strategy Mode *(new — `Steamoff.Core.Enums`, persisted in `AppSettings`)*

> *Spec.md: "The user's chosen approach to deciding which underlying mechanism applies firewall rules."*

```csharp
/// <summary>How FallbackAwareFirewallService decides which strategy to use for an operation.</summary>
public enum FirewallStrategyMode
{
    Auto,              // system decides — tries remembered last-success first, then cascades, FR-009
    ForcePrimary,      // "Вариант 1" only — no fallback, FR-008
    ForceSecondary,    // "Вариант 2" only — no fallback, FR-008
    ForceScriptFile    // "Вариант 3" only — no fallback, FR-008
}
```

**Validation**: any of the four values is always valid; an unrecognized/corrupted persisted value defaults to
`Auto` on load (mirrors the existing `AppSettings` migration-tolerance convention — never throw on load, always
produce a usable default). **Mutability**: changeable at any time via `SettingsWindow`; takes effect for the
*next* operation only (FR-014 — an in-flight `ApplyBlockAsync`/`RemoveOrDisableAsync` call captures the mode at
its start and completes with it).

## E3. Remembered Last-Successful Strategy *(new — `AppSettings` field, backed by `FirewallStrategyVariant?`)*

> *Spec.md: "...consulted by Auto mode to decide which one to try first, and updated whenever any strategy
> succeeds or the remembered one stops working."*

```csharp
public FirewallStrategyVariant? LastSuccessfulFirewallStrategy { get; set; }
```

- `null` = "never recorded a success yet" (fresh install before the self-test runs, or a self-test that found
  nothing working — FR-013's "distinctly recorded, not indistinguishable from never-checked" is satisfied by the
  separate `FirstRunSelfTestRecord.Outcome` field, E4 — this field staying `null` is not itself ambiguous because
  the record field disambiguates "untested" from "tested, nothing worked").
- **Written by**: (a) the first-run self-test seeding it from its findings (FR-010), and (b) every real
  `Auto`-mode cascade run, set to whichever variant ultimately succeeded (FR-009's "self-healing" — including the
  case where the previously-remembered one failed this time and a different one won).
- **Read by**: `Auto`-mode cascade ordering only — forced modes ignore it entirely (they only ever try their one
  forced variant, FR-008).
- **Not written by**: forced-single-variant operations — a deliberate diagnostic run of "Вариант 2" succeeding
  does not silently change what "Авто" will try first next time; only `Auto`-mode runs (and the one-time
  self-test) update this field, keeping the user's forced choice and the system's automatic memory cleanly
  separable (consistent with FR-008's "the user sees exactly what happened with the path they chose" — forcing a
  variant must not have invisible side effects on `Auto` behavior).

## E4. First-Run Self-Test Record *(new — `AppSettings` nested object)*

> *Spec.md: "...describing whether the automatic first-launch check ran, and if so, which strategies it found to
> be working on this machine — consulted to seed the Remembered Last-Successful Strategy and to avoid repeating
> the check on later launches."*

```csharp
public sealed class FirewallSelfTestRecord
{
    public FirewallSelfTestOutcome Outcome { get; set; } = FirewallSelfTestOutcome.NotYetRun;
    public List<FirewallStrategyVariant> WorkingStrategies { get; set; } = new();
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Distinguishes "never probed" from every possible probe result — FR-013.</summary>
public enum FirewallSelfTestOutcome
{
    NotYetRun,           // fresh install / pre-feature settings — triggers the one-time probe on next startup
    CompletedWithResult, // probe ran to completion; WorkingStrategies holds 0..3 entries (0 = none worked)
    Inconclusive         // probe started but could not finish cleanly (interrupted) — FR-013: recorded distinctly,
                         // never retried automatically, Auto falls back to its full ordered cascade unaided
}
```

**State transitions** (one-way, machine-and-installation-scoped per spec.md Assumption — "first launch" is *not*
an external/account concept):

```text
NotYetRun ──(self-test runs to completion)──────────────► CompletedWithResult
NotYetRun ──(self-test starts but is interrupted)───────► Inconclusive
                                                            │
                                          (no further automatic transitions —
                                           never re-probed on later launches;
                                           a fresh install / reset state is the
                                           only way back to NotYetRun, per the
                                           spec.md Assumption on "first launch")
```

`CompletedAt` is `null` until the terminal state is reached; recorded in the user's local timezone offset
(`DateTimeOffset`, consistent with other timestamped fields in the codebase such as `ActualFirewallState.CapturedAt`).

## E5. ScriptFile Strategy Script *(new — conceptual entity; realized as a managed on-disk artifact + writer)*

> *Spec.md: "...its presence and expected content are verified/refreshed as needed before each use, and it is
> adapted from the proven `steamOff.ps1` prototype but conforms to the app's existing rule-naming and grouping
> conventions."*

Not a persisted *settings* entity — a managed file, governed entirely by the content-hash check designed in
research.md R4:

| Aspect | Value |
|---|---|
| Canonical path | `<applicationBaseDirectory>\Scripts\steamoff-firewall.ps1` (one fixed path — never duplicated/suffixed) |
| Owner/writer | `FirewallScriptFileWriter` (new infrastructure helper) |
| Freshness check | SHA-256 of on-disk content vs. the hash of the content this build expects (embedded constant) |
| Refresh trigger | Missing, unreadable, or hash-mismatched — rewritten atomically (temp file + `File.Move` overwrite) |
| Content contract | First statement: `Set-ExecutionPolicy -Scope Process Bypass -Force` (R1); reads
all per-operation data exclusively from `STEAMOFF_*` environment variables (R3); creates/queries/removes rules
exclusively via `FirewallConstants.RuleGroup` + `FirewallRuleNameBuilder.Build(...)` — **never** the prototype
`steamOff.ps1`'s `"SteamOfflineToggle"`/`"Steam Offline IN/OUT - <exe>"` naming (FR-002, carried over verbatim from
the binding constraint in feature 006) |

## E6. Settings surface additions *(`Steamoff.Core.Models.AppSettings`)*

```csharp
// Added to the existing sealed class AppSettings (src/Steamoff.Core/Models/AppSettings.cs):
public FirewallStrategyMode FirewallStrategyMode { get; set; } = FirewallStrategyMode.Auto;
public FirewallStrategyVariant? LastSuccessfulFirewallStrategy { get; set; }
public FirewallSelfTestRecord FirewallSelfTest { get; set; } = new();
```

- **Versioning**: bumps `AppSettings.CurrentVersion` from `2` to `3`; the existing migration path (per features
  002-004 conventions) supplies these three defaults (`Auto` / `null` / fresh `FirewallSelfTestRecord` with
  `Outcome = NotYetRun`) for any settings file persisted at version `2` or earlier — which simultaneously satisfies
  FR-010 ("on the very first launch... and never again afterward") for both brand-new installs *and* upgrades from
  a pre-feature version (both start from `NotYetRun` and trigger exactly one probe).
- **FR-015 guarantee**: a user who never opens the new Settings control sees `FirewallStrategyMode.Auto` behave
  exactly like today's two-way fallback until/unless the self-test or a real cascade populates
  `LastSuccessfulFirewallStrategy` — and even then, `Auto`'s observable behavior ("try the best-known path first,
  fall through on failure") is a strict refinement of, not a change to, the existing contract.

## Relationships

```text
AppSettings
 ├─ FirewallStrategyMode ───────────────┐
 ├─ LastSuccessfulFirewallStrategy ─────┤
 └─ FirewallSelfTest ───────────────────┤   all reference
                                          ▼
                                 FirewallStrategyVariant (E1)
                                          ▲
                                          │ produced/consumed by
                                          │
FallbackAwareFirewallService (orchestrator) ──uses──► IFirewallService × 3
        │                                                  │  Primary   = ComFirewallService          ("Вариант 1")
        │ seeded by (first run only)                       │  Secondary = NetSecurityFirewallService   ("Вариант 2")
        ▼                                                  │  ScriptFile= ScriptFileFirewallService    ("Вариант 3")
FirewallSelfTestRunner ───writes───► FirewallSelfTestRecord (E4)
        │
        └─ relies on the same three IFirewallService instances the orchestrator does (no separate probe-only
           implementations — "test what will actually run" per research.md R5)

ScriptFileFirewallService ──depends on──► FirewallScriptFileWriter ──manages──► steamoff-firewall.ps1 (E5)
                          ──launches───► powershell.exe (-File, elevation-inherited per research.md R2)
                          ──passes data via──► STEAMOFF_* env vars (research.md R3, mirrors PowerShellRuleInvoker)
                          ──names/groups rules via──► FirewallConstants + FirewallRuleNameBuilder (unchanged, FR-002)
```
