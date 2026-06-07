# Steamoff

A Windows desktop "smart network-access switch" for Steam — block or allow
Steam (and any other apps/folders you choose) at the network level using
**only** Microsoft Defender Firewall rules. No process killing, no DRM
patches, no hacks: Steamoff reads and writes real Windows Firewall rules
through the same COM API the Defender UI itself uses, and nothing else.

## What it does
- One big toggle: **Block / Unblock Steam** — creates/removes outbound (and,
  where relevant, inbound) Defender Firewall block rules for the full Steam
  process set (`steam.exe`, `steamservice.exe`, `steamwebhelper.exe`,
  `GameOverlayUI.exe`, `steamerrorreporter*.exe`, `steam_monitor.exe`, CEF
  helper processes, etc. — discovered from the registry
  `HKCU/HKLM\Software\Valve\Steam`).
- **Custom targets**: add your own folders or individual `.exe` files to the
  same on/off switch — via picker dialogs **or by dragging and dropping**
  files/folders/`.lnk` shortcuts straight onto the Settings cards. Every path
  goes through the same normalize → validate → de-duplicate pipeline
  (handles quotes, env vars, `/` vs `\`, duplicated separators, UNC paths,
  and `.lnk` resolution) and shows a live green/red/yellow/gray status
  indicator — see `specs/003-steamoff-settings-paths-ui-fixes/`.
- **Mini-log in the compact view**: the last 30 log lines, color-coded by
  level, refreshed every 5 seconds, with one-click "open full log" and "copy
  diagnostics report" actions.
- **Live status verification**: Steamoff doesn't just remember "I created a
  rule" — it re-reads the actual current Defender Firewall rule state every
  time, so the displayed status always reflects reality (including if you or
  another tool changed the rules outside the app).
- **Tray-resident**: runs from the system tray; the compact view gives an
  at-a-glance status pill, the big toggle, and quick access to Settings.
- **Two views**:
  - *Compact (Steam Switch) View* — the everyday on/off control.
  - *Settings View* — language, modes, custom paths/executables, autostart,
    diagnostics ("Test"/"Status").
- **9 languages** with instant, no-restart switching: Russian (primary,
  highest quality), English, German, French, Spanish, Italian, Portuguese,
  Polish, and Chinese. (No Ukrainian — by design, see `ASSUMPTIONS.md` A15.)
  English always displays as **EN**, never **GB**.
- **First-launch language picker** — a neumorphic "choose your language"
  dialog appears on first run; dismissing it without choosing defaults to
  Russian (the fallback language) and the dialog never reappears.
- **Settings editing workflow**: every change is staged in a draft you can
  **Test** (run diagnostics against the *pending* changes before committing),
  check **Status** against, **Apply** (save, keep editing), **Save** (save
  and close), or **Cancel** (discard everything, including any previewed
  language switch, and roll back to the last saved state).
- **Autostart** via Windows Task Scheduler (no registry Run-key hacks).
- **JSON-persisted settings** under `%ProgramData%\Steamoff` (falls back to
  `%AppData%\Steamoff` if that's not writable).

## What it deliberately does NOT do
- Does not kill, suspend, or patch the Steam process or any of its files.
- Does not use `netsh`/PowerShell shell-outs for firewall changes (COM
  `INetFwPolicy2` only — see `ASSUMPTIONS.md` A2).
- Does not phone home: no telemetry, no cloud APIs, fully offline.
- Does not touch firewall rules it didn't create itself.

## Tech stack
- .NET 8 (LTS), C#, WPF + MVVM (hand-rolled `ObservableObject`/`RelayCommand`)
- Self-contained, single-file `win-x64` publish (no runtime install required
  on the target machine)
- xUnit test suite
- Layered solution: `Steamoff.Core` (models/interfaces/localization data) →
  `Steamoff.Infrastructure` (firewall/settings/autostart/process services) →
  `Steamoff.App` (WPF UI, ViewModels, tray) → `Steamoff.Tests`

## Project layout
```
src/Steamoff.Core/            domain models, interfaces, LanguageManager,
                              LocalizedStringProvider, embedded translation
                              tables (Resources/Localization/{code}.json)
src/Steamoff.Infrastructure/  IFirewallService (COM INetFwPolicy2 backend +
                              documented Netsh fallback), JsonSettingsService,
                              autostart, Steam discovery/diagnostics
src/Steamoff.App/             WPF app: Views, ViewModels, LocalizationProxy,
                              tray (TrayService), App.xaml.cs orchestration
tests/Steamoff.Tests/         xUnit tests + fakes (FakeLogService,
                              FakeLocalizationService) in TestSupport/
specs/                        SpecKit docs:
                              001-steamoff-smart-firewall-switch (core app)
                              002-steamoff-localization-settings
                              003-steamoff-settings-paths-ui-fixes (this feature)
steamOff.ps1                  legacy reference script (kept untouched, never
                              invoked by the app — see ASSUMPTIONS.md A6)
```

## Building & running
```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release   # set DOTNET_ROLL_FORWARD=LatestMajor and
                         # DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 first if your
                         # machine lacks the exact net8.0 desktop runtime —
                         # see KNOWN_LIMITATIONS.md
dotnet publish src/Steamoff.App/Steamoff.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```
The published EXE lands in
`src/Steamoff.App/bin/Release/net8.0-windows/win-x64/publish/Steamoff.App.exe`.
Run it elevated (it will prompt for UAC on its own) — Defender Firewall rule
management requires Administrator rights.

## Producing a release build (`build-release.ps1`)
Run from the repo root (it verifies `Steamoff.slnx` exists in the working
directory and refuses to run anywhere else):
```powershell
.\build-release.ps1
```
This single script runs the full release pipeline end to end: `dotnet
restore`/`build -c Release`/`test -c Release` (with the `DOTNET_ROLL_FORWARD`
workaround already set internally), closes any currently-running `Steamoff`
instance it finds under its own `bin\`/`release\`/`publish*\` trees (it never
touches `steam.exe` or anything outside the repo — see ASSUMPTIONS.md A19/A24),
cleans and recreates `src/Steamoff.App/release/`, and publishes **both**
required variants there:

```
src/Steamoff.App/release/
  Steamoff-with-dotnet-runtime/      Steamoff.exe + README-RUN.txt   (self-contained, ~68 MB)
  Steamoff-without-dotnet-runtime/   Steamoff.exe + README-RUN.txt   (framework-dependent, ~0.5 MB)
  release-manifest.json              versions, sizes, SHA-256 hashes of both outputs
  release-log.txt                    timestamped, bilingual (RU/EN) pipeline log
```

`release\` always contains the **latest** build only — every run empties and
repopulates it from scratch (see ASSUMPTIONS.md A20/A23 for why the output is
named `Steamoff.exe` and where `release-manifest.json` lives). The script
exits non-zero and writes an `ОШИБКА`/`ERROR` line to the log on the first
failing step (wrong working directory, failing tests, build errors, a stuck
running instance, etc.) — see `contracts/release-build-flow.md` in
`specs/004-steamoff-localized-logs-release-flow/` for the exact contract this
script implements, and `ReleaseScriptTests.cs` for its automated coverage.

## Settings & data location
`%ProgramData%\Steamoff\settings.json` (or `%AppData%\Steamoff\settings.json`
as a fallback). The file is versioned (`CurrentVersion`); older files are
migrated in place on load — see `ASSUMPTIONS.md` A3/A14 and
`specs/002-steamoff-localization-settings/data-model.md`.

## Documentation
- `ASSUMPTIONS.md` — every autonomous design decision, with rationale
- `IMPLEMENTATION_LOG.md` — every build error hit and how it was fixed
- `KNOWN_LIMITATIONS.md` — honest current limitations
- `FINAL_REPORT.md` — end-to-end reports for the localization/settings
  feature (002), the settings-paths/UI-fixes feature (003), and the
  localized-logs/release-flow feature (004)
- `specs/004-steamoff-localized-logs-release-flow/` — SpecKit spec/plan/
  research/data-model/quickstart/tasks/contracts for the language-restart
  state machine, localized logging (journal panel + 14 settings actions),
  localized diagnostics, and the `build-release.ps1` release pipeline
- `specs/003-steamoff-settings-paths-ui-fixes/` — SpecKit spec/plan/research/
  data-model/quickstart/tasks/contracts for the Settings paths (Steam path,
  Folders, EXE Files), drag&drop, mini-log, and UI-fixes feature
- `specs/002-steamoff-localization-settings/` — SpecKit spec/plan/research/
  data-model/quickstart/tasks/contracts for the localization feature
- `specs/001-steamoff-smart-firewall-switch/` — SpecKit docs for the core app
