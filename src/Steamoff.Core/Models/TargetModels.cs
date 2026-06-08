using Steamoff.Core.Enums;

namespace Steamoff.Core.Models;

/// <summary>A single executable that Steamoff wants to keep blocked/unblocked. The canonical unit the rule builder and status evaluator operate on.</summary>
public sealed class FirewallTarget
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string ExecutablePath { get; init; }
    public required TargetKind Kind { get; init; }
    public string GroupName { get; init; } = FirewallConstants.RuleGroup;
}

/// <summary>A named collection of targets with expected/covered counts, e.g. "Steam Core 3/3".</summary>
public sealed class TargetGroup
{
    public required string Name { get; init; }
    public required IReadOnlyList<FirewallTarget> Targets { get; init; }
    public int ExpectedCount { get; init; }
    public int CoveredCount { get; init; }

    public string CoverageLabel => $"{CoveredCount}/{ExpectedCount}";
}

/// <summary>A discovered or manually-set Steam installation.</summary>
public sealed class SteamInstallation
{
    public string? Path { get; init; }
    public string? SteamExePath { get; init; }
    public bool IsValid { get; init; }
    public DiscoverySource DiscoverySource { get; init; } = DiscoverySource.None;

    public static SteamInstallation NotFound { get; } = new() { IsValid = false, DiscoverySource = DiscoverySource.None };
}

/// <summary>User-managed folder whose discovered executables are tracked as firewall targets.</summary>
public sealed class FolderBlockTarget
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Path { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Recursive { get; set; } = true;

    /// <summary>Not persisted — populated by the scanner at runtime.</summary>
    public int DiscoveredExeCount { get; set; }

    /// <summary>Not persisted — populated by the status evaluator at runtime.</summary>
    public int ActiveRuleCount { get; set; }

    /// <summary>Not persisted — populated by the status evaluator at runtime.</summary>
    public FolderStatus Status { get; set; } = FolderStatus.Disabled;
}

/// <summary>
/// Outcome of normalizing and validating a user-supplied Steam-path candidate
/// (typed path, dropped file, dialog pick, or auto-discovery result) — see
/// <c>IPathNormalizationService</c>/<c>ISteamPathValidator</c> and spec section 4.
/// </summary>
public sealed class SteamPathCheckResult
{
    /// <summary>The folder path to persist as <c>AppSettings.SteamPath</c> (never an .exe path).</summary>
    public string? NormalizedFolderPath { get; init; }

    /// <summary>Full path to steam.exe inside <see cref="NormalizedFolderPath"/>, when valid.</summary>
    public string? SteamExePath { get; init; }

    public required PathCheckStatus Status { get; init; }

    /// <summary>Localization key for the human-readable status text shown next to the indicator.</summary>
    public required string StatusMessageKey { get; init; }

    public bool IsValid => Status == PathCheckStatus.Valid;

    public static SteamPathCheckResult Empty { get; } = new()
    {
        Status = PathCheckStatus.Empty,
        StatusMessageKey = "settings.steamPath.dropHint"
    };
}

/// <summary>User-managed standalone executable tracked as a firewall target. Steamoff only ever reads its path — never executes it.</summary>
public sealed class ExeBlockTarget
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Path { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>Not persisted — populated by the status evaluator at runtime.</summary>
    public ExeStatus Status { get; set; } = ExeStatus.Unblocked;
}
