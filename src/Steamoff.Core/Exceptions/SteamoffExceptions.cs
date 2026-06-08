namespace Steamoff.Core.Exceptions;

/// <summary>Thrown when a firewall operation fails because the process is not elevated / lacks access (e.g. COM E_ACCESSDENIED).</summary>
public sealed class FirewallAccessDeniedException : Exception
{
    public FirewallAccessDeniedException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>Thrown when a firewall operation fails for a reason other than access denial (COM failure, malformed rule, etc).</summary>
public sealed class FirewallOperationException : Exception
{
    public FirewallOperationException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>Thrown when settings cannot be read or written even after fallback/backup handling.</summary>
public sealed class SettingsPersistenceException : Exception
{
    public SettingsPersistenceException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
