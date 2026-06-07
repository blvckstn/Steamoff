# Contract: Settings View edit session (`SettingsEditSession` + `SettingsViewModel`)

Namespace: `Steamoff.Core.Models` (`SettingsEditSession`),
`Steamoff.App.ViewModels` (`SettingsViewModel`)

## `SettingsEditSession`
```csharp
public sealed class SettingsEditSession
{
    public AppSettings Original { get; private set; }
    public AppSettings Draft { get; private set; }

    public SettingsEditSession(AppSettings savedSettings);   // clones into both Original and Draft

    public bool HasChanges { get; }   // structural diff: Draft's JSON != Original's JSON

    public void CommitDraft();   // Original = Clone(Draft); Draft = Clone(Draft)
    public void DiscardDraft();  // Draft = Clone(Original)
}
```

## Invariants (enforced by the implementation, verified by tests)
1. **Never mutate the saved object directly**: the constructor clones
   `savedSettings` twice (`Original` and `Draft` are independent instances,
   and neither is the same reference as the input). All UI bindings target
   `Draft`; nothing the UI does can reach the object that's actually
   persisted until `CommitDraft` runs.
   (`SettingsEditSessionTests.Constructor_ClonesOriginalAndDraft_AsIndependentObjects`,
   `MutatingDraft_DoesNotAffectOriginal_OrTheSourceObject`)
2. **`HasChanges` is structural, not a dirty flag**: it re-serializes both
   `Draft` and `Original` (camelCase + string-enum, the same options
   `JsonSettingsService` persists with) and compares the JSON. Editing a
   field to a different value flips it to `true`; editing it back to the
   original value flips it back to `false` — there is no "sticky dirty" state.
   (`SettingsEditSessionTests.HasChanges_IsFalse_WhenDraftMatchesOriginal`,
   `HasChanges_BecomesTrue_AfterEditingDraft`)
3. **Commit promotes Draft to the new baseline**: after `CommitDraft`,
   `Original` reflects every pending edit, `HasChanges` is `false` again, and
   `Draft` is a *fresh* clone (not the same reference as the pre-commit
   `Draft`) so further edits can't retroactively alter what was just committed.
   (`SettingsEditSessionTests.CommitDraft_PromotesDraftToOriginal_AndClearsHasChanges`)
4. **Discard rolls back to the last committed baseline — including the
   language**: `DiscardDraft` always restores from the *current* `Original`
   (which may itself be the result of an earlier `CommitDraft` in the same
   session), not from whatever was on disk when the session was constructed.
   A previewed-but-uncommitted language switch is rolled back exactly like
   any other field. (`SettingsEditSessionTests.DiscardDraft_RestoresDraftFromOriginal_IncludingLanguageRollback`,
   `DiscardDraft_AfterCommit_RollsBackToTheCommittedBaseline_NotTheOriginalSavedValue`)
5. **User-added collections survive cloning untouched**: `AdditionalFolders`/
   `AdditionalExecutables` round-trip through the clone with their values and
   identities intact; mutating an item in `Draft`'s list does not affect the
   corresponding item in `Original`'s list (they're independent clones too).
   (`SettingsEditSessionTests.Draft_PreservesUserAddedFolders_AcrossClone`)

## `SettingsViewModel` — the five actions
| Action | Button (RU) | Behavior |
|---|---|---|
| Test | "Тестирование" | `RunTestAsync` — runs `IDiagnosticsService.RunAsync(_session.Draft)` against the **draft** (so users can test pending changes before committing them); updates `LastReport`, which raises `StatusSummaryText`/`LastRunText` |
| Status | "Статус" | Not a separate command — `StatusSummaryText`/`LastRunText` are always-live computed properties rendering the latest `DiagnosticsReport` in the active language |
| Apply | "Применить" | `CommitAsync(closeAfter: false)` — `_session.CommitDraft()`, persists `Original` to `settings.json` (+ autostart install/uninstall sync), raises `SettingsCommitted`, shows a toast, **window stays open** |
| Save | "Сохранить" | `CommitAsync(closeAfter: true)` — identical persistence, but also raises `CloseRequested` |
| Cancel | "Отмена" | `Cancel()` — `_session.DiscardDraft()`, **and explicitly calls `_services.Localization.SetLanguage(_languageOnEntry.Code)`** to roll back any live-previewed language switch that the session diff alone wouldn't undo (the diff only affects `Draft`/`Original`; the *live* `ILocalizationService.CurrentLanguage` was already changed by the preview and needs its own explicit rollback), shows a toast, raises `CloseRequested` |

## Language bar contract
- `Languages` = `services.Localization.AvailableLanguages` (all 9, in order).
- `SelectedLanguage` getter resolves from `_session.Draft.Language`; setter
  (a) writes `_session.Draft.Language = value.Code` (held in the draft only —
  *not* persisted), and (b) calls `_services.Localization.SetLanguage(value.Code)`
  for the live preview redraw. Both happen atomically in the same setter so
  the visible language and the pending draft never disagree.
- `_languageOnEntry` captures `services.Localization.CurrentLanguage` at
  construction time — this is what `Cancel()` restores to, *not* `Original.Language`
  (which would be the same value on a fresh session, but would be wrong after
  an `Apply` followed by further language previews and a `Cancel`).

## Instant redraw of computed status text (the audited gap)
`StatusSummaryText` and `LastRunText` wrap `Loc[...]` lookups. Because they
are C# computed properties (not direct XAML indexer bindings), the
`LocalizationProxy`'s `Item[]` refresh does **not** re-evaluate them. The
view model therefore:
```csharp
_services.Localization.LanguageChanged += OnLanguageChanged;
private void OnLanguageChanged(object? sender, AppLanguage language) {
    OnPropertyChanged(nameof(StatusSummaryText));
    OnPropertyChanged(nameof(LastRunText));
}
public void Dispose() => _services.Localization.LanguageChanged -= OnLanguageChanged;
```
`SettingsViewModel` implements `IDisposable`; `SettingsWindow` calls
`viewModel.Dispose()` in its `Closed` handler so the subscription doesn't
outlive the window. `CompactViewModel` follows the identical pattern for its
own five computed labels (`StatusText`, `ToggleButtonText`, `ModeText`,
`AdminStatusText`, `VersionText`) via `RaiseLanguageDependentChanges()`.
