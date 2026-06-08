using Steamoff.Core.Enums;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Logging;
using Steamoff.Core.Models;
using Steamoff.Core.Services;
using Steamoff.Infrastructure.Firewall;
using Steamoff.Tests.TestSupport;

namespace Steamoff.Tests.Infrastructure;

public sealed class FallbackAwareFirewallServiceTests
{
    [Fact]
    public async Task ApplyBlockAsync_PrimaryThrows_InvokesSecondaryAndLogsFallback()
    {
        var target = Target("steam");
        var primary = ScriptedFirewallService.Throwing(new InvalidOperationException("COM failed"));
        var secondary = ScriptedFirewallService.Succeeding(StateWithActiveRule(target, RuleDirection.Outbound));
        var scriptFile = ScriptedFirewallService.SilentlyNoOps();
        var localizedLog = new FakeLocalizedLogService();
        var service = CreateService(primary, secondary, scriptFile, localizedLog: localizedLog);

        await service.ApplyBlockAsync(new[] { target }, DirectionMode.OutboundOnly);

        Assert.Single(primary.ApplyBlockCalls);
        Assert.Single(secondary.ApplyBlockCalls);
        Assert.Empty(scriptFile.ApplyBlockCalls);
        Assert.Equal(DirectionMode.OutboundOnly, secondary.ApplyBlockCalls[0].DirectionMode);
        Assert.Equal(1, localizedLog.CountOf(LogEventKey.FirewallStrategyFallbackUsed));
        Assert.False(localizedLog.Contains(LogEventKey.FirewallAllStrategiesFailed));
    }

    [Fact]
    public async Task ApplyBlockAsync_PrimaryProducesNoRules_InvokesSecondary()
    {
        var target = Target("steam");
        var primary = ScriptedFirewallService.SilentlyNoOps();
        var secondary = ScriptedFirewallService.Succeeding(StateWithActiveRule(target, RuleDirection.Outbound));
        var scriptFile = ScriptedFirewallService.SilentlyNoOps();
        var service = CreateService(primary, secondary, scriptFile);

        await service.ApplyBlockAsync(new[] { target }, DirectionMode.OutboundOnly);

        Assert.Single(primary.ApplyBlockCalls);
        Assert.Single(secondary.ApplyBlockCalls);
        // Verification retries up to 3 times (with short delays) before concluding
        // NoRulesProduced, to tolerate read-after-write propagation lag in the
        // firewall rule store — a silent no-op still exhausts every attempt.
        Assert.Equal(3, primary.GetCurrentStateCallCount);
    }

    [Fact]
    public async Task ApplyBlockAsync_PrimaryCreatesExpectedRule_DoesNotInvokeSecondary()
    {
        var target = Target("steam");
        var primary = ScriptedFirewallService.Succeeding(StateWithActiveRule(target, RuleDirection.Outbound));
        var secondary = ScriptedFirewallService.SilentlyNoOps();
        var scriptFile = ScriptedFirewallService.SilentlyNoOps();
        var localizedLog = new FakeLocalizedLogService();
        var service = CreateService(primary, secondary, scriptFile, localizedLog: localizedLog);

        await service.ApplyBlockAsync(new[] { target }, DirectionMode.OutboundOnly);

        Assert.Single(primary.ApplyBlockCalls);
        Assert.Empty(secondary.ApplyBlockCalls);
        Assert.Empty(scriptFile.ApplyBlockCalls);
        Assert.False(localizedLog.Contains(LogEventKey.FirewallStrategyFallbackUsed));
    }

    [Fact]
    public async Task RemoveOrDisableAsync_PrimaryThrows_InvokesSecondary()
    {
        var target = Target("steam");
        var primary = ScriptedFirewallService.Throwing(new InvalidOperationException("COM failed"));
        var secondary = ScriptedFirewallService.Succeeding(ActualFirewallState.Empty);
        var scriptFile = ScriptedFirewallService.SilentlyNoOps();
        var service = CreateService(primary, secondary, scriptFile);

        await service.RemoveOrDisableAsync(new[] { target }, RuleCleanupMode.DisableRules);

        Assert.Single(primary.RemoveOrDisableCalls);
        Assert.Single(secondary.RemoveOrDisableCalls);
        Assert.Equal(RuleCleanupMode.DisableRules, secondary.RemoveOrDisableCalls[0].CleanupMode);
    }

