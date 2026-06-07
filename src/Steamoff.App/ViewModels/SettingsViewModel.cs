using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Steamoff.App.Localization;
using Steamoff.App.Services;
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
    private readonly IDialogService _dialogs;
    private readonly AppLanguage _languageOnEntry;
    private SettingsEditSession _session;
    private DiagnosticsReport _lastReport = DiagnosticsReport.NotRunYet;
    private bool _isTesting;
    private bool _isDiscoveringSteamPath;
    private string? _toast;
    private SteamPathCheckResult _steamPathCheck = SteamPathCheckResult.Empty;

    public SettingsViewModel(AppServices services, AppSettings savedSettings, IDialogService? dialogs = null)
    {
        _services = services;
        _dialogs = dialogs ?? services.Dialogs;
        _session = new SettingsEditSession(savedSettings);
        _languageOnEntry = services.Localization.CurrentLanguage;
        Loc = new LocalizationProxy(services.Localization);

        Languages = new ObservableCollection<AppLanguage>(services.Localization.AvailableLanguages);
        Folders = new ObservableCollection<FolderBlockTarget>(_session.Draft.AdditionalFolders);
        Executables = new ObservableCollection<ExeBlockTarget>(_session.Draft.AdditionalExecutables);

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

        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync);
        RemoveFolderCommand = new RelayCommand(p => RemoveFolder(p as FolderBlockTarget));
        RescanFolderCommand = new AsyncRelayCommand(p => RescanFolderAsync(p as FolderBlockTarget));
        OpenFolderLocationCommand = new RelayCommand(p => OpenInExplorer((p as FolderBlockTarget)?.Path));

        AddExeCommand = new AsyncRelayCommand(AddExeAsync);
        RemoveExeCommand = new RelayCommand(p => RemoveExe(p as ExeBlockTarget));
        OpenExeLocationCommand = new RelayCommand(p => OpenInExplorer((p as ExeBlockTarget)?.Path, selectFile: true));
        CheckExeStatusCommand = new RelayCommand(p => CheckExeStatus(p as ExeBlockTarget));

        AutoFindSteamCommand = new AsyncRelayCommand(AutoFindSteamAsync, () => !IsDiscoveringSteamPath);
        BrowseSteamFolderCommand = new RelayCommand(BrowseSteamFolder);

        InitializeSteamPathCheck();
        _services.Localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, AppLanguage language)
    {
        OnPropertyChanged(nameof(StatusSummaryText));
        OnPropertyChanged(nameof(LastRunText));
        OnPropertyChanged(nameof(SteamPathStatusText));
    }

    public void Dispose()
    {
        _services.Localization.LanguageChanged -= OnLanguageChanged;
    }

    public event Action? CloseRequested;
    public event Action<AppSettings>? SettingsCommitted;

    public LocalizationProxy Loc { get; }

    public ObservableCollection<AppLanguage> Languages { get; }
    public ObservableCollection<FolderBlockTarget> Folders { get; }
    public ObservableCollection<ExeBlockTarget> Executables { get; }

    public ICommand SelectLanguageCommand { get; }
    public ICommand TestCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public ICommand AddFolderCommand { get; }
    public ICommand RemoveFolderCommand { get; }
    public ICommand RescanFolderCommand { get; }
    public ICommand OpenFolderLocationCommand { get; }

    public ICommand AddExeCommand { get; }
    public ICommand RemoveExeCommand { get; }
    public ICommand OpenExeLocationCommand { get; }
    public ICommand CheckExeStatusCommand { get; }

    public ICommand AutoFindSteamCommand { get; }
    public ICommand BrowseSteamFolderCommand { get; }

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

    /// <summary>
    /// The Steam-path text shown/edited in the Settings field. Reading returns the
    /// raw draft value (so the user can fix a typo without it being clobbered);
    /// writing re-runs <see cref="ISteamPathValidator"/> on every change (spec
    /// section 4: "validation triggers: on input, focus-lost, drag&amp;drop, dialog
    /// pick, auto-discovery") and, when the candidate resolves to a valid Steam
    /// installation, normalizes the persisted value to the *folder* (never steam.exe).
    /// </summary>
    public string? SteamPath
    {
        get => _session.Draft.SteamPath;
        set => ApplySteamPathCandidate(value);
    }

    public SteamPathCheckResult SteamPathCheck
    {
        get => _steamPathCheck;
        private set
        {
            if (SetProperty(ref _steamPathCheck, value))
            {
                OnPropertyChanged(nameof(SteamPathStatus));
                OnPropertyChanged(nameof(SteamPathStatusText));
                OnPropertyChanged(nameof(IsSteamPathValid));
            }
        }
    }

    public PathCheckStatus SteamPathStatus => _steamPathCheck.Status;

    public string SteamPathStatusText => Loc[_steamPathCheck.StatusMessageKey];

    public bool IsSteamPathValid => _steamPathCheck.IsValid;

    public bool IsDiscoveringSteamPath
    {
        get => _isDiscoveringSteamPath;
        private set
        {
            if (SetProperty(ref _isDiscoveringSteamPath, value))
            {
                ((AsyncRelayCommand)AutoFindSteamCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public EnforcementMode EnforcementMode
    {
        get => _session.Draft.EnforcementMode;
        set => SetDraft(value, v => _session.Draft.EnforcementMode = v, _session.Draft.EnforcementMode);
    }

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

    // ===================== Additional Folders =====================

    private async Task AddFolderAsync()
    {
        var picked = _dialogs.PickFolder(Loc["settings.button.browseFolder"]);
        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        await AddFolderFromPathAsync(picked).ConfigureAwait(true);
    }

    /// <summary>Shared by the Add-Folder dialog and drag&amp;drop — normalizes, validates, and adds to the draft.</summary>
    public async Task AddFolderFromPathAsync(string rawPath)
    {
        var normalized = _services.PathNormalization.NormalizeRawPath(rawPath);

        if (string.Equals(Path.GetExtension(normalized), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = _services.SteamPathValidator.Validate(normalized);
            if (resolved.NormalizedFolderPath is not null)
            {
                normalized = resolved.NormalizedFolderPath;
            }
        }

        if (File.Exists(normalized))
        {
            normalized = Path.GetDirectoryName(normalized) ?? normalized;
        }

        if (!Directory.Exists(normalized))
        {
            Toast = Loc["settings.dialog.folderNotFound"];
            return;
        }

        if (_session.Draft.AdditionalFolders.Any(f => string.Equals(f.Path, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        try
        {
            var folder = await _services.FolderTargets.AddFolderAsync(normalized, displayName: null, recursive: true).ConfigureAwait(true);
            _session.Draft.AdditionalFolders.Add(folder);
            Folders.Add(folder);
            OnPropertyChanged(nameof(SteamPath));
        }
        catch (ArgumentException ex)
        {
            Toast = ex.Message;
        }
    }

    private void RemoveFolder(FolderBlockTarget? folder)
    {
        if (folder is null)
        {
            return;
        }

        _services.FolderTargets.RemoveFolder(folder.Id);
        _session.Draft.AdditionalFolders.RemoveAll(f => f.Id == folder.Id);
        Folders.Remove(folder);
    }

    private async Task RescanFolderAsync(FolderBlockTarget? folder)
    {
        if (folder is null)
        {
            return;
        }

        await _services.FolderTargets.RescanAsync(folder).ConfigureAwait(true);
        var index = Folders.IndexOf(folder);
        if (index >= 0)
        {
            Folders[index] = folder;
        }
    }

    // ===================== Standalone EXE files =====================

    private async Task AddExeAsync()
    {
        var picked = _dialogs.PickExecutableFile(Loc["settings.button.addExe"]);
        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        await AddExeFromPathAsync(picked).ConfigureAwait(true);
    }

    /// <summary>Shared by the Add-EXE dialog and drag&amp;drop — resolves shortcuts, validates, and adds to the draft.</summary>
    public async Task AddExeFromPathAsync(string rawPath)
    {
        var normalized = _services.PathNormalization.NormalizeRawPath(rawPath);

        if (string.Equals(Path.GetExtension(normalized), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = _services.SteamPathValidator.Validate(normalized);
            if (resolved.SteamExePath is not null)
            {
                normalized = resolved.SteamExePath;
            }
        }

        if (!string.Equals(Path.GetExtension(normalized), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            Toast = Loc["settings.dialog.notAnExe"];
            return;
        }

        if (_session.Draft.AdditionalExecutables.Any(e => string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        try
        {
            var exe = _services.ExeTargets.Create(normalized, displayName: null);
            _session.Draft.AdditionalExecutables.Add(exe);
            Executables.Add(exe);
            await Task.CompletedTask;
        }
        catch (ArgumentException ex)
        {
            Toast = ex.Message;
        }
    }

    private void RemoveExe(ExeBlockTarget? exe)
    {
        if (exe is null)
        {
            return;
        }

        _session.Draft.AdditionalExecutables.RemoveAll(e => e.Id == exe.Id);
        Executables.Remove(exe);
    }

    private void CheckExeStatus(ExeBlockTarget? exe)
    {
        if (exe is null)
        {
            return;
        }

        exe.LastSeenAt = File.Exists(exe.Path) ? DateTimeOffset.UtcNow : exe.LastSeenAt;
        exe.Status = File.Exists(exe.Path)
            ? (exe.Enabled ? ExeStatus.MissingRule : ExeStatus.Disabled)
            : ExeStatus.FileNotFound;

        var index = Executables.IndexOf(exe);
        if (index >= 0)
        {
            Executables[index] = exe;
        }
    }

    private static void OpenInExplorer(string? path, bool selectFile = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (selectFile && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return;
        }

        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        else if (File.Exists(path))
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            }
        }
    }

    // ===================== Steam path: discovery, browsing, validation =====================

    /// <summary>
    /// Runs on Settings construction (covers "auto-discovery on settings-open" —
    /// spec section 3): if the saved path is empty/invalid, attempts auto-discovery
    /// and stages the result in the draft (never overwrites a path the user already
    /// trusts and that still validates).
    /// </summary>
    private void InitializeSteamPathCheck()
    {
        var current = _session.Draft.SteamPath;
        if (!string.IsNullOrWhiteSpace(current))
        {
            var existing = _services.SteamPathValidator.Validate(current);
            SteamPathCheck = existing;
            if (existing.IsValid && existing.NormalizedFolderPath is not null)
            {
                _session.Draft.SteamPath = existing.NormalizedFolderPath;
            }

            if (existing.IsValid)
            {
                return;
            }
        }
        else
        {
            SteamPathCheck = SteamPathCheckResult.Empty;
        }

        _ = AutoFindSteamAsync();
    }

    private async Task AutoFindSteamAsync()
    {
        IsDiscoveringSteamPath = true;
        try
        {
            var installation = await _services.SteamDiscovery.DiscoverAsync().ConfigureAwait(true);
            var result = _services.SteamPathValidator.FromInstallation(installation);
            SteamPathCheck = result;

            if (result.IsValid && result.NormalizedFolderPath is not null)
            {
                _session.Draft.SteamPath = result.NormalizedFolderPath;
                OnPropertyChanged(nameof(SteamPath));
                Toast = Loc["settings.steamPath.found"];
            }
            else
            {
                Toast = Loc["settings.steamPath.notFoundAuto"];
            }
        }
        finally
        {
            IsDiscoveringSteamPath = false;
        }
    }

    private void BrowseSteamFolder()
    {
        var initial = Directory.Exists(_session.Draft.SteamPath) ? _session.Draft.SteamPath : null;
        var picked = _dialogs.PickFolder(Loc["settings.steamPath.selectFolder"], initial);
        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        ApplySteamPathCandidate(picked);
    }

    /// <summary>
    /// Central entry point for every Steam-path validation trigger (typed input,
    /// focus-lost re-check, drag&amp;drop, dialog pick, this method is also what
    /// drag&amp;drop in the View should call). Normalizes valid candidates to the
    /// Steam *folder* before persisting — never an .exe path (spec section 4).
    /// </summary>
    public void ApplySteamPathCandidate(string? rawValue)
    {
        var trimmed = rawValue?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            _session.Draft.SteamPath = null;
            SteamPathCheck = SteamPathCheckResult.Empty;
            OnPropertyChanged(nameof(SteamPath));
            return;
        }

        if (string.Equals(trimmed, _session.Draft.SteamPath, StringComparison.Ordinal) && _steamPathCheck.Status != PathCheckStatus.Empty)
        {
            return;
        }

        var result = _services.SteamPathValidator.Validate(trimmed);
        SteamPathCheck = result;
        _session.Draft.SteamPath = result.IsValid && result.NormalizedFolderPath is not null
            ? result.NormalizedFolderPath
            : trimmed;

        OnPropertyChanged(nameof(SteamPath));
    }

    /// <summary>Re-runs validation against the current draft value — wired to the Steam-path field's LostFocus in the View (spec section 4: "validation triggers... focus-lost").</summary>
    public void RevalidateSteamPath()
    {
        if (!string.IsNullOrWhiteSpace(_session.Draft.SteamPath))
        {
            SteamPathCheck = _services.SteamPathValidator.Validate(_session.Draft.SteamPath);
        }
    }
}
