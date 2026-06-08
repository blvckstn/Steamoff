using System.Text.Json;
using System.Text.Json.Serialization;
using Steamoff.Core.Exceptions;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;

namespace Steamoff.Infrastructure.Settings;

/// <summary>
/// Loads/saves AppSettings as JSON under %ProgramData%\Steamoff (falling back
/// Writes are atomic (temp file + File.Replace), corrupted files are backed up
/// rather than overwritten, and the version field drives forward migrations.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private const string FileName = "settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    private readonly ILogService _log;
    private readonly string _directory;
    private readonly bool _usingFallback;

    public JsonSettingsService(ILogService log)
        : this(log, ResolveWritableDirectory(out var fallback), fallback)
    {
    }

    /// <summary>Test seam — lets unit tests point at a temp directory directly.</summary>
    internal JsonSettingsService(ILogService log, string directory, bool usingFallback)
    {
        _log = log;
        _directory = directory;
        _usingFallback = usingFallback;
        Directory.CreateDirectory(_directory);
    }

    public string SettingsFilePath => Path.Combine(_directory, FileName);
    public bool IsUsingFallbackLocation => _usingFallback;

    public async Task<AppSettings> LoadAsync(CancellationToken ct = default)
    {
        var path = SettingsFilePath;
        if (!File.Exists(path))
        {
            _log.Info($"Файл настроек не найден, создаю значения по умолчанию: {path}");
            var defaults = AppSettings.CreateDefault();
            await SaveAsync(defaults, ct).ConfigureAwait(false);
            return defaults;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null)
            {
                throw new JsonException("Десериализация вернула null.");
            }

            return MigrateIfNeeded(settings);
        }
        catch (Exception ex) when (ex is JsonException or System.IO.IOException)
        {
            _log.Error($"Файл настроек повреждён: {path}", ex);
            BackupCorruptedFile(path);

            var defaults = AppSettings.CreateDefault();
            await SaveAsync(defaults, ct).ConfigureAwait(false);
            return defaults;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);

            var tempPath = SettingsFilePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);

            if (File.Exists(SettingsFilePath))
            {
                File.Replace(tempPath, SettingsFilePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, SettingsFilePath);
            }

            _log.Info($"Настройки сохранены: {SettingsFilePath}");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            throw new SettingsPersistenceException($"Не удалось сохранить настройки в '{SettingsFilePath}'.", ex);
        }
    }

    private void BackupCorruptedFile(string path)
    {
        try
        {
            var backupPath = $"{path}.corrupted-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.bak";
            File.Copy(path, backupPath, overwrite: true);
            _log.Warning($"Повреждённый файл настроек скопирован в резервную копию: {backupPath}");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _log.Error("Не удалось создать резервную копию повреждённого файла настроек.", ex);
        }
    }

    private AppSettings MigrateIfNeeded(AppSettings settings)
    {
        if (settings.Version >= AppSettings.CurrentVersion)
        {
            return settings;
        }

        _log.Info($"Миграция настроек с версии {settings.Version} до {AppSettings.CurrentVersion}.");
        // v1 -> v2: added Language/IsFirstLaunchCompleted. System.Text.Json already
        // filled them with the model's defaults ("ru" / false) for files that
        // predate these fields, so loading an old settings.json simply shows the
        // first-launch language dialog once more — exactly the desired behavior.
        // User-added targets (AdditionalFolders/AdditionalExecutables) are
        // preserved as-is because we deserialize into the same model and only
        // bump the version stamp here.
        // v2 -> v3: added FirewallStrategyMode/LastSuccessfulFirewallStrategy/FirewallSelfTest.
        // System.Text.Json already filled them with the model's defaults
        // (Auto / null / a fresh record with Outcome = NotYetRun) for files that
        // predate these fields — which is exactly what triggers the one-time
        // first-launch self-test on the next startup, for upgrades and fresh
        // installs alike.
        settings.Version = AppSettings.CurrentVersion;
        return settings;
    }

    /// <summary>Prefers %ProgramData%\Steamoff; falls back to %AppData%\Steamoff if the former can't be created/written to.</summary>
    private static string ResolveWritableDirectory(out bool usingFallback)
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var primary = Path.Combine(programData, "Steamoff");

        if (TryEnsureWritable(primary))
        {
            usingFallback = false;
            return primary;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        usingFallback = true;
        return Path.Combine(appData, "Steamoff");
    }

    private static bool TryEnsureWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }
}
