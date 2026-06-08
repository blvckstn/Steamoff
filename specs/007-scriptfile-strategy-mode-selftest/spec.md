# Feature Specification: ScriptFile Firewall Strategy, Strategy Mode Selection & First-Run Self-Test

**Feature Branch**: `007-scriptfile-strategy-mode-selftest`

**Created**: 2026-06-08

**Status**: Draft

**Input**: User description: "Third firewall fallback strategy: file-based elevated PowerShell script (ScriptFile strategy), plus user-selectable strategy mode and first-run self-test — adapted from the proven steamOff.ps1 mechanics, launched as an actual elevated .ps1 file (not inline -Command), with Set-ExecutionPolicy -Scope Process Bypass run first; add a Settings choice 'Авто'/'Вариант 1'/'Вариант 2'/'Вариант 3' where Авто remembers and prefers whichever strategy worked last time; and run a safe, non-destructive self-test of all three strategies on first launch so Авто doesn't have to learn by failure."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A third, proven escape-hatch strategy when COM and inline-PowerShell both fail (Priority: P1)

A user whose security software interferes with both the COM-based strategy and the inline-PowerShell (`-Command`) strategy has independently confirmed that running an actual `.ps1` script file directly (as the standalone `steamOff.ps1` prototype does) reliably creates working firewall rules on their machine. Today the app has no way to use that proven path — when both of its built-in strategies are silently disrupted, blocking/unblocking fails outright. This story adds a third strategy that writes an adapted script file to disk and runs it as an elevated file (mirroring the one path the user has proven works), using the exact same rule names and group as the other two strategies so cleanup and status reporting keep working uniformly.

**Why this priority**: Without this, the affected user (and anyone with similar security software behavior) has no way to reliably block Steam through the app at all — they would have to fall back to running a separate, unintegrated script by hand. This is the difference between "the app works for me" and "the app doesn't work for me, I have to use a workaround outside it."

**Independent Test**: Force the app (via the new strategy-mode setting from User Story 2) to use only the third strategy, trigger "Block Steam", and verify that the expected `Steamoff`-named/grouped rules appear, are enabled, target the correct executables, and that "Unblock Steam" removes/disables them again — all without invoking the COM or inline-PowerShell strategies.

**Acceptance Scenarios**:

1. **Given** the app is set to use the third strategy, **When** the user turns Steam blocking on, **Then** firewall rules appear using the exact same naming convention and group as the other two strategies (so the user sees one consistent rule set regardless of which strategy created it), and Steam's network access is blocked.
2. **Given** rules were created by the third strategy, **When** the user turns Steam blocking off, **Then** those rules are removed or disabled (per the existing cleanup convention) and Steam's network access is restored.
3. **Given** one of several target executables cannot be processed (e.g., a transient file lock), **When** the third strategy runs, **Then** it skips that target, logs a warning, and continues processing the rest — exactly like the other two strategies already do.
4. **Given** the third strategy has run multiple times across app sessions, **When** it needs its on-disk script file, **Then** it reuses or safely refreshes that file rather than accumulating multiple stale/conflicting copies.

---

### User Story 2 - Choosing and trusting a specific strategy via Settings (Priority: P2)

A user who wants to understand or control exactly how the app applies firewall rules — for example, to deliberately test which path works on their machine, or to lock in the one that they know works and skip the others — can open Settings and choose between "Авто" (the app decides, preferring whatever worked most recently) and three explicit single-strategy modes ("Вариант 1", "Вариант 2", "Вариант 3"). When a specific variant is chosen, the app uses only that one and reports plainly whether it succeeded — it does not quietly try the others behind the user's back. This turns "I don't know why it isn't working" into "I can see exactly which path works and choose it directly."

**Why this priority**: This is what makes the new third strategy (and the existing two) actually *usable* for diagnosis and trust-building — without it, users cannot tell which path is responsible for success or failure, and cannot lock in a known-good choice. It directly answers the user's frustration about not understanding what the app is doing.

