namespace Steamoff.Core.Enums;

/// <summary>What the user wants the firewall to do with the tracked targets.</summary>
public enum DesiredState
{
    Blocked,
    Unblocked
}

/// <summary>How aggressively Steamoff keeps the actual firewall state in sync with the desired state.</summary>
public enum EnforcementMode
{
    ManualToggle,
    AlwaysBlock,
    AlwaysUnblock,
    PauseMonitoring
}

/// <summary>What happens to Steamoff's rules when the desired state becomes Unblocked.</summary>
public enum RuleCleanupMode
{
    DisableRules,
    DeleteRules
}

/// <summary>Whether Steamoff blocks only outbound traffic, or both directions.</summary>
public enum DirectionMode
{
    OutboundOnly,
    OutboundAndInbound
}

/// <summary>Firewall rule direction (mirrors Windows Firewall direction).</summary>
public enum RuleDirection
{
    Outbound,
    Inbound
}

/// <summary>Firewall rule action (Steamoff only ever creates Block rules, but reads can see Allow too).</summary>
public enum RuleAction
{
    Block,
    Allow
}

/// <summary>Coarse health classification used for status dots / icon colors.</summary>
public enum HealthLevel
{
    Unknown,
    Ok,
    Warning,
    Error,
    Disabled,
    ReadOnly
}

/// <summary>The overall reconciliation result produced by the status evaluator.</summary>
public enum OverallStatus
{
    NotConfigured,
    FullyBlocked,
    FullyUnblocked,
    PartiallyBlocked,
    DriftDetected,
    Error,
    ReadOnlyNoAdmin
}

/// <summary>Which collection a firewall target belongs to (used for coverage accounting).</summary>
public enum TargetKind
{
    SteamCore,
    Folder,
    StandaloneExe
}

/// <summary>Status of a user-managed additional folder.</summary>
public enum FolderStatus
{
    OkBlocked,
    OkUnblocked,
    Partial,
    MissingRules,
    PathNotFound,
    ScanError,
    Disabled
}

/// <summary>Status of a user-managed standalone executable.</summary>
public enum ExeStatus
{
    Blocked,
    Unblocked,
    MissingRule,
    FileNotFound,
    Disabled,
    Error
}

/// <summary>How the Steam installation path was determined.</summary>
public enum DiscoverySource
{
    None,
    Registry,
    RunningProcess,
    DefaultPath,
    Shortcut,
    Manual
}
