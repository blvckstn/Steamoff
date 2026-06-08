<#
    Steamoff release build pipeline.
    Verifies repo, runs restore/build/test, closes any running Steamoff
    instance (never touches Steam itself), recreates release\, publishes
    self-contained and framework-dependent variants, writes README-RUN.txt,
    release-manifest.json and release-log.txt.

    See specs/004-steamoff-localized-logs-release-flow/contracts/release-build-flow.md
    for the exact contract this script implements (pipeline order, process
    safety rules, manifest schema, log line templates). ASSUMPTIONS.md A20
    documents the rename-after-publish approach for producing Steamoff.exe.
#>

[CmdletBinding()]
param(
    # Self-test hook (see I5 in specs/.../tasks.md — "process-safety
    # path-matching predicate, pure function extracted & tested"): when
    # supplied, evaluates Test-SteamoffManagedProcessPath against this single
    # path, prints "True"/"False", and exits — without touching the build
    # pipeline at all. Lets the test suite exercise the real predicate the
    # pipeline uses, in isolation, via a subprocess (ASSUMPTIONS.md A24).
    [string]$TestProcessPath
)

$ErrorActionPreference = 'Stop'

$RepoRoot = $PSScriptRoot

# Process safety ("never touch Steam") — exact name+path double-guard rules
# from contracts/release-build-flow.md. This is the path half of the guard,
# extracted as its own pure, named, isolation-testable function: given a
# candidate process's resolved module path and the repo root, decide whether
# it is safe to treat as "our own Steamoff build artifact" (and therefore
# safe to close) versus a third-party/Steam process that must never be touched.
function Test-SteamoffManagedProcessPath {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$RepoRoot
    )

    $allowedRoots = @(
        (Join-Path $RepoRoot 'src\Steamoff.App\bin\'),
        (Join-Path $RepoRoot 'src\Steamoff.App\release\')
    )
    $publishRootPrefix = Join-Path $RepoRoot 'src\Steamoff.App\publish'

    foreach ($root in $allowedRoots) {
        if ($Path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    if ($Path.StartsWith($publishRootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        $remainder = $Path.Substring($publishRootPrefix.Length)
        if ($remainder.Length -eq 0 -or $remainder[0] -eq '\' -or $remainder[0] -eq '-' -or $remainder[0] -eq '_') {
            return $true
        }
    }

    return $false
}

if ($PSBoundParameters.ContainsKey('TestProcessPath')) {
    Test-SteamoffManagedProcessPath -Path $TestProcessPath -RepoRoot $RepoRoot
    exit 0
}

$AppCsproj = Join-Path $RepoRoot 'src\Steamoff.App\Steamoff.App.csproj'
$ReleaseRoot = Join-Path $RepoRoot 'src\Steamoff.App\release'
$WithRuntimeDir = Join-Path $ReleaseRoot 'Steamoff-with-dotnet-runtime'
$WithoutRuntimeDir = Join-Path $ReleaseRoot 'Steamoff-without-dotnet-runtime'
$LogPath = Join-Path $ReleaseRoot 'release-log.txt'
$ManifestPath = Join-Path $ReleaseRoot 'release-manifest.json'

$global:LogLines = [System.Collections.Generic.List[string]]::new()

function Write-ReleaseLog {
    param([string]$Message)
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $Message"
    Write-Host $line
    $global:LogLines.Add($line)
}

function Save-ReleaseLog {
    if (Test-Path $ReleaseRoot) {
        [System.IO.File]::WriteAllLines($LogPath, $global:LogLines, [System.Text.UTF8Encoding]::new($false))
    }
}

function Fail-Step {
    param([string]$Step, [string]$Details)
    Write-ReleaseLog "ОШИБКА / ERROR: $Step — $Details"
    Save-ReleaseLog
    exit 1
}

# 1. Verify CWD is the repo root
Write-ReleaseLog '=== Запуск сборки релиза / Release build started ==='
if (-not (Test-Path (Join-Path $RepoRoot 'Steamoff.slnx'))) {
    Fail-Step 'verify-root' "Steamoff.slnx не найден в $RepoRoot"
}

Push-Location $RepoRoot
try {
    # 2. dotnet restore
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & dotnet restore | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail-Step 'dotnet restore' "exit code $LASTEXITCODE" }
    $sw.Stop()
    Write-ReleaseLog "dotnet restore — OK ($([int]$sw.Elapsed.TotalSeconds)s)"

    # 3. dotnet build -c Release
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & dotnet build -c Release | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail-Step 'dotnet build -c Release' "exit code $LASTEXITCODE" }
    $sw.Stop()
    Write-ReleaseLog "dotnet build -c Release — OK ($([int]$sw.Elapsed.TotalSeconds)s), 0 ошибок / 0 errors"

    # 4. dotnet test (with roll-forward env vars — environment runtime mismatch workaround, ASSUMPTIONS.md A9)
    $env:DOTNET_ROLL_FORWARD = 'LatestMajor'
    $env:DOTNET_ROLL_FORWARD_TO_PRERELEASE = '1'
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $testOutput = & dotnet test -c Release 2>&1
    $testExit = $LASTEXITCODE
    $sw.Stop()
    if ($testExit -ne 0) { Fail-Step 'dotnet test' "exit code $testExit" }
    $passedLine = ($testOutput | Select-String -Pattern 'Passed!|Пройден' | Select-Object -Last 1)
    $passedCount = $null
    if ($passedLine -and ($passedLine.ToString() -match 'всего\s+(\d+)|Total:\s*(\d+)')) {
        $passedCount = if ($matches[1]) { $matches[1] } else { $matches[2] }
    }
    $countLabel = if ($passedCount) { "$passedCount/$passedCount" } else { 'N/N' }
    Write-ReleaseLog "dotnet test — OK, $countLabel пройдено / $countLabel passed ($([int]$sw.Elapsed.TotalSeconds)s)"
    if ($passedLine) { Write-ReleaseLog "  $($passedLine.ToString().Trim())" }

    # 5. Find & close running Steamoff (name + path double guard — never touch Steam itself).
    # Name filter narrows to "Steamoff*"; Test-SteamoffManagedProcessPath (defined
    # above, self-test-able via -TestProcessPath) supplies the path half of the guard.
    $candidates = Get-Process -Name 'Steamoff*' -ErrorAction SilentlyContinue | Where-Object {
        $path = $null
        try { $path = $_.MainModule.FileName } catch { $path = $null }
        if (-not $path) { return $false }
        return Test-SteamoffManagedProcessPath -Path $path -RepoRoot $RepoRoot
    }

    if (-not $candidates -or $candidates.Count -eq 0) {
        Write-ReleaseLog 'не найден работающий Steamoff / no running Steamoff found'
    }
    else {
        foreach ($proc in $candidates) {
            $procPath = $proc.MainModule.FileName
            Write-ReleaseLog "Найден работающий процесс Steamoff (PID $($proc.Id), путь $procPath) — закрываю..."
            $null = $proc.CloseMainWindow()

            $waited = 0
            while (-not $proc.HasExited -and $waited -lt 5) {
                Start-Sleep -Seconds 1
                $proc.Refresh()
                $waited++
            }

            if (-not $proc.HasExited) {
                Write-ReleaseLog "ПРЕДУПРЕЖДЕНИЕ / WARNING: процесс PID $($proc.Id) ($procPath) не закрылся штатно — принудительное завершение / forced termination"
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
                Start-Sleep -Seconds 2
            }
            else {
                Write-ReleaseLog 'Процесс завершён штатно (CloseMainWindow). / Process closed gracefully.'
            }

            try {
                $stream = [System.IO.File]::Open($procPath, 'Open', 'ReadWrite', 'None')
                $stream.Close()
            }
            catch {
                Fail-Step 'close-running-steamoff' "файл $procPath остаётся заблокированным после завершения процесса"
            }
        }
    }

    # 6. Clean & recreate release\ and both subfolders.
    # Empty contents in place rather than removing the root directory itself —
    # editors/indexers (e.g. VS Code's file watcher) can hold an open handle on
    # the directory even when it has no children, which makes Remove-Item/
    # Rename-Item on the directory fail with "in use" while leaving it otherwise
    # perfectly cleanable. Emptying-in-place is equivalent for "clean & recreate"
    # purposes and avoids that Windows-specific lock entirely.
    if (-not (Test-Path $ReleaseRoot)) {
        New-Item -ItemType Directory -Path $ReleaseRoot -Force | Out-Null
    }
    else {
        Get-ChildItem -Path $ReleaseRoot -Force | Remove-Item -Recurse -Force -Confirm:$false
    }
    New-Item -ItemType Directory -Path $WithRuntimeDir -Force | Out-Null
    New-Item -ItemType Directory -Path $WithoutRuntimeDir -Force | Out-Null
    Write-ReleaseLog 'Папка release очищена и пересоздана. / Release folder cleaned and recreated.'

    $readmeWithRuntime = @'
Steamoff — самодостаточная сборка (со встроенной средой выполнения .NET)

Этот вариант не требует установки .NET — всё нужное уже внутри Steamoff.exe.

Как запустить:
1. Скопируйте Steamoff.exe в любую папку на компьютере.
2. Запустите Steamoff.exe от имени администратора (запросится UAC) —
   это необходимо для управления правилами брандмауэра Defender.
3. Дальше Steamoff работает из системного трея.

Размер файла больше, чем у облегчённой версии — это нормально: внутри
находится среда выполнения .NET 8.
'@

    $readmeWithoutRuntime = @'
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
'@

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)

    function Publish-Variant {
        param(
            [string]$Name,
            [string]$OutDir,
            [bool]$SelfContained,
            [string[]]$ExtraArgs,
            [string]$ReadmeContent,
            [string]$LogLabel
        )

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $publishArgs = @(
            'publish', $AppCsproj,
            '-c', 'Release',
            '-r', 'win-x64',
            "--self-contained=$($SelfContained.ToString().ToLowerInvariant())"
        ) + $ExtraArgs + @('-o', $OutDir)

        & dotnet @publishArgs | Out-Null
        if ($LASTEXITCODE -ne 0) { Fail-Step "publish ($LogLabel)" "exit code $LASTEXITCODE" }
        $sw.Stop()

        $publishedExe = Join-Path $OutDir 'Steamoff.App.exe'
        $finalExe = Join-Path $OutDir 'Steamoff.exe'
        if (-not (Test-Path $publishedExe)) {
            Fail-Step "publish ($LogLabel)" "ожидаемый файл $publishedExe не найден после publish"
        }
        Move-Item -Path $publishedExe -Destination $finalExe -Force

        # The output layout is fixed/exact (Steamoff.exe + README-RUN.txt only) —
        # strip .pdb and any other publish artifacts dotnet drops alongside the exe.
        Get-ChildItem -Path $OutDir -File | Where-Object { $_.Name -ne 'Steamoff.exe' } | Remove-Item -Force

        [System.IO.File]::WriteAllText((Join-Path $OutDir 'README-RUN.txt'), $ReadmeContent, $utf8NoBom)

        $sizeBytes = (Get-Item $finalExe).Length
        $sha256 = (Get-FileHash -Path $finalExe -Algorithm SHA256).Hash
        $sizeMb = [math]::Round($sizeBytes / 1MB, 1)
        Write-ReleaseLog "publish ($LogLabel) — OK ($([int]$sw.Elapsed.TotalSeconds)s) -> $finalExe ($sizeMb MB, sha256=$sha256)"

        return [pscustomobject]@{
            Path      = $finalExe
            SizeBytes = $sizeBytes
            Sha256    = $sha256
        }
    }

    # 7. Publish self-contained
    $withRuntimeResult = Publish-Variant `
        -Name 'Steamoff-with-dotnet-runtime' `
        -OutDir $WithRuntimeDir `
        -SelfContained $true `
        -ExtraArgs @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true') `
        -ReadmeContent $readmeWithRuntime `
        -LogLabel 'self-contained'

    # 8. Publish framework-dependent
    $withoutRuntimeResult = Publish-Variant `
        -Name 'Steamoff-without-dotnet-runtime' `
        -OutDir $WithoutRuntimeDir `
        -SelfContained $false `
        -ExtraArgs @('-p:PublishSingleFile=true') `
        -ReadmeContent $readmeWithoutRuntime `
        -LogLabel 'framework-dependent'

    # 9. Compute version, write release-manifest.json
    $version = $null
    try {
        $versionInfo = (Get-Item $withRuntimeResult.Path).VersionInfo.ProductVersion
        if ($versionInfo) { $version = $versionInfo.Trim() }
    }
    catch { $version = $null }
    if (-not $version) { $version = '1.0.0.0' }

    $builtAt = (Get-Date).ToString('yyyy-MM-ddTHH:mm:sszzz')

    $manifest = [ordered]@{
        appName       = 'Steamoff'
        version       = $version
        builtAt       = $builtAt
        configuration = 'Release'
        runtime       = 'win-x64'
        outputs       = @(
            [ordered]@{
                name                 = 'Steamoff-with-dotnet-runtime'
                type                 = 'self-contained'
                includesDotnetRuntime = $true
                path                 = 'Steamoff-with-dotnet-runtime\Steamoff.exe'
                sizeBytes            = $withRuntimeResult.SizeBytes
                sha256               = $withRuntimeResult.Sha256
            },
            [ordered]@{
                name                 = 'Steamoff-without-dotnet-runtime'
                type                 = 'framework-dependent'
                includesDotnetRuntime = $false
                path                 = 'Steamoff-without-dotnet-runtime\Steamoff.exe'
                sizeBytes            = $withoutRuntimeResult.SizeBytes
                sha256               = $withoutRuntimeResult.Sha256
            }
        )
    }

    $manifestJson = $manifest | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($ManifestPath, $manifestJson, $utf8NoBom)
    Write-ReleaseLog 'release-manifest.json записан / written'

    # 10. Finalize release-log.txt, print final paths
    Write-ReleaseLog '=== Сборка релиза завершена успешно / Release build completed successfully ==='
    Save-ReleaseLog

    Write-Host ''
    Write-Host 'Готовые сборки / Release outputs:'
    Write-Host "  $($withRuntimeResult.Path)"
    Write-Host "  $($withoutRuntimeResult.Path)"
    Write-Host "  $ManifestPath"
    Write-Host "  $LogPath"

    # 11. Exit 0
    exit 0
}
finally {
    Pop-Location
}
