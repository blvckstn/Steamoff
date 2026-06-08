using Steamoff.Core.Localization;
using Steamoff.Core.Logging;
using Steamoff.Infrastructure.Logging;
using Steamoff.Tests.TestSupport;

namespace Steamoff.Tests.Logging;

/// <summary>
/// key resolution, level mapping/dispatch, formatting with/without args, and the
/// fallback chain (missing translation still produces a usable, logged message).
/// </summary>
public sealed class LocalizedLogServiceTests
{
    private static (LocalizedLogService Service, FakeLogService Log) CreateService(string languageCode = "ru")
    {
        var log = new FakeLogService();
        var localization = new LocalizationService(new LanguageManager(languageCode), new LocalizedStringProvider(), log);
        return (new LocalizedLogService(log, localization), log);
    }

    [Fact]
    public async Task LogAsync_ResolvesTheLocalizationKey_ForTheGivenEvent()
    {
        var (service, log) = CreateService("ru");

        await service.LogAsync(LogEventKey.AppStarted);

        Assert.Single(log.InfoMessages);
        Assert.Equal("Приложение запущено", log.InfoMessages[0]);
    }

    [Fact]
    public async Task LogAsync_DispatchesToTheLevelDeclaredInTemplates()
    {
        var (service, log) = CreateService("ru");

        await service.LogAsync(LogEventKey.AppStarted);              // Info
        await service.LogAsync(LogEventKey.DriftDetected);           // Warning
        await service.LogAsync(LogEventKey.FirewallBlockFailed, "boom"); // Error

        Assert.Equal(LogLevel.Info, LogEventTemplates.LevelFor(LogEventKey.AppStarted));
        Assert.Equal(LogLevel.Warning, LogEventTemplates.LevelFor(LogEventKey.DriftDetected));
        Assert.Equal(LogLevel.Error, LogEventTemplates.LevelFor(LogEventKey.FirewallBlockFailed));

        Assert.Contains(log.InfoMessages, m => m == "Приложение запущено");
        Assert.Contains(log.WarningMessages, m => m.Contains("расхождение", StringComparison.Ordinal));
        Assert.Contains(log.ErrorMessages, m => m.Contains("boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LogAsync_FormatsTemplateArguments_AndLeavesArgumentlessTemplatesUntouched()
    {
        var (service, log) = CreateService("ru");

        await service.LogAsync(LogEventKey.AppStarted);
        await service.LogAsync(LogEventKey.FirewallBlockFailed, "доступ запрещён");

        Assert.Equal("Приложение запущено", log.InfoMessages[0]);
        Assert.Equal("Не удалось выполнить блокировку через брандмауэр: доступ запрещён", log.ErrorMessages[0]);
    }

    [Fact]
    public async Task LogAsync_FollowsTheLocalizationFallbackChain_ForUnknownRuntimeLanguages()
    {
        // LocalizedLogService doesn't own fallback logic — it must simply defer to
        // ILocalizationService.GetString, which resolves unknown language codes to
        // the ru fallback table (see LocalizationServiceTests.UnknownLanguageCode_*).
        // Constructing the service with "xx" proves LocalizedLogService doesn't
        // short-circuit or duplicate that chain.
        var (fallbackService, fallbackLog) = CreateService("xx-not-a-real-language");
        var (ruService, ruLog) = CreateService("ru");

        await fallbackService.LogAsync(LogEventKey.AppStarted);
        await ruService.LogAsync(LogEventKey.AppStarted);

        Assert.Equal(ruLog.InfoMessages[0], fallbackLog.InfoMessages[0]);
        Assert.Equal("Приложение запущено", fallbackLog.InfoMessages[0]);
    }
}
