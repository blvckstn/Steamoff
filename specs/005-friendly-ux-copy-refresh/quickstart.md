# Quickstart: Validating the Friendly "Steam Offline Mode" Copy Refresh

This guide walks through proving the feature end-to-end once implemented —
both automated (parity test) and manual (native-language read-through + live
tooltip check across languages).

## Prerequisites

- Built `Steamoff.App` (Release or Debug) — see repo root `build-release.ps1`
  or `dotnet build src/Steamoff.App/Steamoff.App.csproj -c Release`
- Steamoff launched **elevated** (the app requires admin rights — right-click →
  "Run as administrator", or let its own UAC prompt elevate it) so the compact
  view and Settings render with live data
- `tests/Steamoff.Tests` buildable via `dotnet test`

## Step 1 — Automated: localization parity still holds

```powershell
dotnet test tests/Steamoff.Tests --filter "FullyQualifiedName~Localization"
```

**Expected**: All localization-parity tests pass — every key (including any
new `*.tooltip.*` keys) is present with a non-empty value in all 9 language
files (`ru, en, de, es, fr, it, pl, pt, zh`). A failure here means a key was
added to one file and not the others — fix before continuing (contract §2).

## Step 2 — Manual: read the reworked copy in each language

For each of the 9 languages (switch via Settings → language section, restart
when prompted):

1. Open the compact view. Read the big toggle button label and the status text
   beneath it.
2. Right-click the tray icon. Read the "open"/toggle/mode menu items.
3. Open Settings. Read the section headers/explanatory text for additional
   folders, executables, and the renamed firewall/internet-access section.

**Expected** (contract §4 / SC-001 / SC-003): In every language, a reader who
doesn't know the app would describe what they read as "this turns Steam's (or
this program's) internet access off/on — like an offline mode", **not** as
"this blocks/attacks something". No literal "block"/"заблокировать"-style
phrasing remains in any of the in-scope strings (data-model.md §A). Phrasing
should read as natural native copy, not a stiff translation (FR-003).

Record a one-line per-language sign-off, e.g.:

```text
ru: "Автономный режим Steam" / "Steam офлайн" — clear, friendly. OK
en: "Steam Offline Mode" / "Steam is offline" — clear, friendly. OK
de: ...
```

## Step 3 — Manual: status strings stay honest where it matters

Still per language, trigger (or simulate via existing test fixtures/log
inspection) the drift-detected, firewall-error, and no-admin-rights states.

**Expected** (contract — tone vs. FR-005): these strings may read slightly
warmer than before, but still clearly communicate that something needs the
user's attention — a user must not come away thinking everything is fine when
it isn't.

## Step 4 — Manual: tooltips appear, are friendly, and are localized live

With the app running and the language set to (at least) two different
languages in turn:

1. Hover the big toggle button — confirm a short tooltip appears explaining
   what clicking it will do, matching the current toggle state's framing.
2. Hover the settings ("gear"/icon) button, the mini-log expand/collapse
   button, "open full log", and "copy diagnostics" buttons in the compact view.
3. In Settings, hover the enforcement-mode options (always-offline /
   always-online / pause monitoring) and the additional-folder / additional-exe
   enable toggles.
4. Switch the display language and re-hover the same controls.

**Expected** (contract §3 / SC-004): every named control shows a localized,
friendly-toned tooltip; switching language changes the tooltip text without an
app restart, exactly like the existing visible labels already do.

## Step 5 — Confirm no mechanics changed

```powershell
git diff --stat
```

**Expected**: Diff touches only
`src/Steamoff.Core/Resources/Localization/*.json`,
`src/Steamoff.App/Views/MainWindow.xaml`, and
`src/Steamoff.App/Views/SettingsWindow.xaml` (plus this `specs/` folder). No
changes to `ComFirewallService.cs`, `CompactViewModel.ToggleAsync`,
`TargetBuilder`, `TargetScanner`, or any `.cs` view-model/service file other
than (at most) trivial XAML-codegen artifacts (FR-009 / Constitution II).

## Done criteria

- [ ] Step 1 automated parity test passes for all 9 languages
- [ ] Step 2 sign-off recorded for all 9 languages — no alarming "block" wording remains
- [ ] Step 3 confirms drift/error/no-admin strings stay informative
- [ ] Step 4 confirms tooltips on all named controls, in at least 2 languages, updating live on language switch
- [ ] Step 5 confirms the diff is copy/XAML-only — zero mechanics changes
