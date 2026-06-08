# Contract: `IFirewallService`

Namespace: `Steamoff.Core.Interfaces`

```csharp
public interface IFirewallService
{
    // Returns the current snapshot of all rules in the "Steamoff" group.
    Task<ActualFirewallState> GetCurrentStateAsync(CancellationToken ct = default);

    // Ensures Block rules exist & enabled for every target (Outbound always;
    // Inbound only if directionMode == OutboundAndInbound). Idempotent.
    Task ApplyBlockAsync(IReadOnlyList<FirewallTarget> targets, DirectionMode directionMode, CancellationToken ct = default);

    // Per RuleCleanupMode: disables (Enabled=false) or deletes Steamoff rules
    // for the given targets. Idempotent; never touches non-Steamoff rules.
    Task RemoveOrDisableAsync(IReadOnlyList<FirewallTarget> targets, RuleCleanupMode cleanupMode, CancellationToken ct = default);

    // Hard guarantee used by tests & the constitution: returns true only for
    // rules whose Group == "Steamoff" AND Name starts with "Steamoff - Block - ".
    bool IsManagedBySteamoff(FirewallRuleState rule);
}
```

## Invariants (enforced by every implementation, verified by tests)
1. **Naming**: every created rule's `DisplayName` is produced exclusively by
   `FirewallRuleNameBuilder.Build(targetDisplayName, direction)` →
   `"Steamoff - Block - {TargetName} - {Direction}"`.
2. **Grouping**: every created rule's `Group` is the constant `"Steamoff"`.
3. **Scope of mutation**: `RemoveOrDisableAsync` and any internal cleanup MUST
   first filter candidate rules through `IsManagedBySteamoff` — a rule lacking
   either the exact group or the exact prefix is left untouched, full stop.
4. **Action/Direction/Profile defaults**: `Action = Block`,
   `Direction = Outbound` always created; `Inbound` only when
   `directionMode == OutboundAndInbound`; `Profiles = Domain|Private|Public`.
5. **Idempotency**: calling `ApplyBlockAsync` twice with the same targets must
   not create duplicate rules — existing matching rules are enabled/updated in
   place (matched by name).
6. **No process execution of targets**: the service only ever reads
   `FirewallTarget.ExecutablePath` as a string to set `ApplicationName` — it
   never starts the process.

## Implementations
- `ComFirewallService` (default/primary): late-bound COM over
  `HNetCfg.FwPolicy2` / `INetFwPolicy2.Rules` (`INetFwRules`) /
  `HNetCfg.FWRule` (`INetFwRule`). See `research.md` R1.
- `NetshFirewallBackend` (documented fallback, same interface): builds
  `netsh advfirewall firewall add/set/delete rule ...` invocations through
  `ProcessStartInfo.ArgumentList` (never via `cmd /c` string concatenation),
  parses `netsh ... show rule` output for read-back.

## Error Handling
- COM exceptions (`COMException`, e.g. `E_ACCESSDENIED` 0x80070005 when not
  elevated) are caught and re-thrown as `FirewallAccessDeniedException` /
  `FirewallOperationException` (defined in `Steamoff.Core.Exceptions`), which
  the `IStatusEvaluator`/ViewModels translate into `HealthLevel.Error` /
  `OverallStatus.ReadOnlyNoAdmin` rather than crashing the app.
