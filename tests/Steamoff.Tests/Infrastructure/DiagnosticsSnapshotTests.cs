using System.IO;
using Steamoff.Core.Enums;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Localization;
using Steamoff.Core.Models;
using Steamoff.Infrastructure.Diagnostics;
using Steamoff.Tests.TestSupport;

namespace Steamoff.Tests.Infrastructure;

/// <summary>
/// Covers I4 from specs/004-steamoff-localized-logs-release-flow/tasks.md:
/// field completeness, localized rendering, and the pending-restart notice
/// for <see cref="DiagnosticsService.BuildSnapshotAsync"/>/<c>BuildExtendedReportAsync</c>.
/// Dependencies are faked in-place (no shared TestSupport additions) — the
/// real service has six collaborators and this is the only suite that needs
/// all of them wired together.
/// </summary>
public sealed class DiagnosticsSnapshotTests
{
    private static DiagnosticsService CreateService(
        FakeLogService log,
        out FakeSettingsService settings,
        string runtimeLanguage = "ru",
        string selectedLanguage = "ru")
    {
        settings = new FakeSettingsService(new AppSettings
        {
            Language = selectedLanguage,
            DesiredState = DesiredState.Blocked,
            SteamPath = @"C:\Games\Steam",
            AdditionalFolders = { },
            AdditionalExecutables = { }
        });

        var localization = new LocalizationService(new LanguageManager(runtimeLanguage), new LocalizedStringProvider(), log);

        return new DiagnosticsService(
            new FakeUserContextService(),
            settings,
            log,
            new FakeSteamDiscoveryService(),
            new FakeTargetScanner(),
            new FakeFirewallService(),
            new FakeAutostartService(),
            localization);
    }

