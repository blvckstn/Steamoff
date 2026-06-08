using System.Diagnostics;
using System.Xml.Linq;
using Steamoff.Core.Interfaces;

namespace Steamoff.Infrastructure.Autostart;

/// <summary>
/// Creates/removes/verifies the "Steamoff" Windows Task Scheduler entry via
/// schtasks.exe (logon trigger, highest privileges, "--tray" argument). Every
/// argument is passed through ProcessStartInfo.ArgumentList — never through
/// "cmd /c" string concatenation — so user-controlled values (the exe path)
/// </summary>
public sealed class TaskSchedulerAutostartService : IAutostartService
{
    private const string TaskName = "Steamoff";
    private readonly ILogService _log;

    public TaskSchedulerAutostartService(ILogService log)
    {
        _log = log;
    }

    public async Task<bool> IsInstalledAsync(CancellationToken ct = default)
    {
        var (exitCode, _, _) = await RunSchtasksAsync(new[] { "/Query", "/TN", TaskName }, ct).ConfigureAwait(false);
        return exitCode == 0;
    }

    public async Task InstallAsync(string executablePath, CancellationToken ct = default)
    {
        var workingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;
        var taskRun = $"\"{executablePath}\" --tray";

        var args = new[]
        {
            "/Create", "/TN", TaskName,
            "/SC", "ONLOGON",
            "/RL", "HIGHEST",
            "/TR", taskRun,
            "/F"
        };

        var (exitCode, _, error) = await RunSchtasksAsync(args, ct).ConfigureAwait(false);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Не удалось создать автозапуск: {error}");
        }

        _log.Info($"Автозапуск создан: задача '{TaskName}', exe='{executablePath}', рабочая папка='{workingDirectory}'.");
    }

    public async Task UninstallAsync(CancellationToken ct = default)
    {
        var (exitCode, _, error) = await RunSchtasksAsync(new[] { "/Delete", "/TN", TaskName, "/F" }, ct).ConfigureAwait(false);
        if (exitCode != 0 && !error.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Не удалось удалить автозапуск: {error}");
        }

        _log.Info($"Автозапуск удалён: задача '{TaskName}'.");
    }

    public async Task<AutostartCheckResult> VerifyAsync(string expectedExecutablePath, CancellationToken ct = default)
    {
        var (exitCode, xml, _) = await RunSchtasksAsync(new[] { "/Query", "/TN", TaskName, "/XML" }, ct).ConfigureAwait(false);
        if (exitCode != 0)
        {
            return new AutostartCheckResult(IsInstalled: false, PathMatches: false, HighestPrivileges: false, UserMatches: false, Details: "Задача автозапуска не найдена.");
        }

        try
        {
            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

            var command = doc.Descendants(ns + "Command").FirstOrDefault()?.Value ?? string.Empty;
            var runLevel = doc.Descendants(ns + "RunLevel").FirstOrDefault()?.Value ?? string.Empty;
            var userId = doc.Descendants(ns + "UserId").FirstOrDefault()?.Value ?? string.Empty;

            var pathMatches = command.Trim('"').Equals(expectedExecutablePath, StringComparison.OrdinalIgnoreCase);
            var highestPrivileges = string.Equals(runLevel, "HighestAvailable", StringComparison.OrdinalIgnoreCase);
            var currentUser = $"{Environment.UserDomainName}\\{Environment.UserName}";
            var userMatches = userId.EndsWith(Environment.UserName, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(userId, currentUser, StringComparison.OrdinalIgnoreCase);

            var details = (pathMatches, highestPrivileges, userMatches) switch
            {
                (true, true, true) => "Автозапуск настроен корректно.",
                _ => $"Расхождение автозапуска — путь={(pathMatches ? "OK" : "не совпадает")}, " +
                     $"привилегии={(highestPrivileges ? "OK" : "не наивысшие")}, " +
                     $"пользователь={(userMatches ? "OK" : $"'{userId}' != '{currentUser}'")}."
            };

            if (!pathMatches || !highestPrivileges || !userMatches)
            {
                _log.Warning(details);
            }

            return new AutostartCheckResult(IsInstalled: true, pathMatches, highestPrivileges, userMatches, details);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException)
        {
            _log.Error("Не удалось разобрать XML задачи автозапуска.", ex);
            return new AutostartCheckResult(IsInstalled: true, PathMatches: false, HighestPrivileges: false, UserMatches: false, Details: "Не удалось проверить задачу автозапуска (ошибка чтения XML).");
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunSchtasksAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить schtasks.exe.");
        var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return (process.ExitCode, await stdOutTask.ConfigureAwait(false), await stdErrTask.ConfigureAwait(false));
    }
}
