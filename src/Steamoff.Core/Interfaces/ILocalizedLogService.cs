using Steamoff.Core.Logging;

namespace Steamoff.Core.Interfaces;

/// <summary>
/// Writes named, localized journal entries — formats the current-language
/// string for <paramref name="key"/> (via <see cref="ILocalizationService"/>)
/// with the supplied arguments and appends it to <see cref="ILogService"/> at
/// the severity declared in <see cref="LogEventTemplates"/>.
/// </summary>
public interface ILocalizedLogService
{
    Task LogAsync(LogEventKey key, params object[] args);
}
