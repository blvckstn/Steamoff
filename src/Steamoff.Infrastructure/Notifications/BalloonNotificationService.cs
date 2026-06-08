using System.Windows.Forms;
using Steamoff.Core.Interfaces;

namespace Steamoff.Infrastructure.Notifications;

/// <summary>
/// Local-only balloon notifications shown from the tray NotifyIcon — no
/// research R4). The TrayService owns the actual NotifyIcon instance; this
/// service is handed a reference to it so notifications and the tray icon
/// share one underlying Win32 resource.
/// </summary>
public sealed class BalloonNotificationService : INotificationService
{
    private readonly Func<NotifyIcon?> _notifyIconAccessor;

    public BalloonNotificationService(Func<NotifyIcon?> notifyIconAccessor)
    {
        _notifyIconAccessor = notifyIconAccessor;
    }

    public void Show(string title, string message)
    {
        var icon = _notifyIconAccessor();
        if (icon is null || !icon.Visible)
        {
            return;
        }

        icon.BalloonTipTitle = title;
        icon.BalloonTipText = message;
        icon.BalloonTipIcon = ToolTipIcon.Info;
        icon.ShowBalloonTip(5000);
    }
}
