using Steamoff.Core.Enums;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;
using Steamoff.Core.Services;

namespace Steamoff.Infrastructure.Firewall;

public sealed class NetSecurityFirewallService : IFirewallService
{
    private readonly ILogService _log;
    private readonly PowerShellRuleInvoker _invoker;

    public NetSecurityFirewallService(ILogService log, PowerShellRuleInvoker? invoker = null)
    {
        _log = log;
        _invoker = invoker ?? new PowerShellRuleInvoker();
    }

    public bool IsManagedBySteamoff(FirewallRuleState rule) => rule.IsManagedBySteamoff;

    public async Task<ActualFirewallState> GetCurrentStateAsync(CancellationToken ct = default)
    {
        var rules = await _invoker.GetRulesAsync(ct).ConfigureAwait(false);
        return new ActualFirewallState { Rules = rules, CapturedAt = DateTimeOffset.UtcNow };
    }

    public async Task ApplyBlockAsync(IReadOnlyList<FirewallTarget> targets, DirectionMode directionMode, CancellationToken ct = default)
    {
        var failedTargets = 0;
        _log.Info($"NetSecurity firewall: начало применения блокировки. Целей={targets.Count}; directionMode={directionMode}.");
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await UpsertAsync(target, RuleDirection.Outbound, ct).ConfigureAwait(false);
                if (directionMode == DirectionMode.OutboundAndInbound)
                {
                    await UpsertAsync(target, RuleDirection.Inbound, ct).ConfigureAwait(false);
                }
                else
                {
                    await RemoveOrDisableAsync(target, RuleDirection.Inbound, RuleCleanupMode.DisableRules, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedTargets++;
                _log.Warning($"NetSecurity firewall rule update failed for '{target.DisplayName}': {ex.Message}. Target skipped; processing continues.");
            }
        }

        _log.Info($"NetSecurity firewall: применение блокировки завершено. Целей={targets.Count}; успешно={targets.Count - failedTargets}; с ошибками={failedTargets}; directionMode={directionMode}.");
    }

    public async Task RemoveOrDisableAsync(IReadOnlyList<FirewallTarget> targets, RuleCleanupMode cleanupMode, CancellationToken ct = default)
    {
        var failedRules = 0;
        var attemptedRules = 0;
        _log.Info($"NetSecurity firewall: начало очистки правил. Целей={targets.Count}; cleanupMode={cleanupMode}.");
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var direction in new[] { RuleDirection.Outbound, RuleDirection.Inbound })
            {
                attemptedRules++;
                try
                {
                    await RemoveOrDisableAsync(target, direction, cleanupMode, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedRules++;
                    _log.Warning($"NetSecurity firewall rule cleanup failed for '{target.DisplayName}' ({direction}): {ex.Message}. Target skipped; processing continues.");
                }
            }
        }

        _log.Info($"NetSecurity firewall: очистка правил завершена. Проверено правил={attemptedRules}; успешно={attemptedRules - failedRules}; с ошибками={failedRules}; cleanupMode={cleanupMode}.");
    }

    private Task UpsertAsync(FirewallTarget target, RuleDirection direction, CancellationToken ct)
    {
        var name = FirewallRuleNameBuilder.Build(target.DisplayName, direction);
        return _invoker.UpsertBlockRuleAsync(name, target.ExecutablePath, direction, ct);
    }

    private Task RemoveOrDisableAsync(FirewallTarget target, RuleDirection direction, RuleCleanupMode cleanupMode, CancellationToken ct)
    {
        var name = FirewallRuleNameBuilder.Build(target.DisplayName, direction);
        return _invoker.RemoveOrDisableRuleAsync(name, cleanupMode, ct);
    }
}
