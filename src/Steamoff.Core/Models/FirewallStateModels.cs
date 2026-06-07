using Steamoff.Core.Enums;

namespace Steamoff.Core.Models;

/// <summary>Read-back snapshot of one Microsoft Defender Firewall rule.</summary>
public sealed class FirewallRuleState
{
    public required string RuleName { get; init; }
    public required string GroupName { get; init; }
    public required RuleDirection Direction { get; init; }
    public required RuleAction Action { get; init; }
    public required bool Enabled { get; init; }
    public string? ApplicationName { get; init; }
    public string? Profiles { get; init; }

    /// <summary>True only if both the group and the name prefix match Steamoff's convention exactly (Constitution II).</summary>
    public bool IsManagedBySteamoff =>
        string.Equals(GroupName, FirewallConstants.RuleGroup, StringComparison.Ordinal) &&
        RuleName.StartsWith(FirewallConstants.RuleNamePrefix, StringComparison.Ordinal);
}

/// <summary>What the user wants the firewall to look like.</summary>
public sealed class DesiredFirewallState
{
    public required DesiredState State { get; init; }
    public required IReadOnlyList<FirewallTarget> Targets { get; init; }
    public required DirectionMode DirectionMode { get; init; }
}

/// <summary>What the firewall actually looks like right now, as read back from Windows.</summary>
public sealed class ActualFirewallState
{
    public required IReadOnlyList<FirewallRuleState> Rules { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public static ActualFirewallState Empty { get; } = new() { Rules = Array.Empty<FirewallRuleState>() };
}

/// <summary>Describes any mismatch between desired and actual firewall state.</summary>
public sealed class DriftReport
{
    public bool HasDrift { get; init; }
    public IReadOnlyList<string> MissingTargets { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnexpectedlyActiveTargets { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DisabledTargets { get; init; } = Array.Empty<string>();
    public string Summary { get; init; } = string.Empty;

    public static DriftReport None { get; } = new() { HasDrift = false, Summary = "Расхождений не обнаружено." };
}

/// <summary>Aggregate health/coverage snapshot consumed by the dashboard and tray.</summary>
public sealed class HealthStatus
{
    public HealthLevel Level { get; init; } = HealthLevel.Unknown;
    public OverallStatus Overall { get; init; } = OverallStatus.NotConfigured;
    public string Message { get; init; } = string.Empty;
    public double CoveragePercent { get; init; }
    public (int Covered, int Expected) SteamCoreCoverage { get; init; }
    public (int Covered, int Expected) FolderCoverage { get; init; }
    public (int Covered, int Expected) ExeCoverage { get; init; }
    public DateTimeOffset LastCheckedAt { get; init; } = DateTimeOffset.UtcNow;
    public DriftReport? Drift { get; init; }

    public static HealthStatus Unknown { get; } = new()
    {
        Level = HealthLevel.Unknown,
        Overall = OverallStatus.NotConfigured,
        Message = "Состояние ещё не проверено."
    };
}
