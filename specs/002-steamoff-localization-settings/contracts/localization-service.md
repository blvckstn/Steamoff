# Contract: `ILocalizationService`

Namespace: `Steamoff.Core.Interfaces`

```csharp
public interface ILocalizationService
{
    // All languages Steamoff ships with, in display order. Ukrainian is
    // intentionally absent.
    IReadOnlyList<AppLanguage> AvailableLanguages { get; }

    // The language currently driving every localized string.
    AppLanguage CurrentLanguage { get; }

    // Raised after SetLanguage changes CurrentLanguage — every binding,
    // ViewModel, dialog, and the tray refresh from this single event.
    event EventHandler<AppLanguage>? LanguageChanged;

    // Switches the active language immediately (in-memory only — callers
    // persist to settings.json separately, e.g. via SettingsEditSession).
    void SetLanguage(string languageCode);

    // Resolves a language by its persisted code, or null if unknown.
    AppLanguage? FindLanguage(string languageCode);

    // Looks up `key` in the current language; falls back to the fallback
    // language (ru), then to the key itself, logging a missing translation
    // (once per distinct key) in the latter case.
    string GetString(string key);

    // Convenience formatting overload — string.Format(GetString(key), args).
    string GetString(string key, params object[] args);
}
```

## Invariants (enforced by the implementation, verified by tests)
1. **Fixed catalogue, no Ukrainian**: `AvailableLanguages` always returns
   `LanguageManager.SupportedLanguages` — exactly the 9 languages ru, en, de,
   fr, es, it, pt, pl, zh, in that order, and never includes `"uk"`.
   (`LocalizationServiceTests.SupportedLanguages_DoNotIncludeUkrainian`,
   `...IncludeAtLeastTheNineRequiredCodes`)
2. **English display code**: the `en` entry's `DisplayCode` is exactly `"EN"`,
   never `"GB"`. (`LocalizationServiceTests.English_DisplayCode_IsEN_NeverGB`)
3. **Fallback = Russian**: `LanguageManager.FallbackLanguageCode == "ru"` and
   `LanguageManager.Fallback.Code == "ru"`. An unresolvable initial code at
   construction resolves to this fallback, never to `en` or any other
   language. (`LocalizationServiceTests.Fallback_IsRussian`,
   `UnknownLanguageCode_ResolvesToFallback_AndServesFallbackTranslations`)
4. **Lookup chain**: `GetString(key)` checks the current language's table,
   then — only if the current language isn't already `ru` — the `ru` table,
   then returns `key` itself. Each distinct missing key is logged via
   `ILogService.Warning` exactly once per service instance, never on repeat
   lookups. (`LocalizationServiceTests.GetString_ReturnsKeyItself_AndLogsMissingTranslation_WhenKeyIsUnknownEverywhere`,
   `...LogsEachMissingKeyOnlyOnce`)
5. **Key-set parity**: every language's embedded translation table contains
   exactly the same set of keys as the `ru` table — no language ships with
   missing or extra keys. (`LocalizationServiceTests.EveryShippedLanguage_HasNonEmptyTable_WithSameKeySetAsRussian`)
6. **Change notification**: `SetLanguage` raises `LanguageChanged` exactly
   once per actual switch, never for a no-op switch (unknown code, or the
   code that's already current). (`LocalizationServiceTests.SetLanguage_UpdatesCurrentLanguage_AndRaisesLanguageChanged`,
   `...ToSameLanguage_DoesNotRaiseLanguageChanged`, `...ToUnknownCode_IsIgnored`)

## WPF binding bridge — `LocalizationProxy` (Steamoff.App.Localization)
Not part of `Steamoff.Core` (it depends on `System.Windows.Data.Binding`),
but every window/dialog registers one as a local `Loc` resource, and
`App.xaml.cs` registers one app-wide:

```csharp
public sealed class LocalizationProxy : INotifyPropertyChanged
{
    public string this[string key] => service.GetString(key);
    public string GetFormatted(string key, params object[] args) => service.GetString(key, args);
    // Raises PropertyChanged(Binding.IndexerName) — "Item[]" — on every
    // LanguageChanged, so {Binding [key], Source={StaticResource Loc}}
    // bindings re-evaluate immediately.
}
```

**Contract for consumers**: a XAML binding through `Loc[...]` always reflects
the current language with no extra wiring. A *computed C# property* that
wraps `Loc[...]` (e.g. `CompactViewModel.StatusText`) does **not** get this
for free — its owning ViewModel MUST subscribe to `LanguageChanged` and
re-raise `PropertyChanged` for that property itself. See
[../research.md](../research.md) R2 for the rationale and
[../../../IMPLEMENTATION_LOG.md](../../../IMPLEMENTATION_LOG.md) for the two
real instances (`CompactViewModel`, `SettingsViewModel`) where this was
initially missed and then fixed.

## First-launch contract
- While `AppSettings.IsFirstLaunchCompleted == false`, `App.xaml.cs` shows
  `LanguageSelectionWindow` (backed by `LanguageSelectionViewModel`) *before*
  the main window.
- Selecting a card calls `ILocalizationService.SetLanguage` immediately
  (live preview — the dialog's own strings redraw in place).
- Confirming raises `Confirmed(AppLanguage)`; the host persists
  `settings.Language = result.Code` and `settings.IsFirstLaunchCompleted = true`.
- Closing the window without confirming yields `Result == LanguageManager.Fallback`
  (`"ru"`) — the host persists that and still sets `IsFirstLaunchCompleted = true`,
  so the dialog never reappears.
