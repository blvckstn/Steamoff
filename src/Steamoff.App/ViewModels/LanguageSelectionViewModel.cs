using System.Collections.ObjectModel;
using System.Windows.Input;
using Steamoff.App.Localization;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;
using Steamoff.Core.Mvvm;

namespace Steamoff.App.ViewModels;

/// <summary>
/// Drives the first-launch "Your language" dialog: a grid of language cards
/// (flag + code + native name), live-previewed selection, and a single confirm
/// action that persists the choice and marks first launch as completed.
/// </summary>
public sealed class LanguageSelectionViewModel : ObservableObject
{
    private readonly ILocalizationService _localization;
    private AppLanguage _selectedLanguage;

    public LanguageSelectionViewModel(ILocalizationService localization)
    {
        _localization = localization;
        _selectedLanguage = localization.CurrentLanguage;

        Languages = new ObservableCollection<AppLanguage>(localization.AvailableLanguages);
        Loc = new LocalizationProxy(localization);

        SelectCommand = new RelayCommand(p =>
        {
            if (p is AppLanguage language)
            {
                SelectedLanguage = language;
            }
        });

        ConfirmCommand = new RelayCommand(() => Confirmed?.Invoke(SelectedLanguage));
    }

    /// <summary>Raised when the user confirms a choice (or the host treats a dismissal as an implicit RU confirmation).</summary>
    public event Action<AppLanguage>? Confirmed;

    public LocalizationProxy Loc { get; }

    public ObservableCollection<AppLanguage> Languages { get; }

    public ICommand SelectCommand { get; }
    public ICommand ConfirmCommand { get; }

    public AppLanguage SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || ReferenceEquals(_selectedLanguage, value))
            {
                return;
            }

            _selectedLanguage = value;
            _localization.SetLanguage(value.Code);
            OnPropertyChanged();
        }
    }
}
