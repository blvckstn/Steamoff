using System.ComponentModel;
using System.Diagnostics;
using Steamoff.Core.Interfaces;

namespace Steamoff.Infrastructure.UserContext;

/// <summary>
/// Checks current elevation and performs a self-relaunch via ShellExecute's
/// "runas" verb (triggers UAC), preserving arguments and the --tray flag.
/// If the user cancels the UAC prompt (Win32 error 1223 / ERROR_CANCELLED),
/// TryRelaunchElevated returns false with a friendly reason instead of throwing,
/// so the caller can fall back to read-only mode (Constitution principle IV).
/// </summary>
public sealed class ElevationService : IElevationService
{
    private const int ErrorCancelled = 1223;

    public bool IsRunningElevated => TokenElevation.IsProcessElevated();

    public bool TryRelaunchElevated(IReadOnlyList<string> arguments, out string? failureReason)
    {
        failureReason = null;

        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            failureReason = "Не удалось определить путь к текущему исполняемому файлу.";
            return false;
        }

        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
            Arguments = string.Join(' ', arguments.Select(QuoteIfNeeded))
        };

        try
        {
            using var process = Process.Start(psi);
            return process is not null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            failureReason = "Запрос на повышение прав был отклонён пользователем (UAC).";
            return false;
        }
        catch (Win32Exception ex)
        {
            failureReason = $"Не удалось перезапустить от имени администратора: {ex.Message}";
            return false;
        }
    }

    private static string QuoteIfNeeded(string argument) =>
        argument.Contains(' ') ? $"\"{argument}\"" : argument;
}
