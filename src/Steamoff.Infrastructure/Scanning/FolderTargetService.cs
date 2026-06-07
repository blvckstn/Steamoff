using Steamoff.Core.Enums;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;

namespace Steamoff.Infrastructure.Scanning;

/// <summary>Manages the user's "Additional Folders" collection: add/remove/rescan and conversion to firewall targets.</summary>
public sealed class FolderTargetService : IFolderTargetService
{
    private readonly ITargetScanner _scanner;
    private readonly ILogService _log;

    public FolderTargetService(ITargetScanner scanner, ILogService log)
    {
        _scanner = scanner;
        _log = log;
    }

    public async Task<FolderBlockTarget> AddFolderAsync(string path, string? displayName, bool recursive, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Путь к папке не может быть пустым.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new ArgumentException($"Папка не найдена: {fullPath}", nameof(path));
        }

        var folder = new FolderBlockTarget
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(displayName) ? new DirectoryInfo(fullPath).Name : displayName.Trim(),
            Path = fullPath,
            Enabled = true,
            Recursive = recursive,
            Status = FolderStatus.Disabled
        };

        await RescanAsync(folder, ct).ConfigureAwait(false);
        _log.Info($"Добавлена папка для блокировки: {folder.Name} ({folder.Path}), exe найдено: {folder.DiscoveredExeCount}");
        return folder;
    }

    public void RemoveFolder(string id)
    {
        // Persistence/list ownership lives in AppSettings via ISettingsService; this
        // method exists on the contract for symmetry/testability of removal validation.
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Идентификатор папки не может быть пустым.", nameof(id));
        }
    }

    public async Task RescanAsync(FolderBlockTarget folder, CancellationToken ct = default)
    {
        if (!Directory.Exists(folder.Path))
        {
            folder.Status = FolderStatus.PathNotFound;
            folder.DiscoveredExeCount = 0;
            return;
        }

        try
        {
            var executables = await _scanner.ScanFolderForExecutablesAsync(folder.Path, folder.Recursive, progress: null, ct).ConfigureAwait(false);
            folder.DiscoveredExeCount = executables.Count;
            folder.Status = folder.Enabled
                ? (executables.Count > 0 ? FolderStatus.MissingRules : FolderStatus.OkUnblocked)
                : FolderStatus.Disabled;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error($"Ошибка сканирования папки '{folder.Path}'", ex);
            folder.Status = FolderStatus.ScanError;
        }
    }

    public async Task<IReadOnlyList<FirewallTarget>> ToFirewallTargetsAsync(FolderBlockTarget folder, CancellationToken ct = default)
    {
        if (!folder.Enabled || !Directory.Exists(folder.Path))
        {
            return Array.Empty<FirewallTarget>();
        }

        var executables = await _scanner.ScanFolderForExecutablesAsync(folder.Path, folder.Recursive, progress: null, ct).ConfigureAwait(false);

        return executables
            .Select(exe => new FirewallTarget
            {
                Id = exe,
                DisplayName = $"{folder.Name}/{Path.GetFileName(exe)}",
                ExecutablePath = exe,
                Kind = TargetKind.Folder
            })
            .ToList();
    }
}
