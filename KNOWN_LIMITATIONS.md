# KNOWN LIMITATIONS

Honest, current limitations of Steamoff. None of these block normal use; all
are either deliberate scope decisions (with rationale in `ASSUMPTIONS.md`) or
environment quirks of the dev machine that do not affect the shipped EXE.

## Firewall / blocking behavior
- **Already-open connections survive a fresh block** until Steam (or the
  blocked process) is restarted. Steamoff only manages Defender Firewall
  rules — by design it never terminates or otherwise tampers with the Steam
  process (see `ASSUMPTIONS.md` A7, Constitution §II "don't touch Steam").
  A connection opened before the block was applied keeps running on its
  existing socket until the OS/process closes it.
- **Requires Administrator** to create/modify/verify Defender Firewall rules
  (`INetFwPolicy2` write access). Steamoff requests elevation via UAC on
  startup; if the user declines, it runs in a read-only "view current state"
  mode rather than closing (see `ASSUMPTIONS.md` A10).
- **Per-machine, per-user-profile rules**: Defender Firewall rules created by
  Steamoff apply to the local machine's active profiles (Domain/Private/
  Public, per the brief). They are not synced across machines or Windows
  user accounts — each install manages its own rule set.

## Localization
- **Translation depth is two-tier by design** (per the original brief):
  Russian is the primary, most carefully reviewed language; English is solid;
  the remaining seven (DE, FR, ES, IT, PT, PL, ZH) are "basic-but-complete" —
  every key is present and translated (enforced by
  `LocalizationServiceTests.EveryShippedLanguage_HasNonEmptyTable_WithSameKeySetAsRussian`,
  so nothing ever falls back to a raw key or the wrong language), but they
  have not been reviewed by native speakers and may read as somewhat literal
  or stiff in places. Anyone fluent in one of these seven is welcome to
  refine `Steamoff.Core/Resources/Localization/{code}.json` — the key set is
  fixed and parity-tested, so a translation-only edit cannot break anything.
- **No runtime language download/update mechanism** — all 9 tables are
  embedded resources baked into the binary at build time (see `ASSUMPTIONS.md`
  A11). Adding a 10th language or correcting a translation requires a
  rebuild. This was a deliberate trade-off for offline-only operation (no
  cloud APIs, per the Constitution) and single-file publish simplicity.
- **No pluralization/grammatical-gender engine**: strings are flat
  `key → value` with `{0}`/`{1}`-style positional formatting
  (`ILocalizationService.GetString(key, args)` → `string.Format`). Languages
  with complex plural rules (Russian, Polish) rely on hand-written strings
  that read naturally for the specific counts the UI actually surfaces
  (mostly 0/1/many-style status counts), not a general CLDR plural engine.

## Build / development environment (does not affect the published app)
- **`dotnet test` requires a roll-forward env var on this dev machine**: the
  exact `Microsoft.WindowsDesktop.App` 8.0.0 (x64) targeting runtime isn't
  installed locally (only 6.0.16/10.0.8 x64 and 8.0.14 x86 are present), so
  `dotnet test` needs `DOTNET_ROLL_FORWARD=LatestMajor`/
  `DOTNET_ROLL_FORWARD_TO_PRERELEASE=1` to roll forward to 10.0.8 and run.
  This is purely a local-test-host concern — the published self-contained
  `win-x64` build bundles its own runtime and needs nothing installed on the
  target machine. See `IMPLEMENTATION_LOG.md` and `quickstart.md`.
- **Two pre-existing `WFAC010` high-DPI warnings** at build time
  (`Steamoff.App` mixes WPF and WinForms via `NotifyIcon`, and WinForms'
  `ApplicationHighDpiMode` analyzer flags the absence of an explicit
  `app.manifest` DPI declaration even though the WPF host already handles
  Per-Monitor-V2 DPI correctly). Cosmetic; left as-is to avoid unrelated
  scope creep in this feature branch.
- **`RequiresAdmin`-tagged integration tests are skipped by default**
  (`dotnet test --filter "Category!=RequiresAdmin"`) because they need a real
  elevated session with Defender Firewall enabled. They are documented as a
  manual verification step in `FINAL_REPORT.md` / `quickstart.md` rather than
  run in this session's automated pass (see `ASSUMPTIONS.md` A9).

## Packaging
- **Self-contained single-file publish is large** (~162 MB `Steamoff.App.exe`)
  because it bundles the entire .NET 8 Desktop runtime plus WPF/WinForms
  assemblies. This is the correct trade-off for "the end user needs nothing
  pre-installed" (see `ASSUMPTIONS.md` A1) but means the EXE is not a small
  download — there is no trimmed/framework-dependent variant shipped.
