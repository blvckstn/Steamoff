using System.Linq;
using Steamoff.Core.Enums;
using Steamoff.Core.Exceptions;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Logging;
using Steamoff.Core.Models;
using Steamoff.Core.Services;

namespace Steamoff.Infrastructure.Firewall;

/// <summary>
/// Mode-aware 3-way orchestrating <see cref="IFirewallService"/> — promoted from the
/// feature-006 binary fallback to a full Primary ("Вариант 1") → Secondary ("Вариант 2")
/// → ScriptFile ("Вариант 3") cascade, per specs/007-scriptfile-strategy-mode-selftest.
/// In <see cref="FirewallStrategyMode.Auto"/> it tries the remembered last-successful
/// strategy first (self-healing — falls through and re-learns if it stops working),
/// otherwise the canonical order. In a forced mode (<c>ForcePrimary</c>/<c>ForceSecondary</c>/
/// <c>ForceScriptFile</c>) it runs ONLY that single strategy — no silent fallback — so the
/// user can deliberately diagnose one specific path (FR-008). The mode and remembered
/// strategy are captured exactly once at the start of each operation (FR-014) so a
/// mid-operation settings change cannot affect an in-flight call. See
/// specs/007-scriptfile-strategy-mode-selftest/contracts/scriptfile-strategy-and-orchestration.md
/// (C4) for the full decision contract, and specs/006-firewall-fallback-strategy/data-model.md
/// for the original verification rationale (research.md R2/R3) that this builds on.
/// </summary>
public sealed class FallbackAwareFirewallService : IFirewallService
{
    private const int VerificationAttempts = 3;
    private static readonly TimeSpan VerificationRetryDelay = TimeSpan.FromMilliseconds(700);

    // User-confirmed priority: the generated PowerShell .ps1 strategy is the
    // most reliable path on this machine, so Auto must try it before COM.
    private static readonly FirewallStrategyVariant[] CanonicalOrder =
    {
        FirewallStrategyVariant.ScriptFile,
        FirewallStrategyVariant.Secondary,
        FirewallStrategyVariant.Primary
    };

    private readonly IFirewallService _primary;
    private readonly IFirewallService _secondary;
    private readonly IFirewallService _scriptFile;
    private readonly Func<FirewallStrategyMode> _currentModeProvider;
    private readonly Func<FirewallStrategyVariant?> _lastSuccessfulStrategyProvider;
    private readonly Func<FirewallStrategyVariant?, Task> _rememberSuccessAsync;
    private readonly ILogService _log;
    private readonly ILocalizedLogService _localizedLog;

    public FallbackAwareFirewallService(
        IFirewallService primary,
        IFirewallService secondary,
        IFirewallService scriptFile,
        Func<FirewallStrategyMode> currentModeProvider,
        Func<FirewallStrategyVariant?> lastSuccessfulStrategyProvider,
        Func<FirewallStrategyVariant?, Task> rememberSuccessAsync,
        ILogService log,
        ILocalizedLogService localizedLog)
    {
        _primary = primary;
        _secondary = secondary;
        _scriptFile = scriptFile;
        _currentModeProvider = currentModeProvider;
        _lastSuccessfulStrategyProvider = lastSuccessfulStrategyProvider;
        _rememberSuccessAsync = rememberSuccessAsync;
        _log = log;
        _localizedLog = localizedLog;
    }

    public async Task<ActualFirewallState> GetCurrentStateAsync(CancellationToken ct = default)
    {
        var primaryState = await _primary.GetCurrentStateAsync(ct).ConfigureAwait(false);
        if (!HasUnresolvedApplicationName(primaryState))
        {
            return primaryState;
        }

        // ComFirewallService.ToRuleState silently swallows COMException when reading
        // rule.ApplicationName, leaving it null for rules whose application filter was
        // set through the newer NetSecurity/CIM provider (i.e. rules a fallback strategy
        // created). StatusEvaluator.PathsMatch then never matches a null ApplicationName,
        // so the dashboard reports "0% coverage" for rules that are in fact present,
        // enabled and actively blocking — purely a read-side gap in the primary strategy,
        // not a sign that blocking failed. Enrich those entries from the secondary
        // strategy's view, which reads the application filter directly via
        // Get-NetFirewallApplicationFilter and does not hit this gap. ScriptFile is
        // intentionally not consulted here — its GetCurrentStateAsync exists only to
        // satisfy the IFirewallService contract and its own TryStrategyAsync verification
        // when it is the active strategy (contracts C4), unchanged from feature 006 (C3).
        ActualFirewallState secondaryState;
        try
        {
            secondaryState = await _secondary.GetCurrentStateAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warning($"Не удалось уточнить путь приложения для правил Steamoff через резервную стратегию: {ex.Message}. Показываю состояние, прочитанное только через основную (COM) стратегию.");
            return primaryState;
        }

        return new ActualFirewallState
        {
            Rules = MergeApplicationNames(primaryState.Rules, secondaryState.Rules),
            CapturedAt = primaryState.CapturedAt
        };
    }

