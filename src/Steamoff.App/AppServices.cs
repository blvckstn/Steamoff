using Steamoff.Core.Interfaces;
using Steamoff.Core.Localization;
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

    public AppServices()
    {
        Log = new FileLogService();
        UserContext = new UserContextService();
        Elevation = new ElevationService();
        Settings = new JsonSettingsService(Log);
        Firewall = new ComFirewallService(Log);
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
        Diagnostics = new DiagnosticsService(UserContext, Settings, Log, SteamDiscovery, Scanner, Firewall, Autostart, Localization);
        Tray = new TrayService(Log, Localization);
        Notifications = new BalloonNotificationService(() => Tray.NotifyIconForNotifications);
        Dialogs = new WpfDialogService();
    }
}
