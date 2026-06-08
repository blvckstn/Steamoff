using Steamoff.Core.Enums;
using Steamoff.Core.Interfaces;
using Steamoff.Core.Models;

namespace Steamoff.Infrastructure.Scanning;

/// <summary>
/// Manages the user's "Standalone EXEs" collection. Validates input strictly
/// (must exist, must be .exe, must not be a URL, must not be empty) and never
/// executes the target — only reads its path to build a FirewallTarget.
/// </summary>
public sealed class ExeTargetService : IExeTargetService
{
    public void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Путь к файлу не может быть пустым.", nameof(path));
        }

        var trimmed = path.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            throw new ArgumentException("Путь не может быть URL-адресом.", nameof(path));
        }

        if (!string.Equals(Path.GetExtension(trimmed), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Файл должен иметь расширение .exe.", nameof(path));
        }

        if (!File.Exists(trimmed))
        {
            throw new ArgumentException($"Файл не найден: {trimmed}", nameof(path));
        }
    }

    public ExeBlockTarget Create(string path, string? displayName)
    {
        Validate(path);
        var fullPath = Path.GetFullPath(path.Trim());

        return new ExeBlockTarget
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(fullPath) : displayName.Trim(),
            Path = fullPath,
            Enabled = true,
            AddedAt = DateTimeOffset.UtcNow,
            LastSeenAt = File.Exists(fullPath) ? DateTimeOffset.UtcNow : null,
            Status = ExeStatus.Unblocked
        };
    }

    public FirewallTarget ToFirewallTarget(ExeBlockTarget exe) => new()
    {
        Id = exe.Id,
        DisplayName = exe.Name,
        ExecutablePath = exe.Path,
        Kind = TargetKind.StandaloneExe
    };
}
