# Data Model: Feature 004

## 1. Restart-state derivation (no new persisted entity)
```csharp
// Steamoff.Core/Localization/LanguageRestartState.cs
public static class LanguageRestartState
{
    public static bool IsRestartRequired(string selectedLanguageCode, string runtimeLanguageCode)
        => !string.Equals(selectedLanguageCode, runtimeLanguageCode, StringComparison.OrdinalIgnoreCase);
}
```
Consumed by `SettingsViewModel`:
```csharp
public string RuntimeLanguage   => _services.Localization.CurrentLanguage.Code;
public string SelectedLanguage  => _session.Draft.Language;
public bool   IsRestartRequired => LanguageRestartState.IsRestartRequired(SelectedLanguage, RuntimeLanguage);
```
`OnPropertyChanged(nameof(IsRestartRequired))` etc. are raised wherever
`_session.Draft.Language` or the localization service's current language
could have changed (picker selection, Apply, Save, Cancel, construction).
See `contracts/language-restart.md` for the full transition table.

## 2. `LogEventKey` (enum, `Steamoff.Core.Logging`)
One member per named runtime event from the brief (plus the two
`Firewall*Failed` siblings from R3):

```csharp
public enum LogEventKey
{
    AppStarted, AppClosed,
    SettingsOpened, SettingsApplied, SettingsSaved, SettingsCancelled,
    LanguageChangedRestartRequired, RestartRequested, RestartFailed,
    SteamAutoSearchStarted, SteamAutoSearchSucceeded, SteamAutoSearchFailed,
    SteamPathNormalized, SteamPathInvalid,
    FolderAdded, FolderRemoved, ExeAdded, ExeRemoved,
    FirewallBlockStarted, FirewallBlockCompleted,
    FirewallBlockFailed, FirewallUnblockFailed,
    FirewallUnblockStarted, FirewallUnblockCompleted,
    DriftDetected,
    AutostartCreated, AutostartRemoved,
    DiagnosticsCopied,
    ReleaseBuildStarted, ReleaseBuildCompleted, ReleaseBuildFailed,
}
```

## 3. `LogEventTemplates` (static lookup table, `Steamoff.Core.Logging`)
```csharp
public static class LogEventTemplates
{
    public static string LocalizationKeyFor(LogEventKey key);  // e.g. "log.event.appStarted"
    public static LogLevel LevelFor(LogEventKey key);          // Info | Warning | Error
}
```
Backed by a single `IReadOnlyDictionary<LogEventKey, (string Key, LogLevel Level)>`.
Localization keys follow the `log.event.<camelCaseEventName>` convention
(matches the existing `settings.*`/`compact.*` naming style). `LogLevel` is a
new tiny enum (`Info`, `Warning`, `Error`) mapped to the existing
`ILogService.Write(level, message)` string levels (`"INFO"`, `"WARN"`,
`"ERROR"` — matching `FileLogService`'s current literals, confirmed via
existing call sites).

## 4. `ILocalizedLogService` (`Steamoff.Core.Interfaces`)
```csharp
public interface ILocalizedLogService
{
    Task LogAsync(LogEventKey key, params object[] args);
}
```
Implementation (`Steamoff.Infrastructure.Logging.LocalizedLogService`)
composes `ILogService` + `ILocalizationService`:
```csharp
public async Task LogAsync(LogEventKey key, params object[] args)
{
    var template = _localization.GetString(LogEventTemplates.LocalizationKeyFor(key));
    var message = args.Length == 0 ? template : string.Format(template, args);
    await _log.WriteAsync(LevelString(LogEventTemplates.LevelFor(key)), message);
}
```
This guarantees "logged in `RuntimeLanguage`" for free: `_localization` is
the same singleton `ILocalizationService` whose `CurrentLanguage` only
changes via `SetLanguage` — and after R2, that's only at startup and on the
first-launch dialog, i.e. exactly when `RuntimeLanguage` is supposed to
change.

