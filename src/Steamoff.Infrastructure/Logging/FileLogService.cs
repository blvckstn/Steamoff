using System.Reflection;
using System.Text;
using Steamoff.Core.Interfaces;

namespace Steamoff.Infrastructure.Logging;

/// <summary>
/// Append-only structured text log at %ProgramData%\Steamoff\logs\steamoff.log
/// (falls back to %AppData% on the same writability probe as JsonSettingsService —
/// see ASSUMPTIONS A3). Thread-safe via a simple lock; rotates the file once it
/// crosses a size threshold to avoid unbounded growth.
/// </summary>
public sealed class FileLogService : ILogService
{
    private const string LogFileName = "steamoff.log";
    private const long MaxLogSizeBytes = 5 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly string _logDirectory;

    public FileLogService()
        : this(ResolveLogDirectory())
    {
    }

    /// <summary>Test seam — lets unit tests point at a temp directory directly.</summary>
    internal FileLogService(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
        ArchiveMojibakeLogIfNeeded();
    }

    public string LogFilePath => Path.Combine(_logDirectory, LogFileName);

    public void Info(string message) => Write("INFO", message);
    public void Warning(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null)
    {
        var text = exception is null ? message : $"{message} :: {exception.GetType().Name}: {exception.Message}";
        Write("ERROR", text);
    }

    public async Task<IReadOnlyList<string>> ReadLastLinesAsync(int count, CancellationToken ct = default)
    {
        if (!File.Exists(LogFilePath))
        {
            return Array.Empty<string>();
        }

        string[] allLines;
        lock (_gate)
        {
            allLines = File.ReadAllLines(LogFilePath);
        }

        await Task.Yield();
        ct.ThrowIfCancellationRequested();

        return allLines.Length <= count ? allLines : allLines[^count..];
    }

    public async Task<string> BuildDiagnosticsReportAsync(CancellationToken ct = default)
    {
        var version = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.1.0";

        var sb = new StringBuilder();
        sb.AppendLine("=== Steamoff Diagnostics Report ===");
        sb.AppendLine($"Версия: {version}");
        sb.AppendLine($"ОС: {Environment.OSVersion}");
        sb.AppendLine($"Пользователь: {Environment.UserDomainName}\\{Environment.UserName}");
        sb.AppendLine($"Машина: {Environment.MachineName}");
        sb.AppendLine($"Каталог логов: {_logDirectory}");
        sb.AppendLine($"Файл лога: {LogFilePath}");
        sb.AppendLine($"Время отчёта (UTC): {DateTimeOffset.UtcNow:O}");

        var tail = await ReadLastLinesAsync(50, ct).ConfigureAwait(false);
        sb.AppendLine();
        sb.AppendLine($"--- Последние {tail.Count} строк лога ---");
        foreach (var line in tail)
        {
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}";

        lock (_gate)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                // Logging must never crash the app — swallow and move on.
                System.Diagnostics.Debug.WriteLine($"[Steamoff] Не удалось записать в лог: {ex.Message}");
            }
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(LogFilePath))
        {
            return;
        }

        var info = new FileInfo(LogFilePath);
        if (info.Length < MaxLogSizeBytes)
        {
            return;
        }

        var rotatedName = $"{LogFileName}.{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.old";
        var rotatedPath = Path.Combine(_logDirectory, rotatedName);
        File.Move(LogFilePath, rotatedPath, overwrite: true);
    }

    private void ArchiveMojibakeLogIfNeeded()
    {
        if (!File.Exists(LogFilePath))
        {
            return;
        }

        try
        {
            var sample = File.ReadAllText(LogFilePath, Encoding.UTF8);
            if (!LooksLikeMojibake(sample))
            {
                return;
            }

            var archivedName = $"{LogFileName}.{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.mojibake.old";
            File.Move(LogFilePath, Path.Combine(_logDirectory, archivedName), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[Steamoff] Не удалось архивировать лог с поврежденной кодировкой: {ex.Message}");
        }
    }

    private static bool LooksLikeMojibake(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var markers = new[] { "ЌҐ", "г¤", "а®", "Рќ", "Рћ", "СЃ", "вЂ" };
        return markers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    private static string ResolveLogDirectory()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var primary = Path.Combine(programData, "Steamoff", "logs");

        if (TryEnsureWritable(primary))
        {
            return primary;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Steamoff", "logs");
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
