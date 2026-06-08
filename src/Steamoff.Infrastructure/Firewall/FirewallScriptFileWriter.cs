using System.Security.Cryptography;
using System.Text;
using Steamoff.Core.Interfaces;

namespace Steamoff.Infrastructure.Firewall;

/// <summary>Ensures the elevated "Вариант 3" PowerShell script file exists on disk and matches the content this build expects.</summary>
public interface IFirewallScriptFileWriter
{
    /// <summary>Creates or atomically refreshes the managed script file if missing/stale, and returns its canonical path.</summary>
    Task<string> EnsureUpToDateAsync(CancellationToken ct = default);
}

/// <summary>
/// Owns the single canonical on-disk copy of the "Вариант 3" script
/// (&lt;applicationBaseDirectory&gt;\Scripts\steamoff-firewall.ps1), keeping it
/// fresh via a SHA-256 content-hash check and an atomic temp-file-then-move
/// rewrite — so nothing ever accumulates stale or conflicting copies (FR-005).
/// </summary>
public sealed class FirewallScriptFileWriter : IFirewallScriptFileWriter
{
    /// <summary>
    /// The script this build expects to find on disk. Adapted from the proven
    /// steamOff.ps1 New-NetFirewallRule/Get-NetFirewallRule/Remove-NetFirewallRule
    /// mechanics, but driven entirely by STEAMOFF_* environment variables and
    /// FirewallConstants/FirewallRuleNameBuilder-compatible naming/grouping —
    /// never the prototype's "SteamOfflineToggle" naming (FR-002).
    /// </summary>
    internal const string ScriptContent = """
# Steamoff — "Вариант 3" firewall script (generated; do not edit by hand).
# Belt-and-suspenders execution-policy bypass scoped to THIS process only —
# discarded automatically when the process exits, never persisted to the
# registry/machine/user scopes (research.md R1).
try {
    Set-ExecutionPolicy -Scope Process Bypass -Force | Out-Null
} catch {
    # Ignored — a MachinePolicy/UserPolicy GPO can't be overridden from Process
    # scope anyway; the "-File ... -ExecutionPolicy Bypass" launch flag remains
    # the operative safeguard for this single invocation either way.
}

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$Operation = $env:STEAMOFF_OPERATION
$DisplayName = $env:STEAMOFF_DISPLAY_NAME
$Group = $env:STEAMOFF_RULE_GROUP
$Direction = $env:STEAMOFF_RULE_DIRECTION
$Program = $env:STEAMOFF_PROGRAM
$Description = $env:STEAMOFF_RULE_DESCRIPTION
$CleanupMode = $env:STEAMOFF_CLEANUP_MODE

switch ($Operation) {
    'Apply' {
        $existing = @(Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -eq $DisplayName })
        if ($existing.Count -gt 0) {
            foreach ($rule in $existing) {
                Set-NetFirewallRule -InputObject $rule -Direction $Direction -Action Block -Enabled True -Profile Any -Description $Description -ErrorAction Stop | Out-Null
                Set-NetFirewallApplicationFilter -AssociatedNetFirewallRule $rule -Program $Program -ErrorAction Stop | Out-Null
            }
        } else {
            New-NetFirewallRule -DisplayName $DisplayName -Group $Group -Direction $Direction -Program $Program -Action Block -Profile Any -Enabled True -Description $Description -ErrorAction Stop | Out-Null
        }
    }
    'Remove' {
        if ($CleanupMode -eq 'Delete') {
            Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue |
                Where-Object { $_.DisplayName -eq $DisplayName } |
                Remove-NetFirewallRule -ErrorAction Stop
        } else {
            Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue |
                Where-Object { $_.DisplayName -eq $DisplayName } |
                Set-NetFirewallRule -Enabled False -ErrorAction Stop
        }
    }
    'RemoveAll' {
        Get-NetFirewallRule -Group $Group -ErrorAction SilentlyContinue |
            Remove-NetFirewallRule -ErrorAction Stop
    }
    'Query' {
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
    }
    default {
        throw "Steamoff script file: unknown STEAMOFF_OPERATION value '$Operation'."
    }
}
""";

    private static readonly string ExpectedHash = ComputeHash(ScriptContent);

    private readonly ILogService _log;
    private readonly string _scriptPath;

    public FirewallScriptFileWriter(ILogService log, string? applicationBaseDirectory = null)
    {
        _log = log;
        var baseDirectory = applicationBaseDirectory ?? AppContext.BaseDirectory;
        _scriptPath = Path.Combine(baseDirectory, "Scripts", "steamoff-firewall.ps1");
    }

    public async Task<string> EnsureUpToDateAsync(CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(_scriptPath)!;
        Directory.CreateDirectory(directory);

        if (await IsUpToDateAsync(ct).ConfigureAwait(false))
        {
            return _scriptPath;
        }

        var tempPath = Path.Combine(directory, $"{Path.GetFileName(_scriptPath)}.tmp-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(tempPath, ScriptContent, Encoding.UTF8, ct).ConfigureAwait(false);
        try
        {
            File.Move(tempPath, _scriptPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        _log.Info($"Firewall script file (\"Вариант 3\") written/refreshed at '{_scriptPath}'.");
        return _scriptPath;
    }

    private async Task<bool> IsUpToDateAsync(CancellationToken ct)
    {
        if (!File.Exists(_scriptPath))
        {
            return false;
        }

        try
        {
            var existing = await File.ReadAllTextAsync(_scriptPath, Encoding.UTF8, ct).ConfigureAwait(false);
            return string.Equals(ComputeHash(existing), ExpectedHash, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string ComputeHash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
