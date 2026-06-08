# Codex Handoff For Claude Code

Date: 2026-06-08
Repo: `c:\Users\adm\Desktop\13\vibe\Steamoff`

This file is the continuation note for Claude Code after Codex took over the
work. It explains what was changed, why it was changed, how the current state
was verified, and where to continue.

## Current Working State

- The working tree is intentionally dirty. Do not reset or revert unrelated
  changes.
- Feature context is `specs/006-firewall-fallback-strategy`.
- There are also prior UI/copy changes under `specs/005-friendly-ux-copy-refresh`.
- Latest full release build succeeded.
- Latest test suite passed: 122/122 tests.

Current dirty tree snapshot at handoff:

```text
Modified tracked files:
CLAUDE.md
IMPLEMENTATION_LOG.md
src/Steamoff.App/App.xaml.cs
src/Steamoff.App/AppServices.cs
src/Steamoff.App/Themes/DarkOrange.xaml
src/Steamoff.App/ViewModels/CompactViewModel.cs
src/Steamoff.App/Views/MainWindow.xaml
src/Steamoff.App/Views/MainWindow.xaml.cs
src/Steamoff.App/Views/SettingsWindow.xaml
src/Steamoff.App/Views/SettingsWindow.xaml.cs
src/Steamoff.Core/Logging/LogEventKey.cs
src/Steamoff.Core/Logging/LogEventTemplates.cs
src/Steamoff.Core/Models/AppSettings.cs
src/Steamoff.Core/Resources/Localization/de.json
src/Steamoff.Core/Resources/Localization/en.json
src/Steamoff.Core/Resources/Localization/es.json
src/Steamoff.Core/Resources/Localization/fr.json
src/Steamoff.Core/Resources/Localization/it.json
src/Steamoff.Core/Resources/Localization/pl.json
src/Steamoff.Core/Resources/Localization/pt.json
src/Steamoff.Core/Resources/Localization/ru.json
src/Steamoff.Core/Resources/Localization/zh.json
src/Steamoff.Core/Services/StatusEvaluator.cs
src/Steamoff.Infrastructure/Firewall/ComFirewallService.cs
src/Steamoff.Infrastructure/Logging/FileLogService.cs

Untracked files/directories:
CODEX_HANDOFF.md
dotnet-sdk-8.0.421-win-x64.exe
specs/005-friendly-ux-copy-refresh/
specs/006-firewall-fallback-strategy/
src/Steamoff.Core/Models/FirewallStrategyModels.cs
src/Steamoff.Infrastructure/Firewall/FallbackAwareFirewallService.cs
src/Steamoff.Infrastructure/Firewall/NetSecurityFirewallService.cs
src/Steamoff.Infrastructure/Firewall/PowerShellRuleInvoker.cs
tests/Steamoff.Tests/App/WindowClosingLifecycleTests.cs
tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs
tests/Steamoff.Tests/Infrastructure/NetSecurityFirewallServiceTests.cs
tests/Steamoff.Tests/TestSupport/FakeLocalizedLogService.cs
tests/Steamoff.Tests/TestSupport/ScriptedFirewallService.cs
tests/Steamoff.Tests/TestSupport/StaThreadRunner.cs
```

Important command results from the latest successful run:

```text
dotnet build Steamoff.slnx
Result: succeeded, 0 errors

dotnet test tests\Steamoff.Tests\Steamoff.Tests.csproj --no-restore
Result: passed, 122/122

.\build-release.ps1
Result: release build completed successfully
```

Latest release artifacts:

```text
src\Steamoff.App\release\Steamoff-with-dotnet-runtime\Steamoff.exe
SHA256: B395CCADD6B6027A69D60AD7A906B332F78C157B61215BBB79DAEDB2C9D69355
Size: 71709090

src\Steamoff.App\release\Steamoff-without-dotnet-runtime\Steamoff.exe
SHA256: E273BD9A9B4191BC920465413E5C87BE1739930706BB1A3651A1B8D71599E409
Size: 621680
```

## Rule Ownership Logic

Steamoff identifies its own firewall rules by group and deterministic rule
names.

- Rule group: `Steamoff`
- Rule name prefix: `Steamoff - Block - `
- Full rule name format:
  `Steamoff - Block - <TargetDisplayName> - <Outbound|Inbound>`

The active status check does not trust group alone. A rule is counted as an
active expected Steamoff rule only when all of these match:

- exact expected display name
- group is `Steamoff`
- direction is expected
- action is `Block`
- enabled is `true`
- application path matches the target path

