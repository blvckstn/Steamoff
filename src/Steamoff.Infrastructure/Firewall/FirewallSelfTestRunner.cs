using System.Linq;
using Steamoff.Core.Enums;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Logging;
using Steamoff.Core.Models;
using Steamoff.Core.Services;

namespace Steamoff.Infrastructure.Firewall;

/// <summary>One-shot first-launch probe of all three firewall strategies (FR-010/FR-011, contract C5).</summary>
public interface IFirewallSelfTestRunner
{
    /// <summary>
    /// If <c>AppSettings.FirewallSelfTest.Outcome == NotYetRun</c>, safely probes all
    /// three strategies via a create→verify→remove→verify cycle, records the result
    /// (and seeds <c>LastSuccessfulFirewallStrategy</c>), and logs to both journals.
    /// Idempotent — a second call after completion is a no-op.
    /// </summary>
    Task RunIfNeededAsync(CancellationToken ct = default);
}

/// <summary>
/// Probes <see cref="ComFirewallService"/>/<see cref="NetSecurityFirewallService"/>/
/// <see cref="ScriptFileFirewallService"/> — the SAME instances the orchestrator uses,
/// "test exactly what will actually run" (research.md R5) — with a harmless, uniquely
/// named create→verify→remove→verify probe, so a fresh install knows up front which
/// strategies work on this machine and "Авто" mode can start from that knowledge
/// instead of learning by failure on the user's first real block operation.
///
/// Adaptation note (FR-011 group isolation): the contract calls for probe rules to live
/// in a dedicated firewall GROUP ("Steamoff-SelfTest-Probe") rather than
/// <see cref="FirewallConstants.RuleGroup"/>. That is infeasible without modifying the
/// strategies — all three <see cref="IFirewallService"/> implementations hardcode
/// <see cref="FirewallConstants.RuleGroup"/> internally and ignore
/// <see cref="FirewallTarget.GroupName"/> entirely (confirmed by inspection), and
/// "Strategies 1 and 2 must remain functionally unchanged" is a binding constraint.
/// Instead, this runner achieves the real isolation GOALS — the probe can never collide
/// with a real target's rule name, is invisible to coverage counting, and is always
/// cleaned up — by giving the probe a uniquely identifiable sentinel
/// <see cref="FirewallTarget.DisplayName"/> (<see cref="ProbeDisplayName"/>) that
/// <see cref="FirewallRuleNameBuilder"/> turns into a rule name no real Steamoff target
/// could ever produce, and by always removing it via <see cref="RuleCleanupMode.DeleteRules"/>
/// in a try/finally.
/// </summary>
public sealed class FirewallSelfTestRunner : IFirewallSelfTestRunner
{
    /// <summary>
    /// Sentinel display name for the probe target — unique enough that no real Steamoff
    /// target could ever produce a colliding <see cref="FirewallRuleNameBuilder"/> name,
    /// and immediately recognizable in the rule list/logs as "this is the self-test".
    /// </summary>
    private const string ProbeDisplayName = "Steamoff-SelfTest-Probe";

    /// <summary>Harmless, almost-certainly-nonexistent path — the probe never needs the program to actually exist; firewall rule creation doesn't require it.</summary>
    private const string ProbeExecutablePath = @"C:\Steamoff\SelfTestProbe\steamoff-selftest-probe.exe";

    private static readonly FirewallStrategyVariant[] CanonicalOrder =
    {
        FirewallStrategyVariant.Primary,
        FirewallStrategyVariant.Secondary,
        FirewallStrategyVariant.ScriptFile
    };

    private readonly IFirewallService _primary;
    private readonly IFirewallService _secondary;
    private readonly IFirewallService _scriptFile;
    private readonly ISettingsService _settings;
    private readonly ILogService _log;
    private readonly ILocalizedLogService _localizedLog;

    public FirewallSelfTestRunner(
        IFirewallService primary,
        IFirewallService secondary,
        IFirewallService scriptFile,
        ISettingsService settings,
        ILogService log,
        ILocalizedLogService localizedLog)
    {
        _primary = primary;
        _secondary = secondary;
        _scriptFile = scriptFile;
        _settings = settings;
        _log = log;
        _localizedLog = localizedLog;
    }

