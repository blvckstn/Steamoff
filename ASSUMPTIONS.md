# ASSUMPTIONS

Decisions made autonomously while building Steamoff, with rationale. These
were chosen as the safest, most production-correct option available.

## A1. Target Framework: `net8.0-windows` (LTS) targeting pack via SDK
The SDK installed is 10.0.300; `Microsoft.WindowsDesktop.App` runtime 8.0 is
not installed locally, but the .NET 8 SDK workload/targeting packs are
restored from NuGet automatically when building/publishing a `net8.0-windows`
WPF project (`Microsoft.NET.Sdk` resolves `Microsoft.WindowsDesktop.App.Ref`
8.0 via NuGet). Self-contained `win-x64` publish bundles its own runtime, so
the end user does not need .NET 8 Desktop Runtime installed. If NuGet
restoration of the 8.0 ref-pack fails in this offline-ish environment, the
TFM is documented here so it can be bumped to `net10.0-windows` with a single
line change in the `.csproj` files — the architecture does not depend on the
exact TFM.

## A2. Firewall integration: COM `INetFwPolicy2` primary, no shell fallback needed
`INetFwPolicy2`/`INetFwRule` (via the `NetFwTypeLib` COM interop, `HNetCfg.FwPolicy2`)
is fully usable from .NET via `Type.GetTypeFromProgID` + dynamic COM, requiring
no extra NuGet package and no shell-out. This is safer than `netsh`/PowerShell
(no argument escaping, no process spawning, structured rule objects, can read
`Enabled`/`Action`/`Direction`/`Profiles`/`ApplicationName` directly). The
`netsh`/PowerShell fallback described in the brief is therefore implemented as
an documented, code-isolated alternate path (`NetshFirewallBackend`) behind
the same `IFirewallService` interface, but the COM backend is the default and
primary implementation actually wired up.

## A3. Settings root: `%ProgramData%\Steamoff`, fallback `%AppData%\Steamoff`
Implemented exactly as specified — `ProgramData` is preferred because the app
runs elevated and other Windows users on the same machine should see a
consistent state; `AppData\Steamoff` (current user) is the documented fallback
if `ProgramData` is not writable.

## A4. Autostart via Task Scheduler `schtasks`/`TaskService`
Implemented through the `Microsoft.Win32.TaskScheduler`-style abstraction
(`IAutostartService`) backed by direct `schtasks.exe` invocation with fully
quoted, non-interpolated arguments (no string concatenation of user input into
shell commands) — this avoids adding a third-party COM/NuGet dependency while
keeping the same safety guarantee (argument list passed via `ProcessStartInfo.ArgumentList`,
never through `cmd /c`).

## A5. UI reference image
`ui-kit.png` found in repo root — a generic neumorphic dark/orange component
kit (buttons, toggles, progress rings, chat bubbles, search bars). Used as the
visual language reference (rounded corners, soft shadow "neumorphic" surfaces,
orange accent `#FF9F1A`, circular percentage rings for coverage indicators,
pill-style toggle switches for the big Block/Unblock control). The brief's own
color palette (section 18) is treated as authoritative for exact hex values;
`ui-kit.png` informs shapes/components.

## A6. Legacy script (`steamOff.ps1`)
Read in full. Its useful mechanics — registry-based Steam discovery
(`HKCU\Software\Valve\Steam`, `HKLM\...\Valve\Steam`), known-relative-exe list
(`steam.exe`, `steamservice.exe`, `GameOverlayUI.exe`, `steamerrorreporter*.exe`,
`steam_monitor.exe`, `bin\steamservice.exe`), the `steamwebhelper`/CEF pattern
matching, and the `steamapps\common` exclusion — were ported into
`SteamDiscoveryService`/`TargetScanner` as the seed list for "Steam Core"
detection (extended per the spec to also separately track `steamservice.exe`
and `steamwebhelper.exe`). The script itself is preserved untouched at the
repo root as legacy reference (it is **not** invoked by the app — Steamoff
never shells out to PowerShell to manage rules; see A2/Constitution §II).

