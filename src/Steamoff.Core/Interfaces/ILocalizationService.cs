using Steamoff.Core.Models;

namespace Steamoff.Core.Interfaces;

/// <summary>
/// Central source of truth for "what language is the UI in right now, and what
/// does string X say in it". ViewModels, the tray, and dialogs all read through
/// this single service so that switching languages redraws everything at once —
/// no restart required (Constitution VI: calm, cohesive UI).
/// </summary>
public interface ILocalizationService
{
    IReadOnlyList<AppLanguage> AvailableLanguages { get; }

    /// <summary>The language currently driving every localized string.</summary>
    AppLanguage CurrentLanguage { get; }

    /// <summary>Raised after <see cref="SetLanguage"/> changes <see cref="CurrentLanguage"/> — bindings refresh from this.</summary>
    event EventHandler<AppLanguage>? LanguageChanged;

    /// <summary>Switches the active language immediately (in-memory only — callers persist to settings separately).</summary>
    void SetLanguage(string languageCode);

    /// <summary>Resolves a language by its persisted code, or null if unknown.</summary>
    AppLanguage? FindLanguage(string languageCode);

    /// <summary>
    /// Looks up <paramref name="key"/> in the current language; falls back to
    /// the fallback language (ru), then to the key itself, logging a missing
    /// translation in the latter case.
    /// </summary>
    string GetString(string key);

    /// <summary>Convenience formatting overload — equivalent to string.Format(GetString(key), args).</summary>
    string GetString(string key, params object[] args);
}
