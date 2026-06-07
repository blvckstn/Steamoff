using Steamoff.Core.Enums;

namespace Steamoff.Core.Models;

/// <summary>Root persisted configuration object — serialized to settings.json.</summary>
public sealed class AppSettings
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>ISO-639-1 code of the active interface language (e.g. "ru", "en"). Defaults to Russian until the first-launch dialog runs.</summary>
    public string Language { get; set; } = "ru";

    /// <summary>True once the user has gone through (or dismissed) the first-launch language dialog.</summary>
    public bool IsFirstLaunchCompleted { get; set; }

    public DesiredState DesiredState { get; set; } = DesiredState.Unblocked;
    public EnforcementMode EnforcementMode { get; set; } = EnforcementMode.ManualToggle;
    public string? SteamPath { get; set; }
    public bool BlockInbound { get; set; }
    public RuleCleanupMode RuleCleanupMode { get; set; } = RuleCleanupMode.DisableRules;
    public int CheckIntervalSeconds { get; set; } = 15;
    public bool StartWithWindows { get; set; } = true;
    public bool StartMinimizedToTray { get; set; } = true;
    public bool ApplySavedStateOnStartup { get; set; } = true;
    public bool WarnBeforeUnblock { get; set; } = true;
    public bool AutoRestoreWhenAlwaysBlock { get; set; } = true;
    public bool BlockAllExecutablesInSteamFolder { get; set; }
    public List<FolderBlockTarget> AdditionalFolders { get; set; } = new();
    public List<ExeBlockTarget> AdditionalExecutables { get; set; } = new();
    public UiSettings Ui { get; set; } = new();

    public DirectionMode DirectionMode => BlockInbound ? DirectionMode.OutboundAndInbound : DirectionMode.OutboundOnly;

    public static AppSettings CreateDefault() => new();
}

public sealed class UiSettings
{
    public string Theme { get; set; } = "DarkOrange";
    public bool CloseToTray { get; set; } = true;
}
