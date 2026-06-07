using System.Security.Principal;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;

namespace Steamoff.Infrastructure.UserContext;

/// <summary>
/// Reports who is running Steamoff and what they're allowed to do, using
/// WindowsIdentity/WindowsPrincipal — the runtime check the brief requires in
/// addition to (not instead of) the app.manifest's requireAdministrator.
/// </summary>
public sealed class UserContextService : IUserContextService
{
    public UserContextInfo GetCurrentContext()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        var isAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator);
        var isElevated = identity.Owner is not null && IsElevatedToken(identity);
        var hasFirewallAccess = isAdministrator && isElevated;

        var nameParts = identity.Name.Split('\\', 2);
        var domain = nameParts.Length == 2 ? nameParts[0] : Environment.UserDomainName;
        var userName = nameParts.Length == 2 ? nameParts[1] : nameParts[0];

        var isInteractive = Environment.UserInteractive;

        string? warning = null;
        if (isElevated && !string.Equals(userName, Environment.UserName, StringComparison.OrdinalIgnoreCase))
        {
            warning = "Приложение запущено с повышенными правами под другим пользователем. " +
                      "Firewall-операции доступны, но автозапуск и пользовательские настройки могут относиться к этому аккаунту.";
        }
        else if (isAdministrator && !isElevated)
        {
            warning = "Текущий пользователь входит в группу администраторов, но процесс не повышен (UAC). " +
                      "Операции с firewall будут недоступны, пока приложение не будет перезапущено от имени администратора.";
        }

        return new UserContextInfo
        {
            UserName = userName,
            Domain = domain,
            Sid = identity.User?.Value ?? "UNKNOWN",
            IsAdministrator = isAdministrator,
            IsElevated = isElevated,
            HasFirewallAccess = hasFirewallAccess,
            IsInteractiveSession = isInteractive,
            Warning = warning
        };
    }

    /// <summary>
    /// WindowsPrincipal.IsInRole(Administrator) returns true for an admin's
    /// token even when UAC has not elevated the process. The actual elevation
    /// state requires checking the token's elevation type/integrity level —
    /// TokenElevationType reflects whether THIS token is the elevated (Full)
    /// or limited (Limited) half of a split admin token. A non-split-token
    /// administrator (e.g. the built-in Administrator account, or UAC
    /// disabled) reports TokenElevationTypeDefault and is treated as elevated
    /// when it's also in the Administrators role.
    /// </summary>
    private static bool IsElevatedToken(WindowsIdentity identity)
    {
        try
        {
            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                return false;
            }

            return TokenElevation.IsProcessElevated();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
