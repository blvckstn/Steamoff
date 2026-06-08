using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
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
        var app = App.Current;
        if (app is { IsExiting: true })
        {
            app.Log?.Info("MainWindow.Closing: приложение завершает работу — окно закрывается по-настоящему.");
            return;
        }

        app?.Log?.Info($"MainWindow.Closing: окно скрывается в трей (было IsVisible={IsVisible}, WindowState={WindowState}).");

        // Calling Hide() synchronously from inside Closing corrupts the window's
        // internal close-state machine (WPF then throws InvalidOperationException
        // "during closing" on every later Show() — silently swallowed by
        // App.OnDispatcherUnhandledException, so "Open Steamoff" appears to do
        // nothing). Deferring to the dispatcher lets the Closing sequence finish
        // cleanly first.
        e.Cancel = true;
        Dispatcher.BeginInvoke(() =>
        {
            Hide();
            app?.Log?.Info($"MainWindow.Closing: окно скрыто (отложенный Hide выполнен; теперь IsVisible={IsVisible}).");
        }, DispatcherPriority.Background);
    }
}
