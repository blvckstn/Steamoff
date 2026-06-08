using Steamoff.Core.Models;

namespace Steamoff.Core.Interfaces;

/// <summary>Finds the Steam installation via registry, running process, default paths, and shortcuts.</summary>
public interface ISteamDiscoveryService
{
    Task<SteamInstallation> DiscoverAsync(CancellationToken ct = default);

    /// <summary>Validates a user-provided folder: must exist and contain steam.exe.</summary>
    SteamInstallation ValidateManualPath(string candidatePath);
}

/// <summary>Finds Steam Core executables (steam.exe, steamservice.exe, steamwebhelper.exe copies) under a Steam root.</summary>
public interface ITargetScanner
{
    Task<IReadOnlyList<FirewallTarget>> FindSteamCoreTargetsAsync(string steamRoot, bool blockAllInFolder, CancellationToken ct = default);

    /// <summary>Recursively (depth-limited) scans a folder for executables and reports progress; never blocks the calling thread.</summary>
    Task<IReadOnlyList<string>> ScanFolderForExecutablesAsync(string folderPath, bool recursive, IProgress<int>? progress, CancellationToken ct = default);
}

/// <summary>Manages the user's "Additional Folders" collection.</summary>
public interface IFolderTargetService
{
    Task<FolderBlockTarget> AddFolderAsync(string path, string? displayName, bool recursive, CancellationToken ct = default);
    void RemoveFolder(string id);
    Task RescanAsync(FolderBlockTarget folder, CancellationToken ct = default);
    Task<IReadOnlyList<FirewallTarget>> ToFirewallTargetsAsync(FolderBlockTarget folder, CancellationToken ct = default);
}

/// <summary>Manages the user's "Standalone EXEs" collection. Only ever reads paths — never executes them.</summary>
public interface IExeTargetService
{
    /// <summary>Validates path existence, .exe extension, and rejects URLs/empty input. Throws ArgumentException with a user-friendly message on failure.</summary>
    void Validate(string path);

    ExeBlockTarget Create(string path, string? displayName);

    FirewallTarget ToFirewallTarget(ExeBlockTarget exe);
}