**Independent Test**: Open Settings, switch the strategy mode to each of the four options in turn, trigger a block/unblock operation under each, and confirm: (a) forced single-variant modes only ever exercise that one strategy and clearly report its own success/failure without silent fallback, and (b) "Авто" mode's behavior is observably influenced by which strategy most recently succeeded.

**Acceptance Scenarios**:

1. **Given** the user opens Settings, **When** they view the firewall strategy option, **Then** they see four clearly labeled choices: "Авто", "Вариант 1", "Вариант 2", "Вариант 3", with the current selection indicated and a short explanation of what each means.
2. **Given** the user selects "Вариант 2" and triggers blocking, **When** that strategy fails, **Then** the app reports the failure plainly (technical + user-facing log) and does **not** silently try Вариант 1 or 3 — the user sees exactly what happened with the path they chose.
3. **Given** the user selects "Авто" and the third strategy succeeded the last time rules were applied, **When** they trigger blocking again, **Then** the app tries that remembered strategy first, and only falls through to the others if it fails this time (self-healing if conditions change).
4. **Given** the user changes the strategy mode in Settings, **When** they return to the main window and use the toggle, **Then** the new mode takes effect immediately without requiring an app restart.

---

### User Story 3 - Knowing which path works from the very first launch (Priority: P3)

When a user installs and runs the app for the very first time, the app quietly checks — in a way that is completely invisible and harmless to the user and to Steam — which of the three strategies actually works on their specific machine, and remembers the answer. From that point on, "Авто" mode already knows which path to try first, instead of the user having to discover it the hard way (e.g., seeing a failed first attempt). The user can also see, in the log, a clear one-time summary of what the self-test found — "on your machine, strategy X works; strategies Y/Z did not" — so the very first thing they learn about the app's firewall layer is reassuring and informative rather than confusing.

**Why this priority**: This is a quality-of-life and trust-building improvement on top of Stories 1 and 2 — it removes the "first real attempt might fail and confuse the user" risk and gives "Авто" a head start, but the app is fully usable (with manual strategy selection) without it.

**Independent Test**: Reset the app to a "never run before" state, launch it, and verify: (a) a one-time self-test runs automatically, (b) it leaves no trace in the live firewall rule set afterward (no leftover probe rules, no change to any existing Steamoff-managed rules, no observable interruption to Steam or any other app), (c) its outcome is clearly logged in both the technical and the localized user-facing log, and (d) "Авто" mode's first real operation immediately prefers whichever strategy the test found working — without launching the test again on subsequent app starts.

**Acceptance Scenarios**:

1. **Given** the app has never run on this machine before, **When** it starts up, **Then** it automatically and safely probes all three strategies, records which one(s) work, and never repeats this probe on later launches.
2. **Given** the self-test has completed, **When** the user inspects the firewall rules and the log, **Then** there are no leftover probe rules, no changes to any pre-existing Steamoff-managed rule, and a clear, readable summary of the test's findings in the localized log.
3. **Given** the self-test found that only one particular strategy works, **When** the user (in "Авто" mode) performs their first real "Block Steam" action, **Then** the app uses that strategy first rather than starting from the beginning of the cascade.
4. **Given** the self-test is in progress, **When** the user looks at Steam or any other running application, **Then** they observe no interruption, blocking, or behavior change whatsoever — the probe is fully invisible to anything outside the app's own diagnostics.

---

### Edge Cases

