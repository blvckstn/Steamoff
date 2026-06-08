using System.Drawing;
using System.Windows.Forms;
using Steamoff.Core.Enums;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;
using Steamoff.App.Status;
using Application = System.Windows.Application;

namespace Steamoff.App.Tray;

/// <summary>
/// Owns the NotifyIcon: status-colored icon, honest tooltip text, balloon
/// notifications, and the context menu (open / toggle / modes / rescan /
/// settings / logs / exit). Icons are flat-color dots rendered at runtime
/// (see ASSUMPTIONS — simpler and DPI-safe vs. baking multiple .ico assets).
/// All visible text is sourced from <see cref="ILocalizationService"/> and
/// rebuilt instantly when <see cref="ILocalizationService.LanguageChanged"/> fires.
/// </summary>
public sealed class TrayService : ITrayService
{
    private readonly ILogService _log;
    private readonly ILocalizationService _localization;
    private NotifyIcon? _notifyIcon;
    private readonly Dictionary<RobotStatusKind, Icon> _iconCache = new();
    private HealthStatus _lastStatus = new();
    private bool _lastIsReadOnly;

    public event Action? OpenRequested;
    public event Action? BlockRequested;
    public event Action? UnblockRequested;
    public event Action<EnforcementMode>? ModeRequested;
    public event Action? RescanRequested;
    public event Action? SettingsRequested;
    public event Action? LogsRequested;
    public event Action? ExitRequested;

    public TrayService(ILogService log, ILocalizationService localization)
    {
        _log = log;
        _localization = localization;
        _localization.LanguageChanged += (_, _) => RefreshForLanguageChange();
    }

    /// <summary>Exposed so BalloonNotificationService can share the same Win32 NotifyIcon.</summary>
    public NotifyIcon? NotifyIconForNotifications => _notifyIcon;

    public void Initialize()
    {
        if (_notifyIcon is not null)
        {
            return;
        }

        _notifyIcon = new NotifyIcon
        {
            Icon = GetIcon(RobotStatusKind.Online),
            Visible = true,
            Text = TruncateForTooltip(_localization.GetString("status.checking")),
            ContextMenuStrip = BuildContextMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public void UpdateStatus(HealthStatus status, bool isReadOnly)
    {
        _lastStatus = status;
        _lastIsReadOnly = isReadOnly;

        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Icon = GetIcon(ToRobotStatusKind(status.Overall, isReadOnly));
        _notifyIcon.Text = TruncateForTooltip(BuildTooltip(status, isReadOnly));
    }

    public void ShowBalloon(string title, string message)
    {
        if (_notifyIcon is null || !_notifyIcon.Visible)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(5000);
    }

    private void RefreshForLanguageChange()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.ContextMenuStrip = BuildContextMenu();
        _notifyIcon.Text = TruncateForTooltip(BuildTooltip(_lastStatus, _lastIsReadOnly));
    }

    private string BuildTooltip(HealthStatus status, bool isReadOnly)
    {
        if (isReadOnly)
        {
            return $"{_localization.GetString("app.title")}: {_localization.GetString("status.noAdminRights")}";
        }

        var key = status.Overall switch
        {
            OverallStatus.FullyBlocked => "status.blocked",
            OverallStatus.FullyUnblocked => "status.unblocked",
            OverallStatus.PartiallyBlocked => "status.partiallyBlocked",
            OverallStatus.DriftDetected => "status.driftDetected",
            OverallStatus.Error => "status.error",
            OverallStatus.NotConfigured => "status.notConfigured",
            _ => "status.checking"
        };

        return $"{_localization.GetString("app.title")}: {_localization.GetString(key)}";
    }

    private static string TruncateForTooltip(string text) => text.Length <= 63 ? text : text[..63];

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        string T(string key) => _localization.GetString(key);

        menu.Items.Add(T("tray.open"), null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("tray.block"), null, (_, _) => BlockRequested?.Invoke());
        menu.Items.Add(T("tray.unblock"), null, (_, _) => UnblockRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("tray.alwaysBlock"), null, (_, _) => ModeRequested?.Invoke(EnforcementMode.AlwaysBlock));
        menu.Items.Add(T("tray.alwaysUnblock"), null, (_, _) => ModeRequested?.Invoke(EnforcementMode.AlwaysUnblock));
        menu.Items.Add(T("tray.pauseMonitoring"), null, (_, _) => ModeRequested?.Invoke(EnforcementMode.PauseMonitoring));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("tray.rescan"), null, (_, _) => RescanRequested?.Invoke());
        menu.Items.Add(T("tray.settings"), null, (_, _) => SettingsRequested?.Invoke());
        menu.Items.Add(T("tray.logs"), null, (_, _) => LogsRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("tray.exit"), null, (_, _) => ExitRequested?.Invoke());

        return menu;
    }

    private Icon GetIcon(RobotStatusKind status)
    {
        if (_iconCache.TryGetValue(status, out var cached))
        {
            return cached;
        }

        var icon = RobotTrayIconFactory.CreateIcon(status);
        _iconCache[status] = icon;
        return icon;
    }

    private static RobotStatusKind ToRobotStatusKind(OverallStatus status, bool isReadOnly)
    {
        if (isReadOnly)
        {
            return RobotStatusKind.Waiting;
        }

        return status switch
        {
            OverallStatus.FullyBlocked => RobotStatusKind.Offline,
            OverallStatus.FullyUnblocked => RobotStatusKind.Online,
            _ => RobotStatusKind.Waiting
        };
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        foreach (var icon in _iconCache.Values)
        {
            icon.Dispose();
        }

        _iconCache.Clear();
    }
}
