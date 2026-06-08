using System.ComponentModel;
using Steamoff.App.Localization;
using Steamoff.Tests.TestSupport;

namespace Steamoff.Tests.App;

/// <summary>
/// <see cref="LocalizationProxy"/> is the bridge that lets every XAML binding of
/// the form <c>{Binding [key], Source={StaticResource Loc}}</c> redraw instantly
/// on a language switch — these tests prove the indexer reads through to the
/// service and that the WPF indexer-name change notification actually fires.
/// </summary>
public sealed class LocalizationProxyTests
{
    [Fact]
    public void Indexer_ReadsThrough_ToTheUnderlyingService()
    {
        var localization = new FakeLocalizationService();
        var proxy = new LocalizationProxy(localization);

        Assert.Equal(localization.GetString("app.title"), proxy["app.title"]);
    }

    [Fact]
    public void GetFormatted_ReadsThrough_ToTheUnderlyingService()
    {
        var localization = new FakeLocalizationService();
        var proxy = new LocalizationProxy(localization);

        Assert.Equal(localization.GetString("compact.modeLabel", "Manual"), proxy.GetFormatted("compact.modeLabel", "Manual"));
    }

    [Fact]
    public void LanguageChange_RaisesIndexerPropertyChanged_SoBindingsRefresh()
    {
        var localization = new FakeLocalizationService();
        var proxy = new LocalizationProxy(localization);

        var raisedPropertyNames = new List<string?>();
        PropertyChangedEventHandler handler = (_, e) => raisedPropertyNames.Add(e.PropertyName);
        ((INotifyPropertyChanged)proxy).PropertyChanged += handler;

        localization.SetLanguage("en");

        Assert.Contains("Item[]", raisedPropertyNames);
    }

    [Fact]
    public void Indexer_ReflectsNewLanguage_ImmediatelyAfterSwitch()
    {
        var localization = new FakeLocalizationService();
        var proxy = new LocalizationProxy(localization);

        var before = proxy["app.title"];
        localization.SetLanguage("en");
        var after = proxy["app.title"];

        Assert.NotEqual(before, after);
        Assert.StartsWith("[en]", after, StringComparison.Ordinal);
    }
}
