using System.Diagnostics;
using Microsoft.Win32;
using Steamoff.Core.Enums;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;

namespace Steamoff.Infrastructure.Steam;

/// <summary>
/// Finds the Steam installation, in priority order: registry, running process,
/// well-known default paths, then Start Menu / Desktop shortcuts. Ported from
/// </summary>
public sealed class SteamDiscoveryService : ISteamDiscoveryService
{
    private static readonly string[] RegistryCandidates =
    {
        @"HKEY_CURRENT_USER\Software\Valve\Steam:SteamPath",
        @"HKEY_CURRENT_USER\Software\Valve\Steam:InstallPath",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam:InstallPath",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam:InstallPath",
    };

    private static readonly string[] DefaultPaths =
    {
        @"C:\Program Files (x86)\Steam",
        @"C:\Program Files\Steam",
    };

    private readonly ILogService _log;

    public SteamDiscoveryService(ILogService log)
    {
        _log = log;
    }

    public Task<SteamInstallation> DiscoverAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var fromRegistry = TryRegistry();
            if (fromRegistry is not null)
            {
                _log.Info($"Steam обнаружен через реестр: {fromRegistry.Path}");
                return fromRegistry;
            }

            ct.ThrowIfCancellationRequested();
            var fromProcess = TryRunningProcess();
            if (fromProcess is not null)
            {
                _log.Info($"Steam обнаружен через запущенный процесс: {fromProcess.Path}");
                return fromProcess;
            }

            ct.ThrowIfCancellationRequested();
            var fromDefaults = TryDefaultPaths();
            if (fromDefaults is not null)
            {
                _log.Info($"Steam обнаружен по стандартному пути: {fromDefaults.Path}");
                return fromDefaults;
            }

            ct.ThrowIfCancellationRequested();
            var fromShortcuts = TryShortcuts();
            if (fromShortcuts is not null)
            {
                _log.Info($"Steam обнаружен через ярлык: {fromShortcuts.Path}");
                return fromShortcuts;
            }

            _log.Warning("Steam не найден ни одним из методов автообнаружения.");
            return SteamInstallation.NotFound;
        }, ct);
    }

    public SteamInstallation ValidateManualPath(string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || !Directory.Exists(candidatePath))
        {
            return SteamInstallation.NotFound;
        }

        var exePath = Path.Combine(candidatePath, "steam.exe");
        if (!File.Exists(exePath))
        {
            return new SteamInstallation { Path = candidatePath, IsValid = false, DiscoverySource = DiscoverySource.Manual };
        }

        return new SteamInstallation
        {
            Path = candidatePath,
            SteamExePath = exePath,
            IsValid = true,
            DiscoverySource = DiscoverySource.Manual
        };
    }

    private SteamInstallation? TryRegistry()
    {
        foreach (var candidate in RegistryCandidates)
        {
            var parts = candidate.Split(':', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            try
            {
                var (hive, subKey) = SplitHiveAndPath(parts[0]);
                using var key = hive.OpenSubKey(subKey);
                var value = key?.GetValue(parts[1]) as string;
                if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                {
                    var exe = Path.Combine(value, "steam.exe");
                    if (File.Exists(exe))
                    {
                        return new SteamInstallation { Path = value, SteamExePath = exe, IsValid = true, DiscoverySource = DiscoverySource.Registry };
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
            {
                _log.Warning($"Не удалось прочитать реестр '{candidate}': {ex.Message}");
            }
        }

        return null;
    }

    private static (RegistryKey Hive, string SubKey) SplitHiveAndPath(string fullPath)
    {
        var segments = fullPath.Split('\\', 2);
        var hive = segments[0] switch
        {
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            _ => throw new ArgumentOutOfRangeException(nameof(fullPath), $"Неизвестный раздел реестра: {segments[0]}")
        };

        return (hive, segments.Length > 1 ? segments[1] : string.Empty);
    }

    private SteamInstallation? TryRunningProcess()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("steam"))
            {
                try
                {
                    var exePath = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                    {
                        var dir = Path.GetDirectoryName(exePath)!;
                        return new SteamInstallation { Path = dir, SteamExePath = exePath, IsValid = true, DiscoverySource = DiscoverySource.RunningProcess };
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    _log.Warning($"Не удалось получить путь запущенного процесса Steam: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Ошибка перечисления процессов Steam: {ex.Message}");
        }

        return null;
    }

    private static SteamInstallation? TryDefaultPaths()
    {
        foreach (var candidate in DefaultPaths)
        {
            var exe = Path.Combine(candidate, "steam.exe");
            if (File.Exists(exe))
            {
                return new SteamInstallation { Path = candidate, SteamExePath = exe, IsValid = true, DiscoverySource = DiscoverySource.DefaultPath };
            }
        }

        return null;
    }

    private SteamInstallation? TryShortcuts()
    {
        var shortcutFolders = new List<string>();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        var commonStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);

        shortcutFolders.AddRange(new[] { desktop, startMenu, commonStartMenu }.Where(Directory.Exists));

        foreach (var folder in shortcutFolders)
        {
            IEnumerable<string> lnkFiles;
            try
            {
                lnkFiles = Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException)
            {
                continue;
            }

            foreach (var lnk in lnkFiles)
            {
                if (!lnk.Contains("Steam", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target = ShortcutResolver.TryResolveTarget(lnk);
                if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
                {
                    continue;
                }

                if (!string.Equals(Path.GetFileName(target), "steam.exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var dir = Path.GetDirectoryName(target)!;
                return new SteamInstallation { Path = dir, SteamExePath = target, IsValid = true, DiscoverySource = DiscoverySource.Shortcut };
            }
        }

        return null;
    }
}
