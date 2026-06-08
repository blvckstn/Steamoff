namespace Steamoff.Core.Localization;

/// <summary>
/// Pure derivation of "does the app need a restart to fully apply a language
/// change" — see specs/004-steamoff-localized-logs-release-flow/contracts/language-restart.md.
/// Deliberately stateless: callers compare the persisted/draft selection
/// against the language the process actually started in, instead of tracking
/// a separate mutable flag that could drift out of sync.
/// </summary>
public static class LanguageRestartState
{
    public static bool IsRestartRequired(string? selectedLanguageCode, string? runtimeLanguageCode)
        => !string.Equals(selectedLanguageCode, runtimeLanguageCode, StringComparison.OrdinalIgnoreCase);
}
