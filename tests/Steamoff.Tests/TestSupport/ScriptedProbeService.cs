using Steamoff.Core.Enums;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;
using Steamoff.Core.Services;

namespace Steamoff.Tests.TestSupport;

/// <summary>
/// Scriptable <see cref="IFirewallService"/> double for <c>FirewallSelfTestRunner</c>
/// probe-cycle tests. Unlike <see cref="ScriptedFirewallService"/> (which scripts a
/// single canned response), this double simulates real create/remove state so the
/// runner's create-&gt;verify-&gt;remove-&gt;verify cycle can be exercised end to end:
/// <see cref="ApplyBlockAsync"/> "creates" a rule that <see cref="GetCurrentStateAsync"/>
/// subsequently reports, and <see cref="RemoveOrDisableAsync"/> "removes" it again.
/// </summary>
public sealed class ScriptedProbeService : IFirewallService
{
    private readonly Exception? _applyException;
    private readonly Exception? _removeException;
    private readonly Exception? _stateException;
    private readonly bool _simulatesRule;
    private readonly HashSet<string> _activeRuleNames = new(StringComparer.Ordinal);

    public List<(IReadOnlyList<FirewallTarget> Targets, DirectionMode DirectionMode)> ApplyBlockCalls { get; } = new();
    public List<(IReadOnlyList<FirewallTarget> Targets, RuleCleanupMode CleanupMode)> RemoveOrDisableCalls { get; } = new();

    /// <summary>A probe that genuinely creates-then-removes its rule — the strategy "works". The expected sentinel display name is asserted by the caller via <see cref="ApplyBlockCalls"/>.</summary>
    public static ScriptedProbeService WorkingProbe(string expectedDisplayName) => new(
        applyException: null, removeException: null, stateException: null, simulatesRule: true);

    /// <summary>A probe whose ApplyBlock "succeeds" but never actually produces a verifiable rule — the strategy silently does nothing (fails the probe, but isn't an interruption).</summary>
    public static ScriptedProbeService FailingProbe() => new(
        applyException: null, removeException: null, stateException: null, simulatesRule: false);

    public static ScriptedProbeService ThrowingDuringApply(Exception exception) => new(exception, null, null, simulatesRule: false);

    public static ScriptedProbeService ThrowingDuringRemove(Exception exception) => new(null, exception, null, simulatesRule: true);

    public static ScriptedProbeService ThrowingDuringStateRead(Exception exception) => new(null, null, exception, simulatesRule: true);

    private ScriptedProbeService(Exception? applyException, Exception? removeException, Exception? stateException, bool simulatesRule)
    {
        _applyException = applyException;
        _removeException = removeException;
        _stateException = stateException;
        _simulatesRule = simulatesRule;
    }

    public Task ApplyBlockAsync(IReadOnlyList<FirewallTarget> targets, DirectionMode directionMode, CancellationToken ct = default)
    {
        ApplyBlockCalls.Add((targets, directionMode));
        if (_applyException is not null)
        {
            throw _applyException;
        }

        if (_simulatesRule)
        {
            foreach (var target in targets)
            {
                _activeRuleNames.Add(FirewallRuleNameBuilder.Build(target.DisplayName, RuleDirection.Outbound));
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveOrDisableAsync(IReadOnlyList<FirewallTarget> targets, RuleCleanupMode cleanupMode, CancellationToken ct = default)
    {
        RemoveOrDisableCalls.Add((targets, cleanupMode));
        if (_removeException is not null)
        {
            throw _removeException;
        }

        foreach (var target in targets)
        {
            _activeRuleNames.Remove(FirewallRuleNameBuilder.Build(target.DisplayName, RuleDirection.Outbound));
        }

        return Task.CompletedTask;
    }

    public Task<ActualFirewallState> GetCurrentStateAsync(CancellationToken ct = default)
    {
        if (_stateException is not null)
        {
            throw _stateException;
        }

        var rules = _activeRuleNames.Select(name => new FirewallRuleState
        {
            RuleName = name,
            GroupName = FirewallConstants.RuleGroup,
            Direction = RuleDirection.Outbound,
            Action = RuleAction.Block,
            Enabled = true,
            ApplicationName = null,
            Profiles = "Any"
        }).ToArray();

        return Task.FromResult(new ActualFirewallState { Rules = rules });
    }

    public bool IsManagedBySteamoff(FirewallRuleState rule) => rule.IsManagedBySteamoff;
}
