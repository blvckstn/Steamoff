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

    /// <summary>Builds the structured ~18-field snapshot behind the localized extended report (FR — diagnostics must display in the selected/runtime language).</summary>
    Task<DiagnosticsSnapshot> BuildSnapshotAsync(CancellationToken ct = default);

    /// <summary>Fully localized text report rendered from <see cref="BuildSnapshotAsync"/> — extends/replaces <see cref="ILogService.BuildDiagnosticsReportAsync"/> for the Settings journal / Compact "Copy diagnostics" actions.</summary>
    Task<string> BuildExtendedReportAsync(CancellationToken ct = default);
}
