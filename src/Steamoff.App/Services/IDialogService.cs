namespace Steamoff.App.Services;

/// <summary>
/// Thin abstraction over WPF's folder/file picker dialogs so that
/// <c>SettingsViewModel</c> commands (Add Folder/EXE, Browse Steam folder)
/// can be unit-tested without spawning real OS dialogs — see
/// <c>WpfDialogService</c> for the production implementation backed by
/// <c>Microsoft.Win32.OpenFolderDialog</c>/<c>OpenFileDialog</c>.
/// </summary>
public interface IDialogService
{
    /// <summary>Shows a folder picker. Returns the chosen folder path, or null if the user cancelled.</summary>
    string? PickFolder(string title, string? initialDirectory = null);

    /// <summary>Shows a file picker filtered to executables (.exe). Returns the chosen path, or null if the user cancelled.</summary>
    string? PickExecutableFile(string title, string? initialDirectory = null);
}
