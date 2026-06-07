# Feature Specification: Steamoff — Localization & Settings Experience

**Feature branch**: `002-steamoff-localization-settings`
**Status**: Draft → Implemented (v0.1.0)
**Input**: Add a multi-language interface (RU primary, 8 more languages, no
Ukrainian) with a first-launch language picker, and split the UI into a
Compact Steam Switch View plus a full Settings View with a topmost language
bar and an Apply/Save/Cancel/Test/Status workflow built on a draft-clone edit
session.

## User Scenarios & Testing

### Primary user story
A user launches Steamoff for the first time and is greeted — in their own
language where possible — by a small, styled "Ваш язык / Your language"
dialog showing every supported language as a flag + code + native-name card.
They pick one (or close the dialog, which silently defaults to Russian), land
on the small always-on-top Compact Switch View, and can open Settings at any
time. In Settings, the language bar sits at the very top: clicking another
language redraws the entire window instantly, before anything is saved. The
user tweaks a few options, hits "Тестирование" to run diagnostics, sees
"Статус" reflect the last run, then either "Применить" (stay open, keep
editing), "Сохранить" (persist and return to the switch view), or "Отмена"
(roll back every pending change — including the language they were just
previewing).

### Acceptance Scenarios
1. **Given** a fresh install (`isFirstLaunchCompleted = false`), **When** the
   app starts, **Then** a custom neumorphic "Your language" dialog appears
   before the main window, offering all 9 supported languages as cards.
2. **Given** the first-launch dialog is open, **When** the user clicks a
   language card, **Then** the dialog's own strings switch live to that
   language immediately (no separate "preview" step), and the card is
   highlighted in orange.
3. **Given** the first-launch dialog, **When** the user confirms a choice (or
   closes the window without confirming), **Then** `settings.language` is set
   to the chosen code (or `"ru"` on dismissal), `isFirstLaunchCompleted`
   becomes `true`, and the dialog never reappears on subsequent launches.
4. **Given** the user opens Settings, **When** they click a different language
   card in the topmost language bar, **Then** every visible string — section
   headers, field labels, button captions, status text, the language bar
   itself — redraws instantly in the new language, while the change is held
   only in the pending draft (not yet persisted).
5. **Given** pending changes exist (including a previewed language switch),
   **When** the user clicks "Отмена", **Then** every field — and the active
   interface language — reverts to the values that were active when Settings
   was opened, and the window closes.
6. **Given** pending changes exist, **When** the user clicks "Применить",
   **Then** the draft is persisted to `settings.json`, the Compact view and
   tray refresh to match, a confirmation toast appears, and the Settings
   window stays open for further edits.
7. **Given** pending changes exist, **When** the user clicks "Сохранить",
   **Then** the same persistence happens as "Применить" and the window closes,
   returning to the Compact view.
8. **Given** the user clicks "Тестирование", **When** diagnostics finish,
   **Then** "Статус" reflects the latest `DiagnosticsReport` (ok / warning /
   error + timestamp) in the active language, and re-running diagnostics in a
   different language re-renders the same report correctly.
9. **Given** any supported language is active, **When** the tray icon, its
   tooltip, its context menu, or a balloon notification is shown, **Then**
   every string in it matches the active language and updates immediately on
   switch — no restart required.

### Edge Cases
- User dismisses the first-launch dialog via the window's close button (✕)
  instead of confirming → treated as an implicit choice of the fallback
  language (Russian); `isFirstLaunchCompleted` is still set so the dialog
  does not loop.
- A translation key is missing from the active language's table → the English
  fallback chain resolves to Russian, then to the raw key itself, and the miss
  is logged exactly once per key (no log spam on repeated lookups).
- User previews three different languages in a row inside Settings, then hits
  Cancel → the interface returns to the language that was active on entry, not
  the last-previewed one or the first one.
- `settings.json` predates the `language`/`isFirstLaunchCompleted` fields
  (schema v1) → migration fills them with the model defaults (`"ru"` /
  `false`), which makes the first-launch dialog appear once more — the
  intended behavior for upgraders, not a bug.
