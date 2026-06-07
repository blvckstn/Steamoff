using Steamoff.Core.Enums;
using Steamoff.Core.Models;

namespace Steamoff.Core.Interfaces;

/// <summary>
/// The single mutation point for Microsoft Defender Firewall rules. See
/// specs/001-steamoff-smart-firewall-switch/contracts/firewall-service.md
/// for the full invariant contract (naming, grouping, scope of mutation,
/// idempotency) that every implementation must uphold.
/// </summary>
public interface IFirewallService
{
    /// <summary>Reads back every rule currently in the "Steamoff" group.</summary>
    Task<ActualFirewallState> GetCurrentStateAsync(CancellationToken ct = default);

    /// <summary>Ensures Block rules exist and are enabled for every target. Idempotent — safe to call repeatedly.</summary>
    Task ApplyBlockAsync(IReadOnlyList<FirewallTarget> targets, DirectionMode directionMode, CancellationToken ct = default);

    /// <summary>Disables or deletes Steamoff's rules for the given targets, per the configured cleanup mode. Idempotent; never touches foreign rules.</summary>
    Task RemoveOrDisableAsync(IReadOnlyList<FirewallTarget> targets, RuleCleanupMode cleanupMode, CancellationToken ct = default);

    /// <summary>True only if the rule's group AND name prefix exactly match Steamoff's convention.</summary>
    bool IsManagedBySteamoff(FirewallRuleState rule);
}
