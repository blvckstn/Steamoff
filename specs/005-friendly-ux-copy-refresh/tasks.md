# Tasks: Friendly "Steam Offline Mode" UX Copy Refresh

**Input**: Design documents from `specs/005-friendly-ux-copy-refresh/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/localization-copy-contract.md, quickstart.md

**Tests**: No new automated tests are requested by the spec — this is a copy/XAML
change validated by the *existing* localization-parity test (run, not written,
in Polish phase) plus the manual quickstart checklist. No test-writing tasks are
generated per the "tests are optional" rule.

**Organization**: Tasks are grouped by user story from spec.md so each story is
independently completable and demoable. All localization edits touch the same 9
JSON files — within a story, edits to different *files* are marked [P]; edits to
the *same* file are sequenced to avoid clobbering.

## Path Conventions

- Localization resources: `src/Steamoff.Core/Resources/Localization/{lang}.json`
  for `lang ∈ {ru, en, de, es, fr, it, pl, pt, zh}`
- Views: `src/Steamoff.App/Views/MainWindow.xaml`, `src/Steamoff.App/Views/SettingsWindow.xaml`
- Tests: `tests/Steamoff.Tests/` (existing localization-parity suite, run only)

---

## Phase 1: Setup

- [ ] T001 Read all 9 files in `src/Steamoff.Core/Resources/Localization/` and extract the current values of every key listed in data-model.md §A (`compact.blockButton`, `compact.unblockButton`, `compact.statusBlocked`, `compact.statusUnblocked`, `compact.statusPartial`, `tray.block`, `tray.unblock`, `tray.alwaysBlock`, `tray.alwaysUnblock`, `status.blocked`, `status.unblocked`, `status.partiallyBlocked`, `settings.section.firewall`, `settings.section.folders`, `settings.section.exeFiles`) into a scratch reference table (in your working notes, not committed) so every language's "before" wording is visible side-by-side before drafting replacements
- [ ] T002 Confirm the current localization-parity test location and invocation by reading its source under `tests/Steamoff.Tests/` (per specs/002 contracts) and running it once via `dotnet test tests/Steamoff.Tests --filter "FullyQualifiedName~Localization"` to record the passing baseline before any edits

---

## Phase 2: Foundational

*No blocking shared infrastructure changes are needed — the localization plumbing (`ILocalizationService`, `LocalizationProxy`, `Loc[key]` binding) already exists and is reused as-is (research.md §1). This phase is intentionally empty; proceed directly to User Story 1.*

---

## Phase 3: User Story 1 — A non-technical user understands the toggle without feeling alarmed (Priority: P1) 🎯 MVP

**Goal**: Rework the core toggle's wording (compact-view button + status, tray menu open/toggle/mode items) from "block/unblock Steam" to friendly "Steam offline mode" framing, idiomatically, in all 9 languages.

**Independent Test**: Launch the app in each of the 9 languages, read the big toggle button, status text, and tray context menu — confirm none use "block"/"заблокировать"-style phrasing and the meaning ("Steam goes offline / comes back online") is immediately clear (per spec.md User Story 1 acceptance scenarios and SC-001/SC-003).

### Implementation for User Story 1

- [ ] T003 [P] [US1] In `src/Steamoff.Core/Resources/Localization/ru.json`, rewrite the values of `compact.blockButton`, `compact.unblockButton`, `compact.statusBlocked`, `compact.statusUnblocked`, `compact.statusPartial`, `tray.block`, `tray.unblock`, `tray.alwaysBlock`, `tray.alwaysUnblock`, `status.blocked`, `status.unblocked`, `status.partiallyBlocked` using warm "Автономный режим Steam" / "Steam офлайн" framing per data-model.md §A — natural Russian phrasing, not a literal template; keep `{0}`-style placeholders intact where present
- [ ] T004 [P] [US1] Same rewrite as T003 for `src/Steamoff.Core/Resources/Localization/en.json`, using natural English "Steam Offline Mode" / "Steam is offline" framing
- [ ] T005 [P] [US1] Same rewrite as T003 for `src/Steamoff.Core/Resources/Localization/de.json`, using idiomatic German phrasing for the offline-mode concept (not a literal translation of the Russian/English strings)
- [ ] T006 [P] [US1] Same rewrite as T003 for `src/Steamoff.Core/Resources/Localization/es.json`, using idiomatic Spanish phrasing for the offline-mode concept
- [ ] T007 [P] [US1] Same rewrite as T003 for `src/Steamoff.Core/Resources/Localization/fr.json`, using idiomatic French phrasing for the offline-mode concept
- [ ] T008 [P] [US1] Same rewrite as T003 for `src/Steamoff.Core/Resources/Localization/it.json`, using idiomatic Italian phrasing for the offline-mode concept
- [ ] T009 [P] [US1] Same rewrite as T003 for `src/Steamoff.Core/Resources/Localization/pl.json`, using idiomatic Polish phrasing for the offline-mode concept (e.g. "tryb offline", mind grammatical case/gender agreement across the button/status pairs)
- [ ] T010 [P] [US1] Same rewrite as T003 for `src/Steamoff.Core/Resources/Localization/pt.json`, using idiomatic Portuguese phrasing for the offline-mode concept
- [ ] T011 [P] [US1] Same rewrite as T003 for `src/Steamoff.Core/Resources/Localization/zh.json`, using idiomatic Simplified Chinese phrasing (e.g. "离线模式") for the offline-mode concept
- [ ] T012 [US1] Cross-check all 9 files edited in T003-T011: confirm every key in scope still exists with a non-empty value in every file (no key dropped/typo'd during editing), and that within each language the button/status/tray strings read as a *coherent set* (e.g. the button says "switch Steam offline" and the resulting status says "Steam is offline" — not mismatched verbs/nouns)

**Checkpoint**: At this point, User Story 1 is independently functional — launching the app in any language shows friendly toggle/status/tray wording with no "block" framing. This alone is a demoable, shippable increment (MVP).

---

## Phase 4: User Story 2 — A user configuring extra programs/folders understands what "blocking" means there (Priority: P2)

**Goal**: Reword the Settings sections that describe additional folders/executables and the "Firewall" section so they read as "turning off internet access for these programs and folders", in all 9 languages.

**Independent Test**: Open Settings in each of the 9 languages, read the additional-folders/executables section headers and the renamed firewall/internet-access section — confirm they consistently describe the effect as turning off internet access in friendly terms (per spec.md User Story 2 acceptance scenarios).

### Implementation for User Story 2

- [ ] T013 [P] [US2] In `src/Steamoff.Core/Resources/Localization/ru.json`, rewrite `settings.section.firewall` (e.g. "Контроль доступа в интернет" instead of raw "Firewall"), `settings.section.folders`, and `settings.section.exeFiles` to frame the effect as turning off internet access for the chosen programs/folders, in friendly Russian; if the existing template has no explanatory subtext key for these sections, add one short new key per section (e.g. `settings.section.folders.description`, `settings.section.exeFiles.description`, `settings.section.firewall.description`) carrying the "turn off internet access for these programs/folders" framing — and note the new key names for T014-T021 to mirror
- [ ] T014 [P] [US2] Apply the same heading rewrite (and add the same new description keys established in T013, translated idiomatically) to `src/Steamoff.Core/Resources/Localization/en.json`
- [ ] T015 [P] [US2] Apply the same heading rewrite + new description keys to `src/Steamoff.Core/Resources/Localization/de.json`, phrased natively
- [ ] T016 [P] [US2] Apply the same heading rewrite + new description keys to `src/Steamoff.Core/Resources/Localization/es.json`, phrased natively
- [ ] T017 [P] [US2] Apply the same heading rewrite + new description keys to `src/Steamoff.Core/Resources/Localization/fr.json`, phrased natively
- [ ] T018 [P] [US2] Apply the same heading rewrite + new description keys to `src/Steamoff.Core/Resources/Localization/it.json`, phrased natively
- [ ] T019 [P] [US2] Apply the same heading rewrite + new description keys to `src/Steamoff.Core/Resources/Localization/pl.json`, phrased natively
- [ ] T020 [P] [US2] Apply the same heading rewrite + new description keys to `src/Steamoff.Core/Resources/Localization/pt.json`, phrased natively
- [ ] T021 [P] [US2] Apply the same heading rewrite + new description keys to `src/Steamoff.Core/Resources/Localization/zh.json`, phrased natively
- [ ] T022 [US2] If new `settings.section.*.description` keys were introduced in T013-T021, wire them into `src/Steamoff.App/Views/SettingsWindow.xaml` immediately below the corresponding section header `TextBlock` (matching the existing `Style="{StaticResource CaptionTextStyle}"` pattern used for similar explanatory text elsewhere in that file, e.g. around line 80), bound via `Text="{Binding Loc[<new-key>]}"`
- [ ] T023 [US2] Cross-check all 9 files edited in T013-T021: confirm `settings.section.firewall/folders/exeFiles` plus any newly-added description keys exist with non-empty values in all 9 files, and that the heading + description pairs read coherently together per language

**Checkpoint**: User Story 2 is independently functional — opening Settings in any language now explains additional-folder/exe/firewall behavior as "turning off internet access", without alarming "blocking" framing, building on US1's vocabulary.

---

## Phase 5: User Story 3 — A user hovers over a button and gets a quick, friendly explanation (Priority: P3)

**Goal**: Add short, friendly, localized tooltips to the primary compact-view controls and the relevant Settings offline-mode controls, sourced through the existing `Loc[...]` binding mechanism.

**Independent Test**: Hover each named control (big toggle button, settings button, mini-log expand/collapse, open-full-log, copy-diagnostics in the compact view; mode options and folder/exe toggles in Settings) in at least two different languages — confirm a short, friendly, localized tooltip appears and updates live on language switch (per spec.md User Story 3 acceptance scenarios and SC-004).

### Implementation for User Story 3

- [ ] T024 [P] [US3] Add the new tooltip keys from data-model.md §B (`compact.tooltip.toggleButton`, `compact.tooltip.settingsButton`, `compact.tooltip.expandLog`, `compact.tooltip.openFullLog`, `compact.tooltip.copyDiagnostics`, `settings.tooltip.modeAlwaysBlock`, `settings.tooltip.modeAlwaysUnblock`, `settings.tooltip.modePauseMonitoring`, `settings.tooltip.folderToggle`, `settings.tooltip.exeToggle`) to `src/Steamoff.Core/Resources/Localization/ru.json` with short, friendly Russian text matching the US1/US2 tone (each ≤ ~80 chars so it reads well as a hover hint)
- [ ] T025 [P] [US3] Add the same tooltip keys (idiomatic English copy) to `src/Steamoff.Core/Resources/Localization/en.json`
- [ ] T026 [P] [US3] Add the same tooltip keys (idiomatic German copy) to `src/Steamoff.Core/Resources/Localization/de.json`
- [ ] T027 [P] [US3] Add the same tooltip keys (idiomatic Spanish copy) to `src/Steamoff.Core/Resources/Localization/es.json`
- [ ] T028 [P] [US3] Add the same tooltip keys (idiomatic French copy) to `src/Steamoff.Core/Resources/Localization/fr.json`
- [ ] T029 [P] [US3] Add the same tooltip keys (idiomatic Italian copy) to `src/Steamoff.Core/Resources/Localization/it.json`
- [ ] T030 [P] [US3] Add the same tooltip keys (idiomatic Polish copy) to `src/Steamoff.Core/Resources/Localization/pl.json`
- [ ] T031 [P] [US3] Add the same tooltip keys (idiomatic Portuguese copy) to `src/Steamoff.Core/Resources/Localization/pt.json`
- [ ] T032 [P] [US3] Add the same tooltip keys (idiomatic Simplified Chinese copy) to `src/Steamoff.Core/Resources/Localization/zh.json`
- [ ] T033 [US3] In `src/Steamoff.App/Views/MainWindow.xaml`, add `ToolTipService.ToolTip="{Binding Loc[compact.tooltip.toggleButton]}"` to the big toggle button (the `Button` styled with `BigToggleButtonStyle` bound to `ToggleCommand`, around line 58-63), `compact.tooltip.settingsButton` to the settings icon button (around line 39), `compact.tooltip.expandLog` to the mini-log expand/collapse button, `compact.tooltip.openFullLog` to the "open full log" button, and `compact.tooltip.copyDiagnostics` to the "copy diagnostics" button (the latter three around lines 144-147), per contracts/localization-copy-contract.md §3
- [ ] T034 [US3] In `src/Steamoff.App/Views/SettingsWindow.xaml`, add `ToolTipService.ToolTip="{Binding Loc[settings.tooltip.modeAlwaysBlock]}"` / `modeAlwaysUnblock` / `modePauseMonitoring` to the three enforcement-mode option controls (around line 113 area, the `RadioButton`/option controls under "settings.section.modes"), and `ToolTipService.ToolTip="{Binding Loc[settings.tooltip.folderToggle]}"` / `exeToggle` to the additional-folder and additional-executable enable-toggle controls, per contracts/localization-copy-contract.md §3
- [ ] T035 [US3] Cross-check T024-T032: confirm all 10 new tooltip keys exist with non-empty, ≤~80-char friendly values in all 9 files; spot-check that `compact.tooltip.toggleButton`'s wording makes sense read against *both* the "currently online" and "currently offline" states (per spec.md edge cases) — adjust wording to be state-agnostic if it currently reads oddly in one state

**Checkpoint**: User Story 3 is independently functional — every named control shows a friendly, localized tooltip on hover, live-updating on language switch, layered on top of US1/US2's reworded labels.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation that the whole feature holds together per the contracts and quickstart guide.

- [ ] T036 Run `dotnet test tests/Steamoff.Tests --filter "FullyQualifiedName~Localization"` and confirm the localization-parity suite passes with 0 missing/empty keys across all 9 files (contracts §2 / SC-002) — fix any gap immediately by completing the corresponding per-language task above
- [ ] T037 Run `dotnet build src/Steamoff.App/Steamoff.App.csproj -c Release` and confirm a clean build (the new `ToolTipService.ToolTip` bindings in T033/T034 must not introduce XAML resource/binding errors — recall the `SectionHeaderStyle` `XamlParseException` regression class of bug from this same window, and verify no analogous missing-resource issue was introduced)
- [ ] T038 Execute quickstart.md Steps 2-4 manually: launch the rebuilt app elevated, switch through all 9 languages, read the reworked compact-view/tray/Settings copy (Step 2), confirm drift/error/no-admin status strings stay informative (Step 3), and hover every named control in at least 2 languages confirming live-updating localized tooltips (Step 4); record the per-language sign-off table from quickstart.md Step 2
- [ ] T039 Run `git diff --stat` and confirm the change set matches quickstart.md Step 5's expectation — only the 9 localization JSON files, `MainWindow.xaml`, `SettingsWindow.xaml`, and the `specs/005-friendly-ux-copy-refresh/` folder are touched; no `ComFirewallService`, view-model, or other mechanics files changed (FR-009 / Constitution II)
- [ ] T040 Grep the in-scope string *values* (data-model.md §A keys, all 9 files) for residual "block"/"блок"/"unblock"/"разблок" stems and confirm none remain in the reworked button/status/tray/settings-section strings (contracts §4 / SC-001) — out-of-scope status/error strings (`status.driftDetected`, `status.error`, etc., per data-model.md §A "OUT of scope" list) are exempt

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Empty — no blocking work; proceed straight to US1
- **User Story 1 (Phase 3)**: Depends on Setup (T001 reference table makes drafting consistent replacements much easier; T002 establishes the passing-test baseline)
- **User Story 2 (Phase 4)**: Independent of US1's *file edits* (different keys), but should follow US1 so the new Settings copy can reuse the same "offline mode / internet access" vocabulary established there for a coherent voice (soft dependency — US1 informs US2's wording, not a hard code dependency)
- **User Story 3 (Phase 5)**: Independent of US1/US2 *mechanically* (adds new keys + XAML attached properties), but should follow both so tooltip copy can echo the established vocabulary and reference the final button/section labels it's annotating (soft dependency)
- **Polish (Phase 6)**: Depends on all user stories being complete

### User Story Dependencies

- US1 (P1): No dependency on other stories — can ship alone as MVP
- US2 (P2): No hard dependency on US1; soft dependency for vocabulary consistency (recommended to do after US1)
- US3 (P3): No hard dependency on US1/US2; soft dependency for vocabulary consistency and so tooltips reference final labels (recommended to do last)

### Within Each User Story

- All per-language `[P]` edit tasks (e.g. T003-T011) can run in parallel — each touches a different JSON file
- The cross-check task (e.g. T012) must run after all per-language tasks for that story complete
- XAML wiring tasks (T022, T033, T034) must run after the corresponding new keys exist in at least the language currently used for manual testing (recommend completing all per-language tasks first to avoid binding to a not-yet-existent key)

---

## Parallel Execution Examples

### User Story 1 (Phase 3) — all 9 language edits in parallel

```text
T003 [P] [US1] Rewrite ru.json toggle/status/tray strings
T004 [P] [US1] Rewrite en.json toggle/status/tray strings
T005 [P] [US1] Rewrite de.json toggle/status/tray strings
T006 [P] [US1] Rewrite es.json toggle/status/tray strings
T007 [P] [US1] Rewrite fr.json toggle/status/tray strings
T008 [P] [US1] Rewrite it.json toggle/status/tray strings
T009 [P] [US1] Rewrite pl.json toggle/status/tray strings
T010 [P] [US1] Rewrite pt.json toggle/status/tray strings
T011 [P] [US1] Rewrite zh.json toggle/status/tray strings
→ then T012 [US1] cross-check (sequential, depends on all above)
```

### User Story 3 (Phase 5) — all 9 tooltip-key additions in parallel, then XAML wiring

```text
T024 [P] [US3] Add tooltip keys to ru.json
T025 [P] [US3] Add tooltip keys to en.json
... T026-T032 similarly for de/es/fr/it/pl/pt/zh ...
→ then T033 [US3] wire MainWindow.xaml tooltips (sequential — needs keys to exist)
→ then T034 [US3] wire SettingsWindow.xaml tooltips (sequential — can run parallel to T033 if desired, different file)
→ then T035 [US3] cross-check
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup: T001-T002)
2. Complete Phase 3 (User Story 1: T003-T012)
3. **STOP and VALIDATE**: Run quickstart.md Step 2 for the compact view + tray only — confirm the core toggle no longer reads as alarming in any language
4. This alone is a demoable, shippable increment — the most user-visible "block sounds scary" pain point is resolved

