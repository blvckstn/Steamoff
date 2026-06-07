using Steamoff.Core.Interfaces;

namespace Steamoff.Infrastructure.Paths;

/// <summary>
/// Pure string clean-up for raw path input (typed, pasted, or dropped):
/// trims whitespace, strips a single pair of surrounding quotes, expands
/// environment variables (<c>%ProgramFiles(x86)%\Steam</c>), and collapses
/// duplicated directory separators into a single backslash. Never touches
/// the filesystem — see <see cref="SteamPathValidator"/> for existence checks.
/// </summary>
public sealed class PathNormalizationService : IPathNormalizationService
{
    public string NormalizeRawPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        var value = rawPath.Trim();

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1].Trim();
        }

        value = Environment.ExpandEnvironmentVariables(value);
        value = value.Replace('/', '\\');

        // Collapse duplicated separators, but preserve a leading UNC "\\server\share".
        var isUnc = value.StartsWith(@"\\", StringComparison.Ordinal);
        var collapsed = CollapseBackslashes(isUnc ? value[2..] : value);
        value = isUnc ? @"\\" + collapsed : collapsed;

        return value.Trim();
    }

    private static string CollapseBackslashes(string value)
    {
        if (!value.Contains(@"\\", StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        var previousWasSeparator = false;
        foreach (var ch in value)
        {
            if (ch == '\\')
            {
                if (previousWasSeparator)
                {
                    continue;
                }

                previousWasSeparator = true;
            }
            else
            {
                previousWasSeparator = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
