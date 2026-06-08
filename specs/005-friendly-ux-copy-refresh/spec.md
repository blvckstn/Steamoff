# Feature Specification: Friendly "Steam Offline Mode" UX Copy Refresh

**Feature Branch**: `005-friendly-ux-copy-refresh`

**Created**: 2026-06-08

**Status**: Draft

**Input**: User description: "Не должно быть формулировок заблокировать Steam. Должны быть формулировки в виде Автономный режим Steam. В настройках надо сделать так, чтобы формулировки были выключить интернет у данных программ и папок. Формулировки можно немного менять, чтобы они передавали суть того, что происходит, потому что сейчас 'заблокировать' звучит опасно. Надо, чтобы это было дружелюбно — на всех языках. Сделай подсказки (tooltips) при наведении на кнопки. Сделай через spec-kit."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A non-technical user understands the toggle without feeling alarmed (Priority: P1)

A user who isn't comfortable with technical/security jargon opens Steamoff for the first time, looks at the big toggle button, the tray menu, and the status text, and immediately understands: "this switches Steam into an offline mode where it can't reach the internet" — without any wording that sounds like the app is "blocking"/"attacking" their system or doing something risky.

**Why this priority**: This is the core of the request — the current "Заблокировать Steam" / "Block Steam" phrasing reads as alarming and security-threatening to ordinary users, which undermines trust in the whole app. Fixing the primary action's wording (button, status, tray) delivers most of the value on its own.

**Independent Test**: Launch the app, read the big toggle button, the status line beneath it, and the tray context menu's open/toggle items in the user's selected language — confirm none of them use "block"/"заблокировать"-style phrasing, and that the meaning ("Steam goes offline / comes back online") is clear at a glance.

**Acceptance Scenarios**:

1. **Given** Steam is currently online (not in offline mode), **When** the user looks at the compact view's big toggle button and status text, **Then** they see friendly wording that frames the action as switching Steam into an "offline"/"autonomous" mode (e.g. "Автономный режим Steam" / "Steam Offline Mode"), not "blocking".
2. **Given** the user right-clicks the tray icon, **When** the context menu opens, **Then** the toggle-related items ("tray.block"/"tray.unblock", "tray.alwaysBlock"/"tray.alwaysUnblock") read as friendly mode-switch phrasing consistent with the main button's wording, in every supported display language.
3. **Given** the user switches the app's display language, **When** they re-open the compact view and tray menu, **Then** the friendly framing is present and natural-sounding (not a literal machine translation) in that language too.

---

### User Story 2 - A user configuring extra programs/folders understands what "blocking" means there (Priority: P2)

A user adding additional folders or executables to be covered by Steamoff opens Settings and reads the section that explains what happens to those items — they understand it as "these programs/folders will have their internet access turned off", not as some abstract/scary "firewall blocking" operation.

**Why this priority**: Settings is where users make an active configuration decision about extra targets; if the wording there is confusing or alarming, users may avoid using a useful feature (or be afraid they're doing something destructive to their PC).

**Independent Test**: Open Settings, navigate to the additional-folders/executables and firewall sections, and read every visible label/description in the user's language — confirm they consistently describe the effect as "turning off internet access for these programs and folders" in friendly terms.

**Acceptance Scenarios**:

1. **Given** the user opens Settings → "Дополнительные папки"/"Отдельные исполняемые файлы" sections, **When** they read the section headers and any explanatory text, **Then** the wording frames the feature as turning off internet access for the chosen programs/folders, not "blocking" them.
2. **Given** the user opens the section that currently reads "Firewall", **When** they read its heading and description, **Then** the heading/description uses approachable language describing what it does for the user (e.g. controlling which programs can reach the internet) rather than a raw technical term presented without context.

---

### User Story 3 - A user hovers over a button and gets a quick, friendly explanation (Priority: P3)

A user unsure what a particular button or menu toggle does hovers over it and sees a short tooltip in their own language that explains, in plain and reassuring terms, what will happen if they click it.

**Why this priority**: Tooltips are a supporting affordance — they reinforce the friendly framing established by P1/P2 and help uncertain users feel confident before they click, but the app is usable without them (existing labels already convey meaning once reworded).

**Independent Test**: Hover the mouse over the big toggle button, the settings-button, and the mini-log/diagnostics buttons in the compact view, and over the relevant toggle controls in Settings — confirm each shows a short, friendly, localized tooltip describing the action's effect.

**Acceptance Scenarios**:

1. **Given** the user hovers over the compact view's big toggle button, **When** the tooltip appears, **Then** it briefly explains in friendly terms what clicking it will do (turn Steam's internet access off/on), matching the current toggle state.
2. **Given** the user hovers over the "Open settings" button, the mini-log expand/collapse button, "open full log", and "copy diagnostics" buttons, **When** each tooltip appears, **Then** it gives a short, friendly description of that control's purpose.
3. **Given** the user changes the display language, **When** they re-hover the same controls, **Then** tooltips appear in the newly selected language with the same friendly tone.

### Edge Cases

