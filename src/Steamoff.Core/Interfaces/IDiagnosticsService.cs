using Steamoff.Core.Models;

namespace Steamoff.Core.Interfaces;

/// <summary>
/// Backs the Settings View "Тестирование"/"Статус" buttons: runs a battery of
/// read-only checks against the current (draft) configuration and produces a
/// human-readable report. Never mutates firewall rules or files — diagnostics
/// only ever read.
/// </summary>
public interface IDiagnosticsService
{
    Task<DiagnosticsReport> RunAsync(AppSettings settings, CancellationToken ct = default);
}
