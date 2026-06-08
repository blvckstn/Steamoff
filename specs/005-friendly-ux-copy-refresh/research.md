# Phase 0 Research: Friendly "Steam Offline Mode" UX Copy Refresh

No `NEEDS CLARIFICATION` markers remained in the spec, so this phase focuses on
confirming the existing mechanisms this feature will reuse, and settling the
copywriting approach for 9 languages.

## 1. How localized strings reach the UI today

**Decision**: Reuse the existing `Loc[key]` indexer binding exactly as-is —
`{Binding Loc[compact.blockButton]}` etc. — sourced from
`Steamoff.App.Localization.LocalizationProxy`, which wraps
`ILocalizationService`/`LocalizedStringProvider` reading the embedded JSON
resources in `Steamoff.Core/Resources/Localization/*.json`.

**Rationale**: This is already wired through every label in `MainWindow.xaml`
and `SettingsWindow.xaml`, refreshes instantly on `LanguageChanged` (per
`App.xaml.cs` composition root comments), and is exactly the mechanism
specs/002 and specs/004 established and tested. Reusing it means zero new
infrastructure and the existing localization-parity test continues to be the
regression guard (FR-004).

**Alternatives considered**: A separate "tooltip strings" resource file/format —
rejected; it would fragment the localization story, bypass the parity test, and
contradict FR-008 ("tooltip text MUST be sourced from the same localization
mechanism as other UI strings").

## 2. How to attach tooltips in WPF/MVVM without view-model changes

**Decision**: Use `ToolTipService.ToolTip="{Binding Loc[some.tooltip.key]}"` as
an attached-property binding directly in XAML on the target `Button`/control —
the same `DataContext` (the view-model wrapping `Loc`) already flows to these
elements, so no additional binding plumbing or VM properties are required.

**Rationale**: `ToolTipService.ToolTip` accepts arbitrary content including a
bound string, re-evaluates the binding when the `Loc` proxy raises
`PropertyChanged`/`LanguageChanged` (same mechanism that already live-updates
visible `Text="{Binding Loc[...]}"` labels), and requires no code-behind. This
keeps the change purely declarative/XAML+resource, matching the "copy/UX only"
scope (FR-009).

**Alternatives considered**:
- `ToolTip="{Binding Loc[...]}"` (the simple property, not the attached
  `ToolTipService.ToolTip`) — works similarly for most controls, but
  `ToolTipService.ToolTip` is the more idiomatic WPF attached-property form and
  composes more predictably with styled controls that already set `ToolTip` via
  a `Style`; chosen for consistency and to avoid clashing with any existing
  style setters.
- Custom tooltip popups matching the neumorphic design system — rejected as
  over-scoped; the constitution's "no MessageBox-style popups" rule targets
  *modal decision dialogs*, not hover hints, and standard WPF tooltips already
  inherit the dark theme via `DarkOrange.xaml` (it sets `ToolTip`-related styles
  for the app — verified no conflicting global override exists that would need
  changing).

## 3. Approach to "friendly" copy across 9 languages

**Decision**: For each existing key in scope (`compact.blockButton`,
`compact.unblockButton`, `compact.statusBlocked`, `compact.statusUnblocked`,
`compact.statusPartial`, `tray.block`, `tray.unblock`, `tray.alwaysBlock`,
`tray.alwaysUnblock`, `status.blocked`, `status.unblocked`,
`status.partiallyBlocked`, `settings.section.firewall`, `settings.section.folders`,
`settings.section.exeFiles`, and any directly-adjacent explanatory strings),
write **idiomatic** copy per language around the shared concept "Steam/these
programs go offline / come back online — their internet access is switched
off/on", rather than translating one fixed English/Russian phrase word-for-word.
New `*.tooltip.*` keys follow the same per-language idiomatic-phrasing approach.

**Rationale**: The spec explicitly calls for "не просто машинный перевод" —
phrasing the way a native speaker would write friendly software copy (FR-003).
A single canonical phrase translated literally risks sounding stilted or even
wrong in languages with different connotations for "offline"/"autonomous"
(e.g. Polish "tryb offline" vs. a literal "tryb autonomiczny"; Chinese reads
more naturally with "离线模式" — "offline mode" — than a literal "自治模式").
Anchoring on the *meaning* ("turn off this app's/these programs' internet
access — like flipping it into offline mode") and letting each language express
that naturally satisfies FR-003 while still meeting SC-001 (no "block"-style
alarming phrasing survives) and SC-003 (a first-time user can describe the
effect correctly from the label alone).

**Alternatives considered**: Maintaining one master English string and running
it through literal translation for the other 8 — rejected per the user's
explicit instruction and FR-003; this is precisely the "machine-translated"
outcome the spec calls out as insufficient.

## 4. Keeping the localization-parity guarantee intact

**Decision**: Any new `*.tooltip.*` key is added to **all 9** language files in
the same edit pass, with a real (non-placeholder, non-empty) value in each —
never added to one file and back-filled later.

**Rationale**: specs/002/004 established a parity test asserting every key
exists with a non-empty value across all language files; violating that during
intermediate edits would break CI/tests and contradicts FR-004/SC-002. Adding
keys file-by-file in one atomic pass (rather than "add to ru.json, then later
translate into the rest") avoids ever landing in a broken intermediate state.

**Alternatives considered**: Adding keys to `ru.json`/`en.json` first and
deferring the rest — rejected; risks shipping with a failing parity test or
forgetting a language, and the user explicitly asked for *all* languages to be
done thoughtfully, not staged.
