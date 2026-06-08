using System.Text.RegularExpressions;
using Steamoff.Core.Enums;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;

namespace Steamoff.Infrastructure.Scanning;

/// <summary>
/// Discovers Steam Core executables (steam.exe, steamservice.exe, every
/// steamwebhelper.exe copy) and scans arbitrary folders for executables.
/// Cancellable, depth-limited, never blocks the UI thread, tolerates
/// access-denied subdirectories. The known-relative-path list and the
/// "exclude steamapps\common" rule are ported from the legacy steamOff.ps1
/// (see ASSUMPTIONS A6); steamservice.exe / steamwebhelper.exe are tracked as
/// distinct named targets per the brief (section 11), unlike the legacy script
/// which lumped everything into one rule group.
/// </summary>
public sealed class TargetScanner : ITargetScanner
{
    public const int MaxScanDepth = 6;
    private const int MaxResultsPerFolder = 5000;

    private static readonly string[] KnownRelativeSteamCore =
    {
        "steam.exe",
        "steamservice.exe",
        @"bin\steamservice.exe",
    };

    private static readonly Regex SteamCorePattern = new(
        @"^(steam|steamwebhelper)(\.exe)$|^steamwebhelper.*\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogService _log;

    public TargetScanner(ILogService log)
    {
        _log = log;
    }

    public Task<IReadOnlyList<FirewallTarget>> FindSteamCoreTargetsAsync(string steamRoot, bool blockAllInFolder, CancellationToken ct = default)
    {
        return Task.Run<IReadOnlyList<FirewallTarget>>(() =>
        {
            var found = new Dictionary<string, FirewallTarget>(StringComparer.OrdinalIgnoreCase);

            void AddIfExe(string path, string displayName)
            {
                if (!File.Exists(path))
                {
                    return;
                }

                var resolved = Path.GetFullPath(path);
                if (!found.ContainsKey(resolved))
                {
                    found[resolved] = new FirewallTarget
                    {
                        Id = resolved,
                        DisplayName = displayName,
                        ExecutablePath = resolved,
                        Kind = TargetKind.SteamCore
                    };
                }
            }

            foreach (var relative in KnownRelativeSteamCore)
            {
                ct.ThrowIfCancellationRequested();
                AddIfExe(Path.Combine(steamRoot, relative), Path.GetFileName(relative));
            }

            if (Directory.Exists(steamRoot))
            {
                foreach (var exe in EnumerateExecutables(steamRoot, MaxScanDepth, ct))
                {
                    ct.ThrowIfCancellationRequested();

                    if (IsInSteamAppsCommon(exe))
                    {
                        continue;
                    }

                    var fileName = Path.GetFileName(exe);

                    if (blockAllInFolder)
                    {
                        AddIfExe(exe, fileName);
                        continue;
                    }

                    if (SteamCorePattern.IsMatch(fileName))
                    {
                        AddIfExe(exe, fileName);
                    }
                }
            }
            else
            {
                _log.Warning($"Папка Steam не найдена при сканировании Steam Core: {steamRoot}");
            }

            return found.Values
                .OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, ct);
    }

    public Task<IReadOnlyList<string>> ScanFolderForExecutablesAsync(string folderPath, bool recursive, IProgress<int>? progress, CancellationToken ct = default)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            if (!Directory.Exists(folderPath))
            {
                return Array.Empty<string>();
            }

            var results = new List<string>();
            var maxDepth = recursive ? MaxScanDepth : 0;

            foreach (var exe in EnumerateExecutables(folderPath, maxDepth, ct))
            {
                ct.ThrowIfCancellationRequested();
                results.Add(exe);
                progress?.Report(results.Count);

                if (results.Count >= MaxResultsPerFolder)
                {
                    _log.Warning($"Сканирование папки '{folderPath}' остановлено по достижении лимита {MaxResultsPerFolder} файлов.");
                    break;
                }
            }

            return results;
        }, ct);
    }

    /// <summary>Breadth-limited recursive enumeration that swallows per-directory access errors and never throws on a single bad subtree.</summary>
    private IEnumerable<string> EnumerateExecutables(string root, int maxDepth, CancellationToken ct)
    {
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (current, depth) = stack.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current, "*.exe", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException)
            {
                _log.Warning($"Нет доступа к папке '{current}': {ex.Message}");
                continue;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                yield return file;
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException)
            {
                _log.Warning($"Нет доступа к подпапкам '{current}': {ex.Message}");
                continue;
            }

            foreach (var sub in subdirs)
            {
                stack.Push((sub, depth + 1));
            }
        }
    }

    private static bool IsInSteamAppsCommon(string path) =>
        path.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase);
}
