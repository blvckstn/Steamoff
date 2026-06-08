using System.IO;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using OpenFolderDialog = Microsoft.Win32.OpenFolderDialog;

namespace Steamoff.App.Services;

/// <summary>
/// Production <see cref="IDialogService"/> backed by the WPF-native
/// <c>Microsoft.Win32.OpenFolderDialog</c>/<c>OpenFileDialog</c> (.NET 8).
/// Aliased explicitly because <c>System.Windows.Forms</c> — pulled in for
/// the tray <c>NotifyIcon</c> — declares same-named dialog types that would
/// </summary>
public sealed class WpfDialogService : IDialogService
{
    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickExecutableFile(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Executable files (*.exe)|*.exe",
            CheckFileExists = true
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
