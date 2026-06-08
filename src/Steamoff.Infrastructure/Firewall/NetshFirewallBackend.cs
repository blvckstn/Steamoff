using System.Diagnostics;
using System.Text.RegularExpressions;
using Steamoff.Core.Enums;
using Steamoff.Core.Exceptions;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;
using Steamoff.Core.Services;

namespace Steamoff.Infrastructure.Firewall;

/// <summary>
/// Documented fallback IFirewallService implementation that drives
/// "netsh advfirewall firewall ..." instead of the COM API. Not wired up by
/// kept available behind the same interface in case COM interop is unavailable
/// in a given environment. All arguments are passed through
/// ProcessStartInfo.ArgumentList — never concatenated into a shell string —
/// so there is no command-injection surface even though the values
/// (target display names, paths) ultimately originate from user input.
/// </summary>
public sealed class NetshFirewallBackend : IFirewallService
{
    private readonly ILogService _log;

    public NetshFirewallBackend(ILogService log)
    {
        _log = log;
    }

    public bool IsManagedBySteamoff(FirewallRuleState rule) => rule.IsManagedBySteamoff;

    public async Task<ActualFirewallState> GetCurrentStateAsync(CancellationToken ct = default)
    {
        var (exitCode, output, _) = await RunAsync(
            new[] { "advfirewall", "firewall", "show", "rule", $"group={FirewallConstants.RuleGroup}", "verbose" }, ct)
            .ConfigureAwait(false);

        if (exitCode != 0 && !output.Contains("No rules match", StringComparison.OrdinalIgnoreCase))
        {
            throw new FirewallOperationException($"netsh advfirewall завершился с кодом {exitCode}.");
        }

        return new ActualFirewallState { Rules = ParseRules(output), CapturedAt = DateTimeOffset.UtcNow };
    }

    public async Task ApplyBlockAsync(IReadOnlyList<FirewallTarget> targets, DirectionMode directionMode, CancellationToken ct = default)
    {
        foreach (var target in targets)
        {
            await UpsertRuleAsync(target, RuleDirection.Outbound, ct).ConfigureAwait(false);
            if (directionMode == DirectionMode.OutboundAndInbound)
            {
                await UpsertRuleAsync(target, RuleDirection.Inbound, ct).ConfigureAwait(false);
            }
        }
    }

    public async Task RemoveOrDisableAsync(IReadOnlyList<FirewallTarget> targets, RuleCleanupMode cleanupMode, CancellationToken ct = default)
    {
        foreach (var target in targets)
        {
            foreach (var direction in new[] { RuleDirection.Outbound, RuleDirection.Inbound })
            {
                var name = FirewallRuleNameBuilder.Build(target.DisplayName, direction);

                // Defence in depth: never touch a name outside the convention, even though we built it ourselves.
                if (!name.StartsWith(FirewallConstants.RuleNamePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (cleanupMode == RuleCleanupMode.DeleteRules)
                {
                    await RunAsync(new[] { "advfirewall", "firewall", "delete", "rule", $"name={name}", $"group={FirewallConstants.RuleGroup}" }, ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    await RunAsync(new[] { "advfirewall", "firewall", "set", "rule", $"name={name}", $"group={FirewallConstants.RuleGroup}", "new", "enable=no" }, ct)
                        .ConfigureAwait(false);
                }
            }
        }
    }

    private async Task UpsertRuleAsync(FirewallTarget target, RuleDirection direction, CancellationToken ct)
    {
        var name = FirewallRuleNameBuilder.Build(target.DisplayName, direction);
        var dir = direction == RuleDirection.Outbound ? "out" : "in";

        // Try to update an existing Steamoff rule first (idempotent); if that fails, add a new one.
        var (updateExit, _, _) = await RunAsync(new[]
        {
            "advfirewall", "firewall", "set", "rule", $"name={name}", $"group={FirewallConstants.RuleGroup}",
            "new", "enable=yes", "action=block", $"program={target.ExecutablePath}", "profile=domain,private,public"
        }, ct).ConfigureAwait(false);

        if (updateExit == 0)
        {
            _log.Info($"[netsh] Правило обновлено: {name}");
            return;
        }

        var (addExit, _, addError) = await RunAsync(new[]
        {
            "advfirewall", "firewall", "add", "rule", $"name={name}", $"dir={dir}", "action=block",
            $"program={target.ExecutablePath}", $"group={FirewallConstants.RuleGroup}",
            "enable=yes", "profile=domain,private,public",
            $"description={FirewallConstants.RuleDescription}"
        }, ct).ConfigureAwait(false);

        if (addExit != 0)
        {
            throw new FirewallOperationException($"netsh не смог создать правило '{name}': {addError}");
        }

        _log.Info($"[netsh] Правило создано: {name}");
    }

    private static readonly Regex RuleBlockSeparator = new(@"^-{5,}\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Parses the verbose "show rule" text output into structured rule states. Best-effort — netsh has no machine-readable output mode.</summary>
    private static List<FirewallRuleState> ParseRules(string output)
    {
        var results = new List<FirewallRuleState>();
        var blocks = RuleBlockSeparator.Split(output);

        foreach (var block in blocks)
        {
            var name = ExtractField(block, "Rule Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var grouping = ExtractField(block, "Grouping") ?? string.Empty;
            var direction = string.Equals(ExtractField(block, "Direction"), "Out", StringComparison.OrdinalIgnoreCase)
                ? RuleDirection.Outbound
                : RuleDirection.Inbound;
            var action = string.Equals(ExtractField(block, "Action"), "Block", StringComparison.OrdinalIgnoreCase)
                ? RuleAction.Block
                : RuleAction.Allow;
            var enabled = string.Equals(ExtractField(block, "Enabled"), "Yes", StringComparison.OrdinalIgnoreCase);
            var program = ExtractField(block, "Program");
            var profiles = ExtractField(block, "Profiles");

            results.Add(new FirewallRuleState
            {
                RuleName = name.Trim(),
                GroupName = grouping.Trim(),
                Direction = direction,
                Action = action,
                Enabled = enabled,
                ApplicationName = program?.Trim(),
                Profiles = profiles?.Trim()
            });
        }

        return results;
    }

    private static string? ExtractField(string block, string fieldName)
    {
        var match = Regex.Match(block, $@"^{Regex.Escape(fieldName)}:\s*(.+)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("netsh.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new FirewallOperationException("Не удалось запустить netsh.exe.");
        var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return (process.ExitCode, await stdOutTask.ConfigureAwait(false), await stdErrTask.ConfigureAwait(false));
    }
}
