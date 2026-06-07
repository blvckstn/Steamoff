using Steamoff.Core.Localization;

namespace Steamoff.Tests.Localization;

/// <summary>
/// Covers I1 from specs/004-steamoff-localized-logs-release-flow/tasks.md —
/// the 5 state-machine scenarios from contracts/language-restart.md, expressed
/// directly against the pure derivation (no ViewModel/session seam needed).
/// </summary>
public sealed class LanguageRestartStateTests
{
    [Fact]
    public void SettingsOpens_DraftEqualsRuntime_IsNotRequired()
    {
        // Row 1: Settings opens — Draft == persisted == Runtime (typical case).
        Assert.False(LanguageRestartState.IsRestartRequired(selectedLanguageCode: "ru", runtimeLanguageCode: "ru"));
    }

    [Fact]
    public void UserPicksDifferentLanguage_BecomesRequired()
    {
        // Row 2: user picks language X != Runtime — warning shown, Restart now enabled.
        Assert.True(LanguageRestartState.IsRestartRequired(selectedLanguageCode: "en", runtimeLanguageCode: "ru"));
    }

    [Fact]
    public void UserPicksRuntimeLanguageAgain_BecomesNotRequired()
    {
        // Row 3: user picks the original/runtime language again — warning hides.
        Assert.False(LanguageRestartState.IsRestartRequired(selectedLanguageCode: "ru", runtimeLanguageCode: "ru"));
    }

    [Fact]
    public void Cancel_RestoresOriginalDraft_RequiredOnlyIfOriginalDiffersFromRuntime()
    {
        // Row 6: Cancel restores Draft = clone(Original) — the last *persisted* value,
        // which can itself differ from Runtime if a previous Apply left it pending.
        Assert.True(LanguageRestartState.IsRestartRequired(selectedLanguageCode: "en", runtimeLanguageCode: "ru"));
        Assert.False(LanguageRestartState.IsRestartRequired(selectedLanguageCode: "ru", runtimeLanguageCode: "ru"));
    }

    [Fact]
    public void RestartNow_NewProcessRuntimeMatchesPersistedSelection_IsNotRequired()
    {
        // Row 7: after relaunch, the new process starts with CurrentLanguage == persisted Language.
        Assert.False(LanguageRestartState.IsRestartRequired(selectedLanguageCode: "en", runtimeLanguageCode: "en"));
    }

    [Theory]
    [InlineData("ru", "RU")]
    [InlineData("EN", "en")]
    [InlineData(null, null)]
    public void Comparison_IsOrdinalCaseInsensitive(string? selected, string? runtime)
    {
        Assert.False(LanguageRestartState.IsRestartRequired(selected, runtime));
    }
}
