using Steamoff.Core.Enums;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Logging;
using Steamoff.Core.Models;
using Steamoff.Infrastructure.Firewall;
using Steamoff.Tests.TestSupport;

namespace Steamoff.Tests.Infrastructure;

/// <summary>
/// Tests for the first-launch self-test runner (FR-010/FR-011, contract C5).
///
/// Adaptation note (documented also in FirewallSelfTestRunner): the contract's
/// literal "probe rules live in a dedicated 'Steamoff-SelfTest-Probe' GROUP"
/// requirement is infeasible — every IFirewallService implementation hardcodes
/// FirewallConstants.RuleGroup and ignores FirewallTarget.GroupName (confirmed
/// by inspection), and changing that would violate the binding "Strategies 1
/// and 2 must remain functionally unchanged" constraint. The runner instead
/// achieves the real isolation GOAL — probe rules can never collide with real
/// targets, are invisible to coverage counting, and are always cleaned up — by
/// using a uniquely-named sentinel DisplayName ("Steamoff-SelfTest-Probe") that
/// FirewallRuleNameBuilder turns into a rule name no real target could ever
/// produce. These tests assert isolation via that sentinel name.
/// </summary>
public sealed class FirewallSelfTestRunnerTests
{
    private const string ProbeDisplayName = "Steamoff-SelfTest-Probe";

    [Fact]
    public async Task RunIfNeededAsync_Outcome_NotYetRun_ProbesAllThreeAndRecordsCompletedWithResult()
    {
        var primary = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
        var secondary = ScriptedProbeService.FailingProbe();
        var scriptFile = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
        var settings = new FakeSettingsService(AppSettings.CreateDefault());
        var runner = CreateRunner(primary, secondary, scriptFile, settings);

        await runner.RunIfNeededAsync();

        var saved = settings.LastSaved!;
        Assert.Equal(FirewallSelfTestOutcome.CompletedWithResult, saved.FirewallSelfTest.Outcome);
        Assert.Equal(new[] { FirewallStrategyVariant.ScriptFile, FirewallStrategyVariant.Primary }, saved.FirewallSelfTest.WorkingStrategies);
        Assert.NotNull(saved.FirewallSelfTest.CompletedAt);
    }

    [Fact]
    public async Task RunIfNeededAsync_Outcome_AlreadyTerminal_IsNoOp()
    {
        var primary = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
        var secondary = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
        var scriptFile = ScriptedProbeService.WorkingProbe(ProbeDisplayName);

        foreach (var terminalOutcome in new[] { FirewallSelfTestOutcome.CompletedWithResult, FirewallSelfTestOutcome.Inconclusive })
        {
            var initial = AppSettings.CreateDefault();
            initial.FirewallSelfTest = new FirewallSelfTestRecord { Outcome = terminalOutcome };
            var settings = new FakeSettingsService(initial);
            var runner = CreateRunner(primary, secondary, scriptFile, settings);

            await runner.RunIfNeededAsync();

            Assert.Empty(primary.ApplyBlockCalls);
            Assert.Empty(secondary.ApplyBlockCalls);
            Assert.Empty(scriptFile.ApplyBlockCalls);
            Assert.Null(settings.LastSaved);
        }
    }

    [Fact]
    public async Task RunIfNeededAsync_ProbeUsesSentinelName_NeverCollidesWithRealTargets()
    {
        var primary = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
        var secondary = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
        var scriptFile = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
        var settings = new FakeSettingsService(AppSettings.CreateDefault());
        var runner = CreateRunner(primary, secondary, scriptFile, settings);

        await runner.RunIfNeededAsync();

        foreach (var probe in new[] { primary, secondary, scriptFile })
        {
            Assert.All(probe.ApplyBlockCalls, call => Assert.All(call.Targets, t => Assert.Equal(ProbeDisplayName, t.DisplayName)));
            Assert.All(probe.RemoveOrDisableCalls, call => Assert.All(call.Targets, t => Assert.Equal(ProbeDisplayName, t.DisplayName)));
            Assert.All(probe.RemoveOrDisableCalls, call => Assert.Equal(RuleCleanupMode.DeleteRules, call.CleanupMode));
        }
    }

    [Fact]
    public async Task RunIfNeededAsync_ProbeAlwaysCleansUpEvenWhenAStepThrows()
    {
        var primary = ScriptedProbeService.ThrowingDuringApply(new InvalidOperationException("boom"));
        var secondary = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
        var scriptFile = ScriptedProbeService.ThrowingDuringRemove(new InvalidOperationException("cleanup boom"));
        var settings = new FakeSettingsService(AppSettings.CreateDefault());
        var runner = CreateRunner(primary, secondary, scriptFile, settings);

        await runner.RunIfNeededAsync();

        // Apply threw before reaching RemoveOrDisable for `primary` — nothing to clean up there.
        Assert.Empty(primary.RemoveOrDisableCalls);
        // `scriptFile` reached cleanup (and it threw) — the runner must still have attempted it.
        Assert.Single(scriptFile.RemoveOrDisableCalls);

        var saved = settings.LastSaved!;
        Assert.Equal(FirewallSelfTestOutcome.CompletedWithResult, saved.FirewallSelfTest.Outcome);
        // Neither `primary` (threw on apply) nor `scriptFile` (failed full create->verify->remove->verify
        // cycle because cleanup threw) count as "working" — only `secondary` completed cleanly.
        Assert.Equal(new[] { FirewallStrategyVariant.Secondary }, saved.FirewallSelfTest.WorkingStrategies);
    }

