using Steamoff.Core.Enums;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;

namespace Steamoff.Tests.TestSupport;

/// <summary>
/// Scriptable <see cref="IFirewallService"/> double for <c>FallbackAwareFirewallService</c>
/// orchestration tests — lets a test configure, per scenario, whether
/// <see cref="ApplyBlockAsync"/>/<see cref="RemoveOrDisableAsync"/> throw, and what
/// <see cref="GetCurrentStateAsync"/> reports as the resulting rule set, so the
/// orchestrator's "did the primary strategy actually do anything?" verification
/// (research.md R2 / data-model.md RuleApplicationOutcome) can be exercised without
/// </summary>
public sealed class ScriptedFirewallService : IFirewallService
{
    private readonly Func<ActualFirewallState> _stateProvider;
    private readonly Exception? _exceptionToThrow;
    private readonly Task? _awaitBeforeCompleting;

    public List<(IReadOnlyList<FirewallTarget> Targets, DirectionMode DirectionMode)> ApplyBlockCalls { get; } = new();
    public List<(IReadOnlyList<FirewallTarget> Targets, RuleCleanupMode CleanupMode)> RemoveOrDisableCalls { get; } = new();
    public int GetCurrentStateCallCount { get; private set; }

    /// <summary>Builds a fake that succeeds and reports <paramref name="resultingState"/> from <see cref="GetCurrentStateAsync"/> afterwards.</summary>
    public static ScriptedFirewallService Succeeding(ActualFirewallState resultingState) =>
        new(() => resultingState, exceptionToThrow: null);

    /// <summary>Builds a fake whose mutating operations throw <paramref name="exception"/> (simulates <c>StrategyFailureReason.Exception</c>).</summary>
    public static ScriptedFirewallService Throwing(Exception exception) =>
        new(() => ActualFirewallState.Empty, exceptionToThrow: exception);

    /// <summary>Builds a fake that "succeeds" (no exception) but reports an empty rule set — the silent no-op this feature exists to catch (<c>StrategyFailureReason.NoRulesProduced</c>).</summary>
    public static ScriptedFirewallService SilentlyNoOps() =>
        new(() => ActualFirewallState.Empty, exceptionToThrow: null);

    /// <summary>
    /// Builds a fake whose mutating operation awaits <paramref name="gate"/> before
    /// succeeding and reporting <paramref name="resultingState"/> — lets a test hold an
    /// operation "in flight" (e.g. via an ungated <see cref="TaskCompletionSource"/>) to
    /// prove that a mode/memory snapshot is captured once at the start of the operation
    /// and is unaffected by changes made while it is still running (FR-014).
    /// </summary>
    public static ScriptedFirewallService SucceedingAfter(Task gate, ActualFirewallState resultingState) =>
        new(() => resultingState, exceptionToThrow: null, awaitBeforeCompleting: gate);

    private ScriptedFirewallService(Func<ActualFirewallState> stateProvider, Exception? exceptionToThrow, Task? awaitBeforeCompleting = null)
    {
        _stateProvider = stateProvider;
        _exceptionToThrow = exceptionToThrow;
        _awaitBeforeCompleting = awaitBeforeCompleting;
    }

    public Task<ActualFirewallState> GetCurrentStateAsync(CancellationToken ct = default)
    {
        GetCurrentStateCallCount++;
        return Task.FromResult(_stateProvider());
    }

    public async Task ApplyBlockAsync(IReadOnlyList<FirewallTarget> targets, DirectionMode directionMode, CancellationToken ct = default)
    {
        ApplyBlockCalls.Add((targets, directionMode));
        if (_awaitBeforeCompleting is not null)
        {
            await _awaitBeforeCompleting.ConfigureAwait(false);
        }

        if (_exceptionToThrow is not null)
        {
            throw _exceptionToThrow;
        }
    }

    public async Task RemoveOrDisableAsync(IReadOnlyList<FirewallTarget> targets, RuleCleanupMode cleanupMode, CancellationToken ct = default)
    {
        RemoveOrDisableCalls.Add((targets, cleanupMode));
        if (_awaitBeforeCompleting is not null)
        {
            await _awaitBeforeCompleting.ConfigureAwait(false);
        }

        if (_exceptionToThrow is not null)
        {
            throw _exceptionToThrow;
        }
    }

    public bool IsManagedBySteamoff(FirewallRuleState rule) => rule.IsManagedBySteamoff;
}
