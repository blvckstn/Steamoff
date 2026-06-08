using Steamoff.Core.Enums;
using Steamoff.Core.Models;
using Steamoff.Core.Services;
using Steamoff.Infrastructure.Firewall;
using Steamoff.Tests.TestSupport;

namespace Steamoff.Tests.Infrastructure;

public sealed class ScriptFileFirewallServiceTests
{
    private const string FixedScriptPath = @"C:\fake\Scripts\steamoff-firewall.ps1";

    [Fact]
    public async Task ApplyBlockAsync_LaunchesScriptFileWithoutRunasAndUsesSteamoffRuleNamesAndGroup()
    {
        var runner = new CapturingPowerShellRunner();
        var service = new ScriptFileFirewallService(new StubScriptWriter(FixedScriptPath), runner, new FakeLogService());
        var target = Target("Steam Client");

        await service.ApplyBlockAsync(new[] { target }, DirectionMode.OutboundOnly);

        var outbound = Assert.Single(runner.Invocations, i =>
            i.Environment.GetValueOrDefault("STEAMOFF_DISPLAY_NAME") == FirewallRuleNameBuilder.Build(target.DisplayName, RuleDirection.Outbound));
        Assert.Equal("Apply", outbound.Environment["STEAMOFF_OPERATION"]);
        Assert.Equal(FirewallConstants.RuleGroup, outbound.Environment["STEAMOFF_RULE_GROUP"]);
        Assert.Equal(target.ExecutablePath, outbound.Environment["STEAMOFF_PROGRAM"]);
        Assert.Equal("Outbound", outbound.Environment["STEAMOFF_RULE_DIRECTION"]);

        foreach (var invocation in runner.Invocations)
        {
            Assert.Equal("powershell.exe", invocation.FileName);
            Assert.Contains("-File", invocation.Arguments);
            Assert.Contains(FixedScriptPath, invocation.Arguments);
            Assert.DoesNotContain("-Command", invocation.Arguments);
            Assert.DoesNotContain("runas", invocation.Arguments, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ApplyBlockAsync_EnsuresScriptFileIsUpToDateBeforeEachOperation()
    {
        var runner = new CapturingPowerShellRunner();
        var writer = new StubScriptWriter(FixedScriptPath);
        var service = new ScriptFileFirewallService(writer, runner, new FakeLogService());

        await service.ApplyBlockAsync(new[] { Target("Steam Client") }, DirectionMode.OutboundOnly);

        Assert.True(writer.EnsureUpToDateCallCount > 0);
    }

    [Fact]
    public async Task ApplyBlockAsync_PerTargetFailureWarnsAndContinues()
    {
        var runner = new CapturingPowerShellRunner { FailFirstInvocation = true };
        var log = new FakeLogService();
        var service = new ScriptFileFirewallService(new StubScriptWriter(FixedScriptPath), runner, log);

        await service.ApplyBlockAsync(new[] { Target("broken"), Target("ok") }, DirectionMode.OutboundOnly);

        Assert.NotEmpty(log.WarningMessages);
        Assert.True(runner.Invocations.Count >= 3);
        Assert.Contains(runner.Invocations, i =>
            i.Environment.GetValueOrDefault("STEAMOFF_DISPLAY_NAME") == FirewallRuleNameBuilder.Build("ok", RuleDirection.Outbound));
    }

    [Fact]
    public async Task RemoveOrDisableAsync_PassesCleanupModeViaEnvironment()
    {
        var runner = new CapturingPowerShellRunner();
        var service = new ScriptFileFirewallService(new StubScriptWriter(FixedScriptPath), runner, new FakeLogService());
        var target = Target("Steam Client");

        await service.RemoveOrDisableAsync(new[] { target }, RuleCleanupMode.DeleteRules);

        Assert.All(runner.Invocations, i =>
        {
            Assert.Equal("Remove", i.Environment["STEAMOFF_OPERATION"]);
            Assert.Equal("Delete", i.Environment["STEAMOFF_CLEANUP_MODE"]);
        });
    }

    [Fact]
    public async Task RemoveAllManagedRulesAsync_LaunchesScriptFileWithRemoveAllOperation()
    {
        var runner = new CapturingPowerShellRunner();
        var service = new ScriptFileFirewallService(new StubScriptWriter(FixedScriptPath), runner, new FakeLogService());

        await service.RemoveAllManagedRulesAsync();

        var invocation = Assert.Single(runner.Invocations);
        Assert.Equal("RemoveAll", invocation.Environment["STEAMOFF_OPERATION"]);
        Assert.Equal(FirewallConstants.RuleGroup, invocation.Environment["STEAMOFF_RULE_GROUP"]);
        Assert.Contains("-File", invocation.Arguments);
        Assert.Contains(FixedScriptPath, invocation.Arguments);
    }

    [Fact]
    public async Task GetCurrentStateAsync_QueriesViaScriptFileAndParsesJsonRulesAndRecognizesSteamoffConvention()
    {
        var runner = new CapturingPowerShellRunner
        {
            Output = """
[{"RuleName":"Steamoff - Block - steam - Outbound","GroupName":"Steamoff","Direction":"Outbound","Action":"Block","Enabled":true,"ApplicationName":"C:\\Steam\\steam.exe","Profiles":"Any"}]
"""
        };
        var service = new ScriptFileFirewallService(new StubScriptWriter(FixedScriptPath), runner, new FakeLogService());

        var state = await service.GetCurrentStateAsync();

        var rule = Assert.Single(state.Rules);
        Assert.True(service.IsManagedBySteamoff(rule));
        Assert.Equal(RuleDirection.Outbound, rule.Direction);
        Assert.Equal(RuleAction.Block, rule.Action);
        Assert.Single(runner.Invocations, i => i.Environment["STEAMOFF_OPERATION"] == "Query");
    }

    private static FirewallTarget Target(string name) => new()
    {
        Id = name,
        DisplayName = name,
        ExecutablePath = $@"C:\Steam\{name}.exe",
        Kind = TargetKind.SteamCore
    };

    private sealed class StubScriptWriter : IFirewallScriptFileWriter
    {
        private readonly string _path;

        public StubScriptWriter(string path) => _path = path;

        public int EnsureUpToDateCallCount { get; private set; }

        public Task<string> EnsureUpToDateAsync(CancellationToken ct = default)
        {
            EnsureUpToDateCallCount++;
            return Task.FromResult(_path);
        }
    }

    private sealed class CapturingPowerShellRunner : IPowerShellCommandRunner
    {
        public List<PowerShellInvocation> Invocations { get; } = new();
        public bool FailFirstInvocation { get; init; }
        public string Output { get; init; } = string.Empty;

        public Task<PowerShellInvocationResult> RunAsync(PowerShellInvocation invocation, CancellationToken ct = default)
        {
            Invocations.Add(invocation);
            if (FailFirstInvocation && Invocations.Count == 1)
            {
                return Task.FromResult(new PowerShellInvocationResult(1, string.Empty, "simulated failure"));
            }

            return Task.FromResult(new PowerShellInvocationResult(0, Output, string.Empty));
        }
    }
}
