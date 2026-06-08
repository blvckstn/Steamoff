using System.Text.Json;
using Steamoff.Core.Enums;
using Steamoff.Core.Exceptions;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;
using Steamoff.Core.Services;

namespace Steamoff.Infrastructure.Firewall;

/// <summary>
/// "Вариант 3" — applies firewall rules by writing an actual elevated .ps1
/// script file to disk and launching it via "powershell.exe -File ...". This
/// is the proven-reliable execution surface on machines whose security
/// software disrupts COM automation and/or inline "-Command" PowerShell
/// invocation differently than it treats execution of a real script file.
/// Implements the same IFirewallService contract, per-target resilience, and
/// FirewallConstants/FirewallRuleNameBuilder naming conventions as the other
/// two strategies (FR-002).
/// </summary>
public sealed class ScriptFileFirewallService : IFirewallService
{
    private const string PowerShellExe = "powershell.exe";

    private readonly IFirewallScriptFileWriter _scriptWriter;
    private readonly IPowerShellCommandRunner _runner;
    private readonly ILogService _log;

    public ScriptFileFirewallService(IFirewallScriptFileWriter scriptWriter, IPowerShellCommandRunner runner, ILogService log)
    {
        _scriptWriter = scriptWriter;
        _runner = runner;
        _log = log;
    }

    public bool IsManagedBySteamoff(FirewallRuleState rule) => rule.IsManagedBySteamoff;

    public async Task<ActualFirewallState> GetCurrentStateAsync(CancellationToken ct = default)
    {
        var scriptPath = await _scriptWriter.EnsureUpToDateAsync(ct).ConfigureAwait(false);
        var result = await RunCheckedAsync(scriptPath, BuildOperationEnvironment("Query"), ct).ConfigureAwait(false);
        return new ActualFirewallState { Rules = ParseRules(result.StandardOutput), CapturedAt = DateTimeOffset.UtcNow };
    }

    public async Task ApplyBlockAsync(IReadOnlyList<FirewallTarget> targets, DirectionMode directionMode, CancellationToken ct = default)
    {
        var scriptPath = await _scriptWriter.EnsureUpToDateAsync(ct).ConfigureAwait(false);
        var failedTargets = 0;
        _log.Info($"ScriptFile firewall: начало применения блокировки. Целей={targets.Count}; directionMode={directionMode}.");
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await UpsertAsync(scriptPath, target, RuleDirection.Outbound, ct).ConfigureAwait(false);
                if (directionMode == DirectionMode.OutboundAndInbound)
                {
                    await UpsertAsync(scriptPath, target, RuleDirection.Inbound, ct).ConfigureAwait(false);
                }
                else
                {
                    await RemoveOrDisableAsync(scriptPath, target, RuleDirection.Inbound, RuleCleanupMode.DisableRules, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedTargets++;
                _log.Warning($"ScriptFile firewall rule update failed for '{target.DisplayName}': {ex.Message}. Target skipped; processing continues.");
            }
        }

        _log.Info($"ScriptFile firewall: применение блокировки завершено. Целей={targets.Count}; успешно={targets.Count - failedTargets}; с ошибками={failedTargets}; directionMode={directionMode}.");
    }

    public async Task RemoveOrDisableAsync(IReadOnlyList<FirewallTarget> targets, RuleCleanupMode cleanupMode, CancellationToken ct = default)
    {
        var scriptPath = await _scriptWriter.EnsureUpToDateAsync(ct).ConfigureAwait(false);
        var failedRules = 0;
        var attemptedRules = 0;
        _log.Info($"ScriptFile firewall: начало очистки правил. Целей={targets.Count}; cleanupMode={cleanupMode}.");
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var direction in new[] { RuleDirection.Outbound, RuleDirection.Inbound })
            {
                attemptedRules++;
                try
                {
                    await RemoveOrDisableAsync(scriptPath, target, direction, cleanupMode, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedRules++;
                    _log.Warning($"ScriptFile firewall rule cleanup failed for '{target.DisplayName}' ({direction}): {ex.Message}. Target skipped; processing continues.");
                }
            }
        }

        _log.Info($"ScriptFile firewall: очистка правил завершена. Проверено правил={attemptedRules}; успешно={attemptedRules - failedRules}; с ошибками={failedRules}; cleanupMode={cleanupMode}.");
    }

    private Task UpsertAsync(string scriptPath, FirewallTarget target, RuleDirection direction, CancellationToken ct)
    {
        var name = FirewallRuleNameBuilder.Build(target.DisplayName, direction);
        var environment = BuildOperationEnvironment("Apply", new Dictionary<string, string>
        {
            ["STEAMOFF_DISPLAY_NAME"] = name,
            ["STEAMOFF_RULE_DIRECTION"] = ToPowerShellDirection(direction),
            ["STEAMOFF_PROGRAM"] = target.ExecutablePath,
            ["STEAMOFF_RULE_DESCRIPTION"] = FirewallConstants.RuleDescription
        });

        return RunCheckedAsync(scriptPath, environment, ct);
    }

    private Task RemoveOrDisableAsync(string scriptPath, FirewallTarget target, RuleDirection direction, RuleCleanupMode cleanupMode, CancellationToken ct)
    {
        var name = FirewallRuleNameBuilder.Build(target.DisplayName, direction);
        var environment = BuildOperationEnvironment("Remove", new Dictionary<string, string>
        {
            ["STEAMOFF_DISPLAY_NAME"] = name,
            ["STEAMOFF_CLEANUP_MODE"] = cleanupMode == RuleCleanupMode.DeleteRules ? "Delete" : "Disable"
        });

        return RunCheckedAsync(scriptPath, environment, ct);
    }

    private async Task<PowerShellInvocationResult> RunCheckedAsync(string scriptPath, IReadOnlyDictionary<string, string> environment, CancellationToken ct)
    {
        var invocation = new PowerShellInvocation(
            PowerShellExe,
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath },
            environment);

        var result = await _runner.RunAsync(invocation, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            var details = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new FirewallOperationException($"ScriptFile firewall command failed with exit code {result.ExitCode}: {details.Trim()}");
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildOperationEnvironment(string operation, IReadOnlyDictionary<string, string>? extra = null)
    {
        var environment = new Dictionary<string, string>
        {
            ["STEAMOFF_OPERATION"] = operation,
            ["STEAMOFF_RULE_GROUP"] = FirewallConstants.RuleGroup
        };

        if (extra is not null)
        {
            foreach (var item in extra)
            {
                environment[item.Key] = item.Value;
            }
        }

        return environment;
    }

    private static string ToPowerShellDirection(RuleDirection direction) =>
        direction == RuleDirection.Outbound ? "Outbound" : "Inbound";

    private static IReadOnlyList<FirewallRuleState> ParseRules(string standardOutput)
    {
        var json = standardOutput.Trim();
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
            throw new FirewallOperationException("Failed to parse ScriptFile firewall rule output.", ex);
        }
    }

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
