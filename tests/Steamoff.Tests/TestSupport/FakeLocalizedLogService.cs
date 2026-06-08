using Steamoff.Core.Interfaces;
using Steamoff.Core.Logging;

namespace Steamoff.Tests.TestSupport;

/// <summary>In-memory <see cref="ILocalizedLogService"/> double — records every journal call instead of formatting/writing it.</summary>
public sealed class FakeLocalizedLogService : ILocalizedLogService
{
    public sealed record Entry(LogEventKey Key, object[] Args);

    public List<Entry> Entries { get; } = new();

    public Task LogAsync(LogEventKey key, params object[] args)
    {
        Entries.Add(new Entry(key, args));
        return Task.CompletedTask;
    }

    public bool Contains(LogEventKey key) => Entries.Any(e => e.Key == key);

    public int CountOf(LogEventKey key) => Entries.Count(e => e.Key == key);
}
