# Quickstart: Feature 004

## Run & test locally
```powershell
dotnet restore
dotnet build -c Release
$env:DOTNET_ROLL_FORWARD = "LatestMajor"
$env:DOTNET_ROLL_FORWARD_TO_PRERELEASE = "1"
dotnet test -c Release
```

## Try the restart-required language switch
1. Launch Steamoff, open Settings.
2. Pick a different language in the picker — note the warning banner
   ("Для полного применения языка перезапустите Steamoff.") appears and the
   UI text does **not** change yet.
3. Click **Apply** — toast shows "Настройки применены. Для смены языка
   требуется перезапуск." and the window stays open with the same language.
4. Click **"Перезапустить сейчас"/"Restart now"** — Steamoff relaunches
   itself and starts in the new language.
5. Reopen Settings → the picker, the runtime language, and the persisted
   language all agree again; no warning.

## Inspect the journal inside Settings
1. Open Settings → "Журнал"/"Log" section.
2. Watch it auto-refresh as you perform actions (add a folder, toggle the
   switch, run a test) — each produces a localized line at the bottom.
3. Try the level filter (Все/Ошибки/Предупреждения/Информация), "Открыть
   папку логов", "Скопировать диагностику", and "Очистить отображение"
   (note the underlying log file is untouched by the last one).

## Build a release
```powershell
.\build-release.ps1
```
Produces, under `src\Steamoff.App\release\`:
- `Steamoff-with-dotnet-runtime\Steamoff.exe` (+ `README-RUN.txt`) — runs
  on any win-x64 machine, no .NET install needed
- `Steamoff-without-dotnet-runtime\Steamoff.exe` (+ `README-RUN.txt`) —
  smaller, requires .NET 8 Desktop Runtime installed
- `release-manifest.json` — names/sizes/SHA-256 of both outputs
- `release-log.txt` — a step-by-step record of the whole build

The script gracefully closes any running Steamoff instance first (never
touches Steam itself), cleans the release folder, and exits non-zero with a
clear message on any failure.

## Where things live
- Restart-state derivation: [`contracts/language-restart.md`](contracts/language-restart.md)
- Log event → localization key → level table: [`contracts/localized-logging.md`](contracts/localized-logging.md)
- Release manifest schema & publish commands: [`contracts/release-build-flow.md`](contracts/release-build-flow.md)
- Full task breakdown: [`tasks.md`](tasks.md)