Old prototype rules such as group `SteamOfflineToggle` and names like
`Steam Offline OUT...` are not owned by this implementation and should not be
treated as current Steamoff rules.

## Main Fix: Firewall Fallback Strategy

The user log showed that Steam was not being blocked reliably and the fallback
PowerShell path failed with messages equivalent to:

- `Get-NetFirewallRule` receiving null or empty `Group` / `DisplayName`
- `x86 is not recognized`
- cleanup/create failures for Steam targets

Root cause: the old PowerShell invocation passed a `param(...)` script through
`powershell.exe -Command` and then appended arguments. Paths such as
`C:\Program Files (x86)\...` were not bound as intended and were interpreted as
commands or split incorrectly.

Implemented fix:

- Added `PowerShellRuleInvoker`.
- The invoker runs a static script through:
  `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command <script>`.
- User/path/rule values are passed via per-process environment variables,
  not interpolated into the command line.
- NetSecurity scripts read values from environment variables and then call
  cmdlets with strongly assigned local variables.
- Rule lookup avoids fragile parameter binding by filtering:
  `Get-NetFirewallRule -Group $Group | Where-Object { $_.DisplayName -eq $DisplayName }`.

Files involved:

- `src/Steamoff.Infrastructure/Firewall/PowerShellRuleInvoker.cs`
- `src/Steamoff.Infrastructure/Firewall/NetSecurityFirewallService.cs`
- `src/Steamoff.Infrastructure/Firewall/FallbackAwareFirewallService.cs`
- `src/Steamoff.Core/Models/FirewallStrategyModels.cs`
- `src/Steamoff.App/AppServices.cs`

## Firewall Service Behavior

`AppServices.cs` now wires the app firewall service as:

```text
FallbackAwareFirewallService(
    primary: ComFirewallService,
    secondary: NetSecurityFirewallService)
```

Expected behavior:

- COM firewall API is still the primary strategy.
- NetSecurity PowerShell is the fallback strategy.
- Fallback is used when the primary throws or when verification shows expected
  rules were not applied.
- Remove/unblock follows the same primary/fallback idea.
- Both strategies use the same Steamoff group/name ownership contract.

Tests added for this:

- `tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs`
- `tests/Steamoff.Tests/Infrastructure/NetSecurityFirewallServiceTests.cs`
- `tests/Steamoff.Tests/TestSupport/FakeLocalizedLogService.cs`
- `tests/Steamoff.Tests/TestSupport/ScriptedFirewallService.cs`

## Log Encoding Fix

The user reported mojibake in the log UI. Two areas were addressed:

- PowerShell stdout/stderr are read as UTF-8:
  `ProcessStartInfo.StandardOutputEncoding = Encoding.UTF8`
  and `StandardErrorEncoding = Encoding.UTF8`.
- PowerShell scripts set `[Console]::OutputEncoding` and `$OutputEncoding`
  to UTF-8.
- `FileLogService` now checks the existing `steamoff.log` for common mojibake
  markers on startup. If detected, it renames the file to:
  `steamoff.log.<timestamp>.mojibake.old`.

Note: an attempted manual move of the active old log was blocked by access
denied because the running app held the file. The app normally runs elevated,
so the startup archive path should handle it on the next run.

File involved:

- `src/Steamoff.Infrastructure/Logging/FileLogService.cs`

## UI Status Badge And Progress Feedback

The user asked to keep functionality intact and improve the main status UI.

Implemented UI changes:

- The status text area in `MainWindow.xaml` is now a rounded rectangular badge.
- The badge background pulses green when Steam is online/unblocked.
- The badge background pulses orange when Steam is offline/blocked.
- Additional visual states exist for partial/error/drift status.
- A small rotating activity indicator appears while status checking or a
  toggle operation is running.

Files involved:

- `src/Steamoff.App/Views/MainWindow.xaml`
- `src/Steamoff.App/ViewModels/CompactViewModel.cs`

## Progress And Rule Count Feedback

The user wanted to know whether the app is frozen or checking, and wanted the
main window to show how many rules exist.

Implemented behavior:

- `CompactViewModel` has `_isCheckingStatus`, `IsCheckingStatus`,
  `IsActivityVisible`, and `ActivityText`.
- `RefreshStatusAsync` guards against concurrent checks.
- The UI shows status-check activity text while checking.
- The UI shows operation activity text while applying a toggle.
- The UI now exposes rule coverage text:
  - desired blocked: `Active rules: X of Y`
  - desired unblocked: `Remaining active rules: X of Y`

The counts are rule-level counts, not target-level counts:

