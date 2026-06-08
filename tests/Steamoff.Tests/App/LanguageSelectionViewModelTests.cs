using Steamoff.App.ViewModels;
using Steamoff.Core.Localization;
using Steamoff.Tests.TestSupport;

namespace Steamoff.Tests.App;

/// <summary>
/// Drives the first-launch "Your language" dialog's view model: selecting a
/// card previews the language live (Constitution VI — instant redraw, no
/// "Apply" needed to see the effect), and confirming raises the event the host
/// window turns into the persisted choice + isFirstLaunchCompleted = true.
/// </summary>
public sealed class LanguageSelectionViewModelTests
{
    [Fact]
    public void InitialSelection_MatchesTheServicesCurrentLanguage()
    {
        var localization = new FakeLocalizationService();
        var viewModel = new LanguageSelectionViewModel(localization);

        Assert.Equal(localization.CurrentLanguage.Code, viewModel.SelectedLanguage.Code);
    }

    [Fact]
    public void Languages_ListsEveryAvailableLanguage_InOrder()
    {
        var localization = new FakeLocalizationService();
        var viewModel = new LanguageSelectionViewModel(localization);

        Assert.Equal(localization.AvailableLanguages.Select(l => l.Code), viewModel.Languages.Select(l => l.Code));
    }

    [Fact]
    public void SelectingALanguage_PreviewsItLive_ByCallingSetLanguageImmediately()
    {
        var localization = new FakeLocalizationService();
        var viewModel = new LanguageSelectionViewModel(localization);

        var german = viewModel.Languages.Single(l => l.Code == "de");
        viewModel.SelectCommand.Execute(german);

        Assert.Equal("de", viewModel.SelectedLanguage.Code);
        Assert.Equal("de", localization.CurrentLanguage.Code);
        Assert.True(localization.SetLanguageCallCount > 0);
    }

    [Fact]
    public void SelectingTheSameLanguageTwice_DoesNotReRaiseSelectionChange()
    {
        var localization = new FakeLocalizationService();
        var viewModel = new LanguageSelectionViewModel(localization);

        var raiseCount = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LanguageSelectionViewModel.SelectedLanguage))
            {
                raiseCount++;
            }
        };

        var current = viewModel.SelectedLanguage;
        viewModel.SelectCommand.Execute(current);

        Assert.Equal(0, raiseCount);
    }

    [Fact]
    public void Confirm_RaisesConfirmed_WithTheCurrentlySelectedLanguage()
    {
        var localization = new FakeLocalizationService();
        var viewModel = new LanguageSelectionViewModel(localization);

        Steamoff.Core.Models.AppLanguage? confirmed = null;
        viewModel.Confirmed += language => confirmed = language;

        var polish = viewModel.Languages.Single(l => l.Code == "pl");
        viewModel.SelectCommand.Execute(polish);
        viewModel.ConfirmCommand.Execute(null);

        Assert.NotNull(confirmed);
        Assert.Equal("pl", confirmed!.Code);
    }
}
