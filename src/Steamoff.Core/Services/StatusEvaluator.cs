using Steamoff.Core.Enums;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;

namespace Steamoff.Core.Services;

/// <summary>
/// Reconciles the desired firewall state against the actually-observed firewall
/// state and produces a single honest HealthStatus + DriftReport. This is the
/// heart of Constitution principle III ("Honest State") — it never trusts a
/// cached toggle flag, only what GetCurrentStateAsync actually reported.
/// </summary>
public sealed class StatusEvaluator : IStatusEvaluator
{
    public HealthStatus Evaluate(
        DesiredFirewallState desired,
        ActualFirewallState actual,
        UserContextInfo userContext,
        IReadOnlyList<FolderBlockTarget> folders,
        IReadOnlyList<ExeBlockTarget> exes)
    {
        if (!userContext.IsElevated || !userContext.HasFirewallAccess)
        {
            return new HealthStatus
            {
                Level = HealthLevel.ReadOnly,
                Overall = OverallStatus.ReadOnlyNoAdmin,
                Message = "Нет прав администратора — Steamoff работает в режиме просмотра.",
                LastCheckedAt = actual.CapturedAt,
                Drift = null
            };
        }

        if (desired.Targets.Count == 0)
        {
            return new HealthStatus
            {
                Level = HealthLevel.Disabled,
                Overall = OverallStatus.NotConfigured,
                Message = "Steam не найден — нет целей для блокировки.",
                LastCheckedAt = actual.CapturedAt,
                Drift = DriftReport.None
            };
        }

        var perTargetCoverage = desired.Targets
            .Select(target => (Target: target, Covered: IsCovered(target, actual.Rules, desired.DirectionMode)))
            .ToList();

        var coveredCount = perTargetCoverage.Count(t => t.Covered);
        var totalCount = perTargetCoverage.Count;
        var coveragePercent = totalCount == 0 ? 0d : Math.Round(coveredCount * 100.0 / totalCount, 1);

        var steamCore = Aggregate(perTargetCoverage, TargetKind.SteamCore);
        var folderCoverage = Aggregate(perTargetCoverage, TargetKind.Folder);
        var exeCoverage = Aggregate(perTargetCoverage, TargetKind.StandaloneExe);

        var drift = BuildDriftReport(desired, actual.Rules, perTargetCoverage);

        var (level, overall, message) = Classify(desired.State, coveredCount, totalCount, drift);

        return new HealthStatus
        {
            Level = level,
            Overall = overall,
            Message = message,
            CoveragePercent = coveragePercent,
            SteamCoreCoverage = steamCore,
            FolderCoverage = folderCoverage,
            ExeCoverage = exeCoverage,
            LastCheckedAt = actual.CapturedAt,
            Drift = drift
        };
    }

    private static (int Covered, int Expected) Aggregate(
        List<(FirewallTarget Target, bool Covered)> coverage, TargetKind kind)
    {
        var matching = coverage.Where(c => c.Target.Kind == kind).ToList();
        return (matching.Count(c => c.Covered), matching.Count);
    }

    /// <summary>
    /// A target is "covered" if Steamoff has an enabled Block rule for it in the
    /// outbound direction (always required) and — when DirectionMode requires it —
    /// also in the inbound direction.
    /// </summary>
    private static bool IsCovered(FirewallTarget target, IReadOnlyList<FirewallRuleState> rules, DirectionMode directionMode)
    {
        var outboundOk = HasEnabledBlockRule(target, rules, RuleDirection.Outbound);
        if (!outboundOk)
        {
            return false;
        }

        if (directionMode == DirectionMode.OutboundAndInbound)
        {
            return HasEnabledBlockRule(target, rules, RuleDirection.Inbound);
        }

        return true;
    }

    private static bool HasEnabledBlockRule(FirewallTarget target, IReadOnlyList<FirewallRuleState> rules, RuleDirection direction)
    {
        var expectedName = FirewallRuleNameBuilder.Build(target.DisplayName, direction);
        return rules.Any(r =>
            r.IsManagedBySteamoff &&
            string.Equals(r.RuleName, expectedName, StringComparison.Ordinal) &&
            r.Direction == direction &&
            r.Action == RuleAction.Block &&
            r.Enabled &&
            PathsMatch(r.ApplicationName, target.ExecutablePath));
    }

