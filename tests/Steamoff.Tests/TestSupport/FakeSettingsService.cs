using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;

namespace Steamoff.Tests.TestSupport;

/// <summary>In-memory <see cref="ISettingsService"/> double — hands back a seeded snapshot and records every save, so self-test/orchestration tests can assert on persisted state without touching disk.</summary>
public sealed class FakeSettingsService : ISettingsService
{
    private AppSettings _current;

    public FakeSettingsService(AppSettings initial) => _current = initial;

    public AppSettings? LastSaved { get; set; }

    public string SettingsFilePath => "fake://settings.json";

    public bool IsUsingFallbackLocation => false;

    public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(_current);

    public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        _current = settings;
        LastSaved = settings;
        return Task.CompletedTask;
    }
}
