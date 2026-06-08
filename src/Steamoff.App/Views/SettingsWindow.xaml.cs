using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Steamoff.App.Tray;
using Steamoff.App.ViewModels;
using DragEventArgs = System.Windows.DragEventArgs;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;

namespace Steamoff.App.Views;

/// <summary>
/// Settings View — language bar topmost, then mode/path/folder/EXE/autostart/testing
/// sections, with Apply (stays open), Save (closes to Compact) and Cancel (rolls back,
/// including any live-previewed language switch) at the bottom.
/// </summary>
public partial class SettingsWindow : Window
{
    private bool _closingViaViewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        WindowChromeHelper.ApplyDarkTitleBar(this);

        viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) =>
        {
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.Dispose();
        };

        SteamPathDropZone.Drop += OnSteamPathDrop;
        SteamPathDropZone.PreviewDragOver += OnDragOverCopyEffect;
        FoldersDropZone.Drop += OnFoldersDrop;
        FoldersDropZone.PreviewDragOver += OnDragOverCopyEffect;
        ExeDropZone.Drop += OnExeDrop;
        ExeDropZone.PreviewDragOver += OnDragOverCopyEffect;
    }

    public SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    private void OnCloseRequested()
    {
        _closingViaViewModel = true;
        Close();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closingViaViewModel)
        {
            return;
        }

        // The window's own close (X button, or an external Close() during app
        // exit/restart) is treated like Cancel — discard pending edits, including
        // any language preview, instead of silently keeping them. CancelCommand
        // ends by raising CloseRequested -> Close(); calling that synchronously
        // here would re-enter Close() while WPF is still processing this Closing
        // event, which throws InvalidOperationException ("during closing" — the
        // same swallowed exception that was blocking "Open Steamoff" from
        // reopening the main window after a restart). Defer it to the dispatcher
        // so this Closing sequence finishes first.
        e.Cancel = true;
        _closingViaViewModel = true;
        Dispatcher.BeginInvoke(() => ViewModel.CancelCommand.Execute(null), DispatcherPriority.Background);
    }

    private void OnSteamPathLostFocus(object sender, RoutedEventArgs e) => ViewModel.RevalidateSteamPath();

    private static void OnDragOverCopyEffect(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnSteamPathDrop(object sender, DragEventArgs e)
    {
        var path = GetFirstDroppedPath(e);
        if (path is not null)
        {
            ViewModel.ApplySteamPathCandidate(path);
        }

        e.Handled = true;
    }

    private void OnFoldersDrop(object sender, DragEventArgs e)
    {
        var path = GetFirstDroppedPath(e);
        if (path is not null)
        {
            _ = ViewModel.AddFolderFromPathAsync(path);
        }

        e.Handled = true;
    }

    private void OnExeDrop(object sender, DragEventArgs e)
    {
        var path = GetFirstDroppedPath(e);
        if (path is not null)
        {
            _ = ViewModel.AddExeFromPathAsync(path);
        }

        e.Handled = true;
    }

    private static string? GetFirstDroppedPath(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            return files[0];
        }

        return null;
    }
}
