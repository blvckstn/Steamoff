using System.ComponentModel;
using System.Windows;
using Steamoff.App.Tray;
using Steamoff.App.ViewModels;

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

        // The window's own close (X button) is treated like Cancel — discard pending
        // edits, including any language preview, instead of silently keeping them.
        e.Cancel = true;
        _closingViaViewModel = true;
        ViewModel.CancelCommand.Execute(null);
    }
}
