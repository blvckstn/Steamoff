# Research & Decisions: Localization & Settings Experience

## R1 — Translation storage: embedded JSON vs. `.resx`/`.resw`
**Decision**: Flat `key → string` JSON files under
`Resources/Localization/{code}.json`, embedded as resources with logical
names `Steamoff.Core.Resources.Localization.{code}.json`, loaded via
`Assembly.GetManifestResourceStream` and cached per language.

**Why not `.resx`**: `.resx` requires a generated strongly-typed accessor
class per culture and ties lookups to `ResourceManager`/`CultureInfo`
satellite-assembly conventions — heavier to keep in lockstep across 9
languages, harder to hand-audit for "every key present in every language"
(the FR-111 requirement), and satellite assemblies complicate the
single-file self-contained publish (`PublishSingleFile`) feature 001 already
committed to.

**Why JSON wins here**: a flat dictionary is trivial to diff for key-set
parity (see `LocalizationServiceTests.EveryShippedLanguage_HasNonEmptyTable_WithSameKeySetAsRussian`),
trivial to hand-translate without tooling, embeds cleanly as a single-file
resource (no satellite assembly probing), and `System.Text.Json`
deserialization to `Dictionary<string,string>` is a one-liner. The
`LocalizedStringProvider` caches each table after first load, so the
per-lookup cost is a dictionary hit.

**Trade-off accepted**: no compile-time key checking (a typo'd key silently
falls through to the missing-key path). Mitigated by (a) the
`LocalizationService` logging every distinct miss exactly once, and (b) the
key-set-parity test catching translation drift across languages.

## R2 — Live redraw without restart: indexer-binding proxy
**Decision**: `LocalizationProxy : INotifyPropertyChanged` wraps
`ILocalizationService` and exposes `string this[string key] => _service.GetString(key)`.
On every `LanguageChanged`, it raises
`PropertyChanged(new PropertyChangedEventArgs(Binding.IndexerName))` — the
WPF-recognized `"Item[]"` name that tells every active indexer binding to
re-evaluate. XAML then binds with
`{Binding [key], Source={StaticResource Loc}}`, registered once as
`Resources["Loc"]` in `App.xaml.cs`.

**Why not `DynamicResource` + resource dictionaries per language**: WPF
resource dictionaries are static once merged; swapping them at runtime means
either reloading the whole `Application.Resources.MergedDictionaries` (heavy,
prone to flicker and leaked bindings) or manually walking the visual tree.
The indexer trick achieves the same "every binding refreshes" outcome with a
single, well-understood WPF mechanism (`Binding.IndexerName`) and zero extra
infrastructure.

**Trade-off accepted**: ViewModels that expose *computed* strings derived
from `Loc[...]` (e.g. `CompactViewModel.StatusText`,
`SettingsViewModel.StatusSummaryText`) are **not** automatically refreshed by
the proxy — only direct XAML indexer bindings are. Each such ViewModel must
explicitly subscribe to `ILocalizationService.LanguageChanged` and re-raise
`PropertyChanged` for its computed properties. This was missed for
`CompactViewModel`/`SettingsViewModel` during initial wiring and fixed before
sign-off (see [../../IMPLEMENTATION_LOG.md](../../IMPLEMENTATION_LOG.md)) —
documented here so the next localized ViewModel doesn't repeat the gap.

## R3 — Comparing two dynamically-bound `AppLanguage` values for card highlight
**Decision**: `LanguageEqualityConverter : IMultiValueConverter`, bound via
`<MultiBinding>` to `(card.Language, SelectedLanguage)`, returning
`string.Equals(a.Code, b.Code, OrdinalIgnoreCase)`.

**Why not `ConverterParameter`**: classic `IValueConverter.ConverterParameter`
must be a static value (or a `StaticResource`/literal) — it cannot itself be
a live binding, so comparing "this card's language" against "the currently
selected language" (which changes at runtime) is impossible with a
single-value converter and a parameter.

**Why not give `AppLanguage` `INotifyPropertyChanged`**: `AppLanguage` is an
immutable value-ish record (`required` init-only properties) shared as a
static catalogue (`LanguageManager.SupportedLanguages`); making it observable
would be a much larger change for a UI-only concern. `IMultiValueConverter`
solves it locally, in the view layer, with no model changes.

## R4 — Settings draft editing: clone-then-diff vs. command/undo stack
**Decision**: `SettingsEditSession` holds two independent deep clones —
`Original` and `Draft` — produced via a `System.Text.Json`
serialize/deserialize round trip (camelCase + string-enum converter, matching
`JsonSettingsService`'s own options). `HasChanges` compares the two
serialized forms; `CommitDraft` promotes `Draft` to the new `Original`;
`DiscardDraft` re-clones `Draft` from `Original`.

**Why not a command/undo stack**: Settings has ~15 independently-editable
fields plus two collections; a full undo stack would need a command object
per field type and complicate "Cancel = revert *everything* including the
language preview" into "pop every command". Clone-then-diff makes Cancel a
single, trivially-correct operation (`Draft = Clone(Original)`), and `HasChanges`
falls out of the same serialization the persistence layer already uses —
no drift between "what counts as a change" and "what gets saved".

**Trade-off accepted**: `HasChanges` re-serializes both objects on every
access rather than tracking dirtiness incrementally — acceptable because
`AppSettings` is small (~15 scalar fields + two short lists) and the property
is only read on UI-driven cadence (button `CanExecute`, not per-frame).

## R5 — Why the language fallback chain ends at Russian, not English
**Decision**: `LanguageManager.FallbackLanguageCode = "ru"`; the lookup chain
is *current → ru → raw key*.

**Why**: Russian is the project's primary, highest-quality-translated
language (per the feature brief — "RU primary high-quality, EN solid, others
basic-but-not-empty"), and the first-launch dialog itself defaults to Russian
on dismissal. Falling back to the language with the most complete, most
carefully reviewed strings minimizes the chance a user ever sees a raw,
untranslated key — English, while solid, is the *secondary* reference
language here, not the safety net.
