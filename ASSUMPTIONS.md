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