    [Fact]
    public async Task GetCurrentStateAsync_PrimaryMissingApplicationName_EnrichesFromSecondary()
    {
        var target = Target("steam");
        var ruleName = FirewallRuleNameBuilder.Build(target.DisplayName, RuleDirection.Outbound);
        var primaryState = new ActualFirewallState
        {
            Rules = new[]
            {
                new FirewallRuleState
                {
                    RuleName = ruleName,
                    GroupName = FirewallConstants.RuleGroup,
                    Direction = RuleDirection.Outbound,
                    Action = RuleAction.Block,
                    Enabled = true,
                    ApplicationName = null,
                    Profiles = "Any"
                }
            }
        };
        var primary = ScriptedFirewallService.Succeeding(primaryState);
        var secondary = ScriptedFirewallService.Succeeding(StateWithActiveRule(target, RuleDirection.Outbound));
        var scriptFile = ScriptedFirewallService.SilentlyNoOps();
        var service = CreateService(primary, secondary, scriptFile);

        var state = await service.GetCurrentStateAsync();

        var rule = Assert.Single(state.Rules);
        Assert.Equal(target.ExecutablePath, rule.ApplicationName);
    }

    [Fact]
    public async Task GetCurrentStateAsync_PrimaryAlreadyHasApplicationName_DoesNotConsultSecondary()
    {
        var target = Target("steam");
        var primary = ScriptedFirewallService.Succeeding(StateWithActiveRule(target, RuleDirection.Outbound));
        var secondary = ScriptedFirewallService.SilentlyNoOps();
        var scriptFile = ScriptedFirewallService.SilentlyNoOps();
        var service = CreateService(primary, secondary, scriptFile);

        var state = await service.GetCurrentStateAsync();

        Assert.Equal(0, secondary.GetCurrentStateCallCount);
        Assert.Equal(target.ExecutablePath, Assert.Single(state.Rules).ApplicationName);
    }

    [Fact]
    public async Task ApplyBlockAsync_AutoMode_NoRememberedStrategy_TriesCanonicalOrder()
    {
        var target = Target("steam");
        var primary = ScriptedFirewallService.Throwing(new InvalidOperationException("COM failed"));
        var secondary = ScriptedFirewallService.SilentlyNoOps();
        var scriptFile = ScriptedFirewallService.Succeeding(StateWithActiveRule(target, RuleDirection.Outbound));
        var localizedLog = new FakeLocalizedLogService();
        var rememberedCalls = new List<FirewallStrategyVariant?>();
        var service = CreateService(primary, secondary, scriptFile, localizedLog: localizedLog, rememberedCalls: rememberedCalls);

        await service.ApplyBlockAsync(new[] { target }, DirectionMode.OutboundOnly);

        Assert.Single(primary.ApplyBlockCalls);
        Assert.Single(secondary.ApplyBlockCalls);
        Assert.Single(scriptFile.ApplyBlockCalls);
        Assert.Equal(1, localizedLog.CountOf(LogEventKey.FirewallStrategyFallbackUsed));
        Assert.Equal(FirewallStrategyVariant.ScriptFile, Assert.Single(rememberedCalls));
    }

    [Fact]
    public async Task ApplyBlockAsync_AutoMode_RememberedScriptFile_TriesItFirst()
    {
        var target = Target("steam");
        var primary = ScriptedFirewallService.SilentlyNoOps();
        var secondary = ScriptedFirewallService.SilentlyNoOps();
        var scriptFile = ScriptedFirewallService.Succeeding(StateWithActiveRule(target, RuleDirection.Outbound));
        var localizedLog = new FakeLocalizedLogService();
        var rememberedCalls = new List<FirewallStrategyVariant?>();
        var service = CreateService(primary, secondary, scriptFile, localizedLog: localizedLog, remembered: FirewallStrategyVariant.ScriptFile, rememberedCalls: rememberedCalls);

        await service.ApplyBlockAsync(new[] { target }, DirectionMode.OutboundOnly);

        Assert.Single(scriptFile.ApplyBlockCalls);
        Assert.Empty(primary.ApplyBlockCalls);
        Assert.Empty(secondary.ApplyBlockCalls);
        Assert.False(localizedLog.Contains(LogEventKey.FirewallStrategyFallbackUsed));
        Assert.Equal(FirewallStrategyVariant.ScriptFile, Assert.Single(rememberedCalls));
    }

