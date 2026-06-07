using System.IO;
using System.Windows;
using System.Windows.Threading;
using Steamoff.App.Localization;
using Steamoff.App.ViewModels;
using Steamoff.App.Views;
using Steamoff.Core.Models;

namespace Steamoff.App;

/// <summary>
/// Composition root and startup orchestrator: enforces single-instance via a
/// named mutex, loads settings, runs the first-launch language dialog when
/// needed, builds the tray and the Compact View, and registers the
/// <see cref="LocalizationProxy"/> as the app-wide "Loc" resource so every
/// XAML binding (windows, dialogs, tray) refreshes instantly on language switch.
/// </summary>
public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "Local\\Steamoff.SingleInstance.9F2B7F1E";

    private Mutex? _singleInstanceMutex;
    private AppServices? _services;
    private AppSettings? _settings;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;

    /// <summary>True once Exit has been requested — lets MainWindow distinguish "hide to tray" from a real shutdown.</summary>
    public bool IsExiting { get; private set; }

    public new static App Current => (App)System.Windows.Application.Current!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!AcquireSingleInstanceLock())
        {
            System.Windows.MessageBox.Show("Steamoff уже запущен.", "Steamoff", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _services = new AppServices();
        Resources["Loc"] = new LocalizationProxy(_services.Localization);

        _ = StartupAsync();
    }

    private async Task StartupAsync()
    {
        var services = _services!;

        _settings = await services.Settings.LoadAsync().ConfigureAwait(true);
        services.Localization.SetLanguage(_settings.Language);

        if (!_settings.IsFirstLaunchCompleted)
        {
            await RunFirstLaunchDialogAsync(services, _settings).ConfigureAwait(true);
        }

        services.Tray.Initialize();
        WireTray(services);

        var userContext = services.UserContext.GetCurrentContext();
        _mainWindow = new MainWindow(new CompactViewModel(services, _settings, userContext));
        MainWindow = _mainWindow;

        if (!_settings.StartMinimizedToTray)
        {
            _mainWindow.Show();
        }

        await _mainWindow.ViewModel.RefreshStatusAsync().ConfigureAwait(true);
        services.Tray.UpdateStatus(BuildHealthSnapshot(_mainWindow), !userContext.HasFirewallAccess);
    }

    private async Task RunFirstLaunchDialogAsync(AppServices services, AppSettings settings)
    {
        var dialogViewModel = new LanguageSelectionViewModel(services.Localization);
        var dialog = new LanguageSelectionWindow(dialogViewModel);
        dialog.ShowDialog();

        settings.Language = dialog.Result.Code;
        settings.IsFirstLaunchCompleted = true;
        services.Localization.SetLanguage(settings.Language);

        await services.Settings.SaveAsync(settings).ConfigureAwait(true);
        services.Log.Info($"Первый запуск завершён, выбран язык интерфейса: {settings.Language}.");
    }

    private void WireTray(AppServices services)
    {
        services.Tray.OpenRequested += () => ShowMainWindow();
        services.Tray.SettingsRequested += () => OpenSettings();
        services.Tray.LogsRequested += () => OpenLogsFolder(services);
        services.Tray.ExitRequested += () => ExitApplication();

        services.Tray.BlockRequested += () => _mainWindow?.ViewModel.ToggleCommand.Execute(null);
        services.Tray.UnblockRequested += () => _mainWindow?.ViewModel.ToggleCommand.Execute(null);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private void OpenSettings()
    {
        if (_services is null || _settings is null)
        {
            return;
        }

        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(_services, _settings);
        viewModel.SettingsCommitted += OnSettingsCommitted;

        _settingsWindow = new SettingsWindow(viewModel);
        _settingsWindow.Closed += (_, _) =>
        {
            viewModel.SettingsCommitted -= OnSettingsCommitted;
            _settingsWindow = null;
            ShowMainWindow();
        };

        _settingsWindow.Show();
    }

    private void OnSettingsCommitted(AppSettings updated)
    {
        _settings = updated;
        _mainWindow?.ViewModel.UpdateSettings(updated);
        _ = (_mainWindow?.ViewModel.RefreshStatusAsync());
    }

    private static void OpenLogsFolder(AppServices services)
    {
        try
        {
            var directory = Path.GetDirectoryName(services.Log.LogFilePath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            services.Log.Warning($"Не удалось открыть папку с логами: {ex.Message}");
        }
    }

    private void ExitApplication()
    {
        IsExiting = true;
        _settingsWindow?.Close();
        _mainWindow?.Close();
        Shutdown();
    }

    private static HealthStatus BuildHealthSnapshot(MainWindow window) => new()
    {
        Level = window.ViewModel.Level,
        Overall = window.ViewModel.IsBlocked ? Steamoff.Core.Enums.OverallStatus.FullyBlocked : Steamoff.Core.Enums.OverallStatus.FullyUnblocked
    };

    private bool AcquireSingleInstanceLock()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        return createdNew;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _services?.Log.Error("Необработанное исключение в потоке интерфейса.", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting = true;
        _mainWindow?.ViewModel.Dispose();
        _services?.Tray.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