- What happens when a language's natural phrasing for "offline mode" doesn't map 1:1 onto the existing localization-key structure (e.g. grammatical gender/case differences for "Автономный режим Steam" in Slavic languages)? → Each language file is free to phrase the string naturally as long as the same key conveys the same meaning; no key may be left untranslated or copy-pasted from another language.
- How does the system handle a status string that must reflect both "currently in offline mode" and "currently online" — does the friendly framing read naturally in both states? → Status strings for both states must independently read as warm/neutral (e.g. "Steam в автономном режиме" / "Steam на связи"), not just the toggle action.
- What happens to admin/error/drift status strings (e.g. "Ошибка firewall", "Обнаружено расхождение") — should those also be softened? → These remain accurate/technical where precision matters (errors, drift, admin-rights warnings), but should still avoid unnecessarily scary wording (e.g. prefer "Не удалось проверить состояние" over alarming phrasing) — see FR-005.
- What happens when a tooltip would be shown on a disabled control (e.g. toggle disabled because the app lacks firewall access)? → The tooltip still explains the control's purpose, optionally noting why it's currently unavailable, so the user isn't left guessing.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST replace every user-facing string that frames the core toggle action as "blocking"/"unblocking" Steam (compact view button and status, tray menu items, mode labels) with friendly wording that frames the action as switching Steam into/out of an offline (autonomous/no-internet) mode — e.g. along the lines of "Автономный режим Steam" / "Steam Offline Mode" — while preserving the existing localization key names so no code changes to bindings are required.
- **FR-002**: The system MUST reword the Settings sections and any explanatory text that currently describe blocking additional folders/executables or the firewall mechanism so that they describe the effect as turning off internet access for the user's chosen programs and folders, in plain non-technical language.
- **FR-003**: The system MUST present this friendly framing consistently and naturally (not as a literal word-for-word machine translation) in every supported display language: Russian, English, German, Spanish, French, Italian, Polish, Portuguese, and Chinese — each phrased the way a native speaker of that language would write warm, reassuring software copy.
- **FR-004**: The system MUST preserve the existing localization key set — every key that exists today must continue to exist with a non-empty, non-placeholder value in every language file (i.e., the established localization parity guarantee must keep holding after the copy changes).
- **FR-005**: The system MUST keep status/error/warning strings (e.g. firewall error, drift detected, no-admin-rights) accurate and informative; these MAY be softened in tone where possible, but MUST NOT sacrifice clarity about a real problem the user needs to know about.
- **FR-006**: The system MUST provide short, localized tooltips on the primary interactive controls of the compact view (the big toggle button, the settings button, the mini-log expand/collapse, "open full log", and "copy diagnostics" buttons) that briefly explain, in friendly terms, what each control does.
- **FR-007**: The system MUST provide short, localized tooltips on the relevant Settings controls that toggle or configure the offline-mode behavior (e.g. mode radio buttons / enforcement-mode options, the additional-folder and additional-executable enable toggles), explaining their effect in friendly terms.
- **FR-008**: Tooltip text MUST be sourced from the same localization mechanism as other UI strings (so it changes instantly when the user switches languages) and MUST follow the same friendly tone established for the reworded labels.
- **FR-009**: The system MUST NOT change any underlying behavior, firewall rule groups, COM/firewall service calls, or toggle logic — this is a copy/presentation-only change.

### Key Entities

- **Localization string entry**: A `key → text` pair existing once per supported language file; this feature changes the *text* for a defined subset of existing keys (and may add new tooltip-specific keys) without altering key names that are bound from XAML/code.
- **Tooltip**: A short, localized hint string attached to an interactive control, shown on hover, describing the control's effect in friendly terms; sourced through the same localization proxy as labels.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Zero user-facing strings in the compact view, tray menu, or settings sections contain "block"/"заблокировать"/equivalent alarming phrasing for the core Steam offline-mode toggle, across all 9 supported languages.
- **SC-002**: 100% of existing localization keys remain present with non-empty, language-appropriate values in all 9 language files (parity preserved — verified by the existing localization parity test).
- **SC-003**: A first-time, non-technical user can correctly describe what the big toggle button does ("turns Steam's internet access off/on") after reading only the on-screen label, status text, and tooltip — without needing external explanation.
- **SC-004**: At least 5 primary interactive controls in the compact view, and the relevant offline-mode controls in Settings, show a localized tooltip on hover in every supported language.

## Assumptions

- The existing localization-key names referenced from XAML bindings and code (`compact.blockButton`, `tray.block`, `settings.section.firewall`, etc.) are kept as-is; only their *string values* change — renaming keys would require touching binding code and is out of scope for a copy-only change.
- "Friendly tone" is judged by the same bar the user described: a non-technical reader should come away understanding "this turns off [Steam's/this program's] internet access" without feeling like they're doing something risky — exact wording per language is left to natural, idiomatic phrasing rather than a fixed translation table.
- Tooltips are implemented using the standard WPF `ToolTip`/`ToolTipService.ToolTip` mechanism bound through the existing `Loc[...]` localization proxy, matching how other localized strings are already bound in `MainWindow.xaml`/`SettingsWindow.xaml`.
- New tooltip-specific localization keys (if introduced) follow the existing naming convention (e.g. `compact.tooltip.toggleButton`, `settings.tooltip.modeAlwaysBlock`) and are added to all 9 language files to keep parity.
- Status/error strings that describe genuine problems (firewall errors, drift, missing admin rights) stay accurate; only their tone may be softened, not their informational content.
