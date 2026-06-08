using Steamoff.Core.Models;

namespace Steamoff.Core.Localization;

/// <summary>
/// Owns the fixed catalogue of supported languages (deliberately excluding
/// raising <see cref="LanguageChanged"/> whenever it switches.
/// </summary>
public sealed class LanguageManager
{
    public const string FallbackLanguageCode = "ru";

    public static IReadOnlyList<AppLanguage> SupportedLanguages { get; } = new List<AppLanguage>
    {
        new() { Code = "ru", DisplayCode = "RU", NativeName = "Русский", FlagEmoji = "🇷🇺" },
        new() { Code = "en", DisplayCode = "EN", NativeName = "English", FlagEmoji = "🇺🇸" },
        new() { Code = "de", DisplayCode = "DE", NativeName = "Deutsch", FlagEmoji = "🇩🇪" },
        new() { Code = "fr", DisplayCode = "FR", NativeName = "Français", FlagEmoji = "🇫🇷" },
        new() { Code = "es", DisplayCode = "ES", NativeName = "Español", FlagEmoji = "🇪🇸" },
        new() { Code = "it", DisplayCode = "IT", NativeName = "Italiano", FlagEmoji = "🇮🇹" },
        new() { Code = "pt", DisplayCode = "PT", NativeName = "Português", FlagEmoji = "🇵🇹" },
        new() { Code = "pl", DisplayCode = "PL", NativeName = "Polski", FlagEmoji = "🇵🇱" },
        new() { Code = "zh", DisplayCode = "ZH", NativeName = "中文", FlagEmoji = "🇨🇳" },
    }.AsReadOnly();

    public static AppLanguage Fallback { get; } = SupportedLanguages.First(l => l.Code == FallbackLanguageCode);

    private AppLanguage _current;

    public LanguageManager(string? initialLanguageCode = null)
    {
        _current = Resolve(initialLanguageCode) ?? Fallback;
    }

    public AppLanguage Current => _current;

    public event EventHandler<AppLanguage>? LanguageChanged;

    public AppLanguage? Resolve(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return null;
        }

        return SupportedLanguages.FirstOrDefault(l => string.Equals(l.Code, languageCode, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Switches the active language. No-op if the code is unknown or already active.</summary>
    public void SetLanguage(string languageCode)
    {
        var resolved = Resolve(languageCode);
        if (resolved is null || resolved.Code == _current.Code)
        {
            return;
        }

        _current = resolved;
        LanguageChanged?.Invoke(this, _current);
    }
}
