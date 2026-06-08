using Steamoff.Core.Models;

namespace Steamoff.Core.Interfaces;

/// <summary>
/// Cleans up a raw, user-supplied path string before it is interpreted as a
/// filesystem location: strips surrounding quotes/whitespace, expands
/// environment variables (e.g. <c>%ProgramFiles(x86)%</c>), and collapses
/// duplicated directory separators. Pure string transformation — does not
/// touch the filesystem (see <see cref="ISteamPathValidator"/> for that).
/// </summary>
public interface IPathNormalizationService
{
    /// <summary>Trims whitespace, strips surrounding quotes, expands environment variables, and normalizes slashes/casing of separators.</summary>
    string NormalizeRawPath(string rawPath);
}

/// <summary>
/// Resolves and validates a Steam-path candidate coming from any entry point —
/// typed text, drag&amp;drop, a folder/file dialog pick, or auto-discovery — into
/// a <see cref="SteamPathCheckResult"/>. Always reports the *folder* to persist,
/// never an .exe path (spec section 4: "save FOLDER not exe"). Lives in
/// Infrastructure because shortcut (.lnk) resolution requires the internal
/// <c>ShortcutResolver</c> COM helper in this assembly.
/// </summary>
public interface ISteamPathValidator
{
    /// <summary>
    /// Accepts a Steam install folder, a steam.exe path, a quoted/whitespace-padded
    /// path, an environment-variable path, or a .lnk shortcut to any of the above,
    /// and resolves it to a validated Steam folder (or a specific failure reason).
    /// </summary>
    SteamPathCheckResult Validate(string candidatePath);

    /// <summary>Builds a <see cref="SteamPathCheckResult"/> directly from a discovery result (registry/process/default-path/shortcut sources never need re-resolution of shortcuts).</summary>
    SteamPathCheckResult FromInstallation(SteamInstallation installation);
}