    public async Task RunIfNeededAsync(CancellationToken ct = default)
    {
        var settings = await _settings.LoadAsync(ct).ConfigureAwait(false);
        if (settings.FirewallSelfTest.Outcome != FirewallSelfTestOutcome.NotYetRun)
        {
            return;
        }

        List<FirewallStrategyVariant> working;
        try
        {
            working = new List<FirewallStrategyVariant>();
            foreach (var (variant, service) in Strategies())
            {
                if (await ProbeAsync(service, variant, ct).ConfigureAwait(false))
                {
                    working.Add(variant);
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            await RecordInconclusiveAsync(settings, ex).ConfigureAwait(false);
            return;
        }

        await RecordCompletedAsync(settings, working).ConfigureAwait(false);
    }

    private IEnumerable<(FirewallStrategyVariant Variant, IFirewallService Service)> Strategies()
    {
        yield return (FirewallStrategyVariant.Primary, _primary);
        yield return (FirewallStrategyVariant.Secondary, _secondary);
        yield return (FirewallStrategyVariant.ScriptFile, _scriptFile);
    }

    /// <summary>
    /// Runs the create→verify→remove→verify cycle for one strategy. Returns true only if
    /// every step completed cleanly and the probe rule ended up both present (after
    /// creation) and absent (after removal) — anything less means "doesn't fully work
    /// here", which is a normal probe outcome, not an interruption. Cleanup is always
    /// attempted once creation has succeeded (research.md R5's try/finally guarantee);
    /// <see cref="OperationCanceledException"/> is deliberately NOT swallowed here — it
    /// signals the whole run was interrupted, not that this one strategy failed.
    /// </summary>
    private async Task<bool> ProbeAsync(IFirewallService service, FirewallStrategyVariant variant, CancellationToken ct)
    {
        var probe = new[] { BuildProbeTarget() };

        try
        {
            await service.ApplyBlockAsync(probe, DirectionMode.OutboundOnly, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Info($"Самотестирование брандмауэра: \"{DescribeVariant(variant)}\" не смог создать пробное правило — на этом компьютере он, по-видимому, не работает ({ex.Message}).");
            return false;
        }

        var outcome = false;
        try
        {
            outcome = await IsProbeRulePresentAsync(service, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Info($"Самотестирование брандмауэра: \"{DescribeVariant(variant)}\" — не удалось проверить созданное пробное правило ({ex.Message}).");
        }
        finally
        {
            try
            {
                await service.RemoveOrDisableAsync(probe, RuleCleanupMode.DeleteRules, ct).ConfigureAwait(false);
                if (outcome)
                {
                    outcome = !await IsProbeRulePresentAsync(service, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Warning($"Самотестирование брандмауэра: не удалось удалить пробное правило после проверки \"{DescribeVariant(variant)}\" — проверьте список правил вручную ({ex.Message}).");
                outcome = false;
            }
        }

        _log.Info(outcome
            ? $"Самотестирование брандмауэра: \"{DescribeVariant(variant)}\" успешно прошёл полный цикл создания/проверки/удаления пробного правила — этот способ работает на этом компьютере."
            : $"Самотестирование брандмауэра: \"{DescribeVariant(variant)}\" не прошёл полный цикл проверки — этот способ не подходит для этого компьютера.");
        return outcome;
    }

    private static async Task<bool> IsProbeRulePresentAsync(IFirewallService service, CancellationToken ct)
    {
        var expectedName = FirewallRuleNameBuilder.Build(ProbeDisplayName, RuleDirection.Outbound);
        var state = await service.GetCurrentStateAsync(ct).ConfigureAwait(false);
        return state.Rules.Any(rule =>
            string.Equals(rule.RuleName, expectedName, StringComparison.Ordinal) &&
            rule.Enabled &&
            rule.Action == RuleAction.Block);
    }

    private static FirewallTarget BuildProbeTarget() => new()
    {
        Id = ProbeDisplayName,
        DisplayName = ProbeDisplayName,
        ExecutablePath = ProbeExecutablePath,
        Kind = TargetKind.StandaloneExe
    };

    private async Task RecordCompletedAsync(AppSettings settings, List<FirewallStrategyVariant> working)
    {
        settings.FirewallSelfTest = new FirewallSelfTestRecord
        {
            Outcome = FirewallSelfTestOutcome.CompletedWithResult,
            WorkingStrategies = working,
            CompletedAt = DateTimeOffset.UtcNow
        };

        if (working.Count > 0)
        {
            settings.LastSuccessfulFirewallStrategy = working[0];
        }

        await _settings.SaveAsync(settings).ConfigureAwait(false);

        var summary = working.Count > 0
            ? string.Join(", ", working.Select(DescribeVariant))
            : "ни один способ не сработал";
        _log.Info($"Самотестирование брандмауэра завершено. Работают на этом компьютере: {summary}.");
        await _localizedLog.LogAsync(LogEventKey.FirewallSelfTestCompleted, summary).ConfigureAwait(false);
    }

    private async Task RecordInconclusiveAsync(AppSettings settings, Exception ex)
    {
        settings.FirewallSelfTest = new FirewallSelfTestRecord
        {
            Outcome = FirewallSelfTestOutcome.Inconclusive,
            WorkingStrategies = new List<FirewallStrategyVariant>(),
            CompletedAt = DateTimeOffset.UtcNow
        };

        // Persist with a fresh token — the run was interrupted, but recording that fact
        // (so it is never silently retried) must not itself be cancellable.
        await _settings.SaveAsync(settings, CancellationToken.None).ConfigureAwait(false);

        _log.Warning($"Самотестирование брандмауэра было прервано и не дало окончательного результата ({ex.Message}). Режим «Авто» будет работать через обычный канонический порядок без предварительного выбора.");
        await _localizedLog.LogAsync(LogEventKey.FirewallSelfTestInconclusive).ConfigureAwait(false);
    }

    private static string DescribeVariant(FirewallStrategyVariant variant) => variant switch
    {
        FirewallStrategyVariant.Primary => "Вариант 1",
        FirewallStrategyVariant.Secondary => "Вариант 2",
        FirewallStrategyVariant.ScriptFile => "Вариант 3",
        _ => variant.ToString()
    };
}
