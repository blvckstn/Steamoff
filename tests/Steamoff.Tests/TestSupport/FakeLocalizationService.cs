using Steamoff.Core.Interfaces;
using Steamoff.Core.Localization;
using Steamoff.Core.Models;

namespace Steamoff.Tests.TestSupport;

/// <summary>
/// Minimal <see cref="ILocalizationService"/> double for ViewModel tests: returns
/// the key prefixed with the current language code (so assertions can prove a
/// refresh actually happened) and raises <see cref="LanguageChanged"/> on switch,
/// exactly like the real service.
/// </summary>
public sealed class FakeLocalizationService : ILocalizationService
{
    private AppLanguage _current = LanguageManager.Fallback;

    public IReadOnlyList<AppLanguage> AvailableLanguages => LanguageManager.SupportedLanguages;

    public AppLanguage CurrentLanguage => _current;

    public event EventHandler<AppLanguage>? LanguageChanged;

    public int SetLanguageCallCount { get; private set; }

    public void SetLanguage(string languageCode)
    {
        SetLanguageCallCount++;
        var resolved = FindLanguage(languageCode);
        if (resolved is null || resolved.Code == _current.Code)
        {
            return;
        }

        _current = resolved;
        LanguageChanged?.Invoke(this, _current);
    }

    public AppLanguage? FindLanguage(string languageCode) =>
        AvailableLanguages.FirstOrDefault(l => string.Equals(l.Code, languageCode, StringComparison.OrdinalIgnoreCase));

    public string GetString(string key) => $"[{_current.Code}]{key}";

    public string GetString(string key, params object[] args) => string.Format(GetString(key), args);
}