    private static bool HasUnresolvedApplicationName(ActualFirewallState state) =>
        state.Rules.Any(r => r.IsManagedBySteamoff && string.IsNullOrEmpty(r.ApplicationName));

    private static IReadOnlyList<FirewallRuleState> MergeApplicationNames(
        IReadOnlyList<FirewallRuleState> primaryRules,
        IReadOnlyList<FirewallRuleState> secondaryRules)
    {
        var merged = new List<FirewallRuleState>(primaryRules.Count);
        foreach (var rule in primaryRules)
        {
            if (rule.IsManagedBySteamoff && string.IsNullOrEmpty(rule.ApplicationName))
            {
                var match = secondaryRules.FirstOrDefault(s =>
                    s.IsManagedBySteamoff
                    && string.Equals(s.RuleName, rule.RuleName, StringComparison.Ordinal)
                    && s.Direction == rule.Direction
                    && !string.IsNullOrEmpty(s.ApplicationName));

                if (match is not null)
                {
                    merged.Add(new FirewallRuleState
                    {
                        RuleName = rule.RuleName,
                        GroupName = rule.GroupName,
                        Direction = rule.Direction,
                        Action = rule.Action,
                        Enabled = rule.Enabled,
                        ApplicationName = match.ApplicationName,
                        Profiles = rule.Profiles
                    });
                    continue;
                }
            }

            merged.Add(rule);
        }

        return merged;
    }

    public bool IsManagedBySteamoff(FirewallRuleState rule) => _primary.IsManagedBySteamoff(rule);

    public Task ApplyBlockAsync(IReadOnlyList<FirewallTarget> targets, DirectionMode directionMode, CancellationToken ct = default) =>
        ExecuteWithFallbackAsync(
            (service, token) => service.ApplyBlockAsync(targets, directionMode, token),
            state => AnyExpectedRuleIsActivelyBlocking(state, targets, directionMode),
            ct);

    public Task RemoveOrDisableAsync(IReadOnlyList<FirewallTarget> targets, RuleCleanupMode cleanupMode, CancellationToken ct = default) =>
        ExecuteWithFallbackAsync(
            (service, token) => service.RemoveOrDisableAsync(targets, cleanupMode, token),
            state => NoExpectedRuleIsStillActivelyBlocking(state, targets),
            ct);

    private Task ExecuteWithFallbackAsync(
        Func<IFirewallService, CancellationToken, Task> operation,
        Func<ActualFirewallState, bool> verifySuccess,
        CancellationToken ct)
    {
        // Captured exactly once, here, for the whole operation — a mid-operation mode
        // change made by the user in Settings must not affect an already-running call
        // (FR-014, Edge Case "переключение режима во время операции").
        var mode = _currentModeProvider();

        return mode == FirewallStrategyMode.Auto
            ? ExecuteAutoAsync(_lastSuccessfulStrategyProvider(), operation, verifySuccess, ct)
            : ExecuteForcedAsync(ResolveForcedVariant(mode), operation, verifySuccess, ct);
    }

    private static FirewallStrategyVariant ResolveForcedVariant(FirewallStrategyMode mode) => mode switch
    {
        FirewallStrategyMode.ForcePrimary => FirewallStrategyVariant.Primary,
        FirewallStrategyMode.ForceSecondary => FirewallStrategyVariant.Secondary,
        FirewallStrategyMode.ForceScriptFile => FirewallStrategyVariant.ScriptFile,
        _ => FirewallStrategyVariant.Primary
    };

