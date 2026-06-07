using System.Text.Json;
using System.Text.Json.Serialization;

namespace Steamoff.Core.Models;

/// <summary>
/// Backs the Settings View's Apply/Save/Cancel flow. <see cref="Original"/> is
/// the last-saved settings snapshot; <see cref="Draft"/> is a deep clone the
/// UI mutates freely. Apply/Save copy Draft back into the saved settings (and
/// reset Original to match); Cancel discards Draft and restores from Original —
/// including rolling back any language change that was previewed live.
/// </summary>
public sealed class SettingsEditSession
{
    private static readonly JsonSerializerOptions CloneOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public AppSettings Original { get; private set; }
    public AppSettings Draft { get; private set; }

    public SettingsEditSession(AppSettings savedSettings)
    {
        Original = Clone(savedSettings);
        Draft = Clone(savedSettings);
    }

    /// <summary>True if any field of <see cref="Draft"/> differs from <see cref="Original"/>.</summary>
    public bool HasChanges => !JsonSerializer.Serialize(Draft, CloneOptions).Equals(
        JsonSerializer.Serialize(Original, CloneOptions), StringComparison.Ordinal);

    /// <summary>Commits Draft as the new baseline (used by both Apply and Save — they differ only in whether the view then closes).</summary>
    public void CommitDraft()
    {
        Original = Clone(Draft);
        Draft = Clone(Draft);
    }

    /// <summary>Discards Draft and restores it from Original, undoing every pending change including any previewed language switch.</summary>
    public void DiscardDraft()
    {
        Draft = Clone(Original);
    }

    private static AppSettings Clone(AppSettings source)
    {
        var json = JsonSerializer.Serialize(source, CloneOptions);
        return JsonSerializer.Deserialize<AppSettings>(json, CloneOptions)
               ?? throw new InvalidOperationException("Не удалось клонировать настройки для сессии редактирования.");
    }
}
