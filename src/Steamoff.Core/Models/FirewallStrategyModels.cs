namespace Steamoff.Core.Models;

/// <summary>
/// Identifies which <see cref="Steamoff.Core.Interfaces.IFirewallService"/> implementation
/// data-model.md for the full state-machine this participates in.
/// </summary>
public enum FirewallStrategyKind
{
    /// <summary>The late-bound COM automation strategy (<c>ComFirewallService</c> / <c>INetFwPolicy2</c>).</summary>
    Primary,

    /// <summary>The PowerShell <c>NetSecurity</c>-cmdlet strategy (<c>NetSecurityFirewallService</c>).</summary>
    Fallback
}

/// <summary>
/// Why the orchestrator considered the primary strategy to have failed and switched
/// to the fallback. Recorded in the technical log so future debugging can immediately
/// distinguish "it threw" from "it silently produced nothing" — the latter being the
/// exact bug this feature exists to catch (see research.md R2).
/// </summary>
public enum StrategyFailureReason
{
    /// <summary>The primary strategy threw an unhandled exception for the whole operation.</summary>
    Exception,

    /// <summary>
    /// The primary strategy completed without throwing, but verification showed that
    /// the expected Steamoff-group rules were not actually created/updated.
    /// </summary>
    NoRulesProduced
}