    private static bool PathsMatch(string? rulePath, string targetPath)
    {
        if (string.IsNullOrEmpty(rulePath))
        {
            return false;
        }

        return string.Equals(
            rulePath.Trim().TrimEnd('\\'),
            targetPath.Trim().TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
    }

    private static DriftReport BuildDriftReport(
        DesiredFirewallState desired,
        IReadOnlyList<FirewallRuleState> actualRules,
        List<(FirewallTarget Target, bool Covered)> coverage)
    {
        var missing = new List<string>();
        var disabled = new List<string>();
        var unexpectedlyActive = new List<string>();

        foreach (var (target, covered) in coverage)
        {
            var hasAnyRule = HasAnyManagedRule(target, actualRules, desired.DirectionMode);
            if (desired.State == DesiredState.Blocked)
            {
                if (!covered && !hasAnyRule)
                {
                    missing.Add(target.DisplayName);
                }
                else if (!covered && hasAnyRule)
                {
                    disabled.Add(target.DisplayName);
                }
            }
            else // DesiredState.Unblocked
            {
                if (covered)
                {
                    unexpectedlyActive.Add(target.DisplayName);
                }
            }
        }

        var hasDrift = missing.Count > 0 || disabled.Count > 0 || unexpectedlyActive.Count > 0;
        if (!hasDrift)
        {
            return DriftReport.None;
        }

        var parts = new List<string>();
        if (missing.Count > 0)
        {
            parts.Add($"отсутствуют правила для {missing.Count} целей");
        }

        if (disabled.Count > 0)
        {
            parts.Add($"правила отключены для {disabled.Count} целей");
        }

        if (unexpectedlyActive.Count > 0)
        {
            parts.Add($"неожиданно активны правила для {unexpectedlyActive.Count} целей");
        }

        return new DriftReport
        {
            HasDrift = true,
            MissingTargets = missing,
            DisabledTargets = disabled,
            UnexpectedlyActiveTargets = unexpectedlyActive,
            Summary = "Обнаружено расхождение: " + string.Join("; ", parts) + "."
        };
    }

    /// <summary>
    /// Distinguishes "rule exists but disabled/wrong" (drift = restore) from
    /// "rule doesn't exist at all" (drift = recreate) by checking whether any
    /// Steamoff-managed rule with the expected name exists for the relevant
    /// direction(s), regardless of its Enabled/Action/ApplicationName values.
    /// </summary>
    private static bool HasAnyManagedRule(FirewallTarget target, IReadOnlyList<FirewallRuleState> actualRules, DirectionMode directionMode)
    {
        var outboundName = FirewallRuleNameBuilder.Build(target.DisplayName, RuleDirection.Outbound);
        var hasOutbound = actualRules.Any(r => r.IsManagedBySteamoff && string.Equals(r.RuleName, outboundName, StringComparison.Ordinal));
        if (directionMode != DirectionMode.OutboundAndInbound)
        {
            return hasOutbound;
        }

        var inboundName = FirewallRuleNameBuilder.Build(target.DisplayName, RuleDirection.Inbound);
        var hasInbound = actualRules.Any(r => r.IsManagedBySteamoff && string.Equals(r.RuleName, inboundName, StringComparison.Ordinal));
        return hasOutbound || hasInbound;
    }

    private static (HealthLevel Level, OverallStatus Overall, string Message) Classify(
        DesiredState desiredState, int covered, int total, DriftReport drift)
    {
        if (desiredState == DesiredState.Blocked)
        {
            if (covered == total)
            {
                return (HealthLevel.Ok, OverallStatus.FullyBlocked, "Steam заблокирован.");
            }

            if (covered == 0)
            {
                return (HealthLevel.Warning, OverallStatus.PartiallyBlocked, $"Steam пока не заблокирован (0/{total}).");
            }

            return (HealthLevel.Warning, OverallStatus.PartiallyBlocked, $"Steam заблокирован частично ({covered}/{total}).");
        }

        // DesiredState.Unblocked
        if (covered == 0)
        {
            return (HealthLevel.Ok, OverallStatus.FullyUnblocked, "Steam разблокирован.");
        }

        if (covered > 0)
        {
            return (HealthLevel.Warning, OverallStatus.PartiallyBlocked, $"Часть правил всё ещё активна ({covered}/{total}).");
        }

        return (HealthLevel.Warning, OverallStatus.PartiallyBlocked, $"Часть правил всё ещё активна ({covered}/{total}).");
    }
}
