using System.IO;
using Steamoff.Core.Models;

namespace Steamoff.App;

/// <summary>
/// Assembles the full list of <see cref="FirewallTarget"/>s Steamoff currently
/// cares about — Steam Core executables plus the user's additional folders and
/// standalone EXEs — so the compact toggle and the status refresh always work
/// from the exact same desired-state definition (Constitution III: honest state).
/// </summary>
internal static class TargetBuilder
{
    public static async Task<IReadOnlyList<FirewallTarget>> BuildAllTargetsAsync(AppServices services, AppSettings settings, CancellationToken ct)
    {
        var targets = new List<FirewallTarget>();

        var steamRoot = await ResolveSteamRootAsync(services, settings, ct).ConfigureAwait(false);
        if (steamRoot is not null)
        {
            try
            {
                var coreTargets = await services.Scanner
                    .FindSteamCoreTargetsAsync(steamRoot, settings.BlockAllExecutablesInSteamFolder, ct)
                    .ConfigureAwait(false);
                targets.AddRange(coreTargets);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                services.Log.Warning($"Не удалось просканировать папку Steam '{steamRoot}': {ex.Message}");
            }
        }

        foreach (var folder in settings.AdditionalFolders.Where(f => f.Enabled))
        {
            try
            {
                var folderTargets = await services.FolderTargets.ToFirewallTargetsAsync(folder, ct).ConfigureAwait(false);
                targets.AddRange(folderTargets);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                services.Log.Warning($"Не удалось просканировать папку '{folder.Path}': {ex.Message}");
            }
        }

        foreach (var exe in settings.AdditionalExecutables.Where(e => e.Enabled))
        {
            targets.Add(services.ExeTargets.ToFirewallTarget(exe));
        }

        return targets;
    }

    private static async Task<string?> ResolveSteamRootAsync(AppServices services, AppSettings settings, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(settings.SteamPath))
        {
            var manual = services.SteamDiscovery.ValidateManualPath(settings.SteamPath);
            if (manual.IsValid)
            {
                return manual.Path;
            }
        }

        var discovered = await services.SteamDiscovery.DiscoverAsync(ct).ConfigureAwait(false);
        return discovered.IsValid ? discovered.Path : null;
    }
}