    [Fact]
    public async Task RunIfNeededAsync_SeedsLastSuccessfulStrategy_FirstWorkingInCanonicalOrder()
    {
        var primary = ScriptedProbeService.FailingProbe();
        var secondary = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
        var scriptFile = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
        var settings = new FakeSettingsService(AppSettings.CreateDefault());
        var runner = CreateRunner(primary, secondary, scriptFile, settings);

        await runner.RunIfNeededAsync();

        var saved = settings.LastSaved!;
        Assert.Equal(FirewallStrategyVariant.ScriptFile, saved.LastSuccessfulFirewallStrategy);
    }

    [Fact]
    public async Task RunIfNeededAsync_NoneWorking_RecordsCompletedWithResult_EmptyList_DoesNotSeedMemory()
    {
        var primary = ScriptedProbeService.FailingProbe();
        var secondary = ScriptedProbeService.FailingProbe();
        var scriptFile = ScriptedProbeService.FailingProbe();
        var settings = new FakeSettingsService(AppSettings.CreateDefault());
        var runner = CreateRunner(primary, secondary, scriptFile, settings);

        await runner.RunIfNeededAsync();

        var saved = settings.LastSaved!;
        Assert.Equal(FirewallSelfTestOutcome.CompletedWithResult, saved.FirewallSelfTest.Outcome);
        Assert.Empty(saved.FirewallSelfTest.WorkingStrategies);
        Assert.Null(saved.LastSuccessfulFirewallStrategy);
    }

    [Fact]
    public async Task RunIfNeededAsync_Interrupted_RecordsInconclusive_DistinctFromNotYetRun_NeverRetried()
    {
        // OperationCanceledException is the one exception type the per-strategy probe
        // try/catch deliberately does NOT swallow (it would misreport "doesn't work" for
        // what is actually an interruption) — it must propagate and flip the whole run
        // to Inconclusive rather than CompletedWithResult.
        var primary = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
        var secondary = ScriptedProbeService.ThrowingDuringStateRead(new OperationCanceledException("self-test interrupted"));
        var scriptFile = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
        var settings = new FakeSettingsService(AppSettings.CreateDefault());
        var runner = CreateRunner(primary, secondary, scriptFile, settings);

        await runner.RunIfNeededAsync();

        var saved = settings.LastSaved!;
        Assert.Equal(FirewallSelfTestOutcome.Inconclusive, saved.FirewallSelfTest.Outcome);
        Assert.NotEqual(FirewallSelfTestOutcome.NotYetRun, saved.FirewallSelfTest.Outcome);

        // A second run must be a no-op — Inconclusive is terminal, never retried automatically.
        settings.LastSaved = null;
        await runner.RunIfNeededAsync();
        Assert.Null(settings.LastSaved);
    }

    [Fact]
    public async Task RunIfNeededAsync_LogsFirewallSelfTestCompletedOrInconclusive_ToBothLogs()
    {
        {
            var primary = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
            var secondary = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
            var scriptFile = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
            var log = new FakeLogService();
            var localizedLog = new FakeLocalizedLogService();
            var settings = new FakeSettingsService(AppSettings.CreateDefault());
            var runner = CreateRunner(primary, secondary, scriptFile, settings, log, localizedLog);

            await runner.RunIfNeededAsync();

            Assert.NotEmpty(log.InfoMessages);
            Assert.Equal(1, localizedLog.CountOf(LogEventKey.FirewallSelfTestCompleted));
            Assert.False(localizedLog.Contains(LogEventKey.FirewallSelfTestInconclusive));
        }

        {
            var primary = ScriptedProbeService.ThrowingDuringStateRead(new OperationCanceledException("interrupted"));
            var secondary = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
            var scriptFile = ScriptedProbeService.WorkingProbe(ProbeDisplayName);
            var log = new FakeLogService();
            var localizedLog = new FakeLocalizedLogService();
            var settings = new FakeSettingsService(AppSettings.CreateDefault());
            var runner = CreateRunner(primary, secondary, scriptFile, settings, log, localizedLog);

            await runner.RunIfNeededAsync();

            Assert.NotEmpty(log.WarningMessages.Concat(log.ErrorMessages));
            Assert.Equal(1, localizedLog.CountOf(LogEventKey.FirewallSelfTestInconclusive));
            Assert.False(localizedLog.Contains(LogEventKey.FirewallSelfTestCompleted));
        }
    }

    private static FirewallSelfTestRunner CreateRunner(
        IFirewallService primary,
        IFirewallService secondary,
        IFirewallService scriptFile,
        FakeSettingsService settings,
        ILogService? log = null,
        ILocalizedLogService? localizedLog = null) =>
        new(primary, secondary, scriptFile, settings, log ?? new FakeLogService(), localizedLog ?? new FakeLocalizedLogService());
}
