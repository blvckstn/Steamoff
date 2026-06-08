# Contract: Localization Copy & Tooltip Bindings

This "contract" documents the interface this feature must honor: the
localization-key surface consumed by XAML bindings, and the parity guarantee
the existing test suite enforces. Treat it as the acceptance contract between
the copy/resource changes (Steamoff.Core) and the view bindings (Steamoff.App).

## 1. Key-name stability contract

**Guarantee**: Every localization key referenced today from `MainWindow.xaml`,
`SettingsWindow.xaml`, `TrayService.cs`, or any view-model — e.g.
`compact.blockButton`, `tray.block`, `settings.section.firewall` — continues to
exist under the **exact same name** after this feature ships. XAML/code
bindings (`{Binding Loc[compact.blockButton]}`, `_localization.GetString("tray.block")`,
etc.) require **zero changes**.

**Why**: Renaming keys would ripple into `TrayService.BuildContextMenu`,
`CompactViewModel`, `SettingsViewModel`, and every XAML binding — turning a
copy change into a code change, contradicting FR-009 and the "copy/UX only"
scope the user asked for.

**Verification**: `git grep` for each key name in `src/Steamoff.App/` before
and after the change must return the same binding call sites (value changes
only show up in the `.json` diffs, never in `.cs`/`.xaml` diffs for existing
keys).

## 2. Parity contract (every key, every language, non-empty)

**Guarantee**: For the union of all keys across
`src/Steamoff.Core/Resources/Localization/{ru,en,de,es,fr,it,pl,pt,zh}.json`,
each key exists in **all nine** files with a **non-empty** string value.

**Why**: This is the existing regression guard from specs/002 (localization
parity test) — it is what makes "switch language and nothing is missing"
provable. New `*.tooltip.*` keys join this contract the moment they're added to
the first file.

**Verification**: Run the existing localization-parity test (in
`tests/Steamoff.Tests`, see specs/002 contracts for its exact location/name) —
it must report 0 missing/empty keys across all 9 files after the change.

## 3. Tooltip-binding contract

**Guarantee**: Every control named in spec FR-006/FR-007 exposes a hover
tooltip whose text is bound through `ToolTipService.ToolTip="{Binding
Loc[<key>]}"` (or the equivalent `ToolTip="{Binding Loc[<key>]}"` if a
style-setter conflict makes the attached property impractical for a specific
control — document any such exception inline in the XAML with a one-line
comment), where `<key>` is one of the new `*.tooltip.*` entries from
data-model.md §B (or an existing key, if reuse is appropriate and still
conveys the control's purpose in friendly terms).

**Why**: FR-008 requires tooltip text to come from "the same localization
mechanism as other UI strings" so it updates live on language switch — exactly
like every other `Loc[...]`-bound string already in these views.

**Verification** (manual, per quickstart.md): hover each named control in at
least two different selected languages and confirm (a) a tooltip appears,
(b) its text matches the friendly tone of the corresponding label/status string,
and (c) switching the app language updates the tooltip text without restart.

## 4. Tone contract (no alarming "block" framing survives)

**Guarantee**: None of the in-scope strings (data-model.md §A) — in any of the
9 languages — contain phrasing that frames the toggle as "blocking"/
"attacking"/"restricting" something dangerous. They consistently frame the
action/state as switching into/out of an offline mode, or turning internet
access off/on for specific programs — in line with how a friendly consumer app
would phrase it natively in that language (not a literal translation of a
single canonical phrase).

**Why**: This is the literal ask (SC-001) — "заблокировать" reads as scary;
"автономный режим" / "offline mode" reads as a normal, safe feature toggle.

**Verification**: Manual native-language read-through of each changed string
per language (documented per-language sign-off in quickstart.md's validation
checklist) plus a keyword grep for residual "block"/"блок" stems in the
in-scope key set's *values* (not in out-of-scope status/error strings, which
may legitimately retain precise technical terms where FR-005 requires it).
