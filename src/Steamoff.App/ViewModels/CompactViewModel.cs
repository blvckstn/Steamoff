using System.IO;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using Steamoff.App.Localization;
using Steamoff.Core.Enums;
using Steamoff.Core.Models;
using Steamoff.Core.Mvvm;

namespace Steamoff.App.ViewModels;

/// <summary>
/// Drives the Compact Steam Switch View — the small always-available main
/// screen: one big Block/Unblock button, an honest status readout (read back
/// from the firewall, not cached), the active enforcement mode, an admin/
/// firewall-access indicator, and the door into Settings.
/// </summary>
public sealed class CompactViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _refreshTimer;

    private AppSettings _settings;
    private HealthStatus _status = HealthStatus.Unknown;
    private UserContextInfo _userContext;
    private bool _isBusy;

    public CompactViewModel(AppServices services, AppSettings settings, UserContextInfo userContext)
    {
        _services = services;
        _settings = settings;
        _userContext = userContext;
        Loc = new LocalizationProxy(services.Localization);

        ToggleCommand = new AsyncRelayCommand(ToggleAsync, () => !IsBusy && _userContext.HasFirewallAccess);
        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke());

        _services.Localization.LanguageChanged += OnLanguageChanged;

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(Math.Max(10, settings.CheckIntervalSeconds))
        };
        _refreshTimer.Tick += async (_, _) => await RefreshStatusAsync().ConfigureAwait(true);
        _refreshTimer.Start();
    }

    public event Action? SettingsRequested;

    public LocalizationProxy Loc { get; }

    public ICommand ToggleCommand { get; }
    public ICommand OpenSettingsCommand { get; }

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
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(10, settings.CheckIntervalSeconds));
        OnPropertyChanged(nameof(IsBlocked));
        OnPropertyChanged(nameof(ModeText));
        OnPropertyChanged(nameof(ToggleButtonText));
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

            Status = _services.StatusEvaluator.Evaluate(desired, actual, _userContext, _settings.AdditionalFolders, _settings.AdditionalExecutables);
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
                await _services.Firewall.ApplyBlockAsync(targets, _settings.DirectionMode).ConfigureAwait(true);
                _services.Notifications.Show(Loc["notification.blockedTitle"], Loc["notification.blockedBody"]);
            }
            else
            {
                await _services.Firewall.RemoveOrDisableAsync(targets, _settings.RuleCleanupMode).ConfigureAwait(true);
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
        _services.Localization.LanguageChanged -= OnLanguageChanged;
    }
}
