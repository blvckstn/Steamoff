using System.Collections.ObjectModel;
using System.Windows.Input;
using Steamoff.App.Localization;
using Steamoff.Core.Enums;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;
using Steamoff.Core.Mvvm;

namespace Steamoff.App.ViewModels;

/// <summary>
/// Drives the Settings View. Edits happen against a <see cref="SettingsEditSession"/>
/// draft clone (never the saved object directly); Apply/Save commit the draft,
/// Cancel discards it — including rolling back any language switch that was
/// previewed live while the user browsed the language bar.
/// </summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly AppLanguage _languageOnEntry;
    private SettingsEditSession _session;
    private DiagnosticsReport _lastReport = DiagnosticsReport.NotRunYet;
    private bool _isTesting;
    private string? _toast;

    public SettingsViewModel(AppServices services, AppSettings savedSettings)
    {
        _services = services;
        _session = new SettingsEditSession(savedSettings);
        _languageOnEntry = services.Localization.CurrentLanguage;
        Loc = new LocalizationProxy(services.Localization);

        Languages = new ObservableCollection<AppLanguage>(services.Localization.AvailableLanguages);

        SelectLanguageCommand = new RelayCommand(p =>
        {
            if (p is AppLanguage language)
            {
                SelectedLanguage = language;
            }
        });

        TestCommand = new AsyncRelayCommand(RunTestAsync, () => !IsTesting);
        ApplyCommand = new AsyncRelayCommand(() => CommitAsync(closeAfter: false));
        SaveCommand = new AsyncRelayCommand(() => CommitAsync(closeAfter: true));
        CancelCommand = new RelayCommand(Cancel);

        _services.Localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, AppLanguage language)
    {
        OnPropertyChanged(nameof(StatusSummaryText));
        OnPropertyChanged(nameof(LastRunText));
    }

    public void Dispose()
    {
        _services.Localization.LanguageChanged -= OnLanguageChanged;
    }

    public event Action? CloseRequested;
    public event Action<AppSettings>? SettingsCommitted;

    public LocalizationProxy Loc { get; }

    public ObservableCollection<AppLanguage> Languages { get; }

    public ICommand SelectLanguageCommand { get; }
    public ICommand TestCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public AppSettings Draft => _session.Draft;

    public AppLanguage SelectedLanguage
    {
        get => Languages.FirstOrDefault(l => l.Code == _session.Draft.Language) ?? _languageOnEntry;
        set
        {
            if (value is null || value.Code == _session.Draft.Language)
            {
                return;
            }

            _session.Draft.Language = value.Code;
            _services.Localization.SetLanguage(value.Code);
            OnPropertyChanged();
        }
    }

    public bool BlockInbound
    {
        get => _session.Draft.BlockInbound;
        set => SetDraft(value, v => _session.Draft.BlockInbound = v, _session.Draft.BlockInbound);
    }

    public bool BlockAllExecutablesInSteamFolder
    {
        get => _session.Draft.BlockAllExecutablesInSteamFolder;
        set => SetDraft(value, v => _session.Draft.BlockAllExecutablesInSteamFolder = v, _session.Draft.BlockAllExecutablesInSteamFolder);
    }

    public bool StartWithWindows
    {
        get => _session.Draft.StartWithWindows;
        set => SetDraft(value, v => _session.Draft.StartWithWindows = v, _session.Draft.StartWithWindows);
    }

    public bool StartMinimizedToTray
    {
        get => _session.Draft.StartMinimizedToTray;
        set => SetDraft(value, v => _session.Draft.StartMinimizedToTray = v, _session.Draft.StartMinimizedToTray);
    }

    public bool WarnBeforeUnblock
    {
        get => _session.Draft.WarnBeforeUnblock;
        set => SetDraft(value, v => _session.Draft.WarnBeforeUnblock = v, _session.Draft.WarnBeforeUnblock);
    }

    public string? SteamPath
    {
        get => _session.Draft.SteamPath;
        set
        {
            if (_session.Draft.SteamPath == value)
            {
                return;
            }

            _session.Draft.SteamPath = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
        }
    }

    public EnforcementMode EnforcementMode
    {
        get => _session.Draft.EnforcementMode;
        set => SetDraft(value, v => _session.Draft.EnforcementMode = v, _session.Draft.EnforcementMode);
    }

    public IReadOnlyList<FolderBlockTarget> Folders => _session.Draft.AdditionalFolders;
    public IReadOnlyList<ExeBlockTarget> Executables => _session.Draft.AdditionalExecutables;

    public bool IsTesting
    {
        get => _isTesting;
        private set
        {
            if (SetProperty(ref _isTesting, value))
            {
                ((AsyncRelayCommand)TestCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public DiagnosticsReport LastReport
    {
        get => _lastReport;
        private set
        {
            if (SetProperty(ref _lastReport, value))
            {
                OnPropertyChanged(nameof(StatusSummaryText));
                OnPropertyChanged(nameof(LastRunText));
            }
        }
    }

    public string StatusSummaryText
    {
        get
        {
            if (!_lastReport.HasRun)
            {
                return Loc["settings.status.notRunYet"];
            }

            return _lastReport.OverallOutcome switch
            {
                TestOutcome.Ok => Loc["settings.status.ok"],
                TestOutcome.Warning => Loc["settings.status.warning"],
                _ => Loc["settings.status.error"]
            };
        }
    }

    public string LastRunText => _lastReport.HasRun
        ? Loc.GetFormatted("settings.status.lastRun", _lastReport.RanAt.ToLocalTime().ToString("g"))
        : string.Empty;

    public string? Toast
    {
        get => _toast;
        private set => SetProperty(ref _toast, value);
    }

    private void SetDraft<T>(T newValue, Action<T> assign, T currentValue, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(newValue, currentValue))
        {
            return;
        }

        assign(newValue);
        OnPropertyChanged(propertyName);
    }

    private async Task RunTestAsync()
    {
        IsTesting = true;
        Toast = null;
        try
        {
            LastReport = await _services.Diagnostics.RunAsync(_session.Draft).ConfigureAwait(true);
        }
        finally
        {
            IsTesting = false;
        }
    }

    private async Task CommitAsync(bool closeAfter)
    {
        _session.CommitDraft();
        await PersistAsync(_session.Original).ConfigureAwait(true);

        SettingsCommitted?.Invoke(_session.Original);
        Toast = closeAfter ? Loc["settings.toast.saved"] : Loc["settings.toast.applied"];

        if (closeAfter)
        {
            CloseRequested?.Invoke();
        }
    }

    private async Task PersistAsync(AppSettings settings)
    {
        await _services.Settings.SaveAsync(settings).ConfigureAwait(true);

        if (settings.StartWithWindows)
        {
            await _services.Autostart.InstallAsync(Environment.ProcessPath ?? AppContext.BaseDirectory).ConfigureAwait(true);
        }
        else
        {
            await _services.Autostart.UninstallAsync().ConfigureAwait(true);
        }
    }

    private void Cancel()
    {
        _session.DiscardDraft();
        _services.Localization.SetLanguage(_languageOnEntry.Code);
        OnPropertyChanged(null);
        Toast = Loc["settings.toast.cancelled"];
        CloseRequested?.Invoke();
    }
}
