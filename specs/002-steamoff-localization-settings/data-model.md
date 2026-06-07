# Data Model: Localization & Settings Experience

## AppLanguage
*One supported interface language — immutable, used both for persistence and
for rendering language picker cards.*

| Field | Type | Notes |
|---|---|---|
| `Code` | `string` (required) | Stable persistence/lookup key, e.g. `"ru"`, `"en"`, `"zh"` — lowercase ISO-639-1. Stored verbatim in `AppSettings.Language`. |
| `DisplayCode` | `string` (required) | Short code shown in compact UI, e.g. `"RU"`, `"EN"` (always `"EN"`, never `"GB"`), `"ZH"`. |
| `NativeName` | `string` (required) | The language's name in itself, e.g. `"Русский"`, `"English"`, `"中文"`. |
| `FlagEmoji` | `string` (required) | Flag glyph for picker cards, e.g. `"🇷🇺"`, `"🇺🇸"`, `"🇨🇳"`. |

`ToString()` → `"{FlagEmoji} {DisplayCode} {NativeName}"` (debug/log convenience).

**Catalogue** (`LanguageManager.SupportedLanguages`, fixed, in display order):
ru, en, de, fr, es, it, pt, pl, zh — **deliberately excludes Ukrainian** (see
[../../ASSUMPTIONS.md](../../ASSUMPTIONS.md)). `Fallback = SupportedLanguages["ru"]`.

## LanguageManager
*Owns "which language is active right now" and the fixed catalogue.*

- `Current : AppLanguage` — mutable, starts at `Resolve(initialLanguageCode) ?? Fallback`.
- `Resolve(code) : AppLanguage?` — case-insensitive catalogue lookup, `null` if unknown.
- `SetLanguage(code)` — no-op if unknown or already current; otherwise updates
  `Current` and raises `LanguageChanged`.
- `event LanguageChanged : EventHandler<AppLanguage>`

## LocalizedStringProvider
*Loads and caches per-language `key → string` translation tables from
embedded JSON resources.*

- `GetTable(languageCode) : IReadOnlyDictionary<string,string>` — cached;
  returns an empty table (never `null`, never throws) if the embedded
  resource `Steamoff.Core.Resources.Localization.{code}.json` is missing or
  fails to parse.

## ILocalizationService / LocalizationService
*The single facade every ViewModel, dialog, and the tray talk to.*

| Member | Purpose |
|---|---|
| `AvailableLanguages : IReadOnlyList<AppLanguage>` | = `LanguageManager.SupportedLanguages` |
| `CurrentLanguage : AppLanguage` | = `LanguageManager.Current` |
| `event LanguageChanged : EventHandler<AppLanguage>` | re-broadcasts `LanguageManager.LanguageChanged` |
| `SetLanguage(code)` | switches in-memory only — callers persist separately |
| `FindLanguage(code) : AppLanguage?` | = `LanguageManager.Resolve` |
| `GetString(key) : string` | lookup chain: current table → ru table (if current ≠ ru) → the raw key itself, logging each distinct miss exactly once via `ILogService.Warning` |
| `GetString(key, params object[] args) : string` | `string.Format(GetString(key), args)` |

## LocalizationProxy *(Steamoff.App.Localization)*
*WPF binding bridge — not part of Core, lives next to the views that consume it.*

- `string this[string key] => service.GetString(key)`
- `GetFormatted(key, params args) => service.GetString(key, args)`
- Implements `INotifyPropertyChanged`; on `LanguageChanged`, raises
  `PropertyChanged("Item[]")` (`Binding.IndexerName`) so every
  `{Binding [key], Source={StaticResource Loc}}` binding re-evaluates.
- Registered once per window/dialog as `Loc`, and app-wide as
  `Application.Resources["Loc"]`.

## AppSettings — additions
*(full model lives in feature 001's data-model; only the new fields are shown)*

| Field | Type | Default | Notes |
|---|---|---|---|
| `Language` | `string` | `"ru"` | ISO-639-1 code of the active interface language; persisted, restored on launch before the first frame renders. |
| `IsFirstLaunchCompleted` | `bool` | `false` | Gates the first-launch language dialog; set to `true` the moment that dialog closes (confirmed *or* dismissed). |

`AppSettings.CurrentVersion` bumped from `1` to `2`. `JsonSettingsService.MigrateIfNeeded`
bumps `Version` and relies on `System.Text.Json` to fill the two new fields
with the model's defaults for pre-existing files — no hand-written migration
of values, and `AdditionalFolders`/`AdditionalExecutables` survive untouched.

## SettingsEditSession
*Backs the Settings View's Apply/Save/Cancel flow — never lets the UI mutate
the saved `AppSettings` instance directly.*

| Member | Type | Notes |
|---|---|---|
| `Original` | `AppSettings` | Last-committed snapshot (deep clone of what's on disk when the session opened, or of the most recent `CommitDraft`). |
| `Draft` | `AppSettings` | The clone the UI binds to and mutates freely. |
| `HasChanges` | `bool` | `true` iff `Draft`'s camelCase JSON serialization differs from `Original`'s — a structural diff, not a dirty-flag. |
| `CommitDraft()` | — | `Original = Clone(Draft); Draft = Clone(Draft)` — promotes the draft to the new baseline (used by both Apply and Save). |
| `DiscardDraft()` | — | `Draft = Clone(Original)` — reverts every pending edit, including a previewed language switch, in one step. |

Cloning uses the same `System.Text.Json` options
(`PropertyNamingPolicy.CamelCase` + `JsonStringEnumConverter`) as
`JsonSettingsService`'s persistence path, so "what counts as a change" can
never drift from "what gets written to disk".

## Translation key namespaces (excerpt — full set in `Resources/Localization/*.json`)
| Prefix | Covers |
|---|---|
| `app.*` | App title and global chrome strings |
| `language.dialog.*` | First-launch picker title/labels/confirm button |
| `compact.*` | Compact Switch View — status text, toggle button, mode/admin/version labels |
| `settings.*` | Settings View — section headers, field labels, toasts, status/diagnostics summaries |
| `tray.*` | Tray context menu items |
| `status.*` | Shared status vocabulary (blocked/unblocked/read-only/...) used by both the compact view and the tray tooltip |
| `notification.*` | Balloon notification titles/bodies |

Every key listed for `ru` (the reference table) MUST exist in all 9 language
tables — enforced by `LocalizationServiceTests.EveryShippedLanguage_HasNonEmptyTable_WithSameKeySetAsRussian`.
