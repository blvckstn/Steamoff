# Quickstart / Smoke Test — Localization & Settings

Run on a Windows 10/11 machine. Builds on the feature 001 quickstart — this
one focuses on the new localization/settings surface.

## 1. Build, test, publish
```powershell
dotnet restore
dotnet build -c Release
$env:DOTNET_ROLL_FORWARD = "LatestMajor"
$env:DOTNET_ROLL_FORWARD_TO_PRERELEASE = "1"
dotnet test -c Release
dotnet publish .\src\Steamoff.App\Steamoff.App.csproj -c Release -r win-x64 `
  --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```
(The `DOTNET_ROLL_FORWARD*` variables work around a missing exact-version
`Microsoft.WindowsDesktop.App` on some dev machines — see
[../../IMPLEMENTATION_LOG.md](../../IMPLEMENTATION_LOG.md). They are not
required for the published app, only for `dotnet test`.)

## 2. First launch — language picker
1. Delete (or rename) `%ProgramData%\Steamoff\settings.json` (and the
   `%AppData%\Steamoff` fallback copy, if present) to simulate a fresh install.
2. Launch the EXE → before the main window appears, a small dark, rounded,
   orange-accented **"Ваш язык / Your language"** dialog shows a grid of
   9 language cards (flag + code + native name).
3. Click a few different cards → the dialog's own title, subtitle, and
   confirm-bar text switch live, in place, with the clicked card highlighted
   in orange. No flicker, no restart.
4. Click **Confirm** → the dialog closes, the Compact Switch View opens in
   the chosen language, and `settings.json` now contains
   `"language": "<code>"` and `"isFirstLaunchCompleted": true`.
5. Relaunch the app → the dialog does **not** reappear; the chosen language
   persists.

### Dismissal path
1. Reset `settings.json` again (step 1).
2. Launch, then close the picker via its **✕** button without selecting
   anything → the app proceeds in **Russian**, and `settings.json` shows
   `"language": "ru"`, `"isFirstLaunchCompleted": true`.

## 3. Settings View — language bar & instant redraw
1. From the Compact Switch View, click the gear icon → **Settings** opens.
2. Confirm the **language bar is the very first thing at the top**, above
   every section, as a horizontal scrollable row of cards.
3. Click a different language card → every visible string in the window
   (section headers, toggles, buttons, the language bar itself, the
   Test/Status/Apply/Save/Cancel bar) redraws immediately in the new
   language. The active card shows the orange highlight.
4. Open the system tray menu and hover the tray icon → confirm the tooltip
   and every menu item are also already in the new language (no need to
   reopen Settings or restart).

## 4. Apply / Save / Cancel / language rollback
1. In Settings, change the language **and** toggle a couple of switches
   (e.g. "Запускать с Windows").
2. Click **Отмена** → the window closes, the Compact view and tray are back
   in the language that was active *before* you opened Settings, and the
   toggles you changed are reverted. Reopen Settings to confirm nothing
   persisted.
3. Repeat the edits, click **Применить** → a toast confirms the save, the
   window **stays open**, and the Compact view/tray refresh to the new
   language and settings immediately.
4. Change something else, click **Сохранить** → same persistence, but the
   window **closes** back to the Compact view.
5. Inspect `settings.json` after each Apply/Save → confirm `language` and
   the toggled fields match what you picked, and after Cancel → confirm
   nothing changed from the last Apply/Save.

## 5. Test / Status
1. In Settings, click **Тестирование** → diagnostics run; **Статус** updates
   to show the outcome (ok/warning/error) and a "last run at HH:mm" line, in
   the active language.
2. Switch the language while the status is showing a completed report →
   confirm the status text and timestamp re-render in the new language
   without re-running diagnostics (this is the
   `SettingsViewModel.OnLanguageChanged` instant-redraw path —
   see [research.md](research.md) R2).

## 6. Translation completeness spot-check
1. Cycle through all 9 languages in Settings (and the first-launch dialog).
2. Confirm no screen ever shows a raw translation key (e.g. `compact.statusBlocked`)
   instead of translated text — that would indicate a missing key in that
   language's table. (`LocalizationServiceTests` enforce this at the table
   level; this step is the human end-to-end sanity check.)