    /// <summary>
    /// Auto mode: tries strategies in order <c>[remembered ?? Primary, ...rest of the
    /// canonical Primary→Secondary→ScriptFile order, no duplicates]</c> — i.e. "whichever
    /// worked last time, first" (self-healing convergence per contracts C4) — and
    /// remembers whichever one succeeds. A full failure of all three does NOT overwrite
    /// the remembered strategy: a single bad run shouldn't erase the only known-good
    /// memory ("self-healing" means trying it first again next time, not forgetting it).
    /// </summary>
    private async Task ExecuteAutoAsync(
        FirewallStrategyVariant? remembered,
        Func<IFirewallService, CancellationToken, Task> operation,
        Func<ActualFirewallState, bool> verifySuccess,
        CancellationToken ct)
    {
        var order = BuildAutoTryOrder(remembered);

        StrategyFailureReason? firstFailure = null;
        StrategyFailureReason? lastFailure = null;

        for (var i = 0; i < order.Count; i++)
        {
            var variant = order[i];
            var failure = await TryStrategyAsync(ResolveStrategy(variant), operation, verifySuccess, ct).ConfigureAwait(false);
            if (failure is null)
            {
                if (i > 0)
                {
                    _log.Info($"Стратегия применения правил брандмауэра \"{DescribeVariant(order[0])}\" не справилась — операция выполнена через \"{DescribeVariant(variant)}\" вместо неё (режим «Авто»).");
                    await _localizedLog.LogAsync(LogEventKey.FirewallStrategyFallbackUsed, DescribeFailure(firstFailure!.Value)).ConfigureAwait(false);
                }

                await _rememberSuccessAsync(variant).ConfigureAwait(false);
                return;
            }

            firstFailure ??= failure;
            lastFailure = failure;

            if (i < order.Count - 1)
            {
                _log.Warning($"Стратегия применения правил брандмауэра \"{DescribeVariant(variant)}\" не справилась: {DescribeFailure(failure.Value)}. Пробую следующую по порядку (режим «Авто»).");
            }
        }

        _log.Error($"Все три стратегии применения правил брандмауэра потерпели неудачу (режим «Авто»). Последняя ошибка ({DescribeVariant(order[^1])}): {DescribeFailure(lastFailure!.Value)}.");
        await _localizedLog.LogAsync(LogEventKey.FirewallAllStrategiesFailed, DescribeFailure(lastFailure!.Value)).ConfigureAwait(false);
        throw new FirewallOperationException("Не удалось применить правила брандмауэра: ни одна из трёх стратегий (Вариант 1, Вариант 2, Вариант 3) не справилась с задачей.");
    }

    /// <summary>Forced mode: runs ONLY the user-selected strategy — no fallback cascade, win or lose (FR-008, "никакого тихого резерва").</summary>
    private async Task ExecuteForcedAsync(
        FirewallStrategyVariant variant,
        Func<IFirewallService, CancellationToken, Task> operation,
        Func<ActualFirewallState, bool> verifySuccess,
        CancellationToken ct)
    {
        var failure = await TryStrategyAsync(ResolveStrategy(variant), operation, verifySuccess, ct).ConfigureAwait(false);
        if (failure is null)
        {
            // A forced success is still worth remembering — it's better than no data, and
            // it's exactly what a user "locking in" a known-good path expects from Auto
            // if they switch back to it later (contracts C4).
            await _rememberSuccessAsync(variant).ConfigureAwait(false);
            return;
        }

        _log.Error($"Принудительно выбранная стратегия применения правил брандмауэра \"{DescribeVariant(variant)}\" не справилась: {DescribeFailure(failure.Value)}. Другие стратегии не пробуются — режим форсирован пользователем в настройках.");
        await _localizedLog.LogAsync(LogEventKey.FirewallForcedStrategyFailed, DescribeVariant(variant)).ConfigureAwait(false);
        throw new FirewallOperationException($"Не удалось применить правила брандмауэра принудительно выбранной стратегией \"{DescribeVariant(variant)}\".");
    }