### Incremental Delivery

1. Setup → Foundational (empty) → US1 → **Checkpoint A** (ship/demo MVP)
2. Add US2 → **Checkpoint B** (Settings now consistent with the new vocabulary)
3. Add US3 → **Checkpoint C** (tooltips complete the friendly-UX picture)
4. Polish (Phase 6) → final validation, ship

### Suggested Single-Session Order (if doing it all at once)

Setup → US1 (all 9 langs + cross-check) → US2 (all 9 langs + XAML + cross-check)
→ US3 (all 9 langs + XAML + cross-check) → Polish (parity test, build, manual
quickstart pass, diff check, residual-keyword grep)

## Format Validation

All 40 tasks above follow `- [ ] [TaskID] [P?] [Story?] Description with file path`:
- Setup (T001-T002) and Polish (T036-T040): no `[Story]` label ✓
- Foundational phase intentionally empty (no blocking shared work) ✓
- US1 tasks (T003-T012): all carry `[US1]`, per-language tasks carry `[P]` ✓
- US2 tasks (T013-T023): all carry `[US2]`, per-language tasks carry `[P]` ✓
- US3 tasks (T024-T035): all carry `[US3]`, per-language + cross-check carry `[P]` where parallelizable ✓
- Every task names at least one concrete file path ✓
