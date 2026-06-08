namespace Steamoff.App.Logging;

public enum DisplayLogLevel
{
    Info,
    Warning,
    Error
}

public static class LogLineDisplay
{
    public static DisplayLogLevel DetectLevel(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return DisplayLogLevel.Info;
        }

        if (ContainsAny(line, "[ERROR]", "ERROR", "Exception", "failed", "not found", "access denied",
                "не удалось", "ошиб", "отказано", "не найден"))
        {
            return DisplayLogLevel.Error;
        }

        if (ContainsAny(line, "[WARN]", "[WARNING]", "WARNING", "WARN", "drift", "mismatch",
                "расхожд", "предупреж", "частич", "inconclusive"))
        {
            return DisplayLogLevel.Warning;
        }

        return DisplayLogLevel.Info;
    }

    public static bool MatchesTag(string? line, string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return true;
        }

        var level = DetectLevel(line);
        return tag switch
        {
            "[ERROR]" => level == DisplayLogLevel.Error,
            "[WARN]" or "[WARNING]" => level == DisplayLogLevel.Warning,
            "[INFO]" => level == DisplayLogLevel.Info,
            _ => line?.Contains(tag, StringComparison.OrdinalIgnoreCase) == true
        };
    }

    public static string ToDisplayText(string line)
    {
        var text = line.Trim();
        var separator = text.IndexOf(" :: ", StringComparison.Ordinal);
        if (separator > 0)
        {
            text = text[..separator];
        }

        separator = text.IndexOf(" -- ", StringComparison.Ordinal);
        if (separator > 0)
        {
            text = text[..separator];
        }

        return text.Length <= 150 ? text : text[..147] + "...";
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
