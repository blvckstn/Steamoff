namespace Steamoff.Core.Logging;

/// <summary>Severity for a localized journal entry — drives both <see cref="Interfaces.ILogService"/> dispatch and journal-filter matching.</summary>
public enum LogLevel
{
    Info,
    Warning,
    Error
}
