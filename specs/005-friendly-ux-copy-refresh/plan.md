# Implementation Plan: Friendly "Steam Offline Mode" UX Copy Refresh

**Branch**: `005-friendly-ux-copy-refresh` | **Date**: 2026-06-08 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/005-friendly-ux-copy-refresh/spec.md`

## Summary

Replace alarming "block/unblock Steam" wording across the compact view, tray menu,
and Settings with warm, friendly "Steam offline mode" / "turn off internet access
for these programs and folders" framing — consistently and idiomatically across
all 9 supported languages — and add localized tooltips to the primary interactive
controls. This is a pure localization-string + XAML-tooltip change: no firewall
mechanics, view-model logic, or localization key *names* change (FR-009,
Assumptions). The approach is to (1) rewrite the *values* of the existing
`compact.*`, `tray.*`, `status.*`, `settings.section.*` keys in all 9 JSON files
under `src/Steamoff.Core/Resources/Localization/`, (2) add a small set of new
`*.tooltip.*` keys (also to all 9 files) for the controls named in FR-006/FR-007,
and (3) wire `ToolTipService.ToolTip="{Binding Loc[...]}"` onto those controls in
`MainWindow.xaml` and `SettingsWindow.xaml`, exactly mirroring how `Text="{Binding
Loc[...]}"` is already bound there.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (net8.0-windows), WPF + MVVM (existing stack — no change)

**Primary Dependencies**: `Steamoff.Core.Localization` (`ILocalizationService`, `LocalizedStringProvider`), `Steamoff.App.Localization.LocalizationProxy` (existing `Loc[key]` indexer binding already used throughout `MainWindow.xaml` / `SettingsWindow.xaml`)

**Storage**: Static localization JSON files at `src/Steamoff.Core/Resources/Localization/{ru,en,de,es,fr,it,pl,pt,zh}.json` (embedded resources) — N/A for runtime persistence; no settings/schema change

**Testing**: xUnit (`tests/Steamoff.Tests`) — existing localization-parity test (every key present & non-empty in every language file, see specs/002) is the authoritative regression guard; extend it implicitly by keeping all 9 files in sync as new tooltip keys are added

**Target Platform**: Windows 10/11 desktop (existing Steamoff.App WPF executable)

**Project Type**: Desktop app (single WPF project + Core/Infrastructure layers + test project) — existing structure, no new projects

**Performance Goals**: N/A — static string/resource change; no measurable perf impact (tooltips render via standard WPF `ToolTip` on hover)

**Constraints**: Must not change any localization *key names* bound from XAML/code (Assumptions); must keep localization parity (every key present, non-empty, in all 9 files) passing; must not touch `ComFirewallService`, `ToggleAsync`, rule-group naming, or any other mechanics (FR-009, Constitution II)

**Scale/Scope**: ~25-35 existing string values reworded across `compact.*`, `tray.*`, `status.*`, `settings.section.*` (and any inline explanatory strings they touch) × 9 languages; ~8-10 new tooltip keys × 9 languages; XAML edits limited to `MainWindow.xaml` and `SettingsWindow.xaml` adding `ToolTipService.ToolTip` bindings to the controls named in FR-006/FR-007

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Local-Only, No Cloud, No Telemetry** — PASS. No network access added; pure static-resource/XAML change.
- **II. Firewall-Only Enforcement (NON-NEGOTIABLE)** — PASS. No firewall code touched; FR-009 explicitly forbids it. The "Steamoff" rule group / naming convention is unaffected — only how the *user-facing description* of that mechanism reads.
- **III. Honest State (No Lying Toggles)** — PASS. Status strings keep their meaning (FR-005); only tone is softened where it doesn't compromise the "drift/error must be surfaced honestly" guarantee. Edge-case analysis in spec.md explicitly protects this.
- **IV. Respect the Administrator Boundary** — PASS. No change to elevation detection/communication; "no admin rights" status string stays accurate (FR-005), tone may only be softened, not its informational content.
- **V. Test-First for Core Logic** — N/A/PASS. This feature touches no core logic (no new services/abstractions); the relevant existing regression guard is the localization-parity test, which continues to gate the change (FR-004, SC-002).
- **VI. Calm, Cohesive UI (Dark Orange Neumorphic)** — PASS. Tooltips use the standard WPF `ToolTip` mechanism (no custom MessageBox-style popups), consistent with the existing dark-orange neumorphic system; no layout/visual structure changes.

No violations — Complexity Tracking section is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/005-friendly-ux-copy-refresh/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── localization-copy-contract.md
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
src/
├── Steamoff.Core/
│   └── Resources/Localization/
│       ├── ru.json   # reworded compact.*/tray.*/status.*/settings.section.* values + new *.tooltip.* keys
│       ├── en.json   # (same key set, idiomatic English copy)
│       ├── de.json   # (same key set, idiomatic German copy)
│       ├── es.json   # (same key set, idiomatic Spanish copy)
│       ├── fr.json   # (same key set, idiomatic French copy)
│       ├── it.json   # (same key set, idiomatic Italian copy)
│       ├── pl.json   # (same key set, idiomatic Polish copy)
│       ├── pt.json   # (same key set, idiomatic Portuguese copy)
│       └── zh.json   # (same key set, idiomatic Simplified Chinese copy)
├── Steamoff.App/
│   └── Views/
│       ├── MainWindow.xaml      # add ToolTipService.ToolTip="{Binding Loc[...]}" to toggle/settings/log buttons
│       └── SettingsWindow.xaml  # add ToolTipService.ToolTip="{Binding Loc[...]}" to mode/folder/exe toggle controls
│
tests/
└── Steamoff.Tests/
    └── (existing localization parity test continues to cover the new/changed keys — see specs/002 contracts for its location/shape)
```

**Structure Decision**: No new projects or directories beyond the spec folder. This
is a targeted edit to the existing `Steamoff.Core` localization resources and two
existing `Steamoff.App` views, validated by the existing `Steamoff.Tests`
localization-parity suite — matching the layered structure already mandated by
the constitution's Technology Constraints section.

## Complexity Tracking

*No constitution violations — section intentionally left without entries.*
