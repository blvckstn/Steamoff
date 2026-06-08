using Steamoff.Core.Enums;
using Steamoff.Core.Models;
using Steamoff.Core.Services;
using Steamoff.Infrastructure.Firewall;
using Steamoff.Tests.TestSupport;

namespace Steamoff.Tests.Infrastructure;

public sealed class NetSecurityFirewallServiceTests
{
    [Fact]
    public async Task ApplyBlockAsync_UsesSteamoffRuleNamesAndGroupInPowerShellInvocation()
    {
        var runner = new CapturingPowerShellRunner();
        var service = new NetSecurityFirewallService(new FakeLogService(), new PowerShellRuleInvoker(runner));
        var target = Target("Steam Client");

        await service.ApplyBlockAsync(new[] { target }, DirectionMode.OutboundOnly);

        var outbound = Assert.Single(runner.Invocations, i =>
            i.Environment.GetValueOrDefault("STEAMOFF_DISPLAY_NAME") == FirewallRuleNameBuilder.Build(target.DisplayName, RuleDirection.Outbound));
        Assert.Equal(FirewallConstants.RuleGroup, outbound.Environment["STEAMOFF_RULE_GROUP"]);
        Assert.Equal(target.ExecutablePath, outbound.Environment["STEAMOFF_PROGRAM"]);
        Assert.Equal("Outbound", outbound.Environment["STEAMOFF_RULE_DIRECTION"]);

        var inboundDisable = Assert.Single(runner.Invocations, i =>
            i.Environment.GetValueOrDefault("STEAMOFF_DISPLAY_NAME") == FirewallRuleNameBuilder.Build(target.DisplayName, RuleDirection.Inbound));
        Assert.Equal(FirewallConstants.RuleGroup, inboundDisable.Environment["STEAMOFF_RULE_GROUP"]);
    }

    [Fact]
    public async Task ApplyBlockAsync_PerTargetFailureWarnsAndContinues()
    {
        var runner = new CapturingPowerShellRunner
        {
            FailFirstInvocation = true
        };
        var log = new FakeLogService();
        var service = new NetSecurityFirewallService(log, new PowerShellRuleInvoker(runner));

        await service.ApplyBlockAsync(new[] { Target("broken"), Target("ok") }, DirectionMode.OutboundOnly);

        Assert.NotEmpty(log.WarningMessages);
        Assert.True(runner.Invocations.Count >= 3);
        Assert.Contains(runner.Invocations, i =>
            i.Environment.GetValueOrDefault("STEAMOFF_DISPLAY_NAME") == FirewallRuleNameBuilder.Build("ok", RuleDirection.Outbound));
    }

    [Fact]
    public async Task GetCurrentStateAsync_ParsesJsonRulesAndRecognizesSteamoffConvention()
    {
        var runner = new CapturingPowerShellRunner
        {
            Output = """
[{"RuleName":"Steamoff - Block - steam - Outbound","GroupName":"Steamoff","Direction":"Outbound","Action":"Block","Enabled":true,"ApplicationName":"C:\\Steam\\steam.exe","Profiles":"Any"}]
"""
        };
        var service = new NetSecurityFirewallService(new FakeLogService(), new PowerShellRuleInvoker(runner));

        var state = await service.GetCurrentStateAsync();

        var rule = Assert.Single(state.Rules);
        Assert.True(service.IsManagedBySteamoff(rule));
        Assert.Equal(RuleDirection.Outbound, rule.Direction);
        Assert.Equal(RuleAction.Block, rule.Action);
    }

    private static FirewallTarget Target(string name) => new()
    {
        Id = name,
        DisplayName = name,
        ExecutablePath = $@"C:\Steam\{name}.exe",
        Kind = TargetKind.SteamCore
    };

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