## A7. Process killing
The legacy script offered `-KillSteam`. The product brief (section 9-13) does
not ask Steamoff to terminate processes, and Constitution principle II says
Steamoff must not tamper with Steam. Therefore Steamoff **does not** kill or
otherwise manage the Steam process — it only manages firewall rules. This is
called out in `KNOWN_LIMITATIONS.md` (a freshly-blocked Steam may keep an
already-open connection alive until it is restarted by the user).

## A8. Notification mechanism
`INotificationService` is implemented via WPF-hosted Windows toast/balloon
notifications through the tray `NotifyIcon` (`ShowBalloonTip`), which requires
no extra package and stays fully local/offline.

## A9. Test execution location
Builds/tests/publish are executed from this Windows PowerShell-capable
environment via the `dotnet` CLI (SDK 10.0.300 present). `RequiresAdmin`
integration tests are tagged with an xUnit trait and skipped by default
(`dotnet test --filter "Category!=RequiresAdmin"` is the default CI-safe
command); running them requires an elevated shell on a real Windows machine
with Defender Firewall enabled, and is documented as a manual verification
step in `FINAL_REPORT.md`.

## A10. Single instance & elevation relaunch
Implemented via a named Mutex + `ShellExecute`/`Process.Start` with
`UseShellExecute = true` and `Verb = "runas"`. If relaunch succeeds the
original (non-elevated) process exits; if the user cancels UAC
(`Win32Exception` code `1223` — `ERROR_CANCELLED`), the app continues in
read-only mode rather than closing.

## A11. Localization storage: embedded JSON, not `.resx`/satellite assemblies
Translation tables for all 9 languages (`ru`, `en`, `de`, `fr`, `es`, `it`,
`pt`, `pl`, `zh`) are flat `key → string` JSON files embedded as resources
under `Steamoff.Core/Resources/Localization/{code}.json` (logical name
`Steamoff.Core.Resources.Localization.{code}.json`), loaded via
`Assembly.GetManifestResourceStream` and cached per language by
`LocalizedStringProvider`. This was chosen over `.resx`/satellite assemblies
because (a) a flat dictionary is trivial to diff for "every key present in
every language" — enforced by a dedicated test
(`LocalizationServiceTests.EveryShippedLanguage_HasNonEmptyTable_WithSameKeySetAsRussian`),
(b) it embeds as a single resource with no satellite-assembly probing to
complicate the single-file self-contained publish, and (c) it's trivial to
hand-translate without code-gen tooling. See
[specs/002-steamoff-localization-settings/research.md](specs/002-steamoff-localization-settings/research.md)
R1 for the full trade-off discussion. Translation quality: Russian is
high-quality/primary (matches the rest of the app's RU-first UI text),
English is solid, the remaining seven are basic-but-complete (every key
present, no placeholders) — exactly as specified.

## A12. Live language switch: WPF indexer-binding proxy, not resource-dictionary swap
`LocalizationProxy : INotifyPropertyChanged` exposes `string this[string key]`
and raises `PropertyChanged(Binding.IndexerName)` ("Item[]") on every
`ILocalizationService.LanguageChanged`, so every
`{Binding [key], Source={StaticResource Loc}}` binding re-evaluates
immediately — no restart, no manual UI walk. This was chosen over swapping
`Application.Resources.MergedDictionaries` per language (which would require
either reloading the whole merged-dictionary set at runtime — heavy, flicker-
prone — or manually walking the visual tree to re-bind everything). The one
trade-off: *computed* C# properties that wrap `Loc[...]` (not direct XAML
indexer bindings) don't get this for free — each owning ViewModel must
explicitly subscribe to `LanguageChanged` and re-raise its own
`PropertyChanged`. Two such gaps were found and fixed during this session
(`CompactViewModel`, `SettingsViewModel` — see `IMPLEMENTATION_LOG.md` and
[specs/002-steamoff-localization-settings/research.md](specs/002-steamoff-localization-settings/research.md) R2).