    [Fact]
    public async Task ApplyBlockAsync_AutoMode_RememberedStrategyFailsThisTime_FallsThroughAndUpdatesMemory()
    {
        var target = Target("steam");
        var primary = ScriptedFirewallService.Succeeding(StateWithActiveRule(target, RuleDirection.Outbound));
        var secondary = ScriptedFirewallService.SilentlyNoOps();
        var scriptFile = ScriptedFirewallService.Throwing(new InvalidOperationException("script failed this time"));
        var localizedLog = new FakeLocalizedLogService();
        var rememberedCalls = new List<FirewallStrategyVariant?>();
        var service = CreateService(primary, secondary, scriptFile, localizedLog: localizedLog, remembered: FirewallStrategyVariant.ScriptFile, rememberedCalls: rememberedCalls);

        await service.ApplyBlockAsync(new[] { target }, DirectionMode.OutboundOnly);

        // Try-order was [ScriptFile, Primary, Secondary] — ScriptFile failed, Primary won.
        Assert.Single(scriptFile.ApplyBlockCalls);
        Assert.Single(primary.ApplyBlockCalls);
        Assert.Empty(secondary.ApplyBlockCalls);
        Assert.Equal(1, localizedLog.CountOf(LogEventKey.FirewallStrategyFallbackUsed));
        Assert.Equal(FirewallStrategyVariant.Primary, Assert.Single(rememberedCalls));
    }

    [Fact]
    public async Task ApplyBlockAsync_AutoMode_AllThreeFail_LogsFirewallAllStrategiesFailedAndThrows_DoesNotOverwriteMemory()
    {
        var target = Target("steam");
        var primary = ScriptedFirewallService.Throwing(new InvalidOperationException("primary failed"));
        var secondary = ScriptedFirewallService.Throwing(new InvalidOperationException("secondary failed"));
        var scriptFile = ScriptedFirewallService.Throwing(new InvalidOperationException("scriptFile failed"));
        var localizedLog = new FakeLocalizedLogService();
        var rememberedCalls = new List<FirewallStrategyVariant?>();
        var service = CreateService(primary, secondary, scriptFile, localizedLog: localizedLog, remembered: FirewallStrategyVariant.Primary, rememberedCalls: rememberedCalls);

        await Assert.ThrowsAsync<Steamoff.Core.Exceptions.FirewallOperationException>(
            () => service.ApplyBlockAsync(new[] { target }, DirectionMode.OutboundOnly));

        Assert.Single(primary.ApplyBlockCalls);
        Assert.Single(secondary.ApplyBlockCalls);
        Assert.Single(scriptFile.ApplyBlockCalls);
        Assert.Equal(1, localizedLog.CountOf(LogEventKey.FirewallAllStrategiesFailed));
        Assert.False(localizedLog.Contains(LogEventKey.FirewallStrategyFallbackUsed));
        Assert.Empty(rememberedCalls);
    }

