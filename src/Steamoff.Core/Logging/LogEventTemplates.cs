namespace Steamoff.Core.Logging;

/// <summary>
/// Static lookup from <see cref="LogEventKey"/> to its localization key and
/// severity. This is the single source of truth backing
/// <see cref="ILocalizedLogService"/> and the journal's level filter.
/// </summary>
public static class LogEventTemplates
{
    private static readonly IReadOnlyDictionary<LogEventKey, (string LocalizationKey, LogLevel Level)> Entries =
        new Dictionary<LogEventKey, (string, LogLevel)>
        {
            [LogEventKey.AppStarted] = ("log.event.appStarted", LogLevel.Info),
            [LogEventKey.AppClosed] = ("log.event.appClosed", LogLevel.Info),
            [LogEventKey.SettingsOpened] = ("log.event.settingsOpened", LogLevel.Info),
            [LogEventKey.SettingsApplied] = ("log.event.settingsApplied", LogLevel.Info),
            [LogEventKey.SettingsSaved] = ("log.event.settingsSaved", LogLevel.Info),
            [LogEventKey.SettingsCancelled] = ("log.event.settingsCancelled", LogLevel.Info),
            [LogEventKey.LanguageChangedRestartRequired] = ("log.event.languageChangedRestartRequired", LogLevel.Warning),
            [LogEventKey.RestartRequested] = ("log.event.restartRequested", LogLevel.Info),
            [LogEventKey.RestartFailed] = ("log.event.restartFailed", LogLevel.Error),
            [LogEventKey.SteamAutoSearchStarted] = ("log.event.steamAutoSearchStarted", LogLevel.Info),
            [LogEventKey.SteamAutoSearchSucceeded] = ("log.event.steamAutoSearchSucceeded", LogLevel.Info),
            [LogEventKey.SteamAutoSearchFailed] = ("log.event.steamAutoSearchFailed", LogLevel.Error),
            [LogEventKey.SteamPathNormalized] = ("log.event.steamPathNormalized", LogLevel.Info),
            [LogEventKey.SteamPathInvalid] = ("log.event.steamPathInvalid", LogLevel.Warning),
            [LogEventKey.FolderAdded] = ("log.event.folderAdded", LogLevel.Info),
            [LogEventKey.FolderRemoved] = ("log.event.folderRemoved", LogLevel.Info),
            [LogEventKey.ExeAdded] = ("log.event.exeAdded", LogLevel.Info),
            [LogEventKey.ExeRemoved] = ("log.event.exeRemoved", LogLevel.Info),
            [LogEventKey.FirewallBlockStarted] = ("log.event.firewallBlockStarted", LogLevel.Info),
            [LogEventKey.FirewallBlockCompleted] = ("log.event.firewallBlockCompleted", LogLevel.Info),
            [LogEventKey.FirewallBlockFailed] = ("log.event.firewallBlockFailed", LogLevel.Error),
            [LogEventKey.FirewallUnblockStarted] = ("log.event.firewallUnblockStarted", LogLevel.Info),
            [LogEventKey.FirewallUnblockCompleted] = ("log.event.firewallUnblockCompleted", LogLevel.Info),
            [LogEventKey.FirewallUnblockFailed] = ("log.event.firewallUnblockFailed", LogLevel.Error),
            [LogEventKey.FirewallStrategyFallbackUsed] = ("log.event.firewallStrategyFallbackUsed", LogLevel.Warning),
            [LogEventKey.FirewallBothStrategiesFailed] = ("log.event.firewallBothStrategiesFailed", LogLevel.Error),
            [LogEventKey.FirewallAllStrategiesFailed] = ("log.event.firewallAllStrategiesFailed", LogLevel.Error),
            [LogEventKey.FirewallForcedStrategyFailed] = ("log.event.firewallForcedStrategyFailed", LogLevel.Error),
            [LogEventKey.FirewallSelfTestCompleted] = ("log.event.firewallSelfTestCompleted", LogLevel.Info),
            [LogEventKey.FirewallSelfTestInconclusive] = ("log.event.firewallSelfTestInconclusive", LogLevel.Warning),
            [LogEventKey.DriftDetected] = ("log.event.driftDetected", LogLevel.Warning),
            [LogEventKey.AutostartCreated] = ("log.event.autostartCreated", LogLevel.Info),
            [LogEventKey.AutostartRemoved] = ("log.event.autostartRemoved", LogLevel.Info),
            [LogEventKey.DiagnosticsCopied] = ("log.event.diagnosticsCopied", LogLevel.Info),
            [LogEventKey.ReleaseBuildStarted] = ("log.event.releaseBuildStarted", LogLevel.Info),
            [LogEventKey.ReleaseBuildCompleted] = ("log.event.releaseBuildCompleted", LogLevel.Info),
            [LogEventKey.ReleaseBuildFailed] = ("log.event.releaseBuildFailed", LogLevel.Error)
        };

    public static string LocalizationKeyFor(LogEventKey key) => Entries[key].LocalizationKey;

    public static LogLevel LevelFor(LogEventKey key) => Entries[key].Level;
}
