using Steamoff.Core.Localization;
using Steamoff.Core.Logging;
using Steamoff.Infrastructure.Logging;
using Steamoff.Tests.TestSupport;

namespace Steamoff.Tests.Logging;

/// <summary>
/// Covers I3 from specs/004-steamoff-localized-logs-release-flow/tasks.md — 7
/// representative settings-action categories (open / apply-save-cancel / folder
/// / exe / steam-path / autostart / test-outcome).
///
/// <para>
/// <c>SettingsViewModel</c> itself can't be unit-tested — it's built
/// through <c>AppServices</c>, a concrete sealed class with a parameterless
/// constructor that eagerly wires real platform services and has no fakeable
/// seam (see ASSUMPTIONS.md A16, which made the same call for feature 003).
/// This suite instead exercises the actual seam <c>SettingsViewModel</c> calls
/// for each action — <see cref="LocalizedLogService.LogAsync"/> — proving the
/// exact <see cref="LogEventKey"/> each category maps to resolves to a real,
/// non-empty, correctly-leveled localized message end to end. Deferred per
/// A16-style reasoning: the ViewModel→LocalizedLog wiring itself (one-line
/// call sites, see SettingsViewModel.cs:84/380/553/632/805/399/494) is
/// reviewed by inspection rather than executed through the ViewModel.
/// </para>
/// </summary>
public sealed class SettingsActionLogEventsTests
{
    private static (LocalizedLogService Service, FakeLogService Log) CreateService()
    {
        var log = new FakeLogService();
        var localization = new LocalizationService(new LanguageManager("ru"), new LocalizedStringProvider(), log);
        return (new LocalizedLogService(log, localization), log);
    }

    [Fact]
    public async Task Open_LogsSettingsOpened_AsInfo()
    {
        var (service, log) = CreateService();

        await service.LogAsync(LogEventKey.SettingsOpened);

        Assert.Equal(LogLevel.Info, LogEventTemplates.LevelFor(LogEventKey.SettingsOpened));
        Assert.Single(log.InfoMessages);
        Assert.False(string.IsNullOrWhiteSpace(log.InfoMessages[0]));
    }

    [Theory]
    [InlineData(LogEventKey.SettingsApplied)]
    [InlineData(LogEventKey.SettingsSaved)]
    [InlineData(LogEventKey.SettingsCancelled)]
    public async Task ApplySaveCancel_EachLogsItsOwnDistinctEvent_AsInfo(LogEventKey key)
    {
        var (service, log) = CreateService();

        await service.LogAsync(key);

        Assert.Equal(LogLevel.Info, LogEventTemplates.LevelFor(key));
        var message = Assert.Single(log.InfoMessages);
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    public async Task Folder_LogsAddedAndRemoved_WithThePathArgumentFormatted()
    {
        var (service, log) = CreateService();
        const string path = @"C:\Games\MyMod";

        await service.LogAsync(LogEventKey.FolderAdded, path);
        await service.LogAsync(LogEventKey.FolderRemoved, path);

        Assert.Equal(2, log.InfoMessages.Count);
        Assert.Contains(path, log.InfoMessages[0]);
        Assert.Contains(path, log.InfoMessages[1]);
        Assert.NotEqual(log.InfoMessages[0], log.InfoMessages[1]);
    }

    [Fact]
    public async Task Exe_LogsAddedAndRemoved_WithThePathArgumentFormatted()
    {
        var (service, log) = CreateService();
        const string path = @"C:\Games\MyMod\launcher.exe";

        await service.LogAsync(LogEventKey.ExeAdded, path);
        await service.LogAsync(LogEventKey.ExeRemoved, path);

        Assert.Equal(2, log.InfoMessages.Count);
        Assert.Contains(path, log.InfoMessages[0]);
        Assert.Contains(path, log.InfoMessages[1]);
        Assert.NotEqual(log.InfoMessages[0], log.InfoMessages[1]);
    }

    [Fact]
    public async Task SteamPath_LogsNormalizedAsInfo_AndInvalidAsWarning_BothCarryingTheTypedPath()
    {
        var (service, log) = CreateService();
        const string typed = @"D:\SteamLibrary";
        const string normalized = @"D:\SteamLibrary\steamapps";

        await service.LogAsync(LogEventKey.SteamPathNormalized, typed, normalized);
        await service.LogAsync(LogEventKey.SteamPathInvalid, typed);

        Assert.Equal(LogLevel.Info, LogEventTemplates.LevelFor(LogEventKey.SteamPathNormalized));
        Assert.Equal(LogLevel.Warning, LogEventTemplates.LevelFor(LogEventKey.SteamPathInvalid));
        Assert.Contains(log.InfoMessages, m => m.Contains(typed, StringComparison.Ordinal) && m.Contains(normalized, StringComparison.Ordinal));
        Assert.Contains(log.WarningMessages, m => m.Contains(typed, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Autostart_LogsCreatedAndRemoved_AsDistinctInfoEvents()
    {
        var (service, log) = CreateService();

        await service.LogAsync(LogEventKey.AutostartCreated);
        await service.LogAsync(LogEventKey.AutostartRemoved);

        Assert.Equal(LogLevel.Info, LogEventTemplates.LevelFor(LogEventKey.AutostartCreated));
        Assert.Equal(LogLevel.Info, LogEventTemplates.LevelFor(LogEventKey.AutostartRemoved));
        Assert.Equal(2, log.InfoMessages.Count);
        Assert.NotEqual(log.InfoMessages[0], log.InfoMessages[1]);
    }

    [Fact]
    public async Task TestOutcome_DiagnosticsCopied_LogsAsInfo()
    {
        var (service, log) = CreateService();

        await service.LogAsync(LogEventKey.DiagnosticsCopied);

        Assert.Equal(LogLevel.Info, LogEventTemplates.LevelFor(LogEventKey.DiagnosticsCopied));
        Assert.Single(log.InfoMessages);
        Assert.False(string.IsNullOrWhiteSpace(log.InfoMessages[0]));
    }
}