- What happens if the on-disk script file used by the third strategy is deleted, corrupted, or modified by something else between app runs? → The app must detect this and regenerate/refresh it safely before relying on it again.
- What happens if the self-test itself cannot run cleanly (e.g., it is interrupted, or none of the three strategies work at all)? → The app must record that outcome too (rather than silently treating it as "untested"), must not retry it on every subsequent launch, and "Авто" mode must still fall back to its full ordered cascade.
- What happens if the user forces a specific variant that turns out not to work on their machine at all? → The app must clearly explain, in the user-facing log/summary, that the chosen variant failed and that "Авто" or another variant may work better — without changing the user's choice for them.
- What happens if the user switches strategy mode mid-operation (e.g., while a block/unblock is in progress)? → The change must apply starting from the next operation; an in-flight operation must complete using the mode it started with.
- What happens across an app update where the third strategy's on-disk script content changes (e.g., a bug fix to the adapted script)? → The refreshed script must replace the old one safely, without leaving both versions present or confusing the rule-ownership/cleanup logic.
- What happens if the remembered "last successful strategy" becomes unavailable or starts failing? → "Авто" mode must notice, fall through the rest of the cascade as it always has, and update its remembered preference to the new winner.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a third way to apply, remove, and query Steam-blocking firewall rules — based on running an actual script file (adapted from the user's proven `steamOff.ps1` approach) rather than COM automation or an inline command string — that produces rules indistinguishable in name, group, and blocking behavior from the other two paths.
- **FR-002**: The third strategy MUST use the exact same rule-group identity and the exact same rule-naming convention as the existing two strategies, so that rules it creates are recognized, displayed, counted, and cleaned up identically to rules created by either of the others, and so a user can never end up with two differently-named "families" of Steamoff rules.
- **FR-003**: The third strategy MUST apply the same per-target resilience already established: if it cannot process one target (application) it must skip that one, record why, and continue with the rest — never aborting the whole operation because of a single problematic target.
- **FR-004**: The third strategy MUST run with administrative rights without prompting the user for elevation again (the app already runs elevated) and MUST ensure nothing about script-execution restrictions can prevent it from creating rules — without permanently changing any machine-wide or user-wide script-execution setting.
- **FR-005**: The on-disk script file the third strategy depends on MUST be created if missing, and safely refreshed/replaced if its expected content has changed (e.g., after an app update) — the system MUST never end up relying on a stale, partially-written, or unexpectedly-modified copy, and MUST never accumulate multiple conflicting copies across runs.
- **FR-006**: For every block/unblock operation, the system MUST clearly record — in both the technical log and the user-facing localized log — which of the three strategies actually performed the work, mirroring the existing convention for the first two strategies.
- **FR-007**: Users MUST be able to open Settings and choose how the system decides which strategy to use, from exactly four options: "Авто" (system decides), "Вариант 1", "Вариант 2", "Вариант 3" (each forcing one specific strategy). The current choice MUST be visible, and changing it MUST take effect for the next operation without requiring a restart.
- **FR-008**: When the user has forced a specific variant, the system MUST use only that one strategy for the operation — it MUST NOT silently attempt either of the other two — and MUST clearly report, to both logs, whether that single forced attempt succeeded or failed and why.
- **FR-009**: When the mode is "Авто", the system MUST remember which strategy most recently succeeded (persisted across app restarts) and MUST try that one first; if it fails this time, the system MUST fall through the remaining strategies in a defined order, exactly as today's two-strategy fallback already behaves, and MUST update its remembered preference to whichever strategy ultimately succeeds.
- **FR-010**: On the very first launch of the application on a given machine (and never again afterward), the system MUST automatically run a safe, non-destructive check of all three strategies to learn which one(s) actually work, and MUST use that result to set the initial "remembered last-successful strategy" used by "Авто" mode.
- **FR-011**: The first-run check MUST be fully invisible and harmless from the user's and Steam's perspective: it MUST NOT create any rule that persists afterward, MUST NOT alter, remove, or interfere with any pre-existing Steamoff-managed rule, and MUST NOT cause any observable interruption to Steam or to any other running application — even momentarily.
- **FR-012**: The outcome of the first-run check MUST be clearly recorded in both the technical log and the user-facing localized log, in terms a non-technical user can understand (which path(s) worked, which didn't, and what that means for how the app will behave going forward).
- **FR-013**: If the first-run check itself cannot complete cleanly (interrupted, environment doesn't allow it, or none of the three strategies work), the system MUST record that outcome distinctly (not indistinguishable from "never checked"), MUST NOT re-run the check on every subsequent launch, and "Авто" mode MUST continue to operate via its full ordered fallback cascade.
- **FR-014**: Switching the strategy-mode setting MUST NOT affect an operation that is already in progress; the in-progress operation MUST complete using the mode that was active when it started, and the new mode MUST apply starting with the next operation.
- **FR-015**: All existing behavior, naming, grouping, and user-facing copy established by prior features MUST remain unchanged for users who keep the default "Авто" mode and never interact with the new setting — this feature is additive, not a redesign of existing flows.

### Key Entities

- **Firewall Strategy Mode**: The user's chosen approach to deciding which underlying mechanism applies firewall rules. One of: Auto (system decides, prefers last success), Variant 1 (force first/COM-based), Variant 2 (force second/inline-PowerShell-based), Variant 3 (force third/script-file-based). Persisted as part of the user's settings.
- **Remembered Last-Successful Strategy**: A small piece of persisted state recording which of the three strategies most recently completed a block/unblock operation successfully — consulted by Auto mode to decide which one to try first, and updated whenever any strategy succeeds or the remembered one stops working.
- **First-Run Self-Test Record**: A one-time, persisted outcome describing whether the automatic first-launch check ran, and if so, which strategies it found to be working on this machine — consulted to seed the Remembered Last-Successful Strategy and to avoid repeating the check on later launches.
- **ScriptFile Strategy Script**: The on-disk script file the third strategy depends on — its presence and expected content are verified/refreshed as needed before each use, and it is adapted from the proven `steamOff.ps1` prototype but conforms to the app's existing rule-naming and grouping conventions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a machine where neither of the app's first two strategies can create working firewall rules (due to third-party security software interference), the user can successfully block and unblock Steam through the app — using the third, file-based strategy — with the same one-click experience as on any other machine.
- **SC-002**: A user can determine, within one settings interaction and one block/unblock attempt, exactly which of the three strategies is responsible for success or failure on their machine — with no ambiguity about whether a "silent" fallback occurred.
- **SC-003**: After the very first launch, "Авто" mode's first real block/unblock attempt uses a working strategy on the first try at least as often as it would after the user had manually discovered the right one — i.e., the app does not need a "failed first attempt" to learn what the self-test could have told it immediately.
- **SC-004**: The first-run check completes without the user ever noticing any change in Steam's connectivity, any new firewall rule appearing in their security software's UI afterward, or any prompt/interruption — confirmed by inspecting the rule set immediately before and after first launch and finding it unchanged apart from the recorded test outcome.
- **SC-005**: Across 50 consecutive block/unblock cycles in "Авто" mode on a machine where exactly one strategy reliably works, at least 49 of those cycles succeed on the first attempted strategy (i.e., "Авто" correctly remembers and prefers the working one, rather than re-discovering it each time).
- **SC-006**: Users who never open the new setting see no difference whatsoever in the app's behavior, copy, or flow compared to before this feature shipped.

## Assumptions

- The application continues to run with administrative privileges throughout its session (an existing, established precondition for all firewall-related features) — the third strategy can rely on this and does not need to request elevation separately.
- "Adapted from `steamOff.ps1`" means reusing its proven mechanics for creating/removing/querying rules via the NetSecurity PowerShell surface from an actual script file — not its naming convention, menu/interactive flow, or any of its other prototype-only behaviors, all of which are explicitly out of scope and must not leak into the product.
- A small number of probe operations during the first-run check (creating and immediately removing a harmless, clearly-temporary rule, or an equivalent non-destructive verification) is an acceptable and standard way to determine "does this mechanism actually work here" — as long as it is fast, leaves no trace, and never overlaps with real Steam-blocking rules.
- The definition of "first launch" is machine-and-installation scoped (tracked in the app's own persisted settings/state), not tied to any external account or online service — reinstalling or resetting the app's local state is an acceptable, expected way for a user (or support engineer) to make the check run again if ever needed.
- The four-option strategy-mode setting is presented as a single choice in the existing Settings surface, consistent with how other preferences are presented there — no new top-level UI surface is introduced.
- "Вариант 1/2/3" map directly and stably to the three strategies in the order they were introduced (COM-based, inline-PowerShell-based, script-file-based respectively); this mapping is treated as a stable identity for the lifetime of the feature so that user choices and remembered/tested outcomes remain meaningful across app updates.
