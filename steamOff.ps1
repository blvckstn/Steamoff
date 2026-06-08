#requires -RunAsAdministrator

param(
    [ValidateSet("block", "allow", "status")]
    [string]$Mode,

    [ValidateSet("client", "folder")]
    [string]$Scope = "client",

    [string]$SteamPath,

    [string[]]$ExtraPaths = @(),

    [switch]$KillSteam
)

$RuleGroup = "SteamOfflineToggle"

function Resolve-SteamPath {
    param([string]$ManualPath)

    if ($ManualPath -and (Test-Path $ManualPath -PathType Container)) {
        return (Resolve-Path $ManualPath).Path
    }

    $registryPaths = @(
        "HKCU:\Software\Valve\Steam",
        "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam",
        "HKLM:\SOFTWARE\Valve\Steam"
    )

    foreach ($reg in $registryPaths) {
        try {
            $value = (Get-ItemProperty -Path $reg -ErrorAction Stop).InstallPath
            if ($value -and (Test-Path $value -PathType Container)) {
                return (Resolve-Path $value).Path
            }
        } catch {}
    }

    $fallback = "${env:ProgramFiles(x86)}\Steam"
    if (Test-Path $fallback -PathType Container) {
        return (Resolve-Path $fallback).Path
    }

    throw "Не удалось найти папку Steam. Укажи путь вручную через -SteamPath `"D:\Steam`""
}

function Add-Exe {
    param(
        [string]$Path,
        [System.Collections.Generic.HashSet[string]]$Set
    )

    if ($Path -and (Test-Path $Path -PathType Leaf)) {
        $resolved = (Resolve-Path $Path).Path
        if ([System.IO.Path]::GetExtension($resolved) -ieq ".exe") {
            [void]$Set.Add($resolved)
        }
    }
}

function Add-PathExecutables {
    param(
        [string]$Path,
        [System.Collections.Generic.HashSet[string]]$Set
    )

    if (-not $Path) { return }

    if (Test-Path $Path -PathType Leaf) {
        Add-Exe -Path $Path -Set $Set
        return
    }

    if (Test-Path $Path -PathType Container) {
        Get-ChildItem -Path $Path -Filter "*.exe" -Recurse -ErrorAction SilentlyContinue |
            ForEach-Object {
                Add-Exe -Path $_.FullName -Set $Set
            }
    }
}

function Get-SteamExecutables {
    param(
        [string]$SteamRoot,
        [string]$ScopeMode,
        [string[]]$Extra
    )

    $set = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase
    )

    if ($ScopeMode -eq "client") {
        $knownRelative = @(
            "steam.exe",
            "steamservice.exe",
            "GameOverlayUI.exe",
            "steamerrorreporter.exe",
            "steamerrorreporter64.exe",
            "steam_monitor.exe",
            "bin\steamservice.exe"
        )

        foreach ($rel in $knownRelative) {
            Add-Exe -Path (Join-Path $SteamRoot $rel) -Set $set
        }

        Get-ChildItem -Path $SteamRoot -Filter "*.exe" -Recurse -ErrorAction SilentlyContinue |
            Where-Object {
                $_.FullName -notmatch "\\steamapps\\common\\" -and
                (
                    $_.Name -match "^(steam|steamwebhelper|GameOverlayUI|streaming_client|WriteMiniDump).*\.exe$" -or
                    $_.FullName -match "\\bin\\cef\\"
                )
            } |
            ForEach-Object {
                Add-Exe -Path $_.FullName -Set $set
            }
    }

    if ($ScopeMode -eq "folder") {
        Get-ChildItem -Path $SteamRoot -Filter "*.exe" -Recurse -ErrorAction SilentlyContinue |
            ForEach-Object {
                Add-Exe -Path $_.FullName -Set $set
            }
    }

    foreach ($extraPath in $Extra) {
        Add-PathExecutables -Path $extraPath -Set $set
    }

    return $set
}

function Remove-SteamRules {
    Get-NetFirewallRule -Group $RuleGroup -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule
}

function Get-SteamStatus {
    $rules = Get-NetFirewallRule -Group $RuleGroup -ErrorAction SilentlyContinue

    if (-not $rules) {
        return @{
            IsBlocked = $false
            Outbound = 0
            Inbound = 0
        }
    }

    return @{
        IsBlocked = $true
        Outbound = ($rules | Where-Object { $_.Direction -eq "Outbound" }).Count
        Inbound = ($rules | Where-Object { $_.Direction -eq "Inbound" }).Count
    }
}

function Show-Status {
    $status = Get-SteamStatus

    Write-Host ""
    Write-Host "=============================="
    Write-Host " Steam Offline Toggle"
    Write-Host "=============================="

    if ($status.IsBlocked) {
        Write-Host "Статус: ИНТЕРНЕТ ДЛЯ STEAM ЗАБЛОКИРОВАН" -ForegroundColor Red
        Write-Host "Outbound rules: $($status.Outbound)"
        Write-Host "Inbound rules:  $($status.Inbound)"
    } else {
        Write-Host "Статус: интернет для Steam НЕ заблокирован этим скриптом." -ForegroundColor Green
    }

    Write-Host "=============================="
    Write-Host ""
}

function Block-Steam {
    param(
        [string]$ScopeMode,
        [bool]$ShouldKillSteam
    )

    $resolvedSteamPath = Resolve-SteamPath -ManualPath $SteamPath

    if ($ShouldKillSteam) {
        Get-Process steam, steamwebhelper, steamservice, GameOverlayUI -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction SilentlyContinue
    }

    Remove-SteamRules

    $executables = Get-SteamExecutables `
        -SteamRoot $resolvedSteamPath `
        -ScopeMode $ScopeMode `
        -Extra $ExtraPaths

    if ($executables.Count -eq 0) {
        throw "Не найдено exe-файлов для блокировки."
    }

    foreach ($exe in ($executables | Sort-Object)) {
        $leaf = Split-Path $exe -Leaf

        New-NetFirewallRule `
            -DisplayName "Steam Offline OUT - $leaf" `
            -Group $RuleGroup `
            -Direction Outbound `
            -Program $exe `
            -Action Block `
            -Profile Any `
            -Enabled True | Out-Null

        New-NetFirewallRule `
            -DisplayName "Steam Offline IN - $leaf" `
            -Group $RuleGroup `
            -Direction Inbound `
            -Program $exe `
            -Action Block `
            -Profile Any `
            -Enabled True | Out-Null
    }

    Write-Host ""
    Write-Host "Готово: Steam заблокирован через Windows Firewall." -ForegroundColor Red
    Write-Host "Папка Steam: $resolvedSteamPath"
    Write-Host "Режим: $ScopeMode"
    Write-Host "Заблокировано exe-файлов: $($executables.Count)"
    Write-Host ""
}