## A13. Comparing two live-bound `AppLanguage`s for card highlight
`LanguageEqualityConverter` is an `IMultiValueConverter` (compares
`(card.Language, SelectedLanguage)` by `Code`), not a single-value converter
with a `ConverterParameter` — `ConverterParameter` cannot itself be a dynamic
binding, and `AppLanguage` deliberately has no `INotifyPropertyChanged` (it's
an immutable, statically-shared catalogue entry). The `IMultiValueConverter`
solves the "is this card the active selection" comparison entirely in the
view layer with no model changes. See
[specs/002-steamoff-localization-settings/research.md](specs/002-steamoff-localization-settings/research.md) R3.

## A14. Settings draft editing: clone-then-diff, not a command/undo stack
`SettingsEditSession` clones `AppSettings` twice via a `System.Text.Json`
round trip (the same camelCase + string-enum options `JsonSettingsService`
persists with) to produce independent `Original`/`Draft` instances;
`HasChanges` is a structural diff of their serialized forms, `CommitDraft`
promotes `Draft` to the new baseline, `DiscardDraft` re-clones from
`Original`. This makes "Cancel = revert everything, including a previewed
language switch" a single, trivially-correct operation, and guarantees
"what counts as a change" can never drift from "what gets persisted" (both
paths share the same serializer options). See
[specs/002-steamoff-localization-settings/research.md](specs/002-steamoff-localization-settings/research.md) R4.

## A15. Fallback language is Russian, not English
`LanguageManager.FallbackLanguageCode = "ru"`. The brief asked for "RU
primary high-quality, EN solid, others basic" — Russian therefore has the
most complete, most carefully reviewed strings, making it the safer net to
catch missing keys in any other language (including English). The
first-launch dialog also defaults to Russian on dismissal, for the same
reason. See
[specs/002-steamoff-localization-settings/research.md](specs/002-steamoff-localization-settings/research.md) R5.

## A16. No new `SettingsViewModel`/`CompactViewModel`/UI-smoke tests for feature 003
`AppServices` is a concrete `sealed class` with a parameterless constructor
that eagerly constructs real platform services (`FileLogService`,
`JsonSettingsService`, `ComFirewallService`, `TrayService`/`NotifyIcon`, COM
shortcut resolution, etc.) — there is no fakeable seam (no interface, no DI
container, no internal test-only constructor), and a grep of the existing
suite confirms **zero** tests construct `AppServices` or any
`AppServices`-dependent ViewModel today. Adding seam-free tests for
`SettingsViewModel`/`CompactViewModel`/a `SettingsWindow`-opens-once UI smoke
test would force one of: (a) writing real files to the user's AppData/registry
from a "unit" test, or (b) threading ~18 constructor parameters through a new
internal test-only `AppServices` constructor (plus `InternalsVisibleTo`) —
both are out-of-scope architecture changes for a brief that explicitly says
"fix the UI, preserve the App/Core/Infrastructure/Tests architecture, don't
rewrite from scratch." Decision: covered the new logic at the layer where it
*is* seam-friendly instead — `PathNormalizationService`/`SteamPathValidator`
got 20 focused unit tests (`PathNormalizationServiceTests`,
`SteamPathValidatorTests`, real temp-directory trees + a fake shortcut
resolver delegate, no registry/COM access), and localization parity for the
~33 new keys is already exercised end-to-end by the existing
`LocalizationServiceTests.EveryShippedLanguage_HasNonEmptyTable_WithSameKeySetAsRussian`.
See `specs/003-steamoff-settings-paths-ui-fixes/tasks.md` H3/H4/H6 and
`KNOWN_LIMITATIONS.md`.

## A17. `IsRestartRequired`/`RuntimeLanguage`/`SelectedLanguage` are derived, not stored
The brief specified these as mutable `{ get; set; }` properties with an
explicit state machine (Apply/Save/Cancel/Restart). Rather than adding three
new pieces of independently-mutable state that could drift apart (and would
duplicate the already-existing `_session.Draft.Language`/
`ILocalizationService.CurrentLanguage`), they are exposed as **derived,
read-only** values:
- `RuntimeLanguage` = `ILocalizationService.CurrentLanguage.Code`
- `SelectedLanguage` = `_session.Draft.Language`
- `IsRestartRequired` = `LanguageRestartState.IsRestartRequired(SelectedLanguage, RuntimeLanguage)`
  ⇔ the two codes differ (ordinal, case-insensitive)

