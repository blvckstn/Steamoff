using Steamoff.Core.Enums;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;
using Steamoff.Infrastructure.Steam;

namespace Steamoff.Infrastructure.Paths;

/// <summary>
/// Resolves any Steam-path candidate — a folder, a steam.exe path, a quoted/
/// padded/env-var string, or a .lnk shortcut — into a validated Steam folder.
/// Always reports the folder to persist, never an .exe path (spec section 4).
/// </summary>
/// <remarks>
/// The shortcut-resolution step is injected as a delegate (defaulting to the
/// real COM-backed <see cref="ShortcutResolver"/>) so tests can substitute a
/// fake resolver instead of relying on real .lnk files and Shell COM — see
/// spec section 10 ("`.lnk` via fake resolver").
/// </remarks>
public sealed class SteamPathValidator : ISteamPathValidator
{
    private const string SteamExeName = "steam.exe";

    private readonly IPathNormalizationService _normalizer;
    private readonly Func<string, string?> _resolveShortcut;

    public SteamPathValidator(IPathNormalizationService normalizer, Func<string, string?>? shortcutResolver = null)
    {
        _normalizer = normalizer;
        _resolveShortcut = shortcutResolver ?? ShortcutResolver.TryResolveTarget;
    }

    public SteamPathCheckResult Validate(string candidatePath)
    {
        var normalized = _normalizer.NormalizeRawPath(candidatePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return SteamPathCheckResult.Empty;
        }

        var wasNormalized = !string.Equals(normalized, candidatePath.Trim(), StringComparison.Ordinal);

        if (string.Equals(Path.GetExtension(normalized), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var target = _resolveShortcut(normalized);
            if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
            {
                return new SteamPathCheckResult
                {
                    Status = PathCheckStatus.ShortcutUnresolved,
                    StatusMessageKey = "settings.steamPath.invalid"
                };
            }

            return ResolveExeOrFolder(target, successMessageKey: "settings.steamPath.shortcutResolved");
        }

        if (File.Exists(normalized))
        {
            return ResolveExeOrFolder(normalized, successMessageKey: wasNormalized ? "settings.steamPath.normalized" : "settings.steamPath.found");
        }

        if (Directory.Exists(normalized))
        {
            return ResolveFolder(normalized, successMessageKey: wasNormalized ? "settings.steamPath.normalized" : "settings.steamPath.found");
        }

        return new SteamPathCheckResult
        {
            Status = PathCheckStatus.PathNotFound,
            StatusMessageKey = "settings.steamPath.notExist"
        };
    }

    public SteamPathCheckResult FromInstallation(SteamInstallation installation)
    {
        if (!installation.IsValid || string.IsNullOrWhiteSpace(installation.Path))
        {
            return new SteamPathCheckResult
            {
                Status = PathCheckStatus.Empty,
                StatusMessageKey = "settings.steamPath.notFoundAuto"
            };
        }

        return new SteamPathCheckResult
        {
            NormalizedFolderPath = installation.Path,
            SteamExePath = installation.SteamExePath,
            Status = PathCheckStatus.Valid,
            StatusMessageKey = "settings.steamPath.found"
        };
    }

    /// <summary>The candidate is a file path — it must be steam.exe; the folder to persist is its parent.</summary>
    private static SteamPathCheckResult ResolveExeOrFolder(string filePath, string successMessageKey)
    {
        if (!string.Equals(Path.GetFileName(filePath), SteamExeName, StringComparison.OrdinalIgnoreCase))
        {
            return new SteamPathCheckResult
            {
                Status = PathCheckStatus.WrongExe,
                StatusMessageKey = "settings.steamPath.wrongExe"
            };
        }

        var folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return new SteamPathCheckResult
            {
                Status = PathCheckStatus.PathNotFound,
                StatusMessageKey = "settings.steamPath.notExist"
            };
        }

        return new SteamPathCheckResult
        {
            NormalizedFolderPath = folder,
            SteamExePath = filePath,
            Status = PathCheckStatus.Valid,
            StatusMessageKey = successMessageKey
        };
    }

    /// <summary>The candidate is a folder — steam.exe must live directly inside it.</summary>
    private static SteamPathCheckResult ResolveFolder(string folderPath, string successMessageKey)
    {
        var exePath = Path.Combine(folderPath, SteamExeName);
        if (!File.Exists(exePath))
        {
            return new SteamPathCheckResult
            {
                NormalizedFolderPath = folderPath,
                Status = PathCheckStatus.SteamExeNotFound,
                StatusMessageKey = "settings.steamPath.exeNotFound"
            };
        }

        return new SteamPathCheckResult
        {
            NormalizedFolderPath = folderPath,
            SteamExePath = exePath,
            Status = PathCheckStatus.Valid,
            StatusMessageKey = successMessageKey
        };
    }
}