    [Fact]
    public async Task BuildSnapshotAsync_PopulatesAllFields_FromCollaborators()
    {
        var log = new FakeLogService();
        var service = CreateService(log, out var settings);

        var snapshot = await service.BuildSnapshotAsync();

        Assert.False(string.IsNullOrWhiteSpace(snapshot.AppVersion));
        Assert.Equal("ru", snapshot.CurrentLanguageCode);
        Assert.Equal("ru", snapshot.SelectedLanguageCode);
        Assert.False(snapshot.IsRestartRequired);
        Assert.Contains(@"\", snapshot.WindowsUserName);
        Assert.True(snapshot.IsElevated);
        Assert.Equal(settings.SettingsFilePath, snapshot.SettingsPath);
        Assert.Equal(log.LogFilePath, snapshot.LogPath);
        Assert.Equal(@"C:\Games\Steam", snapshot.SteamPath);
        Assert.True(snapshot.IsSteamPathValid);
        Assert.Equal(0, snapshot.AdditionalFolderCount);
        Assert.Equal(0, snapshot.SeparateExeCount);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.FirewallDesiredState));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.FirewallActualState));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.DriftStatus));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.AutostartStatus));
        Assert.Null(snapshot.LastTestResult); // RunAsync was never called — no cached report yet

        // LastReleaseBuildPath mirrors File.Exists on the hardcoded manifest path
        // (ASSUMPTIONS.md A23) — its value is environment-dependent (build-release.ps1
        // may already have populated release\ in this checkout), so assert the same
        // existence check the service performs rather than assuming either state.
        const string expectedManifestPath = @"C:\Users\adm\Desktop\13\vibe\Steamoff\src\Steamoff.App\release\release-manifest.json";
        var expectedBuildPath = File.Exists(expectedManifestPath) ? Path.GetDirectoryName(expectedManifestPath) : null;
        Assert.Equal(expectedBuildPath, snapshot.LastReleaseBuildPath);
    }

    [Fact]
    public async Task BuildExtendedReportAsync_RendersFieldLabels_InTheRuntimeLanguage()
    {
        var ruLog = new FakeLogService();
        var ruService = CreateService(ruLog, out _, runtimeLanguage: "ru", selectedLanguage: "ru");
        var ruReport = await ruService.BuildExtendedReportAsync();

        Assert.Contains("=== Отчёт диагностики Steamoff ===", ruReport);
        Assert.Contains("Версия приложения", ruReport);

        var enLog = new FakeLogService();
        var enService = CreateService(enLog, out _, runtimeLanguage: "en", selectedLanguage: "en");
        var enReport = await enService.BuildExtendedReportAsync();

        Assert.Contains("=== Steamoff Diagnostics Report ===", enReport);
        Assert.Contains("Application version", enReport);
        Assert.DoesNotContain("Версия приложения", enReport);
    }

    [Fact]
    public async Task BuildExtendedReportAsync_IncludesPendingRestartNotice_OnlyWhenLanguagesDiffer()
    {
        var matchingLog = new FakeLogService();
        var matchingService = CreateService(matchingLog, out _, runtimeLanguage: "ru", selectedLanguage: "ru");
        var matchingReport = await matchingService.BuildExtendedReportAsync();

        Assert.DoesNotContain("будет применён после перезапуска", matchingReport);

        var pendingLog = new FakeLogService();
        var pendingService = CreateService(pendingLog, out _, runtimeLanguage: "ru", selectedLanguage: "en");
        var pendingReport = await pendingService.BuildExtendedReportAsync();

        Assert.Contains("Выбран новый язык: en. Он будет применён после перезапуска.", pendingReport);
    }

    private sealed class FakeUserContextService : IUserContextService
    {
        public UserContextInfo GetCurrentContext() => new()
        {
            UserName = "tester",
            Domain = "STEAMOFF",
            Sid = "S-1-5-21-0000000000-0000000000-0000000000-1001",
            IsAdministrator = true,
            IsElevated = true,
            HasFirewallAccess = true
        };
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        private readonly AppSettings _settings;

        public FakeSettingsService(AppSettings settings) => _settings = settings;

        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(_settings);

        public Task SaveAsync(AppSettings settings, CancellationToken ct = default) => Task.CompletedTask;

        public string SettingsFilePath => @"C:\ProgramData\Steamoff\settings.json";

        public bool IsUsingFallbackLocation => false;
    }

    private sealed class FakeSteamDiscoveryService : ISteamDiscoveryService
    {
        public Task<SteamInstallation> DiscoverAsync(CancellationToken ct = default) =>
            Task.FromResult(new SteamInstallation { Path = @"C:\Games\Steam", IsValid = true });

        public SteamInstallation ValidateManualPath(string candidatePath) =>
            new() { Path = candidatePath, IsValid = true };
    }

    private sealed class FakeTargetScanner : ITargetScanner
    {
        public Task<IReadOnlyList<FirewallTarget>> FindSteamCoreTargetsAsync(string steamRoot, bool blockAllInFolder, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FirewallTarget>>(Array.Empty<FirewallTarget>());

        public Task<IReadOnlyList<string>> ScanFolderForExecutablesAsync(string folderPath, bool recursive, IProgress<int>? progress, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private sealed class FakeFirewallService : IFirewallService
    {
        public Task<ActualFirewallState> GetCurrentStateAsync(CancellationToken ct = default) =>
            Task.FromResult(new ActualFirewallState
            {
                Rules = new[]
                {
                    new FirewallRuleState
                    {
                        RuleName = "Steamoff Block steam.exe",
                        GroupName = "Steamoff",
                        Direction = RuleDirection.Outbound,
                        Action = RuleAction.Block,
                        Enabled = true
                    }
                }
            });

        public Task ApplyBlockAsync(IReadOnlyList<FirewallTarget> targets, DirectionMode directionMode, CancellationToken ct = default) =>
            throw new NotSupportedException("Diagnostics never mutates firewall state.");

        public Task RemoveOrDisableAsync(IReadOnlyList<FirewallTarget> targets, RuleCleanupMode cleanupMode, CancellationToken ct = default) =>
            throw new NotSupportedException("Diagnostics never mutates firewall state.");

        public bool IsManagedBySteamoff(FirewallRuleState rule) => rule.GroupName == "Steamoff";
    }

    private sealed class FakeAutostartService : IAutostartService
    {
        public Task<bool> IsInstalledAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task InstallAsync(string executablePath, CancellationToken ct = default) =>
            throw new NotSupportedException("Diagnostics never mutates autostart state.");

        public Task UninstallAsync(CancellationToken ct = default) =>
            throw new NotSupportedException("Diagnostics never mutates autostart state.");

        public Task<AutostartCheckResult> VerifyAsync(string expectedExecutablePath, CancellationToken ct = default) =>
            throw new NotSupportedException("Not exercised by snapshot/report building.");
    }
}
