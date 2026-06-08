using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Steamoff.App.Localization;
using Steamoff.App.ViewModels;
using Steamoff.App.Views;
using Steamoff.Core.Logging;
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
    private StartupOverlayWindow? _startupOverlayWindow;
    private bool _startedFromTrayArgument;

    /// <summary>True once Exit has been requested — lets MainWindow distinguish "hide to tray" from a real shutdown.</summary>
    public bool IsExiting { get; private set; }

    public new static App Current => (App)System.Windows.Application.Current!;

    /// <summary>Exposes the composition root's log sink to windows (e.g. MainWindow's Closing tracking) without giving them the full AppServices.</summary>
    internal Steamoff.Core.Interfaces.ILogService? Log => _services?.Log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _startedFromTrayArgument = e.Args.Any(arg => string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase));

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

        try
        {
            _settings = await services.Settings.LoadAsync().ConfigureAwait(true);
            var isFirstLaunch = !_settings.IsFirstLaunchCompleted;
            services.RefreshSettingsSnapshot(_settings);
            services.Localization.SetLanguage(_settings.Language);
            await services.LocalizedLog.LogAsync(LogEventKey.AppStarted).ConfigureAwait(true);

            if (isFirstLaunch)
            {
                await RunFirstLaunchDialogAsync(services, _settings).ConfigureAwait(true);
            }

            _startupOverlayWindow = new StartupOverlayWindow();
            _startupOverlayWindow.Show();

            services.Tray.Initialize();
            WireTray(services);

            var userContext = services.UserContext.GetCurrentContext();
            services.Log.Info("StartupAsync: создаю главное окно...");
            _mainWindow = new MainWindow(new CompactViewModel(services, _settings, userContext));
            MainWindow = _mainWindow;
            _mainWindow.ViewModel.SettingsRequested += OpenSettings;
            services.Log.Info($"StartupAsync: главное окно создано (_mainWindow назначено). StartMinimizedToTray={_settings.StartMinimizedToTray}.");

            if (ShouldShowMainWindowOnStartup(_settings, isFirstLaunch, _startedFromTrayArgument))
            {
                _mainWindow.Show();
                services.Log.Info($"StartupAsync: вызван Show() при запуске. IsVisible={_mainWindow.IsVisible}, WindowState={_mainWindow.WindowState}.");
            }

            await _mainWindow.ViewModel.RefreshStatusAsync().ConfigureAwait(true);
            services.Tray.UpdateStatus(BuildHealthSnapshot(_mainWindow), !userContext.HasFirewallAccess);
            CloseStartupOverlay();
        }
        catch (Exception ex)
        {
            CloseStartupOverlay();
            services.Log.Error("StartupAsync: исключение при запуске приложения — главное окно могло не создаться.", ex);
            throw;
        }
    }

    private void CloseStartupOverlay()
    {
        if (_startupOverlayWindow is null)
        {
            return;
        }

        _startupOverlayWindow.Close();
        _startupOverlayWindow = null;
    }

    public static bool ShouldShowMainWindowOnStartup(AppSettings settings, bool isFirstLaunch, bool startedFromTrayArgument) =>
        isFirstLaunch || !settings.StartMinimizedToTray || !startedFromTrayArgument;

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

        services.Tray.BlockRequested += () => _mainWindow?.ViewModel.EnableOfflineCommand.Execute(null);
        services.Tray.UnblockRequested += () => _mainWindow?.ViewModel.DisableOfflineCommand.Execute(null);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _services?.Log.Info("ShowMainWindow: главное окно ещё не создано (_mainWindow is null) — запрос проигнорирован.");
            return;
        }

        _services?.Log.Info($"ShowMainWindow: запрошено отображение. До вызова Show(): IsVisible={_mainWindow.IsVisible}, WindowState={_mainWindow.WindowState}.");
        try
        {
            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }

            _mainWindow.Activate();
            _services?.Log.Info($"ShowMainWindow: после вызова Show()/Activate(): IsVisible={_mainWindow.IsVisible}, WindowState={_mainWindow.WindowState}.");
        }
        catch (Exception ex)
        {
            _services?.Log.Error("ShowMainWindow: исключение при попытке отобразить главное окно.", ex);
            throw;
        }
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
        viewModel.RestartRequested += RestartApplication;

        _settingsWindow = new SettingsWindow(viewModel);
        _settingsWindow.Closed += (_, _) =>
        {
            viewModel.SettingsCommitted -= OnSettingsCommitted;
            viewModel.RestartRequested -= RestartApplication;
            _settingsWindow = null;
            ShowMainWindow();
        };

        _settingsWindow.Show();
    }

    private void OnSettingsCommitted(AppSettings updated)
    {
        _settings = updated;
        _services?.RefreshSettingsSnapshot(updated);
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

    /// <summary>
    /// "Restart now": relaunches Steamoff with the same command-line arguments
    /// and tears the current instance down. Reuses <see cref="IElevationService.TryRelaunchElevated"/>
    /// rather than duplicating the relaunch mechanism — Steamoff always needs
    /// admin rights for firewall management, so the "runas" UAC prompt it
    /// triggers is the same one the app already requires on every launch, not
    /// an extra one (recorded as ASSUMPTIONS.md A21).
    /// </summary>
    private void RestartApplication()
    {
        if (_services is null)
        {
            return;
        }

        var arguments = Environment.GetCommandLineArgs().Skip(1).ToList();
        if (_services.Elevation.TryRelaunchElevated(arguments, out var failureReason))
        {
            ExitApplication();
            return;
        }

        _ = _services.LocalizedLog.LogAsync(LogEventKey.RestartFailed, failureReason ?? "unknown");
        _services.Notifications.Show("Steamoff", _services.Localization.GetString("settings.toast.restartFailed"));
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
        if (_services is not null)
        {
            _services.LocalizedLog.LogAsync(LogEventKey.AppClosed).GetAwaiter().GetResult();
        }

        _mainWindow?.ViewModel.Dispose();
        _services?.Tray.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
