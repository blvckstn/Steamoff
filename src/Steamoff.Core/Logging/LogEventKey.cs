namespace Steamoff.Core.Logging;

/// <summary>
/// Named, localizable journal events — see
/// specs/004-steamoff-localized-logs-release-flow/contracts/localized-logging.md
/// for the full key → localization-key → level table. Each member maps to a
/// `log.event.*` localization key via <see cref="LogEventTemplates"/>.
/// </summary>
public enum LogEventKey
{
    AppStarted,
    AppClosed,
    SettingsOpened,
    SettingsApplied,
    SettingsSaved,
    SettingsCancelled,
    LanguageChangedRestartRequired,
    RestartRequested,
    RestartFailed,
    SteamAutoSearchStarted,
    SteamAutoSearchSucceeded,
    SteamAutoSearchFailed,
    SteamPathNormalized,
    SteamPathInvalid,
    FolderAdded,
    FolderRemoved,
    ExeAdded,
    ExeRemoved,
    FirewallBlockStarted,
    FirewallBlockCompleted,
    FirewallBlockFailed,
    FirewallUnblockStarted,
    FirewallUnblockCompleted,
    FirewallUnblockFailed,
    FirewallStrategyFallbackUsed,
    FirewallBothStrategiesFailed,
    FirewallAllStrategiesFailed,
    FirewallForcedStrategyFailed,
    FirewallSelfTestCompleted,
    FirewallSelfTestInconclusive,
    DriftDetected,
    AutostartCreated,
    AutostartRemoved,
    DiagnosticsCopied,
    ReleaseBuildStarted,
    ReleaseBuildCompleted,
    ReleaseBuildFailed
}
