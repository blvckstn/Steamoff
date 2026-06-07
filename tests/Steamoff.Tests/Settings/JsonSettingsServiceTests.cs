using System.IO;
using Steamoff.Core.Models;
using Steamoff.Infrastructure.Settings;
using Steamoff.Tests.TestSupport;

namespace Steamoff.Tests.Settings;

/// <summary>
/// Exercises persistence, first-launch defaults, and v1 -> v2 migration through
/// <see cref="JsonSettingsService"/>'s internal test seam (a temp directory in
/// place of %ProgramData%/%AppData%) — see <c>InternalsVisibleTo</c> in
/// Steamoff.Infrastructure/AssemblyInfo.cs.
/// </summary>
public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string _directory;

    public JsonSettingsServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"SteamoffTests-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private JsonSettingsService CreateService() => new(new FakeLogService(), _directory, usingFallback: false);

    [Fact]
    public async Task LoadAsync_WhenNoFileExists_CreatesDefaults_WithRussianLanguage_AndFirstLaunchNotCompleted()
    {
        var service = CreateService();

        var settings = await service.LoadAsync();

        Assert.Equal("ru", settings.Language);
        Assert.False(settings.IsFirstLaunchCompleted);
        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.True(File.Exists(service.SettingsFilePath));
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsLanguageAndFirstLaunchFlag()
    {
        var service = CreateService();
        var settings = AppSettings.CreateDefault();
        settings.Language = "de";
        settings.IsFirstLaunchCompleted = true;

        await service.SaveAsync(settings);
        var reloaded = await service.LoadAsync();

        Assert.Equal("de", reloaded.Language);
        Assert.True(reloaded.IsFirstLaunchCompleted);
    }

    [Fact]
    public async Task LoadAsync_MigratesV1Settings_BumpsVersion_AndFillsLanguageFieldsWithDefaults()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");

        // A v1 settings.json predates the language/first-launch fields entirely.
        await File.WriteAllTextAsync(path, """
        {
          "version": 1,
          "desiredState": "unblocked",
          "enforcementMode": "manualToggle"
        }
        """);

        var service = CreateService();
        var settings = await service.LoadAsync();

        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.Equal("ru", settings.Language);
        Assert.False(settings.IsFirstLaunchCompleted);
    }

    [Fact]
    public async Task LoadAsync_OnAlreadyCurrentVersion_DoesNotRewriteUnrelatedFields()
    {
        var service = CreateService();
        var settings = AppSettings.CreateDefault();
        settings.Language = "pl";
        settings.IsFirstLaunchCompleted = true;
        settings.SteamPath = @"C:\Games\Steam";

        await service.SaveAsync(settings);
        var reloaded = await service.LoadAsync();

        Assert.Equal(AppSettings.CurrentVersion, reloaded.Version);
        Assert.Equal("pl", reloaded.Language);
        Assert.Equal(@"C:\Games\Steam", reloaded.SteamPath);
    }
}
