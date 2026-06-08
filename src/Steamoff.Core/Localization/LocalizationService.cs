using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;

namespace Steamoff.Core.Localization;

/// <summary>
/// Facade combining <see cref="LanguageManager"/> (which language is active)
/// with <see cref="LocalizedStringProvider"/> (what its strings say). This is
/// the only type ViewModels, dialogs, and the tray talk to for translated text.
/// Lookup order: current language → fallback (ru) → the key itself (logged).
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private readonly LanguageManager _languageManager;
    private readonly LocalizedStringProvider _stringProvider;
    private readonly ILogService? _log;
    private readonly HashSet<string> _loggedMissingKeys = new(StringComparer.Ordinal);

    public LocalizationService(LanguageManager languageManager, LocalizedStringProvider stringProvider, ILogService? log = null)
    {
        _languageManager = languageManager;
        _stringProvider = stringProvider;
        _log = log;
        _languageManager.LanguageChanged += (_, language) => LanguageChanged?.Invoke(this, language);
    }

    public IReadOnlyList<AppLanguage> AvailableLanguages => LanguageManager.SupportedLanguages;

    public AppLanguage CurrentLanguage => _languageManager.Current;

    public event EventHandler<AppLanguage>? LanguageChanged;

    public void SetLanguage(string languageCode) => _languageManager.SetLanguage(languageCode);

    public AppLanguage? FindLanguage(string languageCode) => _languageManager.Resolve(languageCode);

    public string GetString(string key)
    {
        var currentTable = _stringProvider.GetTable(_languageManager.Current.Code);
        if (currentTable.TryGetValue(key, out var value))
        {
            return value;
        }

        if (_languageManager.Current.Code != LanguageManager.FallbackLanguageCode)
        {
            var fallbackTable = _stringProvider.GetTable(LanguageManager.FallbackLanguageCode);
            if (fallbackTable.TryGetValue(key, out var fallbackValue))
            {
                return fallbackValue;
            }
        }

        if (_loggedMissingKeys.Add(key))
        {
            _log?.Warning($"Отсутствует перевод для ключа локализации: '{key}' (язык: {_languageManager.Current.Code}).");
        }

        return key;
    }

    public string GetString(string key, params object[] args) => string.Format(GetString(key), args);
}
