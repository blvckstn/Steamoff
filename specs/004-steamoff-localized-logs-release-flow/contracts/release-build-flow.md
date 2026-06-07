# Contract: Release Build Flow (`build-release.ps1`)

## Output layout (fixed, exact)
```
src\Steamoff.App\release\
  Steamoff-with-dotnet-runtime\
    Steamoff.exe
    README-RUN.txt
  Steamoff-without-dotnet-runtime\
    Steamoff.exe
    README-RUN.txt
  release-manifest.json
  release-log.txt
```
Always rooted at `C:\Users\adm\Desktop\13\vibe\Steamoff\src\Steamoff.App\release\`
— recreated from scratch on every run (clean → recreate → publish).

## Publish commands (verbatim, must match exactly)
```powershell
# Self-contained (bundles the .NET runtime — runs on any win-x64 machine)
dotnet publish .\src\Steamoff.App\Steamoff.App.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o "C:\Users\adm\Desktop\13\vibe\Steamoff\src\Steamoff.App\release\Steamoff-with-dotnet-runtime"

# Framework-dependent (smaller — requires .NET 8 Desktop Runtime installed)
dotnet publish .\src\Steamoff.App\Steamoff.App.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true `
  -o "C:\Users\adm\Desktop\13\vibe\Steamoff\src\Steamoff.App\release\Steamoff-without-dotnet-runtime"
```
Both produce a single `Steamoff.exe` (the `.csproj`'s `<AssemblyName>` is
`Steamoff.App`, but `-p:AssemblyName=Steamoff` … — actually simplest: rename/
copy the produced `Steamoff.App.exe` to `Steamoff.exe` post-publish, OR set
`-p:AssemblyName=Steamoff` on the publish command so the single file is
emitted as `Steamoff.exe` directly. The script uses the rename approach to
avoid perturbing the `.csproj`'s existing assembly identity, version stamps,
and any path assumptions elsewhere — rename is the smaller, safer, more
reversible change. Recorded in `ASSUMPTIONS.md` **A20**.)

## `README-RUN.txt` content (verbatim, RU; both variants get a build-specific note)
**`Steamoff-with-dotnet-runtime\README-RUN.txt`**:
```
Steamoff — самодостаточная сборка (со встроенной средой выполнения .NET)

Этот вариант не требует установки .NET — всё нужное уже внутри Steamoff.exe.

Как запустить:
1. Скопируйте Steamoff.exe в любую папку на компьютере.
2. Запустите Steamoff.exe от имени администратора (запросится UAC) —
   это необходимо для управления правилами брандмауэра Defender.
3. Дальше Steamoff работает из системного трея.

Размер файла больше, чем у облегчённой версии — это нормально: внутри
находится среда выполнения .NET 8.
```
**`Steamoff-without-dotnet-runtime\README-RUN.txt`**:
```
Steamoff — облегчённая сборка (требуется установленный .NET)

Перед запуском убедитесь, что на компьютере установлен
.NET 8 Desktop Runtime (x64): https://dotnet.microsoft.com/download/dotnet/8.0

Как запустить:
1. Установите .NET 8 Desktop Runtime, если он ещё не установлен.
2. Скопируйте Steamoff.exe в любую папку на компьютере.
3. Запустите Steamoff.exe от имени администратора (запросится UAC) —
   это необходимо для управления правилами брандмауэра Defender.
4. Дальше Steamoff работает из системного трея.

Этот файл значительно меньше самодостаточной версии, потому что среда
выполнения .NET берётся из уже установленной на компьютере.
```

