namespace Steamoff.Core.Models;

/// <summary>
/// One supported interface language: a stable code used for persistence and
/// resource lookup, the short code shown in compact UI (e.g. "EN", never "GB"),
/// the language's native display name, and a flag emoji for language pickers.
/// </summary>
public sealed class AppLanguage
{
    public required string Code { get; init; }
    public required string DisplayCode { get; init; }
    public required string NativeName { get; init; }
    public required string FlagEmoji { get; init; }

    public override string ToString() => $"{FlagEmoji} {DisplayCode} {NativeName}";
}
