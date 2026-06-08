using System.Runtime.InteropServices;
using Steamoff.Core.Enums;
using Steamoff.Core.Exceptions;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;
using Steamoff.Core.Services;

namespace Steamoff.Infrastructure.Firewall;

/// <summary>
/// Default IFirewallService implementation: talks to Microsoft Defender Firewall
/// via the late-bound INetFwPolicy2 COM API (HNetCfg.FwPolicy2 / HNetCfg.FWRule).
/// See specs/.../research.md R1 and ASSUMPTIONS.md A2 for why late-bound COM
/// (no interop assembly / no NuGet package) was chosen, and
/// contracts/firewall-service.md for the invariants this class must uphold.
/// </summary>
public sealed class ComFirewallService : IFirewallService
{
    private const string PolicyProgId = "HNetCfg.FwPolicy2";
    private const string RuleProgId = "HNetCfg.FWRule";

    // NET_FW_PROFILE2_DOMAIN | NET_FW_PROFILE2_PRIVATE | NET_FW_PROFILE2_PUBLIC
    private const int AllProfiles = 1 | 2 | 4;

    private const int NetFwActionBlock = 0;
    private const int NetFwActionAllow = 1;
    private const int NetFwRuleDirIn = 1;
    private const int NetFwRuleDirOut = 2;

    private const uint ErrorAccessDenied = 0x80070005;

    private readonly ILogService _log;

    public ComFirewallService(ILogService log)
    {
        _log = log;
    }

    public bool IsManagedBySteamoff(FirewallRuleState rule) => rule.IsManagedBySteamoff;