function Allow-Steam {
    Remove-SteamRules
    Write-Host ""
    Write-Host "Готово: интернет для Steam снова разрешён." -ForegroundColor Green
    Write-Host "Правила $RuleGroup удалены."
    Write-Host ""
}

function Show-Menu {
    while ($true) {
        Show-Status

        Write-Host "Что сделать?"
        Write-Host "1 — Выключить интернет только для Steam-клиента"
        Write-Host "2 — Жёстко выключить интернет для всей папки Steam"
        Write-Host "3 — Включить интернет обратно"
        Write-Host "4 — Обновить статус"
        Write-Host "0 — Выход"
        Write-Host ""

        $choice = Read-Host "Введите номер"

        switch ($choice) {
            "1" {
                Block-Steam -ScopeMode "client" -ShouldKillSteam $true
                pause
            }
            "2" {
                Write-Host ""
                Write-Host "Внимание: этот режим может заблокировать интернет играм, лаунчерам, античитам и cloud save." -ForegroundColor Yellow
                $confirm = Read-Host "Продолжить? Y/N"

                if ($confirm -match "^[YyДд]$") {
                    Block-Steam -ScopeMode "folder" -ShouldKillSteam $true
                } else {
                    Write-Host "Отменено."
                }

                pause
            }
            "3" {
                Allow-Steam
                pause
            }
            "4" {
                continue
            }
            "0" {
                exit
            }
            default {
                Write-Host "Не понял команду. Введите 1, 2, 3, 4 или 0." -ForegroundColor Yellow
                pause
            }
        }
    }
}

if (-not $Mode) {
    Show-Menu
    exit
}

if ($Mode -eq "status") {
    Show-Status
    exit
}

if ($Mode -eq "allow") {
    Show-Status
    Allow-Steam
    Show-Status
    exit
}

if ($Mode -eq "block") {
    Show-Status
    Block-Steam -ScopeMode $Scope -ShouldKillSteam ([bool]$KillSteam)
    Show-Status
    exit
}