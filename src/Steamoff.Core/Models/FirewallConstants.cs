namespace Steamoff.Core.Models;

/// <summary>
/// The single source of truth for how Steamoff names and groups its firewall rules.
/// Constitution principle II requires every Steamoff-managed rule to carry both
/// this exact group and this exact name prefix, and forbids touching rules that don't.
/// </summary>
public static class FirewallConstants
{
    public const string RuleGroup = "Steamoff";
    public const string RuleNamePrefix = "Steamoff - Block - ";
    public const string RuleDescription = "Created by Steamoff. Blocks selected executable via Microsoft Defender Firewall.";
}
