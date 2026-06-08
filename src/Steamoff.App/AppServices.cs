using Steamoff.Core.Interfaces;
using Steamoff.Core.Localization;
using Steamoff.Core.Models;
using Steamoff.Core.Services;
using Steamoff.Infrastructure.Autostart;
using Steamoff.Infrastructure.Diagnostics;
using Steamoff.Infrastructure.Firewall;
using Steamoff.Infrastructure.Logging;
using Steamoff.Infrastructure.Notifications;
using Steamoff.Infrastructure.Paths;
using Steamoff.Infrastructure.Scanning;
using Steamoff.Infrastructure.Settings;
using Steamoff.Infrastructure.Steam;
using Steamoff.Infrastructure.UserContext;
using Steamoff.App.Services;
using Steamoff.App.Tray;

namespace Steamoff.App;

/// <summary>
/// Composition root. Steamoff intentionally has zero third-party DI container —
/// the dependency graph is small and static, so plain constructor wiring keeps
/// the single-file publish simple and the startup path easy to read end to end.
/// </summary>
public sealed class AppServices
{
    public ILogService Log { get; }
    public IUserContextService UserContext { get; }
    public IElevationService Elevation { get; }
    public ISettingsService Settings { get; }
    public IFirewallService Firewall { get; }
    public ScriptFileFirewallService ScriptFileFirewall { get; }
    public ISteamDiscoveryService SteamDiscovery { get; }
    public IPathNormalizationService PathNormalization { get; }
    public ISteamPathValidator SteamPathValidator { get; }
    public ITargetScanner Scanner { get; }
    public IFolderTargetService FolderTargets { get; }
    public IExeTargetService ExeTargets { get; }
    public IStatusEvaluator StatusEvaluator { get; }
    public IAutostartService Autostart { get; }
    public ILocalizationService Localization { get; }
    public ILocalizedLogService LocalizedLog { get; }
    public IDiagnosticsService Diagnostics { get; }
    public TrayService Tray { get; }
    public INotificationService Notifications { get; }
    public IDialogService Dialogs { get; }
    public IFirewallSelfTestRunner SelfTestRunner { get; }

    /// <summary>
    /// Latest known <see cref="AppSettings"/> snapshot — read by <see cref="Firewall"/>'s
    /// mode/remembered-strategy delegates (contracts C4/C6). AppServices is built before
    /// settings finish loading (<c>App.StartupAsync</c> awaits <c>Settings.LoadAsync</c>
    /// after construction), so a default snapshot is used until <see cref="RefreshSettingsSnapshot"/>
    /// is called for the first time — and again on every later commit — keeping the
    /// orchestrator's view current without giving it a direct dependency on the settings
    /// persistence layer (preserving its existing fakes-based testability per C4's note).
    /// </summary>
    private AppSettings _settingsSnapshot = AppSettings.CreateDefault();

    /// <summary>Called by <c>App</c> once settings finish loading and again after every commit, so the firewall orchestrator's mode/memory delegates always see the latest values.</summary>
    public void RefreshSettingsSnapshot(AppSettings settings) => _settingsSnapshot = settings;

    public AppServices()
    {
        Log = new FileLogService();
        UserContext = new UserContextService();
        Elevation = new ElevationService();
        Settings = new JsonSettingsService(Log);
        SteamDiscovery = new SteamDiscoveryService(Log);
        PathNormalization = new PathNormalizationService();
        SteamPathValidator = new SteamPathValidator(PathNormalization);
        Scanner = new TargetScanner(Log);
        FolderTargets = new FolderTargetService(Scanner, Log);
        ExeTargets = new ExeTargetService();
        StatusEvaluator = new StatusEvaluator();
        Autostart = new TaskSchedulerAutostartService(Log);
        Localization = new LocalizationService(new LanguageManager(), new LocalizedStringProvider(), Log);
        LocalizedLog = new LocalizedLogService(Log, Localization);
        var primaryFirewall = new ComFirewallService(Log);
        var secondaryFirewall = new NetSecurityFirewallService(Log);
        ScriptFileFirewall = new ScriptFileFirewallService(new FirewallScriptFileWriter(Log), new ProcessPowerShellCommandRunner(), Log);
        Firewall = new FallbackAwareFirewallService(
            primaryFirewall,
            secondaryFirewall,
            ScriptFileFirewall,
            () => _settingsSnapshot.FirewallStrategyMode,
            () => _settingsSnapshot.LastSuccessfulFirewallStrategy,
            async variant =>
            {
                _settingsSnapshot.LastSuccessfulFirewallStrategy = variant;
                await Settings.SaveAsync(_settingsSnapshot).ConfigureAwait(false);
            },
            Log,
            LocalizedLog);
        SelfTestRunner = new FirewallSelfTestRunner(primaryFirewall, secondaryFirewall, ScriptFileFirewall, Settings, Log, LocalizedLog);
        Diagnostics = new DiagnosticsService(UserContext, Settings, Log, SteamDiscovery, Scanner, Firewall, Autostart, Localization);
        Tray = new TrayService(Log, Localization);
        Notifications = new BalloonNotificationService(() => Tray.NotifyIconForNotifications);
        Dialogs = new WpfDialogService();
    }
}
