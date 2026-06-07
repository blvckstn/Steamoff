using Steamoff.Core.Enums;
using Steamoff.Core.Models;

namespace Steamoff.Core.Services;

/// <summary>
/// The single place that knows how to turn a target name + direction into a
/// stable, safe Steamoff rule name. Guarantees the "Steamoff - Block - X - Y"
/// convention required by Constitution principle II and the firewall-service
/// contract — every IFirewallService implementation must route rule creation
/// through this builder so naming can never drift.
/// </summary>
public static class FirewallRuleNameBuilder
{
    /// <summary>Builds "Steamoff - Block - &lt;TargetName&gt; - &lt;Direction&gt;", sanitizing the target name so it can never break the prefix/parsing convention.</summary>
    public static string Build(string targetDisplayName, RuleDirection direction)
    {
        var safeName = Sanitize(targetDisplayName);
        return $"{FirewallConstants.RuleNamePrefix}{safeName} - {direction}";
    }

    /// <summary>Extracts the target display name portion from a Steamoff-managed rule name, or null if the name doesn't match the convention.</summary>
    public static string? TryParseTargetName(string ruleName, RuleDirection direction)
    {
        if (!ruleName.StartsWith(FirewallConstants.RuleNamePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var suffix = $" - {direction}";
        if (!ruleName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        var start = FirewallConstants.RuleNamePrefix.Length;
        var length = ruleName.Length - start - suffix.Length;
        return length >= 0 ? ruleName.Substring(start, length) : null;
    }

    /// <summary>
    /// Removes characters that would make the rule name ambiguous to parse back
    /// (the " - " separator) while staying human-readable and stable for the
    /// same input (no GUIDs, no timestamps — required for idempotent re-application).
    /// </summary>
    private static string Sanitize(string targetDisplayName)
    {
        var trimmed = targetDisplayName.Trim();
        return trimmed.Replace(" - ", " — ");
    }
}
