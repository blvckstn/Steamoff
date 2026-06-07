using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using Steamoff.App.Localization;
using Steamoff.Core.Enums;
using Steamoff.Core.Localization;
using Steamoff.Core.Logging;
using Steamoff.Core.Models;
using Steamoff.Core.Mvvm;
using Clipboard = System.Windows.Clipboard;

namespace Steamoff.App.ViewModels;

/// <summary>
/// Drives the Compact Steam Switch View — the small always-available main
/// screen: one big Block/Unblock button, an honest status readout (read back
/// from the firewall, not cached), the active enforcement mode, an admin/
/// firewall-access indicator, and the door into Settings.
/// </summary>
public sealed class CompactViewModel : ObservableObject, IDisposable
{
    private const int MiniLogLineCount = 30;

    private readonly AppServices _services;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _logRefreshTimer;

    private AppSettings _settings;
    private HealthStatus _status = HealthStatus.Unknown;
    private UserContextInfo _userContext;
    private bool _isBusy;
    private bool _isLogExpanded;

    public CompactViewModel(AppServices services, AppSettings settings, UserContextInfo userContext)
    {
        _services = services;
        _settings = settings;
        _userContext = userContext;
        Loc = new LocalizationProxy(services.Localization);

        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => !IsBusy && _userContext.HasFirewallAccess);
        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke());
        ExpandLogCommand = new RelayCommand(() => IsLogExpanded = !IsLogExpanded);
        OpenFullLogCommand = new RelayCommand(OpenFullLog);
        CopyDiagnosticsCommand = new AsyncRelayCommand(CopyDiagnosticsAsync);

        _services.Localization.LanguageChanged += OnLanguageChanged;

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(Math.Max(10, settings.CheckIntervalSeconds))
        };
        _refreshTimer.Tick += async (_, _) => await RefreshStatusAsync().ConfigureAwait(true);
        _refreshTimer.Start();

        _logRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _logRefreshTimer.Tick += async (_, _) => await RefreshRecentLogLinesAsync().ConfigureAwait(true);
        _logRefreshTimer.Start();

        _ = RefreshRecentLogLinesAsync();
    }

    public event Action? SettingsRequested;

    public LocalizationProxy Loc { get; }

    public ICommand ToggleCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ExpandLogCommand { get; }
    public ICommand OpenFullLogCommand { get; }
    public ICommand CopyDiagnosticsCommand { get; }

    public ObservableCollection<string> RecentLogLines { get; } = new();

    public bool IsLogExpanded
    {
        get => _isLogExpanded;
        set
        {
            if (SetProperty(ref _isLogExpanded, value))
            {
                OnPropertyChanged(nameof(ExpandLogButtonText));
            }
        }
    }

    public string ExpandLogButtonText => IsLogExpanded ? Loc["compact.miniLog.collapse"] : Loc["compact.miniLog.expand"];

    public bool HasRecentLogLines => RecentLogLines.Count > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((AsyncRelayCommand)ToggleCommand).RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(ToggleButtonText));
            }
        }
    }

    public HealthStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsBlocked));
                OnPropertyChanged(nameof(ToggleButtonText));
                OnPropertyChanged(nameof(CoverageText));
            }
        }
    }

    public bool IsBlocked => _settings.DesiredState == DesiredState.Blocked;

    /// <summary>True when the persisted language differs from the running process's language — mirrors <see cref="ViewModels.SettingsViewModel.IsRestartRequired"/> (no <see cref="Core.Models.SettingsEditSession"/> here, so it's derived straight from <see cref="AppServices"/>).</summary>
    public bool IsRestartRequired => LanguageRestartState.IsRestartRequired(_settings.Language, _services.Localization.CurrentLanguage.Code);

    public string StatusText => _status.Overall switch
    {
        OverallStatus.FullyBlocked => Loc["compact.statusBlocked"],
        OverallStatus.FullyUnblocked => Loc["compact.statusUnblocked"],
        OverallStatus.PartiallyBlocked => Loc["compact.statusPartial"],
        OverallStatus.DriftDetected => Loc["compact.statusDrift"],
        OverallStatus.Error => Loc["compact.statusError"],
        OverallStatus.NotConfigured => Loc["compact.statusNotConfigured"],
        OverallStatus.ReadOnlyNoAdmin => Loc["status.readOnly"],
        _ => Loc["compact.statusChecking"]
    };

    public string CoverageText => _status.Overall is OverallStatus.NotConfigured or OverallStatus.Error
        ? string.Empty
        : $"{_status.CoveragePercent:0}%";

    public string ToggleButtonText => IsBusy
        ? Loc["compact.statusChecking"]
        : (IsBlocked ? Loc["compact.unblockButton"] : Loc["compact.blockButton"]);

    public string ModeText => Loc.GetFormatted("compact.modeLabel", ModeDisplayName(_settings.EnforcementMode));

    public string AdminStatusText => _userContext.HasFirewallAccess ? Loc["compact.adminOk"] : Loc["compact.adminMissing"];

    public bool HasAdminAccess => _userContext.HasFirewallAccess;

    public string VersionText => Loc.GetFormatted("compact.versionLabel", AppVersion);

    public HealthLevel Level => _userContext.HasFirewallAccess ? _status.Level : HealthLevel.ReadOnly;

    private static string AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";

    private static string ModeDisplayName(EnforcementMode mode) => mode switch
    {
        EnforcementMode.AlwaysBlock => "Always Block",
        EnforcementMode.AlwaysUnblock => "Always Unblock",
        EnforcementMode.PauseMonitoring => "Pause Monitoring",
        _ => "Manual Toggle"
    };

    private void OnLanguageChanged(object? sender, AppLanguage language) => RaiseLanguageDependentChanges();

    public void RaiseLanguageDependentChanges()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ToggleButtonText));
        OnPropertyChanged(nameof(ModeText));
        OnPropertyChanged(nameof(AdminStatusText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(ExpandLogButtonText));
        OnPropertyChanged(nameof(IsRestartRequired));
    }

    public async Task RefreshRecentLogLinesAsync(CancellationToken ct = default)
    {
        try
        {
            var lines = await _services.Log.ReadLastLinesAsync(MiniLogLineCount, ct).ConfigureAwait(true);

            RecentLogLines.Clear();
            foreach (var line in lines)
            {
                RecentLogLines.Add(line);
            }

            OnPropertyChanged(nameof(HasRecentLogLines));
        }
        catch (IOException)
        {
            // The log file may be momentarily locked by a concurrent write — skip this refresh tick.
        }
    }

    private void OpenFullLog()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_services.Log.LogFilePath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            _services.Log.Error("Не удалось открыть файл лога.", ex);
        }
    }

    private async Task CopyDiagnosticsAsync()
    {
        var report = await _services.Diagnostics.BuildExtendedReportAsync().ConfigureAwait(true);
        Clipboard.SetText(report);
        _services.Notifications.Show(Loc["compact.miniLog.title"], Loc["compact.miniLog.copied"]);
        await _services.LocalizedLog.LogAsync(LogEventKey.DiagnosticsCopied).ConfigureAwait(true);
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(10, settings.CheckIntervalSeconds));
        OnPropertyChanged(nameof(IsBlocked));
        OnPropertyChanged(nameof(ModeText));
        OnPropertyChanged(nameof(ToggleButtonText));
        OnPropertyChanged(nameof(IsRestartRequired));
    }

    public void UpdateUserContext(UserContextInfo userContext)
    {
        _userContext = userContext;
        ((AsyncRelayCommand)ToggleCommand).RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(AdminStatusText));
        OnPropertyChanged(nameof(HasAdminAccess));
        OnPropertyChanged(nameof(Level));
    }

    public async Task RefreshStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var targets = await TargetBuilder.BuildAllTargetsAsync(_services, _settings, ct).ConfigureAwait(true);
            var desired = new DesiredFirewallState
            {
                State = _settings.DesiredState,
                Targets = targets,
                DirectionMode = _settings.DirectionMode
            };

            var actual = _userContext.HasFirewallAccess
                ? await _services.Firewall.GetCurrentStateAsync(ct).ConfigureAwait(true)
                : ActualFirewallState.Empty;

            var wasDrifting = _status.Overall == OverallStatus.DriftDetected;
            Status = _services.StatusEvaluator.Evaluate(desired, actual, _userContext, _settings.AdditionalFolders, _settings.AdditionalExecutables);

            if (Status.Overall == OverallStatus.DriftDetected && !wasDrifting)
            {
                await _services.LocalizedLog.LogAsync(LogEventKey.DriftDetected).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Core.Exceptions.FirewallOperationException)
        {
            _services.Log.Error("Не удалось обновить статус firewall.", ex);
            Status = new HealthStatus
            {
                Level = HealthLevel.Error,
                Overall = OverallStatus.Error,
                Message = ex.Message
            };
        }
    }

    private async Task ToggleAsync()
    {
        IsBusy = true;
        try
        {
            var targets = await TargetBuilder.BuildAllTargetsAsync(_services, _settings, default).ConfigureAwait(true);
            var newState = _settings.DesiredState == DesiredState.Blocked ? DesiredState.Unblocked : DesiredState.Blocked;

            if (newState == DesiredState.Blocked)
            {
                await _services.LocalizedLog.LogAsync(LogEventKey.FirewallBlockStarted).ConfigureAwait(true);
                await _services.Firewall.ApplyBlockAsync(targets, _settings.DirectionMode).ConfigureAwait(true);
                await _services.LocalizedLog.LogAsync(LogEventKey.FirewallBlockCompleted).ConfigureAwait(true);
                _services.Notifications.Show(Loc["notification.blockedTitle"], Loc["notification.blockedBody"]);
            }
            else
            {
                await _services.LocalizedLog.LogAsync(LogEventKey.FirewallUnblockStarted).ConfigureAwait(true);
                await _services.Firewall.RemoveOrDisableAsync(targets, _settings.RuleCleanupMode).ConfigureAwait(true);
                await _services.LocalizedLog.LogAsync(LogEventKey.FirewallUnblockCompleted).ConfigureAwait(true);
                _services.Notifications.Show(Loc["notification.unblockedTitle"], Loc["notification.unblockedBody"]);
            }

            _settings.DesiredState = newState;
            await _services.Settings.SaveAsync(_settings).ConfigureAwait(true);
            OnPropertyChanged(nameof(IsBlocked));

            await RefreshStatusAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Core.Exceptions.FirewallOperationException)
        {
            _services.Log.Error("Не удалось переключить состояние блокировки Steam.", ex);
            await _services.LocalizedLog.LogAsync(
                _settings.DesiredState == DesiredState.Blocked ? LogEventKey.FirewallUnblockFailed : LogEventKey.FirewallBlockFailed,
                ex.Message).ConfigureAwait(true);
            Status = new HealthStatus
            {
                Level = HealthLevel.Error,
                Overall = OverallStatus.Error,
                Message = ex.Message
            };
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _logRefreshTimer.Stop();
        _services.Localization.LanguageChanged -= OnLanguageChanged;
    }
}
