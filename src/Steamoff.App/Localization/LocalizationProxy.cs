using System.ComponentModel;
using Steamoff.Core.Interfaces;
using Binding = System.Windows.Data.Binding;

namespace Steamoff.App.Localization;

/// <summary>
/// XAML-friendly facade over <see cref="ILocalizationService"/>: exposes translated
/// strings through an indexer so views can bind with <c>{Binding [key], Source={StaticResource Loc}}</c>.
/// Raising PropertyChanged for the WPF indexer name ("Item[]") on every language
/// switch makes every such binding re-evaluate immediately — the mechanism behind
/// "change language without restarting the app".
/// </summary>
public sealed class LocalizationProxy : INotifyPropertyChanged
{
    private readonly ILocalizationService _service;

    public LocalizationProxy(ILocalizationService service)
    {
        _service = service;
        _service.LanguageChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }

    public string this[string key] => _service.GetString(key);

    /// <summary>Formats a localized template (e.g. "Mode: {0}") with the given arguments.</summary>
    public string GetFormatted(string key, params object[] args) => _service.GetString(key, args);

    public event PropertyChangedEventHandler? PropertyChanged;
}
