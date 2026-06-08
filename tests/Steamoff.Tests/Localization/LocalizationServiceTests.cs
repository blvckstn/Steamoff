using Steamoff.Core.Localization;
using Steamoff.Tests.TestSupport;

namespace Steamoff.Tests.Localization;

public sealed class LocalizationServiceTests
{
    private static LocalizationService CreateService(FakeLogService log, string? initialLanguage = null) =>
        new(new LanguageManager(initialLanguage), new LocalizedStringProvider(), log);

    [Fact]
    public void SupportedLanguages_DoNotIncludeUkrainian()
    {
        Assert.DoesNotContain(LanguageManager.SupportedLanguages, l => l.Code.Equals("uk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SupportedLanguages_IncludeAtLeastTheNineRequiredCodes()
    {
        var codes = LanguageManager.SupportedLanguages.Select(l => l.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expected in new[] { "ru", "en", "de", "fr", "es", "it", "pt", "pl", "zh" })
        {
            Assert.Contains(expected, codes);
        }
    }

    [Fact]
    public void English_DisplayCode_IsEN_NeverGB()
    {
        var english = LanguageManager.SupportedLanguages.Single(l => l.Code == "en");

        Assert.Equal("EN", english.DisplayCode);
        Assert.NotEqual("GB", english.DisplayCode);
    }

    [Fact]
    public void Fallback_IsRussian()
    {
        Assert.Equal("ru", LanguageManager.Fallback.Code);
        Assert.Equal(LanguageManager.FallbackLanguageCode, LanguageManager.Fallback.Code);
    }

    [Fact]
    public void EveryShippedLanguage_HasNonEmptyTable_WithSameKeySetAsRussian()
    {
        var provider = new LocalizedStringProvider();
        var ruKeys = provider.GetTable("ru").Keys.ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(ruKeys);

        foreach (var language in LanguageManager.SupportedLanguages)
        {
            var table = provider.GetTable(language.Code);
            Assert.True(table.Count > 0, $"Language '{language.Code}' has no embedded translation table.");
            Assert.True(ruKeys.SetEquals(table.Keys), $"Language '{language.Code}' has a different key set than ru (missing or extra keys).");
        }
    }

    [Fact]
    public void GetString_ReturnsTranslation_FromCurrentLanguage()
    {
        var log = new FakeLogService();
        var service = CreateService(log, "ru");

        var value = service.GetString("app.title");

        Assert.False(string.IsNullOrWhiteSpace(value));
        Assert.NotEqual("app.title", value);
    }

    [Fact]
    public void UnknownLanguageCode_ResolvesToFallback_AndServesFallbackTranslations()
    {
        var ruService = CreateService(new FakeLogService(), "ru");
        var ruValue = ruService.GetString("app.title");

        // An unrecognized code resolves to the fallback (ru) at construction time,
        // so this proves the "fallback = ru" guarantee end to end.
        var unknownLanguageService = CreateService(new FakeLogService(), "xx");

        Assert.Equal(LanguageManager.Fallback.Code, unknownLanguageService.CurrentLanguage.Code);
        Assert.Equal(ruValue, unknownLanguageService.GetString("app.title"));
    }

    [Fact]
    public void GetString_ReturnsKeyItself_AndLogsMissingTranslation_WhenKeyIsUnknownEverywhere()
    {
        var log = new FakeLogService();
        var service = CreateService(log, "ru");

        const string missingKey = "this.key.does.not.exist.anywhere";
        var value = service.GetString(missingKey);

        Assert.Equal(missingKey, value);
        Assert.Contains(log.WarningMessages, m => m.Contains(missingKey, StringComparison.Ordinal));
    }

    [Fact]
    public void GetString_LogsEachMissingKeyOnlyOnce()
    {
        var log = new FakeLogService();
        var service = CreateService(log, "ru");

        const string missingKey = "another.missing.key";
        service.GetString(missingKey);
        service.GetString(missingKey);
        service.GetString(missingKey);

        Assert.Single(log.WarningMessages, m => m.Contains(missingKey, StringComparison.Ordinal));
    }

    [Fact]
    public void SetLanguage_UpdatesCurrentLanguage_AndRaisesLanguageChanged()
    {
        var log = new FakeLogService();
        var service = CreateService(log, "ru");

        AppLanguageChangedRecorder recorder = new();
        service.LanguageChanged += recorder.Handle;

        service.SetLanguage("en");

        Assert.Equal("en", service.CurrentLanguage.Code);
        Assert.Equal("en", recorder.LastLanguage?.Code);
        Assert.Equal(1, recorder.RaiseCount);
    }

    [Fact]
    public void SetLanguage_ToSameLanguage_DoesNotRaiseLanguageChanged()
    {
        var log = new FakeLogService();
        var service = CreateService(log, "ru");

        AppLanguageChangedRecorder recorder = new();
        service.LanguageChanged += recorder.Handle;

        service.SetLanguage("ru");

        Assert.Equal(0, recorder.RaiseCount);
    }

    [Fact]
    public void SetLanguage_ToUnknownCode_IsIgnored()
    {
        var log = new FakeLogService();
        var service = CreateService(log, "ru");

        service.SetLanguage("xx-not-a-real-language");

        Assert.Equal("ru", service.CurrentLanguage.Code);
    }

    private sealed class AppLanguageChangedRecorder
    {
        public int RaiseCount { get; private set; }
        public Steamoff.Core.Models.AppLanguage? LastLanguage { get; private set; }

        public void Handle(object? sender, Steamoff.Core.Models.AppLanguage language)
        {
            RaiseCount++;
            LastLanguage = language;
        }
    }
}
