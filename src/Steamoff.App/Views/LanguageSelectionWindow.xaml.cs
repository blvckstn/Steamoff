using System.Windows;
using Steamoff.App.Tray;
using Steamoff.App.ViewModels;
using Steamoff.Core.Localization;
using Steamoff.Core.Models;

namespace Steamoff.App.Views;

/// <summary>
/// First-launch "Your language" picker — a styled, resizable, neumorphic dialog
/// (never a stock MessageBox). Closing without confirming is treated by the
/// caller as an implicit choice of the fallback language (Russian).
/// </summary>
public partial class LanguageSelectionWindow : Window
{
    private readonly LanguageSelectionViewModel _viewModel;
    private bool _confirmed;

    public LanguageSelectionWindow(LanguageSelectionViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        WindowChromeHelper.ApplyDarkTitleBar(this);
        viewModel.Confirmed += OnConfirmed;
    }

    /// <summary>The language the user ended up with — confirmed choice, or the fallback if dismissed.</summary>
    public AppLanguage Result { get; private set; } = LanguageManager.Fallback;

    private void OnConfirmed(AppLanguage language)
    {
        _confirmed = true;
        Result = language;
        DialogResult = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Confirmed -= OnConfirmed;

        if (!_confirmed)
        {
            Result = LanguageManager.Fallback;
        }

        base.OnClosed(e);
    }
}
