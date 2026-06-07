using System.ComponentModel;
using System.Windows;
using Steamoff.App.Tray;
using Steamoff.App.ViewModels;

namespace Steamoff.App.Views;

/// <summary>
/// The Compact Steam Switch View — Steamoff's small always-available main
/// screen. Closing it hides to tray instead of exiting (Constitution: tray-resident
/// app), unless the application is genuinely shutting down.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(CompactViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        WindowChromeHelper.ApplyDarkTitleBar(this);
    }

    public CompactViewModel ViewModel => (CompactViewModel)DataContext;

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (App.Current is App { IsExiting: true })
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