    [Theory]
    [InlineData(FirewallStrategyMode.ForcePrimary)]
    [InlineData(FirewallStrategyMode.ForceSecondary)]
    [InlineData(FirewallStrategyMode.ForceScriptFile)]
    public async Task ApplyBlockAsync_ForcedMode_UsesOnlyThatStrategy_NeverInvokesOthers(FirewallStrategyMode mode)
    {
        var target = Target("steam");
        var primary = ScriptedFirewallService.Succeeding(StateWithActiveRule(target, RuleDirection.Outbound));
        var secondary = ScriptedFirewallService.Succeeding(StateWithActiveRule(target, RuleDirection.Outbound));
        var scriptFile = ScriptedFirewallService.Succeeding(StateWithActiveRule(target, RuleDirection.Outbound));
        var service = CreateService(primary, secondary, scriptFile, mode: mode);

        await service.ApplyBlockAsync(new[] { target }, DirectionMode.OutboundOnly);

        var (expected, others) = mode switch
        {
            FirewallStrategyMode.ForcePrimary => (primary, new[] { secondary, scriptFile }),
            FirewallStrategyMode.ForceSecondary => (secondary, new[] { primary, scriptFile }),
            FirewallStrategyMode.ForceScriptFile => (scriptFile, new[] { primary, secondary }),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        Assert.Single(expected.ApplyBlockCalls);
        Assert.All(others, other => Assert.Empty(other.ApplyBlockCalls));
    }

    [Fact]
    public async Task ApplyBlockAsync_ForcedModeFails_LogsFirewallForcedStrategyFailed_NoSilentFallback()
    {
        var target = Target("steam");
        var primary = ScriptedFirewallService.SilentlyNoOps();
        var secondary = ScriptedFirewallService.SilentlyNoOps();
        var scriptFile = ScriptedFirewallService.Throwing(new InvalidOperationException("script failed"));
        var localizedLog = new FakeLocalizedLogService();
        var service = CreateService(primary, secondary, scriptFile, localizedLog: localizedLog, mode: FirewallStrategyMode.ForceScriptFile);

        await Assert.ThrowsAsync<Steamoff.Core.Exceptions.FirewallOperationException>(
            () => service.ApplyBlockAsync(new[] { target }, DirectionMode.OutboundOnly));

        Assert.Single(scriptFile.ApplyBlockCalls);
        Assert.Empty(primary.ApplyBlockCalls);
        Assert.Empty(secondary.ApplyBlockCalls);
        Assert.Equal(1, localizedLog.CountOf(LogEventKey.FirewallForcedStrategyFailed));
        Assert.False(localizedLog.Contains(LogEventKey.FirewallAllStrategiesFailed));
        Assert.False(localizedLog.Contains(LogEventKey.FirewallStrategyFallbackUsed));
    }

    [Fact]
    public async Task ApplyBlockAsync_ForcedModeSucceeds_StillUpdatesRememberedStrategy()
    {
        var target = Target("steam");
        var primary = ScriptedFirewallService.Succeeding(StateWithActiveRule(target, RuleDirection.Outbound));
        var secondary = ScriptedFirewallService.SilentlyNoOps();
        var scriptFile = ScriptedFirewallService.SilentlyNoOps();
        var rememberedCalls = new List<FirewallStrategyVariant?>();
        var service = CreateService(primary, secondary, scriptFile, mode: FirewallStrategyMode.ForcePrimary, rememberedCalls: rememberedCalls);

        await service.ApplyBlockAsync(new[] { target }, DirectionMode.OutboundOnly);

        Assert.Equal(FirewallStrategyVariant.Primary, Assert.Single(rememberedCalls));
    }

    [Fact]
    public async Task ApplyBlockAsync_ModeCapturedOncePerOperation_MidOperationModeChangeDoesNotAffectInFlightCall()
    {
        var target = Target("steam");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var currentMode = FirewallStrategyMode.Auto;
        var primary = ScriptedFirewallService.SucceedingAfter(gate.Task, StateWithActiveRule(target, RuleDirection.Outbound));
        var secondary = ScriptedFirewallService.SilentlyNoOps();
        var scriptFile = ScriptedFirewallService.SilentlyNoOps();
        var rememberedCalls = new List<FirewallStrategyVariant?>();
        var service = new FallbackAwareFirewallService(
            primary, secondary, scriptFile,
            () => currentMode,
            () => null,
            variant => { rememberedCalls.Add(variant); return Task.CompletedTask; },
            new FakeLogService(),
            new FakeLocalizedLogService());

        var operation = service.ApplyBlockAsync(new[] { target }, DirectionMode.OutboundOnly);

        // Flip the mode while the operation is still in flight (gated on `primary`) —
        // the already-running call must keep using the mode it captured at the start.
        currentMode = FirewallStrategyMode.ForceScriptFile;
        gate.SetResult();
        await operation;

        Assert.Single(primary.ApplyBlockCalls);
        Assert.Empty(secondary.ApplyBlockCalls);
        Assert.Empty(scriptFile.ApplyBlockCalls);
        Assert.Equal(FirewallStrategyVariant.Primary, Assert.Single(rememberedCalls));
    }

    private static FallbackAwareFirewallService CreateService(
        IFirewallService primary,
        IFirewallService secondary,
        IFirewallService scriptFile,
        ILogService? log = null,
        ILocalizedLogService? localizedLog = null,
        FirewallStrategyMode mode = FirewallStrategyMode.Auto,
        FirewallStrategyVariant? remembered = null,
        List<FirewallStrategyVariant?>? rememberedCalls = null)
    {
        rememberedCalls ??= new List<FirewallStrategyVariant?>();
        return new FallbackAwareFirewallService(
            primary,
            secondary,
            scriptFile,
            () => mode,
            () => remembered,
            variant =>
            {
                rememberedCalls.Add(variant);
                return Task.CompletedTask;
            },
            log ?? new FakeLogService(),
            localizedLog ?? new FakeLocalizedLogService());
    }

    private static FirewallTarget Target(string name) => new()
    {
        Id = name,
        DisplayName = name,
        ExecutablePath = $@"C:\Steam\{name}.exe",
        Kind = TargetKind.SteamCore
    };

    private static ActualFirewallState StateWithActiveRule(FirewallTarget target, RuleDirection direction) => new()
    {
        Rules = new[]
        {
            new FirewallRuleState
            {
                RuleName = FirewallRuleNameBuilder.Build(target.DisplayName, direction),
                GroupName = FirewallConstants.RuleGroup,
                Direction = direction,
                Action = RuleAction.Block,
                Enabled = true,
                ApplicationName = target.ExecutablePath,
                Profiles = "Any"
            }
        }
    };
}