- A language's embedded JSON resource is missing or fails to parse at runtime
  → `LocalizedStringProvider` returns an empty table for it, and every lookup
  falls through the same fallback chain as a single missing key.

## Requirements

### Functional Requirements
- **FR-101**: System MUST ship with at least 9 interface languages — Russian,
  English, German, French, Spanish, Italian, Portuguese, Polish, Chinese —
  and MUST NOT include Ukrainian.
- **FR-102**: System MUST represent each language as an `AppLanguage` with a
  stable `Code` (persistence/lookup key), a `DisplayCode` shown in compact UI
  (English is always `"EN"`, never `"GB"`), a `NativeName`, and a `FlagEmoji`.
- **FR-103**: System MUST persist the active language (`AppSettings.Language`)
  and the first-launch flag (`AppSettings.IsFirstLaunchCompleted`) in
  `settings.json`, surviving restarts and migrating cleanly from older schemas.
- **FR-104**: System MUST show a custom-styled (never `MessageBox`) "Your
  language" dialog on the very first launch, offering every supported language
  as a flag+code+name card with an orange "selected" highlight, and MUST
  default to Russian + mark first-launch complete if the dialog is dismissed
  without an explicit confirmation.
- **FR-105**: System MUST resolve every displayed string through a single
  `ILocalizationService`, with lookup order *current language → fallback
  (`ru`) → the raw key itself*, logging each distinct missing key exactly once.
- **FR-106**: System MUST raise `ILocalizationService.LanguageChanged` on every
  switch, and MUST wire every window, dialog, ViewModel, the tray icon
  (tooltip + context menu + balloons), and notifications to refresh from it
  immediately — no application restart, no stale cached strings.
- **FR-107**: System MUST present two views: a small always-available
  **Compact Steam Switch View** (status, big block/unblock toggle, mode,
  admin indicator, settings entry point) and a full **Settings View**.
- **FR-108**: The Settings View MUST place a horizontal, scrollable language
  picker at the very top, above every other section; selecting a card switches
  the interface language live and stores the choice in the pending draft only.
- **FR-109**: The Settings View MUST edit a cloned `SettingsEditSession.Draft`
  — never the saved `AppSettings` instance directly — exposing `Original`,
  `Draft`, and a computed `HasChanges` flag derived from a structural diff.
- **FR-110**: The Settings View MUST provide five actions: "Тестирование"
  (run diagnostics against the draft), "Статус" (render the latest
  `DiagnosticsReport`), "Применить" (persist + stay open), "Сохранить"
  (persist + close), and "Отмена" (discard the entire draft, including any
  previewed language switch, and close).
- **FR-111**: System MUST localize, at minimum, every screen name, menu item,
  status string, dialog, tray string, and button caption — Russian at
  high quality (primary language), English solid, the remaining seven at a
  basic-but-complete level (no empty/missing keys, every key present in every
  language's table).
- **FR-112**: All localization-aware surfaces MUST remain consistent with the
  existing dark-neumorphic, orange-accent UI Kit — rounded corners, soft
  shadows, custom window chrome, resizable/adaptive layouts, no stock
  `MessageBox`.

### Key Entities
`AppLanguage`, `LanguageManager`, `LocalizationService`,
`LocalizedStringProvider`, `LocalizationProxy`, `SettingsEditSession`,
`AppSettings.Language` / `AppSettings.IsFirstLaunchCompleted` — see
[data-model.md](data-model.md).

## Review & Acceptance Checklist
- [x] No cloud APIs, no telemetry — all 9 language tables ship as embedded
      resources inside the single-file EXE (Constitution I)
- [x] Instant, restart-free redraw of every surface on language switch
      (Constitution VI, FR-106)
- [x] Custom dialog system used for the first-launch picker — no `MessageBox`
      (Constitution VI, FR-104/112)
- [x] Settings edits never mutate the saved object directly — clone-based
      `SettingsEditSession` with explicit Apply/Save/Cancel semantics (FR-109/110)
- [x] Unit-testable: `LocalizationService`, `SettingsEditSession`,
      `LocalizationProxy`, `LanguageSelectionViewModel`, settings persistence
      and migration all covered by fakes/temp-directory seams (Constitution V)
