using Steamoff.Core.Models;

namespace Steamoff.Core.Interfaces;

/// <summary>Loads/saves AppSettings as JSON with atomic writes, corruption backup, and version migration.</summary>
public interface ISettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);

    /// <summary>The actual path settings.json was loaded from / will be saved to (ProgramData or AppData fallback).</summary>
    string SettingsFilePath { get; }

    /// <summary>True if Steamoff fell back to %AppData% because %ProgramData% was not writable.</summary>
    bool IsUsingFallbackLocation { get; }
}

/// <summary>Reconciles desired vs actual firewall state into a single honest HealthStatus.</summary>
public interface IStatusEvaluator
{
    HealthStatus Evaluate(
        DesiredFirewallState desired,
        ActualFirewallState actual,
        UserContextInfo userContext,
        IReadOnlyList<FolderBlockTarget> folders,
        IReadOnlyList<ExeBlockTarget> exes);
}

/// <summary>Creates/removes/verifies the Windows Task Scheduler autostart entry.</summary>
public interface IAutostartService
{
    Task<bool> IsInstalledAsync(CancellationToken ct = default);
    Task InstallAsync(string executablePath, CancellationToken ct = default);
    Task UninstallAsync(CancellationToken ct = default);

    /// <summary>Checks the installed task against the current exe path/user/privilege level and reports any drift.</summary>
    Task<AutostartCheckResult> VerifyAsync(string expectedExecutablePath, CancellationToken ct = default);
}

public sealed record AutostartCheckResult(bool IsInstalled, bool PathMatches, bool HighestPrivileges, bool UserMatches, string? Details);

/// <summary>Reports who is running Steamoff and what they can do.</summary>
public interface IUserContextService
{
    UserContextInfo GetCurrentContext();
}

/// <summary>Handles checking for / requesting elevation and self-relaunch.</summary>
public interface IElevationService
{
    bool IsRunningElevated { get; }

    /// <summary>Relaunches the current executable with the "runas" verb, preserving arguments. Returns true if the new elevated process started (caller should then exit).</summary>
    bool TryRelaunchElevated(IReadOnlyList<string> arguments, out string? failureReason);
}

/// <summary>Structured local logging to %ProgramData%\Steamoff\logs\steamoff.log (with %AppData% fallback).</summary>
public interface ILogService
{
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);

    string LogFilePath { get; }
    Task<IReadOnlyList<string>> ReadLastLinesAsync(int count, CancellationToken ct = default);
    Task<string> BuildDiagnosticsReportAsync(CancellationToken ct = default);
}

/// <summary>Local balloon/toast notifications shown from the tray icon. No network calls.</summary>
public interface INotificationService
{
    void Show(string title, string message);
}

/// <summary>Owns the NotifyIcon, its color/tooltip, and its context menu.</summary>
public interface ITrayService : IDisposable
{
    void Initialize();
    void UpdateStatus(HealthStatus status, bool isReadOnly);
    void ShowBalloon(string title, string message);
}