    private static IReadOnlyList<FirewallStrategyVariant> BuildAutoTryOrder(FirewallStrategyVariant? remembered)
    {
        var order = new List<FirewallStrategyVariant>(CanonicalOrder.Length)
        {
            FirewallStrategyVariant.ScriptFile
        };

        if (remembered is not null && !order.Contains(remembered.Value))
        {
            order.Add(remembered.Value);
        }

        foreach (var variant in CanonicalOrder)
        {
            if (!order.Contains(variant))
            {
                order.Add(variant);
            }
        }

        return order;
    }

    private IFirewallService ResolveStrategy(FirewallStrategyVariant variant) => variant switch
    {
        FirewallStrategyVariant.Primary => _primary,
        FirewallStrategyVariant.Secondary => _secondary,
        FirewallStrategyVariant.ScriptFile => _scriptFile,
        _ => _primary
    };

    private static string DescribeVariant(FirewallStrategyVariant variant) => variant switch
    {
        FirewallStrategyVariant.Primary => "Вариант 1",
        FirewallStrategyVariant.Secondary => "Вариант 2",
        FirewallStrategyVariant.ScriptFile => "Вариант 3",
        _ => variant.ToString()
    };

    private static async Task<StrategyFailureReason?> TryStrategyAsync(
        IFirewallService service,
        Func<IFirewallService, CancellationToken, Task> operation,
        Func<ActualFirewallState, bool> verifySuccess,
        CancellationToken ct)
    {
        try
        {
            await operation(service, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StrategyFailureReason.Exception;
        }

        // The firewall rule store can briefly lag behind a just-completed write —
        // especially across separate elevated powershell.exe invocations (NetSecurity/
        // ScriptFile strategies) — so an immediate re-read can spuriously look like
        // "nothing changed". Re-check a few times with a short delay before concluding
        // NoRulesProduced, so a real silent no-op is still caught while a propagation lag
        // is not misreported as "the strategy failed" (the user-observed false alarm).
        for (var attempt = 1; attempt <= VerificationAttempts; attempt++)
        {
            var state = await service.GetCurrentStateAsync(ct).ConfigureAwait(false);
            if (verifySuccess(state))
            {
                return null;
            }

            if (attempt < VerificationAttempts)
            {
                await Task.Delay(VerificationRetryDelay, ct).ConfigureAwait(false);
            }
        }

        return StrategyFailureReason.NoRulesProduced;
    }

    private static string DescribeFailure(StrategyFailureReason reason) => reason switch
    {
        StrategyFailureReason.Exception => "операция завершилась с ошибкой",
        StrategyFailureReason.NoRulesProduced => "операция формально завершилась без ошибок, но ожидаемые правила фактически не появились/не изменились",
        _ => reason.ToString()
    };

    /// <summary>True if at least one expected "Steamoff" rule is present, enabled and blocking — "something meaningful was created" per research.md R2 (deliberately coarse, not an exhaustive per-target/per-direction match).</summary>
    private static bool AnyExpectedRuleIsActivelyBlocking(ActualFirewallState state, IReadOnlyList<FirewallTarget> targets, DirectionMode directionMode)
    {
        if (targets.Count == 0)
        {
            return true;
        }

        foreach (var target in targets)
        {
            if (IsRuleActivelyBlocking(state, target, RuleDirection.Outbound))
            {
                return true;
            }

            if (directionMode == DirectionMode.OutboundAndInbound && IsRuleActivelyBlocking(state, target, RuleDirection.Inbound))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if no expected "Steamoff" rule for any target is still actively blocking — i.e. the cleanup actually had an effect (or the rules were already gone, which is an equally valid idempotent success).</summary>
    private static bool NoExpectedRuleIsStillActivelyBlocking(ActualFirewallState state, IReadOnlyList<FirewallTarget> targets)
    {
        foreach (var target in targets)
        {
            if (IsRuleActivelyBlocking(state, target, RuleDirection.Outbound) || IsRuleActivelyBlocking(state, target, RuleDirection.Inbound))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRuleActivelyBlocking(ActualFirewallState state, FirewallTarget target, RuleDirection direction)
    {
        var expectedName = FirewallRuleNameBuilder.Build(target.DisplayName, direction);
        foreach (var rule in state.Rules)
        {
            if (rule.IsManagedBySteamoff
                && rule.Enabled
                && rule.Action == RuleAction.Block
                && rule.Direction == direction
                && string.Equals(rule.RuleName, expectedName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
