namespace Steamoff.Core.Models;

/// <summary>Snapshot of who is running Steamoff and what they're allowed to do.</summary>
public sealed class UserContextInfo
{
    public required string UserName { get; init; }
    public string? Domain { get; init; }
    public required string Sid { get; init; }
    public required bool IsAdministrator { get; init; }
    public required bool IsElevated { get; init; }
    public required bool HasFirewallAccess { get; init; }
    public bool IsInteractiveSession { get; init; }
    public string? Warning { get; init; }

    public string DisplayName => string.IsNullOrEmpty(Domain) ? UserName : $"{Domain}\\{UserName}";
}
