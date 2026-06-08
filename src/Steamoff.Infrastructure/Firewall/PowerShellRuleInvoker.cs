using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Steamoff.Core.Enums;
using Steamoff.Core.Exceptions;
using Steamoff.Core.Models;

namespace Steamoff.Infrastructure.Firewall;

public sealed record PowerShellInvocation(
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment);

public sealed record PowerShellInvocationResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IPowerShellCommandRunner
{
    Task<PowerShellInvocationResult> RunAsync(PowerShellInvocation invocation, CancellationToken ct = default);
}

public sealed class ProcessPowerShellCommandRunner : IPowerShellCommandRunner
{
    public async Task<PowerShellInvocationResult> RunAsync(PowerShellInvocation invocation, CancellationToken ct = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var item in invocation.Environment)
        {
            startInfo.Environment[item.Key] = item.Value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new FirewallOperationException("Failed to start powershell.exe for NetSecurity firewall operation.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return new PowerShellInvocationResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }
}

public sealed class PowerShellRuleInvoker
{
    private const string PowerShellExe = "powershell.exe";

    private readonly IPowerShellCommandRunner _runner;

    public PowerShellRuleInvoker(IPowerShellCommandRunner? runner = null)
    {
        _runner = runner ?? new ProcessPowerShellCommandRunner();
    }

    public Task UpsertBlockRuleAsync(string displayName, string programPath, RuleDirection direction, CancellationToken ct = default)
    {
        const string script = """
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$DisplayName = $env:STEAMOFF_DISPLAY_NAME
$Group = $env:STEAMOFF_RULE_GROUP
$Direction = $env:STEAMOFF_RULE_DIRECTION
$Program = $env:STEAMOFF_PROGRAM
$Description = $env:STEAMOFF_RULE_DESCRIPTION
$existing = @(Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -eq $DisplayName })
if ($existing.Count -gt 0) {
  foreach ($rule in $existing) {
    Set-NetFirewallRule -InputObject $rule -Direction $Direction -Action Block -Enabled True -Profile Any -Description $Description -ErrorAction Stop | Out-Null
    Set-NetFirewallApplicationFilter -AssociatedNetFirewallRule $rule -Program $Program -ErrorAction Stop | Out-Null
  }
} else {
  New-NetFirewallRule -DisplayName $DisplayName -Group $Group -Direction $Direction -Program $Program -Action Block -Profile Any -Enabled True -Description $Description -ErrorAction Stop | Out-Null
}
""";

        return RunCheckedAsync(BuildInvocation(
            script,
            new Dictionary<string, string>
            {
                ["STEAMOFF_DISPLAY_NAME"] = displayName,
                ["STEAMOFF_RULE_GROUP"] = FirewallConstants.RuleGroup,
                ["STEAMOFF_RULE_DIRECTION"] = ToPowerShellDirection(direction),
                ["STEAMOFF_PROGRAM"] = programPath,
                ["STEAMOFF_RULE_DESCRIPTION"] = FirewallConstants.RuleDescription
            }), ct);
    }

    public Task RemoveOrDisableRuleAsync(string displayName, RuleCleanupMode cleanupMode, CancellationToken ct = default)
    {
        if (cleanupMode == RuleCleanupMode.DeleteRules)
        {
            const string deleteScript = """
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$DisplayName = $env:STEAMOFF_DISPLAY_NAME
$Group = $env:STEAMOFF_RULE_GROUP
Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue |
  Where-Object { $_.DisplayName -eq $DisplayName } |
  Remove-NetFirewallRule -ErrorAction Stop
""";

            return RunCheckedAsync(BuildInvocation(deleteScript, RuleEnvironment(displayName)), ct);
        }

        const string disableScript = """
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$DisplayName = $env:STEAMOFF_DISPLAY_NAME
$Group = $env:STEAMOFF_RULE_GROUP
Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue |
  Where-Object { $_.DisplayName -eq $DisplayName } |
  Set-NetFirewallRule -Enabled False -ErrorAction Stop
""";

        return RunCheckedAsync(BuildInvocation(disableScript, RuleEnvironment(displayName)), ct);
    }

    public async Task<IReadOnlyList<FirewallRuleState>> GetRulesAsync(CancellationToken ct = default)
    {
        const string script = """
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$Group = $env:STEAMOFF_RULE_GROUP
$rules = @(Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue)
$rules | ForEach-Object {
  $app = Get-NetFirewallApplicationFilter -AssociatedNetFirewallRule $_ -ErrorAction SilentlyContinue | Select-Object -First 1
  $program = if ($app) { $app.Program } else { $null }
  [pscustomobject]@{
    RuleName = $_.DisplayName
    GroupName = $_.Group
    Direction = $_.Direction.ToString()
    Action = $_.Action.ToString()
    Enabled = [bool]$_.Enabled
    ApplicationName = $program
    Profiles = $_.Profile.ToString()
  }
} | ConvertTo-Json -Compress -Depth 4
""";

        var result = await RunAsync(BuildInvocation(script, new Dictionary<string, string>
        {
            ["STEAMOFF_RULE_GROUP"] = FirewallConstants.RuleGroup
        }), ct).ConfigureAwait(false);
        var json = result.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<FirewallRuleState>();
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            if (json.StartsWith("[", StringComparison.Ordinal))
            {
                return JsonSerializer.Deserialize<List<FirewallRuleDto>>(json, options)?.Select(ToRuleState).ToArray()
                    ?? Array.Empty<FirewallRuleState>();
            }

            var single = JsonSerializer.Deserialize<FirewallRuleDto>(json, options);
            return single is null ? Array.Empty<FirewallRuleState>() : new[] { ToRuleState(single) };
        }
        catch (JsonException ex)
        {
            throw new FirewallOperationException("Failed to parse NetSecurity firewall rule output.", ex);
        }
    }

    private async Task RunCheckedAsync(PowerShellInvocation invocation, CancellationToken ct)
    {
        await RunAsync(invocation, ct).ConfigureAwait(false);
    }

    private async Task<PowerShellInvocationResult> RunAsync(PowerShellInvocation invocation, CancellationToken ct)
    {
        var result = await _runner.RunAsync(invocation, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            var details = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new FirewallOperationException($"NetSecurity firewall command failed with exit code {result.ExitCode}: {details.Trim()}");
        }

        return result;
    }

    private static PowerShellInvocation BuildInvocation(string script, IReadOnlyDictionary<string, string> environment)
    {
        var arguments = new List<string>
        {
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-Command",
            script
        };

        return new PowerShellInvocation(PowerShellExe, arguments, environment);
    }

    private static Dictionary<string, string> RuleEnvironment(string displayName) => new()
    {
        ["STEAMOFF_DISPLAY_NAME"] = displayName,
        ["STEAMOFF_RULE_GROUP"] = FirewallConstants.RuleGroup
    };

    private static string ToPowerShellDirection(RuleDirection direction) =>
        direction == RuleDirection.Outbound ? "Outbound" : "Inbound";

    private static FirewallRuleState ToRuleState(FirewallRuleDto dto) => new()
    {
        RuleName = dto.RuleName ?? string.Empty,
        GroupName = dto.GroupName ?? string.Empty,
        Direction = string.Equals(dto.Direction, "Inbound", StringComparison.OrdinalIgnoreCase) ? RuleDirection.Inbound : RuleDirection.Outbound,
        Action = string.Equals(dto.Action, "Allow", StringComparison.OrdinalIgnoreCase) ? RuleAction.Allow : RuleAction.Block,
        Enabled = dto.Enabled,
        ApplicationName = dto.ApplicationName,
        Profiles = dto.Profiles
    };

    private sealed class FirewallRuleDto
    {
        public string? RuleName { get; set; }
        public string? GroupName { get; set; }
        public string? Direction { get; set; }
        public string? Action { get; set; }
        public bool Enabled { get; set; }
        public string? ApplicationName { get; set; }
        public string? Profiles { get; set; }
    }
}
