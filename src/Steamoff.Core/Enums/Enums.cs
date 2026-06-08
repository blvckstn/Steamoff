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

/// <summary>
/// Result classification for a normalized/validated Steam-path candidate —
/// drives the validity indicator color and status text in Settings (spec section 4).
/// </summary>
public enum PathCheckStatus
{
    /// <summary>Nothing entered yet — indicator shown in neutral/gray.</summary>
    Empty,

    /// <summary>Entered but not yet (re-)validated — indicator shown in yellow.</summary>
    Unchecked,

    /// <summary>Resolved to a folder containing steam.exe — indicator shown in green.</summary>
    Valid,

    /// <summary>The path doesn't exist on disk.</summary>
    PathNotFound,

    /// <summary>The folder exists but no steam.exe was found inside it.</summary>
    SteamExeNotFound,

    /// <summary>An .exe was selected/dropped, but its name isn't steam.exe.</summary>
    WrongExe,

    /// <summary>A shortcut (.lnk) was provided but its target couldn't be resolved.</summary>
    ShortcutUnresolved
}

/// <summary>Stable identity for one of the three concrete IFirewallService implementations — "Вариант 1/2/3".</summary>
public enum FirewallStrategyVariant
{
    /// <summary>"Вариант 1" — ComFirewallService (COM/INetFwPolicy2).</summary>
    Primary,

    /// <summary>"Вариант 2" — NetSecurityFirewallService (inline -Command PowerShell).</summary>
    Secondary,

    /// <summary>"Вариант 3" — ScriptFileFirewallService (elevated .ps1 file).</summary>
    ScriptFile
}

/// <summary>How FallbackAwareFirewallService decides which strategy to use for an operation.</summary>
public enum FirewallStrategyMode
{
    /// <summary>System decides — tries ScriptFile first, then remembered/cascade fallbacks.</summary>
    Auto,

    /// <summary>Force "Вариант 1" only — no fallback.</summary>
    ForcePrimary,

    /// <summary>Force "Вариант 2" only — no fallback.</summary>
    ForceSecondary,

    /// <summary>Force "Вариант 3" only — no fallback.</summary>
    ForceScriptFile
}

/// <summary>Distinguishes "never probed" from every possible probe result of the first-run self-test.</summary>
public enum FirewallSelfTestOutcome
{
    /// <summary>Fresh install / pre-feature settings — triggers the one-time probe on next startup.</summary>
    NotYetRun,

    /// <summary>Probe ran to completion; WorkingStrategies holds 0..3 entries (0 = none worked).</summary>
    CompletedWithResult,

    /// <summary>Probe started but could not finish cleanly (interrupted) — recorded distinctly, never retried automatically.</summary>
    Inconclusive
}
