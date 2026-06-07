using Steamoff.Core.Exceptions;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;

namespace Steamoff.Infrastructure.Diagnostics;

/// <summary>
/// Runs the read-only check battery behind the Settings View "Тестирование"
/// button: elevation, settings/log file access, Steam discovery, additional
/// folders/EXEs validity, firewall read access, and autostart consistency.
/// Every check only reads — it never mutates firewall rules, files, or Steam.
/// </summary>
public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly IUserContextService _userContext;
    private readonly ISettingsService _settings;
    private readonly ILogService _log;
    private readonly ISteamDiscoveryService _steamDiscovery;
    private readonly ITargetScanner _scanner;
    private readonly IFirewallService _firewall;
    private readonly IAutostartService _autostart;

    public DiagnosticsService(
        IUserContextService userContext,
        ISettingsService settings,
        ILogService log,
        ISteamDiscoveryService steamDiscovery,
        ITargetScanner scanner,
        IFirewallService firewall,
        IAutostartService autostart)
    {
        _userContext = userContext;
        _settings = settings;
        _log = log;
        _steamDiscovery = steamDiscovery;
        _scanner = scanner;
        _firewall = firewall;
        _autostart = autostart;
    }

    public async Task<DiagnosticsReport> RunAsync(AppSettings settings, CancellationToken ct = default)
    {
        var checks = new List<DiagnosticCheckResult>();

        CheckElevation(checks);
        CheckPathAccess(checks, "settings.json", _settings.SettingsFilePath, _settings.IsUsingFallbackLocation);
        CheckPathAccess(checks, "log", _log.LogFilePath, usingFallback: false);

        var steamRoot = await CheckSteamAsync(checks, settings, ct).ConfigureAwait(false);
        if (steamRoot is not null)
        {
            await CheckSteamCoreAsync(checks, steamRoot, settings.BlockAllExecutablesInSteamFolder, ct).ConfigureAwait(false);
        }

        CheckFolders(checks, settings.AdditionalFolders);
        CheckExecutables(checks, settings.AdditionalExecutables);
        await CheckFirewallAsync(checks, ct).ConfigureAwait(false);
        await CheckAutostartAsync(checks, settings, ct).ConfigureAwait(false);

        var overall = checks.Count == 0
            ? TestOutcome.Warning
            : checks.Max(c => c.Outcome);

        return new DiagnosticsReport
        {
            Checks = checks,
            OverallOutcome = overall,
            RanAt = DateTimeOffset.UtcNow
        };
    }

    private void CheckElevation(List<DiagnosticCheckResult> checks)
    {
        var context = _userContext.GetCurrentContext();
        checks.Add(context.HasFirewallAccess
            ? Ok("Admin/Elevation", "Приложение запущено с правами администратора — операции с firewall доступны.")
            : Error("Admin/Elevation", context.Warning ?? "Нет прав администратора — операции с firewall недоступны (режим только для чтения)."));
    }

    private static void CheckPathAccess(List<DiagnosticCheckResult> checks, string label, string path, bool usingFallback)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                checks.Add(usingFallback
                    ? Warning(label, $"Каталог доступен, но используется резервное расположение: {path}")
                    : Ok(label, $"Каталог доступен для записи: {path}"));
            }
            else
            {
                checks.Add(Error(label, $"Каталог не найден или недоступен: {path}"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            checks.Add(Error(label, $"Не удалось проверить доступ к '{path}': {ex.Message}"));
        }
    }

    private async Task<string?> CheckSteamAsync(List<DiagnosticCheckResult> checks, AppSettings settings, CancellationToken ct)
    {
        SteamInstallation installation;
        if (!string.IsNullOrWhiteSpace(settings.SteamPath))
        {
            installation = _steamDiscovery.ValidateManualPath(settings.SteamPath);
        }
        else
        {
            installation = await _steamDiscovery.DiscoverAsync(ct).ConfigureAwait(false);
        }

        if (installation.IsValid && installation.Path is not null)
        {
            checks.Add(Ok("Steam", $"Steam найден: {installation.Path} (источник: {installation.DiscoverySource})."));
            return installation.Path;
        }

        checks.Add(Warning("Steam", "Steam не найден автоматически. Укажите путь вручную в настройках."));
        return null;
    }

    private async Task CheckSteamCoreAsync(List<DiagnosticCheckResult> checks, string steamRoot, bool blockAllInFolder, CancellationToken ct)
    {
        try
        {
            var targets = await _scanner.FindSteamCoreTargetsAsync(steamRoot, blockAllInFolder, ct).ConfigureAwait(false);
            checks.Add(targets.Count > 0
                ? Ok("Steam Core", $"Найдено исполняемых файлов ядра Steam: {targets.Count}.")
                : Warning("Steam Core", "Не удалось найти steam.exe / steamservice.exe в указанной папке."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            checks.Add(Error("Steam Core", $"Ошибка сканирования папки Steam: {ex.Message}"));
        }
    }

    private static void CheckFolders(List<DiagnosticCheckResult> checks, IReadOnlyList<FolderBlockTarget> folders)
    {
        if (folders.Count == 0)
        {
            checks.Add(Ok("Папки", "Дополнительные папки не настроены."));
            return;
        }

        var missing = folders.Where(f => !Directory.Exists(f.Path)).ToList();
        checks.Add(missing.Count == 0
            ? Ok("Папки", $"Все дополнительные папки доступны ({folders.Count}).")
            : Warning("Папки", $"Не найдено {missing.Count} из {folders.Count} папок: {string.Join(", ", missing.Select(f => f.Name))}."));
    }

    private static void CheckExecutables(List<DiagnosticCheckResult> checks, IReadOnlyList<ExeBlockTarget> exes)
    {
        if (exes.Count == 0)
        {
            checks.Add(Ok("EXE-файлы", "Отдельные исполняемые файлы не настроены."));
            return;
        }

        var missing = exes.Where(e => !File.Exists(e.Path)).ToList();
        checks.Add(missing.Count == 0
            ? Ok("EXE-файлы", $"Все отслеживаемые файлы найдены ({exes.Count}).")
            : Warning("EXE-файлы", $"Не найдено {missing.Count} из {exes.Count} файлов: {string.Join(", ", missing.Select(e => e.Name))}."));
    }

    private async Task CheckFirewallAsync(List<DiagnosticCheckResult> checks, CancellationToken ct)
    {
        try
        {
            var state = await _firewall.GetCurrentStateAsync(ct).ConfigureAwait(false);
            checks.Add(Ok("Firewall", $"Чтение правил Microsoft Defender Firewall работает (найдено правил Steamoff: {state.Rules.Count})."));
        }
        catch (FirewallAccessDeniedException)
        {
            checks.Add(Error("Firewall", "Нет доступа к Microsoft Defender Firewall — требуются права администратора."));
        }
        catch (FirewallOperationException ex)
        {
            checks.Add(Error("Firewall", $"Не удалось прочитать правила firewall: {ex.Message}"));
        }
    }

    private async Task CheckAutostartAsync(List<DiagnosticCheckResult> checks, AppSettings settings, CancellationToken ct)
    {
        if (!settings.StartWithWindows)
        {
            checks.Add(Ok("Автозапуск", "Автозапуск отключён в настройках — проверка пропущена."));
            return;
        }

        try
        {
            var installed = await _autostart.IsInstalledAsync(ct).ConfigureAwait(false);
            checks.Add(installed
                ? Ok("Автозапуск", "Задача автозапуска установлена в Планировщике заданий Windows.")
                : Warning("Автозапуск", "Автозапуск включён в настройках, но задача в Планировщике заданий не найдена. Сохраните настройки для её установки."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            checks.Add(Error("Автозапуск", $"Не удалось проверить задачу автозапуска: {ex.Message}"));
        }
    }

    private static DiagnosticCheckResult Ok(string name, string message) => new() { Name = name, Outcome = TestOutcome.Ok, Message = message };
    private static DiagnosticCheckResult Warning(string name, string message) => new() { Name = name, Outcome = TestOutcome.Warning, Message = message };
    private static DiagnosticCheckResult Error(string name, string message) => new() { Name = name, Outcome = TestOutcome.Error, Message = message };
}
