using Steamoff.App;
using Steamoff.Core.Models;

namespace Steamoff.Tests.App;

public sealed class StartupVisibilityPolicyTests
{
    [Fact]
    public void FirstLaunch_AlwaysShowsMainWindow_EvenWhenTrayStartupIsEnabled()
    {
        var settings = new AppSettings { StartMinimizedToTray = true };

        Assert.True(global::Steamoff.App.App.ShouldShowMainWindowOnStartup(settings, isFirstLaunch: true, startedFromTrayArgument: true));
    }

    [Fact]
    public void NormalManualLaunch_ShowsMainWindow()
    {
        var settings = new AppSettings { StartMinimizedToTray = true };

        Assert.True(global::Steamoff.App.App.ShouldShowMainWindowOnStartup(settings, isFirstLaunch: false, startedFromTrayArgument: false));
    }

    [Fact]
    public void TrayArgumentLaunch_HidesMainWindow_WhenSettingIsEnabled()
    {
        var settings = new AppSettings { StartMinimizedToTray = true };

        Assert.False(global::Steamoff.App.App.ShouldShowMainWindowOnStartup(settings, isFirstLaunch: false, startedFromTrayArgument: true));
    }
}