This single derived expression reproduces every transition the brief
describes — including the subtle "Cancel resets `IsRestartRequired` to false
unless other *persisted* pending changes remain" rule — for free, because
`_session.Draft` after `DiscardDraft()` reflects the *last persisted* value,
not "whatever the user most recently clicked." It is the safer production
choice: fewer moving parts, impossible to desynchronize, zero persistence
changes. See [specs/004-steamoff-localized-logs-release-flow/research.md](specs/004-steamoff-localized-logs-release-flow/research.md) R1
and [contracts/language-restart.md](specs/004-steamoff-localized-logs-release-flow/contracts/language-restart.md).

## A18. `ReleaseBuild*` log keys exist in localization tables but are not written via `ILocalizedLogService`
`ReleaseBuildStarted/Completed/Failed` are translated into all 9 languages
(the brief lists them among the ~30 events to template), but `build-release.ps1`
writes its own plain-text, bilingual `release-log.txt` directly — a
standalone PowerShell process has no running `ILocalizationService` instance
and no `RuntimeLanguage` (that's a property of a *live Steamoff process*).
The keys remain available — and parity-tested — for a hypothetical future
in-app "trigger a release build" feature. See
[specs/004-steamoff-localized-logs-release-flow/research.md](specs/004-steamoff-localized-logs-release-flow/research.md) R5.

## A19. `build-release.ps1` may force-close Steamoff (unlike the human-in-the-loop choice in feature 003)
During feature 003's publish, a running Steamoff instance (PID 148260) was
deliberately *not* force-killed — that was a one-off interactive build where
leaving the human in control was safer. Here the brief explicitly asks for
an **automated** "terminate Steamoff if running" step (§7/§10), so
`build-release.ps1` is allowed to `Stop-Process -Force` — but only after a
soft-close attempt (`CloseMainWindow` + 3-5s wait), and only against
processes that pass a strict double guard: name starts with `"Steamoff"`
**and** `MainModule.FileName` resolves to a path inside Steamoff's own
`bin\`/`release\`/`publish*\` trees. `steam.exe`/`steamwebhelper.exe`/any
third-party or out-of-tree process can never match. See
[specs/004-steamoff-localized-logs-release-flow/research.md](specs/004-steamoff-localized-logs-release-flow/research.md) R7
and [contracts/release-build-flow.md](specs/004-steamoff-localized-logs-release-flow/contracts/release-build-flow.md) "Process safety".

## A20. Publish output renamed to `Steamoff.exe` post-publish, `.csproj` untouched
The brief's exact two-variant layout requires each output to be named
`Steamoff.exe`, but `Steamoff.App.csproj`'s `<AssemblyName>` is
`Steamoff.App` (and changing it would ripple into other path assumptions,
shortcuts, and the existing single-file publish referenced throughout
`README.md`/`FINAL_REPORT.md`). `build-release.ps1` therefore publishes
normally (producing `Steamoff.App.exe`) and renames the single output file to
`Steamoff.exe` inside each variant folder — a smaller, fully-reversible,
script-local change that satisfies the brief's exact required filenames
without touching the project's assembly identity. See
[specs/004-steamoff-localized-logs-release-flow/contracts/release-build-flow.md](specs/004-steamoff-localized-logs-release-flow/contracts/release-build-flow.md)
"Publish commands".

## A21 — "Restart now" reuses `IElevationService.TryRelaunchElevated`

`SettingsViewModel.RestartNowCommand` → `App.RestartApplication()` does not
duplicate process-relaunch logic. It calls the existing
`IElevationService.TryRelaunchElevated(arguments, out failureReason)`
(`src/Steamoff.Infrastructure/UserContext/ElevationService.cs`), which already
resolves `Process.GetCurrentProcess().MainModule.FileName`, builds a
`ProcessStartInfo` with `UseShellExecute = true, Verb = "runas"`, preserves
`Environment.GetCommandLineArgs()`, and handles `Win32Exception` (including
UAC-cancel, error 1223) with localized failure reasons.

**Why:** Steamoff always requires administrator rights to manage Defender
Firewall rules — the app already shows a UAC prompt on every elevated launch,
so "runas" on restart is not an *extra* prompt, it's the *same* one the user
already expects. Writing a second, non-elevated relaunch path would (a) leave
the relaunched instance under-privileged, requiring yet another elevation
round-trip, and (b) duplicate argument-quoting/Win32-error-handling logic that
is already implemented and exercised. Reuse keeps a single source of truth for
"how Steamoff relaunches itself".

**How to apply:** On success, `RestartApplication()` calls the existing
`ExitApplication()` teardown path (`_settingsWindow?.Close()`,
`_mainWindow?.Close()`, `Shutdown()` — which also disposes the tray via
`OnExit`). On failure, it logs `LogEventKey.RestartFailed` with the returned
`failureReason` and shows a balloon notification via `INotificationService`
using the localized `settings.toast.restartFailed` string — the current
instance is left running untouched, exactly as `contracts/language-restart.md`
specifies.

## A22 — `DiagnosticsSnapshot` field names spell out `CurrentLanguageCode`/`SelectedLanguageCode` rather than reusing the contract's `RuntimeLanguage`/`SelectedLanguage`

`contracts/language-restart.md` defines `RuntimeLanguage` (=
`_services.Localization.CurrentLanguage.Code`, the language the running
process actually renders in) and `SelectedLanguage` (= `_session.Draft.Language`,
the Settings-window draft pick, which can differ from both the runtime and the
persisted value while the window is open). `DiagnosticsSnapshot` needs the
*persisted* value — `settings.Language` — not the live draft, because the
snapshot can be built from outside an open Settings window (e.g. "Copy
diagnostics" from the journal, or a future CLI/background path) where no
`SettingsEditSession`/`Draft` exists at all.

**Why:** Reusing `SelectedLanguage` verbatim for a different underlying value
(`settings.Language` vs `_session.Draft.Language`) would silently conflate two
distinct concepts that the language-restart contract is careful to keep apart
— a future reader skimming both documents would reasonably assume they're the
same field. Spelling out `CurrentLanguageCode` (= contract's `RuntimeLanguage`)
and `SelectedLanguageCode` (= contract's persisted `settings.Language`, *not*
its `Draft.Language`) makes the snapshot's actual data source unambiguous from
the property name alone, without requiring a cross-reference to the
session-draft contract to disambiguate.

**How to apply:** `DiagnosticsService.BuildSnapshotAsync` sets
`CurrentLanguageCode = _localization.CurrentLanguage.Code` and
`SelectedLanguageCode = settings.Language` (`DiagnosticsService.cs:99-100`).
`IsRestartRequired` is computed from these two exactly as the contract's
`IsRestartRequired` is from `RuntimeLanguage`/`SelectedLanguage`
(`LanguageRestartState.IsRestartRequired`, ordinal case-insensitive
comparison) — the *logic* is identical, only the snapshot's field names make
explicit which of the contract's two language notions ("what's running" vs
"what's persisted") each one captures.

## A23 — `DiagnosticsService.LastReleaseBuildPath` reads a hardcoded absolute path to this checkout's `release\release-manifest.json`

`DiagnosticsSnapshot.LastReleaseBuildPath` reports where the most recent
`.\build-release.ps1` run wrote its output, for the "Diagnostics" panel and
the extended report's `diagnostics.field.lastReleaseBuildPath` line. The brief
fixes the release output location to `src/Steamoff.App/release/` *relative to
this repository* (see A20/A24 and `contracts/release-build-flow.md`) — there
is no per-machine settings key, environment variable, or discoverable
manifest-search convention for it, and inventing one would be exactly the
kind of speculative abstraction the brief says not to add ("не переписывай...
исправляй поверх существующей архитектуры").

**Why:** The only "honest" way to report a path that is contractually fixed
to a specific location in *this* checkout is to hardcode that location and
check whether a manifest actually exists there
(`File.Exists(ReleaseManifestPath) ? Path.GetDirectoryName(...) : null` —
`DiagnosticsService.cs:221-222`). Deriving it dynamically (e.g. walking up
from `AppContext.BaseDirectory` looking for `Steamoff.slnx`, the way
`ReleaseScriptTests.FindRepoRoot` does for test purposes) would be more
"portable" in the abstract, but the published `Steamoff.exe` runs from
`release\Steamoff-with-dotnet-runtime\`, far outside the source tree, where no
such walk could ever resolve back to a development checkout — so the
"portable" version would just always return `null` in the one binary users
actually run, which is strictly worse than a value that is at least correct
for the development/CI machine that builds and inspects it.

**How to apply:** `ReleaseManifestPath` in `DiagnosticsService.cs:28` is a
`private const string` literal pointing at
`C:\Users\adm\Desktop\13\vibe\Steamoff\src\Steamoff.App\release\release-manifest.json`.
`FindLastReleaseBuildPath()` returns `null` whenever that file is absent —
exactly the expected, correct value on a fresh checkout or on an end-user
machine running the published binary, where "no diagnostic info about a
release build that was never produced on this machine" is the only honest
answer. `DiagnosticsSnapshotTests.BuildSnapshotAsync_PopulatesAllFields_FromCollaborators`
mirrors this same `File.Exists` check in its assertion (rather than assuming
either presence or absence) so the test is correct in both states of a
checkout — see `DiagnosticsSnapshotTests.cs:76-81` and the "Feature 004" entry
in `IMPLEMENTATION_LOG.md`.

## A24 — `Test-SteamoffManagedProcessPath` extracted as a named, self-test-able PowerShell function rather than left as an inline `Where-Object` predicate

I5 (`tasks.md`) calls for the release-script's process-safety path-matching
predicate to be "a pure function, extracted and tested in isolation". The
predicate decides whether a candidate process's `MainModule.FileName` lies
inside one of Steamoff's own build-output trees (`bin\`, `release\`,
`publish*\`) — the path half of the "never touch Steam" double guard from
`contracts/release-build-flow.md` "Process safety". `build-release.ps1` is
PowerShell, and the project has no Pester/cross-language test harness, so a
C# test cannot import or directly invoke a PowerShell function.

**Why:** Two alternatives were rejected: (a) leaving the predicate as an
inline `Where-Object { ... }` block (untestable from C#, and the brief
explicitly calls it out as needing isolation testing), or (b) reimplementing
the same matching logic in C# purely for test purposes (duplicating the rule
in two languages — a maintenance hazard where the two copies could silently
drift, exactly the kind of risk the project's existing whole-table parity
tests for localization were written to prevent). Extracting the predicate
into a named `function Test-SteamoffManagedProcessPath { param($Path,
$RepoRoot) ... }` and exposing a `-TestProcessPath <path>` CLI parameter that
evaluates it and exits lets the *real* production predicate run as a
subprocess from `ReleaseScriptTests`, with zero duplicated logic and zero new
test infrastructure.

**How to apply:** `build-release.ps1` declares `[CmdletBinding()] param([string]
$TestProcessPath)`; when the parameter is bound, it calls
`Test-SteamoffManagedProcessPath -Path $TestProcessPath -RepoRoot $RepoRoot`,
prints `True`/`False` to stdout, and exits 0 — without touching restore/build/
test/publish at all (`build-release.ps1:14-23,63-66`). The main pipeline (step
5, "find & close running Steamoff") calls the exact same function.
`ReleaseScriptTests.InvokeScriptSelfTest` runs `powershell.exe -File
build-release.ps1 -TestProcessPath "<candidate>"` as a subprocess and parses
the trimmed stdout as a boolean (`ReleaseScriptTests.cs:134-141`), exercising
both "accepts the three managed trees" and "rejects anything that looks like
Steam or lies outside the repo" cases.
