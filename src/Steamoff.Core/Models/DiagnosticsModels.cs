namespace Steamoff.Core.Models;

/// <summary>Outcome bucket for one diagnostic check or the report as a whole.</summary>
public enum TestOutcome
{
    Ok,
    Warning,
    Error
}

/// <summary>One line item from the Settings View "Testing" run (e.g. "Steam path", "Firewall access").</summary>
public sealed class DiagnosticCheckResult
{
    public required string Name { get; init; }
    public required TestOutcome Outcome { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// The aggregate result shown in the Settings View "Status" block. Persisted
/// only in memory for the session — re-running Testing replaces it.
/// </summary>
public sealed class DiagnosticsReport
{
    public required IReadOnlyList<DiagnosticCheckResult> Checks { get; init; }
    public required TestOutcome OverallOutcome { get; init; }
    public DateTimeOffset RanAt { get; init; } = DateTimeOffset.UtcNow;

    public static DiagnosticsReport NotRunYet { get; } = new()
    {
        Checks = Array.Empty<DiagnosticCheckResult>(),
        OverallOutcome = TestOutcome.Warning,
        RanAt = DateTimeOffset.MinValue
    };

    public bool HasRun => RanAt != DateTimeOffset.MinValue;
}
