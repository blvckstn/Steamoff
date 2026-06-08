# Phase 1 Data Model: Friendly "Steam Offline Mode" UX Copy Refresh

This feature introduces no new persisted entities, settings fields, or runtime
data structures. It only changes the *values* of existing localization-string
entries and adds a small number of new entries of the same shape. This document
records the shape of what's being edited so the implementation stays consistent.

## Entity: Localization String Entry

Already defined by the existing localization system (specs/002); restated here
for reference since this feature edits/extends instances of it.

| Field   | Type   | Description |
|---------|--------|-------------|
| `key`   | string | Dot-namespaced identifier (e.g. `compact.blockButton`, `tray.alwaysBlock`, `compact.tooltip.toggleButton`). MUST be identical (same name) across all 9 language files — this is the parity invariant (FR-004). |
| `value` | string | The localized, human-facing text for that key in one specific language. MUST be non-empty, non-placeholder, and idiomatically phrased for that language (FR-003). May contain `{0}`-style format placeholders where the existing key already used them (e.g. `compact.modeLabel`: `"Режим: {0}"`) — this feature does not change placeholder usage, only surrounding wording. |

**Validation rules** (enforced by the existing localization-parity test, per
specs/002/004 conventions):
- Every `key` present in any one of the 9 files MUST be present in all 9.
- Every `value` MUST be non-empty.
- (Established convention, not newly introduced) keys are grouped by feature
  area via dot-prefixes (`compact.*`, `tray.*`, `status.*`, `settings.*`).

**State transitions**: N/A — these are static resource values, not stateful
entities. The only "transition" is edit-time (old value → new friendly value),
which is a one-time content change, not a runtime state machine.

## Change Set Overview

### A. Existing keys whose *values* change (same key name, new copy, ×9 languages)

Grouped by area, per FR-001/FR-002/FR-005 (exact final wording is a copywriting
task per research.md §3 — this table defines *which* keys are in scope and
*what concept* their new value must convey):

| Key | Current concept (to replace) | Required new concept |
|-----|------------------------------|-----------------------|
| `compact.blockButton` | "Block Steam" (action button) | Switch Steam into offline/no-internet mode |
| `compact.unblockButton` | "Unblock Steam" (action button) | Switch Steam back to having internet access |
| `compact.statusBlocked` | "Steam is blocked" | "Steam is in offline mode" / "Steam can't reach the internet" (warm framing) |
| `compact.statusUnblocked` | "Steam is unblocked" | "Steam is online" / "Steam has internet access" |
| `compact.statusPartial` | "Steam is partially blocked" | "Some of Steam is offline" (still accurate — honest-state principle) |
| `tray.block` | "Block Steam" (menu item) | Same friendly framing as `compact.blockButton` |
| `tray.unblock` | "Unblock Steam" (menu item) | Same friendly framing as `compact.unblockButton` |
| `tray.alwaysBlock` | "Always block" (mode) | "Always keep Steam offline" / equivalent friendly mode description |
| `tray.alwaysUnblock` | "Always unblock" (mode) | "Always keep Steam online" / equivalent |
| `status.blocked` | "Steam is blocked" (tray tooltip) | Same friendly framing as `compact.statusBlocked` |
| `status.unblocked` | "Steam is unblocked" (tray tooltip) | Same friendly framing as `compact.statusUnblocked` |
| `status.partiallyBlocked` | "Partially blocked" (tray tooltip) | Same friendly framing as `compact.statusPartial` |
| `settings.section.firewall` | "Firewall" (raw technical heading) | Approachable heading describing what the section controls (e.g. "Internet access control" / "What can reach the internet") |
| `settings.section.folders` | "Additional folders" | May gain a short explanatory subtext key (see below) framed as "these folders' programs will have their internet access turned off" |
| `settings.section.exeFiles` | "Individual executables" | Same explanatory framing as folders, for single programs |

Strings explicitly OUT of scope for meaning changes (per FR-005 / spec edge
cases) — tone may be gently warmed, but accuracy/information content is
preserved as-is:
`status.driftDetected`, `status.error`, `status.noAdminRights`,
`status.notConfigured`, `status.checking`, `compact.statusDrift`,
`compact.statusError`, `compact.statusNotConfigured`, `compact.adminMissing`,
`compact.adminOk`.

### B. New keys added (same shape, ×9 languages each)

Tooltip keys, per FR-006/FR-007/FR-008. Namespaced under `*.tooltip.*` to match
existing dot-prefix conventions (`compact.miniLog.*`, `settings.toast.*`, etc.):

| New key | Attached to control | Must convey |
|---------|--------------------|-------------|
| `compact.tooltip.toggleButton` | Big toggle button (`BigToggleButtonStyle` button bound to `ToggleCommand`) | What clicking it does *right now* (varies with current state — phrased generically enough to read naturally in both states, or the binding may select between an "on"/"off" tooltip key pair if needed during implementation) |
| `compact.tooltip.settingsButton` | "Open settings" icon button | Opens the settings window |
| `compact.tooltip.expandLog` | Mini-log expand/collapse button | Shows/hides the recent activity log |
| `compact.tooltip.openFullLog` | "Open full log" button | Opens the full log file/folder |
| `compact.tooltip.copyDiagnostics` | "Copy diagnostics" button | Copies diagnostic info for troubleshooting/support |
| `settings.tooltip.modeAlwaysBlock` | "Always offline" mode radio/option | What choosing this mode means going forward |
| `settings.tooltip.modeAlwaysUnblock` | "Always online" mode radio/option | What choosing this mode means going forward |
| `settings.tooltip.modePauseMonitoring` | "Pause monitoring" mode radio/option | What pausing monitoring means |
| `settings.tooltip.folderToggle` | Additional-folder enable switch | What enabling it does (turn off internet for that folder's programs) |
| `settings.tooltip.exeToggle` | Additional-executable enable switch | What enabling it does (turn off internet for that program) |

**Note**: The exact final key list/naming may be adjusted by ±1-2 entries
during implementation if a control is found to already have an equivalent
string usable as a tooltip (avoiding duplication) — any such adjustment must
preserve the "added to all 9 files" invariant (FR-004) and the friendly-tone
requirement (FR-008).

## Relationships

- Each `Localization String Entry` belongs to exactly one `key` and exists once
  per language file (1 key : 9 values).
- Tooltip entries (`*.tooltip.*`) are referenced from XAML via
  `ToolTipService.ToolTip="{Binding Loc[<key>]}"`, the same `Loc` proxy instance
  already injected as `DataContext.Loc` for `MainWindow`/`SettingsWindow` (no
  new relationship type — reuses the existing `Loc[key]` indexer binding path).