## `release-manifest.json` (schema — see `data-model.md` §7 for full example)
Fields: `appName`, `version`, `builtAt` (ISO-8601 with offset), `configuration`,
`runtime`, `outputs[]` — each with `name`, `type`
(`"self-contained" | "framework-dependent"`), `includesDotnetRuntime` (bool),
`path` (relative to `release\`), `sizeBytes` (`(Get-Item).Length`), `sha256`
(`(Get-FileHash -Algorithm SHA256).Hash`).

## `release-log.txt` line templates (plain text, timestamped, bilingual-leaning)
```
[2026-06-07 14:02:11] === Запуск сборки релиза / Release build started ===
[2026-06-07 14:02:11] Найден работающий процесс Steamoff (PID 12345, путь ...) — закрываю...
[2026-06-07 14:02:14] Процесс завершён штатно (CloseMainWindow). / Process closed gracefully.
[2026-06-07 14:02:14] Папка release очищена и пересоздана. / Release folder cleaned and recreated.
[2026-06-07 14:02:15] dotnet restore — OK (Xs)
[2026-06-07 14:03:40] dotnet build -c Release — OK (Xs), 0 ошибок / 0 errors
[2026-06-07 14:05:10] dotnet test — OK, 53/53 пройдено / 53/53 passed (Xs)
[2026-06-07 14:07:02] publish (self-contained) — OK → ...\Steamoff-with-dotnet-runtime\Steamoff.exe (122.4 MB, sha256=AB12...)
[2026-06-07 14:08:55] publish (framework-dependent) — OK → ...\Steamoff-without-dotnet-runtime\Steamoff.exe (1.2 MB, sha256=CD34...)
[2026-06-07 14:08:56] release-manifest.json записан / written
[2026-06-07 14:08:56] === Сборка релиза завершена успешно / Release build completed successfully ===
```
On any failure: a line `[timestamp] ОШИБКА / ERROR: <step> — <details>` is
appended, the script prints the same message to the console with a non-zero
`exit`, and it does **not** proceed to clean/overwrite a previously-good
release folder mid-failure (clean only happens once, before publishing
starts — a failed publish leaves a half-filled folder *with* the error
logged, which is more diagnosable than silently wiping it again on retry).

## Process safety ("never touch Steam") — exact rules
1. Enumerate `Get-Process` candidates whose `ProcessName` starts with
   `"Steamoff"` **and** whose `MainModule.FileName` resolves to a path under
   any of:
   - `<repoRoot>\src\Steamoff.App\bin\`
   - `<repoRoot>\src\Steamoff.App\release\`
   - `<repoRoot>\src\Steamoff.App\publish*\` (any prior publish-output dir)
2. For each candidate: `CloseMainWindow()`, then poll up to 3–5 s for exit;
   if still running, `Stop-Process -Force` and log a **warning** line noting
   the forced termination (PID + path); then wait 1–2 s and verify the file
   is unlocked (`Test-FileLock`-style open-for-write probe) before
   proceeding.
3. **Never** enumerate or act on `steam.exe`, `steamservice.exe`,
   `steamwebhelper.exe`, `GameOverlayUI.exe`, any `steamerrorreporter*`, or
   any process whose path is outside the three trees above — the name+path
   double guard makes an accidental match on a third-party process
   effectively impossible.
4. If no matching process is found, log "не найден работающий Steamoff /
   no running Steamoff found" and continue immediately — this is the normal
   case in CI/clean-machine runs.

## Pipeline order (must match exactly — stop on first failure)
1. Verify CWD is the repo root (`Test-Path .\Steamoff.slnx`)
2. `dotnet restore`
3. `dotnet build -c Release`
4. `dotnet test` (with `DOTNET_ROLL_FORWARD*` env vars set, matching README)
5. Find & close running Steamoff (rules above)
6. Clean & recreate `release\` and both subfolders
7. Publish self-contained → rename to `Steamoff.exe` → write `README-RUN.txt`
8. Publish framework-dependent → rename to `Steamoff.exe` → write `README-RUN.txt`
9. Compute sizes/hashes, write `release-manifest.json`
10. Finalize `release-log.txt`, print final paths to console
11. Exit `0`

Any non-zero exit from `dotnet restore/build/test` or `dotnet publish`, or
any unrecoverable process-termination failure, aborts at that step with a
clear console error, an `ERROR` line in `release-log.txt`, and `exit 1`.
