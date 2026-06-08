using System.IO;
using Steamoff.Infrastructure.Firewall;
using Steamoff.Tests.TestSupport;

namespace Steamoff.Tests.Infrastructure;

public sealed class FirewallScriptFileWriterTests : IDisposable
{
    private readonly string _baseDirectory;

    public FirewallScriptFileWriterTests()
    {
        _baseDirectory = Path.Combine(Path.GetTempPath(), $"steamoff-scriptwriter-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_baseDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string ScriptPath => Path.Combine(_baseDirectory, "Scripts", "steamoff-firewall.ps1");

    [Fact]
    public async Task EnsureUpToDateAsync_MissingFile_CreatesItWithExpectedContent()
    {
        var writer = new FirewallScriptFileWriter(new FakeLogService(), _baseDirectory);

        var path = await writer.EnsureUpToDateAsync();

        Assert.Equal(ScriptPath, path);
        Assert.True(File.Exists(path));
        var content = await File.ReadAllTextAsync(path);
        Assert.Equal(FirewallScriptFileWriter.ScriptContent, content);
        Assert.Contains("Set-ExecutionPolicy", content, StringComparison.Ordinal);
        Assert.Contains("STEAMOFF_OPERATION", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureUpToDateAsync_FreshAndUpToDate_LeavesFileUntouched()
    {
        var writer = new FirewallScriptFileWriter(new FakeLogService(), _baseDirectory);
        await writer.EnsureUpToDateAsync();
        var firstWriteTime = File.GetLastWriteTimeUtc(ScriptPath);

        await Task.Delay(50);
        await writer.EnsureUpToDateAsync();

        Assert.Equal(firstWriteTime, File.GetLastWriteTimeUtc(ScriptPath));
    }

    [Fact]
    public async Task EnsureUpToDateAsync_StaleOrCorruptedContent_RewritesAtomicallyToExpectedContent()
    {
        var writer = new FirewallScriptFileWriter(new FakeLogService(), _baseDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath)!);
        await File.WriteAllTextAsync(ScriptPath, "# stale content from a previous build, hash will not match");

        var path = await writer.EnsureUpToDateAsync();

        var content = await File.ReadAllTextAsync(path);
        Assert.Equal(FirewallScriptFileWriter.ScriptContent, content);

        // Exactly one canonical file remains — no temp/backup copies left behind (FR-005).
        var entries = Directory.GetFileSystemEntries(Path.GetDirectoryName(ScriptPath)!);
        Assert.Single(entries);
    }

    [Fact]
    public async Task EnsureUpToDateAsync_ReturnsTheCanonicalFixedPath()
    {
        var writer = new FirewallScriptFileWriter(new FakeLogService(), _baseDirectory);

        var first = await writer.EnsureUpToDateAsync();
        var second = await writer.EnsureUpToDateAsync();

        Assert.Equal(first, second);
        Assert.Equal(ScriptPath, first);
    }
}
