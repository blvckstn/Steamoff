using System.Reflection;
using System.Text;
using Steamoff.Core.Enums;
using Steamoff.Core.Exceptions;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Localization;
using Steamoff.Core.Models;

namespace Steamoff.Infrastructure.Diagnostics;

/// <summary>
/// Runs the read-only check battery behind the Settings View "Тестирование"
/// button: elevation, settings/log file access, Steam discovery, additional
/// folders/EXEs validity, firewall read access, and autostart consistency.
/// Every check only reads — it never mutates firewall rules, files, or Steam.
/// All check titles/messages are rendered through <c>diagnostics.check.*</c>
/// localization templates so the report follows the active interface language
/// (FR: "diagnostics must display in the selected/runtime language").
/// </summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
    /// <summary>
    /// Hardcoded per the project brief's release-flow contract: the final build
    /// location <see cref="BuildSnapshotAsync"/> can honestly report on without
    /// guessing at a user-machine layout that doesn't apply to this project.
    /// </summary>
    private const string ReleaseManifestPath = @"C:\Users\adm\Desktop\13\vibe\Steamoff\src\Steamoff.App\release\release-manifest.json";

    private readonly IUserContextService _userContext;
    private readonly ISettingsService _settings;
    private readonly ILogService _log;
    private readonly ISteamDiscoveryService _steamDiscovery;
    private readonly ITargetScanner _scanner;
    private readonly IFirewallService _firewall;
    private readonly IAutostartService _autostart;
    private readonly ILocalizationService _localization;

    private DiagnosticsReport? _lastReport;

    public DiagnosticsService(
        IUserContextService userContext,
        ISettingsService settings,
        ILogService log,
        ISteamDiscoveryService steamDiscovery,
        ITargetScanner scanner,
        IFirewallService firewall,
        IAutostartService autostart,
        ILocalizationService localization)
    {
        _userContext = userContext;
        _settings = settings;
        _log = log;
        _steamDiscovery = steamDiscovery;
        _scanner = scanner;
        _firewall = firewall;
        _autostart = autostart;
        _localization = localization;
    }

    public async Task<DiagnosticsReport> RunAsync(AppSettings settings, CancellationToken ct = default)
    {
        var checks = new List<DiagnosticCheckResult>();

        CheckElevation(checks);
        CheckPathAccess(checks, _localization.GetString("diagnostics.check.settingsLabel"), _settings.SettingsFilePath, _settings.IsUsingFallbackLocation);
        CheckPathAccess(checks, _localization.GetString("diagnostics.check.logLabel"), _log.LogFilePath, usingFallback: false);

        var steamRoot = await CheckSteamAsync(checks, settings, ct).ConfigureAwait(false);
        if (steamRoot is not null)
        {
            await CheckSteamCoreAsync(checks, steamRoot, settings.BlockAllExecutablesInSteamFolder, ct).ConfigureAwait(false);
        }

        CheckFolders(checks, settings.AdditionalFolders);
        CheckExecutables(checks, settings.AdditionalExecutables);
        await CheckFirewallAsync(checks, ct).ConfigureAwait(false);
        await CheckAutostartAsync(checks, settings, ct).ConfigureAwait(false);

        var overall = checks.Count == 0
            ? TestOutcome.Warning
            : checks.Max(c => c.Outcome);

        var report = new DiagnosticsReport
        {
            Checks = checks,
            OverallOutcome = overall,
            RanAt = DateTimeOffset.UtcNow
        };

        _lastReport = report;
        return report;
    }

    public async Task<DiagnosticsSnapshot> BuildSnapshotAsync(CancellationToken ct = default)
    {
        var settings = await _settings.LoadAsync(ct).ConfigureAwait(false);
        var userContext = _userContext.GetCurrentContext();
        var currentLanguageCode = _localization.CurrentLanguage.Code;
        var selectedLanguageCode = settings.Language;

        var steamPathValid = !string.IsNullOrWhiteSpace(settings.SteamPath)
            && _steamDiscovery.ValidateManualPath(settings.SteamPath).IsValid;

        var (firewallDesired, firewallActual, driftStatus) = await DescribeFirewallAsync(settings, ct).ConfigureAwait(false);
        var autostartStatus = await DescribeAutostartAsync(ct).ConfigureAwait(false);

        return new DiagnosticsSnapshot(
            AppVersion: AppVersion,
            CurrentLanguageCode: currentLanguageCode,
            SelectedLanguageCode: selectedLanguageCode,
            IsRestartRequired: LanguageRestartState.IsRestartRequired(selectedLanguageCode, currentLanguageCode),
            WindowsUserName: $"{Environment.UserDomainName}\\{Environment.UserName}",
            IsElevated: userContext.HasFirewallAccess,
            SettingsPath: _settings.SettingsFilePath,
            LogPath: _log.LogFilePath,
            SteamPath: settings.SteamPath ?? string.Empty,
            IsSteamPathValid: steamPathValid,
            AdditionalFolderCount: settings.AdditionalFolders.Count,
            SeparateExeCount: settings.AdditionalExecutables.Count,
            FirewallDesiredState: firewallDesired,
            FirewallActualState: firewallActual,
            DriftStatus: driftStatus,
            AutostartStatus: autostartStatus,
            LastTestResult: DescribeLastTestResult(),
            LastReleaseBuildPath: FindLastReleaseBuildPath());
    }

    public async Task<string> BuildExtendedReportAsync(CancellationToken ct = default)
    {
        var snapshot = await BuildSnapshotAsync(ct).ConfigureAwait(false);
        var tail = await _log.ReadLastLinesAsync(200, ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.AppendLine(_localization.GetString("diagnostics.report.title"));
        AppendField(sb, "diagnostics.field.appVersion", snapshot.AppVersion);
        AppendField(sb, "diagnostics.field.currentLanguage", snapshot.CurrentLanguageCode);
        AppendField(sb, "diagnostics.field.selectedLanguage", snapshot.SelectedLanguageCode);
        AppendField(sb, "diagnostics.field.restartRequired", FormatBool(snapshot.IsRestartRequired));
        AppendField(sb, "diagnostics.field.windowsUser", snapshot.WindowsUserName);
        AppendField(sb, "diagnostics.field.elevated", FormatBool(snapshot.IsElevated));
        AppendField(sb, "diagnostics.field.settingsPath", snapshot.SettingsPath);
        AppendField(sb, "diagnostics.field.logPath", snapshot.LogPath);
        AppendField(sb, "diagnostics.field.steamPath", string.IsNullOrEmpty(snapshot.SteamPath) ? _localization.GetString("diagnostics.field.notAvailable") : snapshot.SteamPath);
        AppendField(sb, "diagnostics.field.steamPathValid", FormatBool(snapshot.IsSteamPathValid));
        AppendField(sb, "diagnostics.field.additionalFolderCount", snapshot.AdditionalFolderCount);
        AppendField(sb, "diagnostics.field.separateExeCount", snapshot.SeparateExeCount);
        AppendField(sb, "diagnostics.field.firewallDesired", snapshot.FirewallDesiredState);
        AppendField(sb, "diagnostics.field.firewallActual", snapshot.FirewallActualState);
        AppendField(sb, "diagnostics.field.driftStatus", snapshot.DriftStatus);
        AppendField(sb, "diagnostics.field.autostartStatus", snapshot.AutostartStatus);
        AppendField(sb, "diagnostics.field.lastTestResult", snapshot.LastTestResult ?? _localization.GetString("diagnostics.field.notAvailable"));
        AppendField(sb, "diagnostics.field.lastReleaseBuildPath", snapshot.LastReleaseBuildPath ?? _localization.GetString("diagnostics.field.notAvailable"));

        if (snapshot.IsRestartRequired)
        {
            sb.AppendLine();
            sb.AppendLine(_localization.GetString("diagnostics.languagePendingRestart", snapshot.SelectedLanguageCode));
        }

        sb.AppendLine();
        sb.AppendLine(_localization.GetString("diagnostics.report.logTail", tail.Count));
        foreach (var line in tail)
        {
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private string? DescribeLastTestResult() => _lastReport?.OverallOutcome switch
    {
        TestOutcome.Ok => _localization.GetString("diagnostics.outcome.success"),
        TestOutcome.Warning => _localization.GetString("diagnostics.outcome.warning"),
        TestOutcome.Error => _localization.GetString("diagnostics.outcome.error"),
        _ => null
    };

    private async Task<(string Desired, string Actual, string Drift)> DescribeFirewallAsync(AppSettings settings, CancellationToken ct)
    {
        var desired = settings.DesiredState == DesiredState.Blocked
            ? _localization.GetString("status.blocked")
            : _localization.GetString("status.unblocked");

        try
        {
            var actualState = await _firewall.GetCurrentStateAsync(ct).ConfigureAwait(false);
            var hasActiveBlockingRules = actualState.Rules.Any(r => r.Enabled && r.Action == RuleAction.Block);

            var actual = hasActiveBlockingRules
                ? _localization.GetString("status.blocked")
                : _localization.GetString("status.unblocked");

            var expectingBlock = settings.DesiredState == DesiredState.Blocked;
            var drift = expectingBlock == hasActiveBlockingRules
                ? _localization.GetString("settings.status.ok")
                : _localization.GetString("status.driftDetected");

            return (desired, actual, drift);
        }
        catch (FirewallOperationException)
        {
            var notAvailable = _localization.GetString("diagnostics.field.notAvailable");
            return (desired, notAvailable, notAvailable);
        }
    }

    private async Task<string> DescribeAutostartAsync(CancellationToken ct)
    {
        try
        {
            var installed = await _autostart.IsInstalledAsync(ct).ConfigureAwait(false);
            return installed ? _localization.GetString("common.yes") : _localization.GetString("common.no");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return _localization.GetString("diagnostics.field.notAvailable");
        }
    }

    private static string? FindLastReleaseBuildPath() =>
        File.Exists(ReleaseManifestPath) ? Path.GetDirectoryName(ReleaseManifestPath) : null;

    private string FormatBool(bool value) => value ? _localization.GetString("common.yes") : _localization.GetString("common.no");

    private void AppendField(StringBuilder sb, string fieldKey, object value) =>
        sb.AppendLine(_localization.GetString(fieldKey) + ": " + value);

    private static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";

    private void CheckElevation(List<DiagnosticCheckResult> checks)
    {
        var context = _userContext.GetCurrentContext();
        checks.Add(context.HasFirewallAccess
            ? Ok(_localization.GetString("diagnostics.check.elevation.title"), _localization.GetString("diagnostics.check.elevation.ok"))
            : Error(_localization.GetString("diagnostics.check.elevation.title"), context.Warning ?? _localization.GetString("diagnostics.check.elevation.error")));
    }

    private void CheckPathAccess(List<DiagnosticCheckResult> checks, string label, string path, bool usingFallback)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                checks.Add(usingFallback
                    ? Warning(label, _localization.GetString("diagnostics.check.pathAccess.fallback", path))
                    : Ok(label, _localization.GetString("diagnostics.check.pathAccess.ok", path)));
            }
            else
            {
                checks.Add(Error(label, _localization.GetString("diagnostics.check.pathAccess.notFound", path)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            checks.Add(Error(label, _localization.GetString("diagnostics.check.pathAccess.error", path, ex.Message)));
        }
    }

    private async Task<string?> CheckSteamAsync(List<DiagnosticCheckResult> checks, AppSettings settings, CancellationToken ct)
    {
        SteamInstallation installation;
        if (!string.IsNullOrWhiteSpace(settings.SteamPath))
        {
            installation = _steamDiscovery.ValidateManualPath(settings.SteamPath);
        }
        else
        {
            installation = await _steamDiscovery.DiscoverAsync(ct).ConfigureAwait(false);
        }

        if (installation.IsValid && installation.Path is not null)
        {
            checks.Add(Ok(_localization.GetString("diagnostics.check.steam.title"), _localization.GetString("diagnostics.check.steam.found", installation.Path, installation.DiscoverySource)));
            return installation.Path;
        }

        checks.Add(Warning(_localization.GetString("diagnostics.check.steam.title"), _localization.GetString("diagnostics.check.steam.notFound")));
        return null;
    }

    private async Task CheckSteamCoreAsync(List<DiagnosticCheckResult> checks, string steamRoot, bool blockAllInFolder, CancellationToken ct)
    {
        var title = _localization.GetString("diagnostics.check.steamCore.title");
        try
        {
            var targets = await _scanner.FindSteamCoreTargetsAsync(steamRoot, blockAllInFolder, ct).ConfigureAwait(false);
            checks.Add(targets.Count > 0
                ? Ok(title, _localization.GetString("diagnostics.check.steamCore.found", targets.Count))
                : Warning(title, _localization.GetString("diagnostics.check.steamCore.notFound")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            checks.Add(Error(title, _localization.GetString("diagnostics.check.steamCore.error", ex.Message)));
        }
    }

    private void CheckFolders(List<DiagnosticCheckResult> checks, IReadOnlyList<FolderBlockTarget> folders)
    {
        var title = _localization.GetString("diagnostics.check.folders.title");
        if (folders.Count == 0)
        {
            checks.Add(Ok(title, _localization.GetString("diagnostics.check.folders.none")));
            return;
        }

        var missing = folders.Where(f => !Directory.Exists(f.Path)).ToList();
        checks.Add(missing.Count == 0
            ? Ok(title, _localization.GetString("diagnostics.check.folders.allOk", folders.Count))
            : Warning(title, _localization.GetString("diagnostics.check.folders.someMissing", missing.Count, folders.Count, string.Join(", ", missing.Select(f => f.Name)))));
    }

    private void CheckExecutables(List<DiagnosticCheckResult> checks, IReadOnlyList<ExeBlockTarget> exes)
    {
        var title = _localization.GetString("diagnostics.check.executables.title");
        if (exes.Count == 0)
        {
            checks.Add(Ok(title, _localization.GetString("diagnostics.check.executables.none")));
            return;
        }

        var missing = exes.Where(e => !File.Exists(e.Path)).ToList();
        checks.Add(missing.Count == 0
            ? Ok(title, _localization.GetString("diagnostics.check.executables.allOk", exes.Count))
            : Warning(title, _localization.GetString("diagnostics.check.executables.someMissing", missing.Count, exes.Count, string.Join(", ", missing.Select(e => e.Name)))));
    }

    private async Task CheckFirewallAsync(List<DiagnosticCheckResult> checks, CancellationToken ct)
    {
        var title = _localization.GetString("diagnostics.check.firewall.title");
        try
        {
            var state = await _firewall.GetCurrentStateAsync(ct).ConfigureAwait(false);
            checks.Add(Ok(title, _localization.GetString("diagnostics.check.firewall.ok", state.Rules.Count)));
        }
        catch (FirewallAccessDeniedException)
        {
            checks.Add(Error(title, _localization.GetString("diagnostics.check.firewall.accessDenied")));
        }
        catch (FirewallOperationException ex)
        {
            checks.Add(Error(title, _localization.GetString("diagnostics.check.firewall.error", ex.Message)));
        }
    }

    private async Task CheckAutostartAsync(List<DiagnosticCheckResult> checks, AppSettings settings, CancellationToken ct)
    {
        var title = _localization.GetString("diagnostics.check.autostart.title");
        if (!settings.StartWithWindows)
        {
            checks.Add(Ok(title, _localization.GetString("diagnostics.check.autostart.disabled")));
            return;
        }

        try
        {
            var installed = await _autostart.IsInstalledAsync(ct).ConfigureAwait(false);
            checks.Add(installed
                ? Ok(title, _localization.GetString("diagnostics.check.autostart.installed"))
                : Warning(title, _localization.GetString("diagnostics.check.autostart.missing")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            checks.Add(Error(title, _localization.GetString("diagnostics.check.autostart.error", ex.Message)));
        }
    }

    private static DiagnosticCheckResult Ok(string name, string message) => new() { Name = name, Outcome = TestOutcome.Ok, Message = message };
    private static DiagnosticCheckResult Warning(string name, string message) => new() { Name = name, Outcome = TestOutcome.Warning, Message = message };
    private static DiagnosticCheckResult Error(string name, string message) => new() { Name = name, Outcome = TestOutcome.Error, Message = message };
}