- expected rule count = target count * direction count
- direction count is usually 1 for outbound-only and 2 for inbound+outbound
- active expected rule count applies the strict ownership checks listed above

File involved:

- `src/Steamoff.App/ViewModels/CompactViewModel.cs`

## Startup Status Logic

The user reported that startup immediately logged a mismatch even when this was
not useful or accurate from the user's point of view.

Implemented adjustment:

- On the first status refresh, user-facing localized drift/mismatch log output
  is suppressed.
- Technical status details are still written to the technical log.
- `StatusEvaluator` now treats "desired blocked but zero rules exist" as
  partial blocked coverage rather than a scary drift/error state.
- `StatusEvaluator` treats "desired unblocked but some rules remain" as partial
  blocked coverage rather than drift.

This makes startup show factual coverage instead of implying a bad state before
the app has finished inspecting existing rules.

File involved:

- `src/Steamoff.Core/Services/StatusEvaluator.cs`

## Additional Diagnostics

More diagnostic detail was added so future failures explain what happened:

- Status check start/end includes desired state, target count, expected rule
  count, active expected rule count, found Steamoff rule count, status, and
  coverage.
- Toggle start/end includes old/new desired state, target count, direction mode,
  cleanup mode, expected rules, final status, active/expected rules, and
  coverage.
- NetSecurity block/remove logs summarize target/rule attempts, successes,
  failures, direction mode, and cleanup mode.

Files involved:

- `src/Steamoff.App/ViewModels/CompactViewModel.cs`
- `src/Steamoff.Infrastructure/Firewall/NetSecurityFirewallService.cs`
- `src/Steamoff.Core/Logging/LogEventKey.cs`
- `src/Steamoff.Core/Logging/LogEventTemplates.cs`
- `src/Steamoff.Core/Resources/Localization/*.json`

## WPF Lifecycle Fixes Present In Tree

The dirty tree also contains lifecycle fixes around close/hide behavior for the
main and settings windows. These appear to have been started before Codex took
over, but Codex verified them with the current test run.

The intent:

- Avoid swallowed WPF exceptions caused by changing visibility during the
  `Closing` event.
- Defer hide/close actions through the dispatcher.
- Keep tray "Open Steamoff" behavior reliable after closing/hiding windows.

Files/tests:

- `src/Steamoff.App/Views/MainWindow.xaml.cs`
- `src/Steamoff.App/Views/SettingsWindow.xaml.cs`
- `tests/Steamoff.Tests/App/WindowClosingLifecycleTests.cs`
- `tests/Steamoff.Tests/TestSupport/StaThreadRunner.cs`

## Build Issues Encountered

Two environment/build issues were encountered and resolved:

- `build-release.ps1` initially failed because a running `Steamoff` process
  locked `release\Steamoff-without-dotnet-runtime\Steamoff.exe`.
  The process was closed and the release build was rerun successfully.
- A partial release cleanup temporarily removed release README placeholders.
  They were restored before the successful final release build.

Latest release build is good.

## Files To Review First

For Claude Code continuation, review these first:

```text
CLAUDE.md
specs/006-firewall-fallback-strategy/plan.md
src/Steamoff.App/AppServices.cs
src/Steamoff.App/ViewModels/CompactViewModel.cs
src/Steamoff.App/Views/MainWindow.xaml
src/Steamoff.Core/Services/StatusEvaluator.cs
src/Steamoff.Infrastructure/Firewall/FallbackAwareFirewallService.cs
src/Steamoff.Infrastructure/Firewall/NetSecurityFirewallService.cs
src/Steamoff.Infrastructure/Firewall/PowerShellRuleInvoker.cs
src/Steamoff.Infrastructure/Logging/FileLogService.cs
tests/Steamoff.Tests/Infrastructure/FallbackAwareFirewallServiceTests.cs
tests/Steamoff.Tests/Infrastructure/NetSecurityFirewallServiceTests.cs
tests/Steamoff.Tests/App/WindowClosingLifecycleTests.cs
```

## Suggested Next Steps

1. Run the app manually as administrator and verify real Steam connectivity
   toggles match the UI state.
2. Confirm that a fresh run with an old mojibake `steamoff.log` archives the
   old log and writes readable UTF-8 Russian text.
3. Confirm the status badge and activity spinner look correct at 0%, partial
   coverage, 100%, and while a toggle is in progress.
4. If everything is good, stage only the intended feature files and leave
   unrelated local artifacts such as `dotnet-sdk-8.0.421-win-x64.exe` out of
   any commit.
