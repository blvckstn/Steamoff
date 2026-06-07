using System.Reflection;
using System.Text.Json;

namespace Steamoff.Core.Localization;

/// <summary>
/// Loads the flat key→string translation tables that ship as embedded JSON
/// resources (Resources/Localization/*.json, embedded under the
/// "Steamoff.Core.Resources.Localization.{code}.json" logical name) and
/// caches them per language code. Embedding keeps the published single-file
/// EXE self-contained — no loose files to lose track of (see ASSUMPTIONS).
/// </summary>
public sealed class LocalizedStringProvider
{
    private const string ResourceNamespace = "Steamoff.Core.Resources.Localization";

    private readonly Assembly _assembly;
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public LocalizedStringProvider(Assembly? assembly = null)
    {
        _assembly = assembly ?? typeof(LocalizedStringProvider).Assembly;
    }

    /// <summary>Returns the translation table for <paramref name="languageCode"/>, or an empty table if no resource exists for it.</summary>
    public IReadOnlyDictionary<string, string> GetTable(string languageCode)
    {
        var code = languageCode.ToLowerInvariant();
        if (_cache.TryGetValue(code, out var cached))
        {
            return cached;
        }

        var table = LoadTable(code);
        _cache[code] = table;
        return table;
    }

    private IReadOnlyDictionary<string, string> LoadTable(string code)
    {
        var resourceName = $"{ResourceNamespace}.{code}.json";
        using var stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var table = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return table is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(table, StringComparer.Ordinal);
    }
}
