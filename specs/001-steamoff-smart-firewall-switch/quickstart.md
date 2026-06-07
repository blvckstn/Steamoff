# Quickstart / Smoke Test

Run on a Windows 10/11 machine with an administrator account.

## 1. Build & publish
```powershell
dotnet restore
dotnet build -c Release
dotnet test --filter "Category!=RequiresAdmin"
dotnet publish .\src\Steamoff.App\Steamoff.App.csproj -c Release -r win-x64 `
  --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```
The published EXE appears under
`src\Steamoff.App\bin\Release\net8.0-windows\win-x64\publish\Steamoff.App.exe`.

## 2. First launch
1. Double-click the EXE → UAC prompt appears (manifest requires admin).
2. Approve → main window opens on **Dashboard**.
3. Confirm the titlebar shows "Запущено как: `<DOMAIN>\<User>`",
   "Права администратора: Да", "Firewall-доступ: Да".
4. Confirm Steam was auto-discovered (path shown) or, if not installed,
   the "Steam не найден" gray state + folder picker appears.

## 3. Block / Unblock cycle
1. Press the big toggle → "Заблокировать Steam".
2. Wait for the operation to finish → status pill turns green, tray icon
   turns green, **Firewall Rules** tab lists `Steamoff - Block - steam.exe -
   Outbound` (and siblings for `steamservice.exe`, every `steamwebhelper.exe`).
3. Open Windows Defender Firewall with Advanced Security → confirm a
   `Steamoff` group with matching rules exists, `Action = Block`,
   `Enabled = Yes`.
4. Press the toggle again → confirmation dialog (if `warnBeforeUnblock`) →
   confirm → rules become disabled (default `ruleCleanupMode = DisableRules`),
   status turns red.

## 4. Drift detection
1. With Steam blocked, manually delete the `Steamoff` rule group from Windows
   Firewall.
2. Within `checkIntervalSeconds`, Steamoff's status should turn orange
   ("Обнаружено расхождение"); in `AlwaysBlock` mode rules should be silently
   recreated and a balloon notification shown; in `ManualToggle`/`PauseMonitoring`
   it should just report drift.

## 5. Folders / EXE files
1. Folders tab → "Добавить папку" → pick a folder with a few `.exe`s →
   confirm scan finds them, status updates, coverage % changes on Dashboard.
2. EXE Files tab → "Добавить EXE" → try a non-`.exe` path (rejected), a
   missing path (rejected), then a real `.exe` (accepted, status `Unblocked`
   until applied).

## 6. Autostart
1. Settings → "Создать автозапуск" → confirm Task Scheduler shows a
   `Steamoff` task, logon trigger, "Run with highest privileges".
2. "Проверить автозапуск" should report OK (path/user/privilege match).

## 7. Tray & close-to-tray
1. Click the window's close (×) → window hides, tray icon remains, balloon
   confirms it's still running.
2. Right-click tray icon → exercise each context menu command.
3. Tray → "Выход" → process exits fully.

## 8. Read-only mode
1. Relaunch without admin (e.g., via a limited account or by cancelling UAC).
2. Confirm `AdminRequiredDialog`/`UacDeniedDialog` appears, read-only mode
   disables Block/Unblock/Settings-mutation controls, Logs/Diagnostics remain
   browsable.
