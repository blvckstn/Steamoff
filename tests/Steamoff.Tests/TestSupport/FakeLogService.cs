using Steamoff.Core.Interfaces;

namespace Steamoff.Tests.TestSupport;

/// <summary>In-memory <see cref="ILogService"/> double — captures messages instead of touching disk.</summary>
public sealed class FakeLogService : ILogService
{
    public List<string> InfoMessages { get; } = new();
    public List<string> WarningMessages { get; } = new();
    public List<string> ErrorMessages { get; } = new();

    public void Info(string message) => InfoMessages.Add(message);
    public void Warning(string message) => WarningMessages.Add(message);
    public void Error(string message, Exception? exception = null) => ErrorMessages.Add(message);

    public string LogFilePath => "fake://steamoff.log";

    public Task<IReadOnlyList<string>> ReadLastLinesAsync(int count, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public Task<string> BuildDiagnosticsReportAsync(CancellationToken ct = default) =>
        Task.FromResult(string.Empty);
}