## 5. `DiagnosticsSnapshot` (record, `Steamoff.Core.Models`)
~18 fields, one bilingual-ready structured payload behind the localized
extended report:
```csharp
public sealed record DiagnosticsSnapshot(
    string AppVersion,
    string CurrentLanguageCode,
    string SelectedLanguageCode,
    bool IsRestartRequired,
    string WindowsUserName,
    bool IsElevated,
    string SettingsPath,
    string LogPath,
    string SteamPath,
    bool IsSteamPathValid,
    int AdditionalFolderCount,
    int SeparateExeCount,
    string FirewallDesiredState,
    string FirewallActualState,
    string DriftStatus,
    string AutostartStatus,
    string? LastTestResult,
    string? LastReleaseBuildPath);
```
`IDiagnosticsService` gains:
```csharp
Task<DiagnosticsSnapshot> BuildSnapshotAsync();
Task<string> BuildExtendedReportAsync(); // fully localized text, replaces/extends BuildDiagnosticsReportAsync
```
The extended report renders every `DiagnosticsSnapshot` field through
`ILocalizationService.GetString("diagnostics.field.*", value)` templates, and
appends the pending-restart notice
(`diagnostics.languagePendingRestart`, formatted with the selected language's
display code) only when `IsRestartRequired` is true.

## 6. Journal panel state (on `SettingsViewModel`, no new persisted model)
```csharp
public ObservableCollection<string> JournalLines { get; }       // filtered view
public string JournalFilter { get; set; }                       // "all" | "error" | "warning" | "info"
public bool HasJournalLines { get; }
public IRelayCommand RefreshJournalCommand { get; }
public IRelayCommand OpenLogFolderCommand { get; }
public IRelayCommand CopyDiagnosticsCommand { get; }             // shared with Compact
public IRelayCommand ClearJournalDisplayCommand { get; }
```
Backed by `ILogService.ReadLastLinesAsync(200)` plus an in-memory
`List<string> _journalCache` (so "Clear display" empties `JournalLines`
without re-reading, and the next timer tick repopulates from `_journalCache`
or a fresh read). Mirrors `CompactViewModel.RecentLogLines` 1:1, just with a
larger window (200 vs 30) and a level filter — see
`contracts/localized-logging.md` "Journal panel" for the filter→level
mapping (`LogLineContainsConverter`'s existing `[ERROR]`/`[WARN]`/`[INFO]`
substring convention is reused, no new parsing logic).

## 7. `release-manifest.json` schema
```jsonc
{
  "appName": "Steamoff",
  "version": "1.0.0.0",
  "builtAt": "2026-06-07T12:34:56+03:00",
  "configuration": "Release",
  "runtime": "win-x64",
  "outputs": [
    {
      "name": "Steamoff-with-dotnet-runtime",
      "type": "self-contained",
      "includesDotnetRuntime": true,
      "path": "Steamoff-with-dotnet-runtime\\Steamoff.exe",
      "sizeBytes": 123456789,
      "sha256": "AB12...EF"
    },
    {
      "name": "Steamoff-without-dotnet-runtime",
      "type": "framework-dependent",
      "includesDotnetRuntime": false,
      "path": "Steamoff-without-dotnet-runtime\\Steamoff.exe",
      "sizeBytes": 1234567,
      "sha256": "CD34...AB"
    }
  ]
}
```
Produced by `build-release.ps1` via `Get-FileHash -Algorithm SHA256` and
`(Get-Item $path).Length`; `version` read from the built assembly
(`(Get-Item Steamoff.exe).VersionInfo.ProductVersion` or the `.csproj`
`<Version>`, whichever resolves — see `contracts/release-build-flow.md`).

## 8. `release-log.txt` (plain text, not JSON)
Append-only, timestamped lines (same `yyyy-MM-dd HH:mm:ss` convention as
`FileLogService` for consistency), recording: build start time, whether a
running Steamoff was found/closed (and how — soft vs forced), whether the
release folder existed/was cleaned, restore/build/test outcomes (pass/fail +
duration), each publish command and its result, output paths, sizes,
SHA-256 hashes, and any error with enough detail to diagnose offline. See
`contracts/release-build-flow.md` for the exact line templates.
