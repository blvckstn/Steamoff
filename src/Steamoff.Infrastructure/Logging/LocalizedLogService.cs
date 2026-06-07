using Steamoff.Core.Interfaces;
using Steamoff.Core.Logging;

namespace Steamoff.Infrastructure.Logging;

/// <summary>
/// Composes <see cref="ILocalizationService"/> + <see cref="ILogService"/>:
/// resolves the `log.event.*` template for a <see cref="LogEventKey"/> in the
/// current runtime language, formats it with the supplied arguments, and
/// dispatches to the matching <see cref="ILogService"/> write method for the
/// severity declared in <see cref="LogEventTemplates"/>. `ILogService` exposes
/// only synchronous `Info`/`Warning`/`Error` writers (no generic async
/// `WriteAsync`), so the write itself is synchronous; the method stays `Task`
/// so call sites can `await` consistently with the rest of the async surface.
/// </summary>
public sealed class LocalizedLogService : ILocalizedLogService
{
    private readonly ILogService _log;
    private readonly ILocalizationService _localization;

    public LocalizedLogService(ILogService log, ILocalizationService localization)
    {
        _log = log;
        _localization = localization;
    }

    public Task LogAsync(LogEventKey key, params object[] args)
    {
        var localizationKey = LogEventTemplates.LocalizationKeyFor(key);
        var message = args.Length == 0
            ? _localization.GetString(localizationKey)
            : _localization.GetString(localizationKey, args);

        switch (LogEventTemplates.LevelFor(key))
        {
            case LogLevel.Warning:
                _log.Warning(message);
                break;
            case LogLevel.Error:
                _log.Error(message);
                break;
            default:
                _log.Info(message);
                break;
        }

        return Task.CompletedTask;
    }
}
