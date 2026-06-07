# Quickstart: Steamoff — Settings Paths & UI Fixes

## Try it locally
```powershell
dotnet build src/Steamoff.App/Steamoff.App.csproj -c Debug
dotnet run --project src/Steamoff.App/Steamoff.App.csproj
```
Run elevated (or via the app's own UAC prompt) to exercise firewall-dependent
status; the Settings UI itself works without elevation.

## Walkthroughs

### Add a folder / EXE
1. Open Settings (gear icon **or** the bottom-left "Settings" button — both
   now route to the same window).
2. In "Дополнительные папки", click **+ Добавить папку**, pick a directory.
   It appears as a row with an enable toggle, EXE count, status, and
   Rescan/Open/Delete buttons. Dragging a folder onto the card works the
   same way.
3. In "Отдельные EXE-файлы", click **+ Добавить EXE**, pick an `.exe`
   (or drop one, or a `.lnk` pointing at one). Non-`.exe` targets are
   rejected with a toast.

### Steam path
1. Type, paste, or drop a path — including a `steam.exe` path, a `.lnk`
   shortcut, a quoted path, or one with `%ProgramFiles(x86)%`/mixed slashes.
2. The indicator dot turns green ("Steam найден" / valid) or red (with a
   specific reason: not found / no `steam.exe` / wrong exe / shortcut
   unresolved); yellow while unchecked, gray when empty.
3. Click **Найти автоматически** to re-run discovery, or **Выбрать папку**
   to browse manually. Discovery also runs automatically on startup and
   whenever Settings opens if the current path is empty/invalid.

### Mini-log (Compact view)
The card under the admin indicator shows the last ~30 log lines,
auto-refreshing every 5 seconds, color-coded by `[ERROR]`/`[WARNING]`/
`[INFO]`. **Развернуть** toggles the action row; **Открыть полный лог** opens
the log file in the default viewer; **Скопировать диагностику** copies
`ILogService.BuildDiagnosticsReportAsync()`'s output to the clipboard.

## Run the tests
```powershell
$env:DOTNET_ROLL_FORWARD = "LatestMajor"
$env:DOTNET_ROLL_FORWARD_TO_PRERELEASE = "1"
dotnet test tests/Steamoff.Tests/Steamoff.Tests.csproj -c Debug
```

## Publish
```powershell
dotnet publish src/Steamoff.App/Steamoff.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
