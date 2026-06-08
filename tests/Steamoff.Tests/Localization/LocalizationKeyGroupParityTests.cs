using Steamoff.Core.Localization;
using Steamoff.Core.Logging;

namespace Steamoff.Tests.Localization;

/// <summary>
/// checks for the three new key groups this feature introduced
/// (<c>log.event.*</c>, <c>diagnostics.*</c>, <c>settings.journal.*</c>), on top
/// of the existing whole-table parity coverage in
/// <see cref="LocalizationServiceTests.EveryShippedLanguage_HasNonEmptyTable_WithSameKeySetAsRussian"/>
/// (which already guarantees every shipped language has the exact same key set
/// as ru — these tests add group-level intent checks that survive even if that
/// broader assertion were ever loosened).
/// </summary>
public sealed class LocalizationKeyGroupParityTests
{
    private static readonly string[] LanguageCodes = { "ru", "en", "de", "fr", "es", "it", "pt", "pl", "zh" };

    [Fact]
    public void EveryLogEventKey_HasATemplateThatResolvesInEveryShippedLanguage()
    {
        var provider = new LocalizedStringProvider();

        foreach (var key in Enum.GetValues<LogEventKey>())
        {
            var localizationKey = LogEventTemplates.LocalizationKeyFor(key);

            foreach (var code in LanguageCodes)
            {
                var table = provider.GetTable(code);
                Assert.True(table.ContainsKey(localizationKey),
                    $"Language '{code}' is missing '{localizationKey}' for LogEventKey.{key}.");
                Assert.False(string.IsNullOrWhiteSpace(table[localizationKey]),
                    $"Language '{code}' has an empty translation for '{localizationKey}'.");
            }
        }
    }

    [Theory]
    [InlineData("diagnostics.report.title")]
    [InlineData("diagnostics.report.logTail")]
    [InlineData("diagnostics.languagePendingRestart")]
    [InlineData("diagnostics.field.appVersion")]
    [InlineData("diagnostics.field.lastTestResult")]
    [InlineData("diagnostics.field.lastReleaseBuildPath")]
    [InlineData("diagnostics.field.notAvailable")]
    [InlineData("diagnostics.outcome.success")]
    [InlineData("diagnostics.outcome.warning")]
    [InlineData("diagnostics.outcome.error")]
    public void DiagnosticsReportKeys_ResolveInEveryShippedLanguage(string key)
    {
        var provider = new LocalizedStringProvider();

        foreach (var code in LanguageCodes)
        {
            var table = provider.GetTable(code);
            Assert.True(table.ContainsKey(key), $"Language '{code}' is missing '{key}'.");
            Assert.False(string.IsNullOrWhiteSpace(table[key]), $"Language '{code}' has an empty translation for '{key}'.");
        }
    }

    [Theory]
    [InlineData("settings.journal.title")]
    [InlineData("settings.journal.filter.all")]
    [InlineData("settings.journal.filter.errors")]
    [InlineData("settings.journal.filter.warnings")]
    [InlineData("settings.journal.filter.info")]
    [InlineData("settings.journal.refresh")]
    [InlineData("settings.journal.openFolder")]
    [InlineData("settings.journal.copyDiagnostics")]
    [InlineData("settings.journal.clearDisplay")]
    [InlineData("settings.journal.empty")]
    [InlineData("settings.journal.cleared")]
    public void SettingsJournalKeys_ResolveInEveryShippedLanguage(string key)
    {
        var provider = new LocalizedStringProvider();

        foreach (var code in LanguageCodes)
        {
            var table = provider.GetTable(code);
            Assert.True(table.ContainsKey(key), $"Language '{code}' is missing '{key}'.");
            Assert.False(string.IsNullOrWhiteSpace(table[key]), $"Language '{code}' has an empty translation for '{key}'.");
        }
    }

    [Fact]
    public void DiagnosticsReportLogTail_FormatsTheLineCountArgument_InEveryShippedLanguage()
    {
        var provider = new LocalizedStringProvider();

        foreach (var code in LanguageCodes)
        {
            var template = provider.GetTable(code)["diagnostics.report.logTail"];
            var formatted = string.Format(template, 200);

            Assert.Contains("200", formatted);
            Assert.NotEqual(template, formatted);
        }
    }
}