    public Task<ActualFirewallState> GetCurrentStateAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return WithPolicy(policy =>
            {
                var rules = new List<FirewallRuleState>();
                dynamic ruleCollection = policy.Rules;
                foreach (dynamic rule in ruleCollection)
                {
                    try
                    {
                        if (!string.Equals((string)rule.Grouping, FirewallConstants.RuleGroup, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        rules.Add(ToRuleState(rule));
                    }
                    finally
                    {
                        if (Marshal.IsComObject(rule))
                        {
                            Marshal.ReleaseComObject(rule);
                        }
                    }
                }

                return new ActualFirewallState { Rules = rules, CapturedAt = DateTimeOffset.UtcNow };
            });
        }, ct);
    }

    public Task ApplyBlockAsync(IReadOnlyList<FirewallTarget> targets, DirectionMode directionMode, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            WithPolicy(policy =>
            {
                foreach (var target in targets)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        UpsertRule(policy, target, RuleDirection.Outbound);
                        if (directionMode == DirectionMode.OutboundAndInbound)
                        {
                            UpsertRule(policy, target, RuleDirection.Inbound);
                        }
                        else
                        {
                            // If a previously-created Inbound rule exists (mode switched), disable it rather than leaving it active and unmanaged.
                            TryDisableExisting(policy, target, RuleDirection.Inbound);
                        }
                    }
                    catch (IOException ex)
                    {
                        // A target can become transiently inaccessible between scan time and rule
                        // creation (e.g. Steam mid-update locks/replaces one of its helper executables).
                        // The Windows Firewall COM API then rejects ApplicationName with ERROR_FILE_NOT_FOUND,
                        // surfaced by .NET as FileNotFoundException/IOException. One bad target must not
                        // abort blocking for every other target, including Steam itself.
                        _log.Warning($"Не удалось создать/обновить правило firewall для '{target.DisplayName}': {ex.Message}. Цель пропущена, обработка продолжена.");
                    }
                }

                return true;
            });
        }, ct);
    }

    public Task RemoveOrDisableAsync(IReadOnlyList<FirewallTarget> targets, RuleCleanupMode cleanupMode, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            WithPolicy(policy =>
            {
                dynamic ruleCollection = policy.Rules;
                foreach (var target in targets)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var direction in new[] { RuleDirection.Outbound, RuleDirection.Inbound })
                    {
                        var name = FirewallRuleNameBuilder.Build(target.DisplayName, direction);
                        ApplyCleanupToNamedRule(ruleCollection, name, cleanupMode);
                    }
                }

                return true;
            });
        }, ct);
    }

    private void ApplyCleanupToNamedRule(dynamic ruleCollection, string ruleName, RuleCleanupMode cleanupMode)
    {
        dynamic? existing = TryFind(ruleCollection, ruleName);
        if (existing is null)
        {
            return;
        }

        try
        {
            // Defence in depth: re-validate group + prefix before mutating, exactly like IsManagedBySteamoff would.
            if (!string.Equals((string)existing.Grouping, FirewallConstants.RuleGroup, StringComparison.Ordinal) ||
                !ruleName.StartsWith(FirewallConstants.RuleNamePrefix, StringComparison.Ordinal))
            {
                _log.Warning($"Отказ от изменения правила '{ruleName}' — оно не принадлежит группе Steamoff.");
                return;
            }

            if (cleanupMode == RuleCleanupMode.DeleteRules)
            {
                ruleCollection.Remove(ruleName);
                _log.Info($"Правило firewall удалено: {ruleName}");
            }
            else
            {
                existing.Enabled = false;
                _log.Info($"Правило firewall отключено: {ruleName}");
            }
        }
        finally
        {
            if (Marshal.IsComObject(existing))
            {
                Marshal.ReleaseComObject(existing);
            }
        }
    }

    private void TryDisableExisting(dynamic policy, FirewallTarget target, RuleDirection direction)
    {
        dynamic ruleCollection = policy.Rules;
        var name = FirewallRuleNameBuilder.Build(target.DisplayName, direction);
        dynamic? existing = TryFind(ruleCollection, name);
        if (existing is null)
        {
            return;
        }

        try
        {
            if (string.Equals((string)existing.Grouping, FirewallConstants.RuleGroup, StringComparison.Ordinal))
            {
                existing.Enabled = false;
            }
        }
        finally
        {
            if (Marshal.IsComObject(existing))
            {
                Marshal.ReleaseComObject(existing);
            }
        }
    }

    private void UpsertRule(dynamic policy, FirewallTarget target, RuleDirection direction)
    {
        dynamic ruleCollection = policy.Rules;
        var name = FirewallRuleNameBuilder.Build(target.DisplayName, direction);

        dynamic? existing = TryFind(ruleCollection, name);
        if (existing is not null)
        {
            try
            {
                if (!string.Equals((string)existing.Grouping, FirewallConstants.RuleGroup, StringComparison.Ordinal))
                {
                    _log.Warning($"Конфликт имён правил: '{name}' существует, но не принадлежит Steamoff. Пропускаю.");
                    return;
                }

                existing.Enabled = true;
                existing.Action = NetFwActionBlock;
                existing.ApplicationName = target.ExecutablePath;
                existing.Profiles = AllProfiles;
                _log.Info($"Правило firewall обновлено и включено: {name}");
                return;
            }
            finally
            {
                if (Marshal.IsComObject(existing))
                {
                    Marshal.ReleaseComObject(existing);
                }
            }
        }

        var ruleType = Type.GetTypeFromProgID(RuleProgId)
            ?? throw new FirewallOperationException($"Не удалось получить COM-тип {RuleProgId}.");
        dynamic newRule = Activator.CreateInstance(ruleType)!;
        try
        {
            newRule.Name = name;
            newRule.Description = FirewallConstants.RuleDescription;
            newRule.ApplicationName = target.ExecutablePath;
            newRule.Protocol = 256; // NET_FW_IP_PROTOCOL_ANY
            newRule.Direction = direction == RuleDirection.Outbound ? NetFwRuleDirOut : NetFwRuleDirIn;
            newRule.Enabled = true;
            newRule.Grouping = FirewallConstants.RuleGroup;
            newRule.Profiles = AllProfiles;
            newRule.Action = NetFwActionBlock;

            ruleCollection.Add(newRule);
            _log.Info($"Правило firewall создано: {name}");
        }
        finally
        {
            if (Marshal.IsComObject(newRule))
            {
                Marshal.ReleaseComObject(newRule);
            }
        }
    }

    private static dynamic? TryFind(dynamic ruleCollection, string name)
    {
        try
        {
            dynamic found = ruleCollection.Item(name);
            return found;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static FirewallRuleState ToRuleState(dynamic rule)
    {
        RuleDirection direction = (int)rule.Direction == NetFwRuleDirOut ? RuleDirection.Outbound : RuleDirection.Inbound;
        RuleAction action = (int)rule.Action == NetFwActionBlock ? RuleAction.Block : RuleAction.Allow;

        string? appName = null;
        try
        {
            appName = (string)rule.ApplicationName;
        }
        catch (COMException)
        {
            // Some rules legitimately have no associated application.
        }

        return new FirewallRuleState
        {
            RuleName = (string)rule.Name,
            GroupName = (string)rule.Grouping,
            Direction = direction,
            Action = action,
            Enabled = (bool)rule.Enabled,
            ApplicationName = appName,
            Profiles = SafeProfilesToString(rule)
        };
    }

    private static string? SafeProfilesToString(dynamic rule)
    {
        try
        {
            int mask = (int)rule.Profiles;
            var names = new List<string>();
            if ((mask & 1) != 0) names.Add("Domain");
            if ((mask & 2) != 0) names.Add("Private");
            if ((mask & 4) != 0) names.Add("Public");
            return names.Count == 0 ? null : string.Join(", ", names);
        }
        catch (COMException)
        {
            return null;
        }
    }

    private T WithPolicy<T>(Func<dynamic, T> action)
    {
        var policyType = Type.GetTypeFromProgID(PolicyProgId)
            ?? throw new FirewallOperationException($"Не удалось получить COM-тип {PolicyProgId}. Microsoft Defender Firewall может быть отключён.");

        object? policy = null;
        try
        {
            policy = Activator.CreateInstance(policyType);
            if (policy is null)
            {
                throw new FirewallOperationException("Не удалось создать экземпляр INetFwPolicy2.");
            }

            return action((dynamic)policy);
        }
        catch (COMException ex) when (unchecked((uint)ex.HResult) == ErrorAccessDenied)
        {
            throw new FirewallAccessDeniedException("Нет доступа к Microsoft Defender Firewall — требуются права администратора.", ex);
        }
        catch (COMException ex)
        {
            throw new FirewallOperationException($"Ошибка при обращении к Microsoft Defender Firewall: {ex.Message}", ex);
        }
        finally
        {
            if (policy is not null && Marshal.IsComObject(policy))
            {
                Marshal.ReleaseComObject(policy);
            }
        }
    }
}
