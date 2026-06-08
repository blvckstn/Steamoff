using Steamoff.Infrastructure.Paths;

namespace Steamoff.Tests.Infrastructure;

/// <summary>
/// Pure-function contract for <see cref="PathNormalizationService"/> — every
/// raw-input shape named in spec section 4 (quotes, env vars, slashes,
/// duplicated separators, whitespace, UNC paths) collapses to one clean,
/// </summary>
public sealed class PathNormalizationServiceTests
{
    private readonly PathNormalizationService _service = new();

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        Assert.Equal(@"C:\Games\Steam", _service.NormalizeRawPath("  C:\\Games\\Steam  "));
    }

    [Fact]
    public void StripsASingleMatchingPairOfQuotes()
    {
        Assert.Equal(@"C:\Games\Steam", _service.NormalizeRawPath("\"C:\\Games\\Steam\""));
    }

    [Fact]
    public void ConvertsForwardSlashesToBackslashes()
    {
        Assert.Equal(@"C:\Games\Steam", _service.NormalizeRawPath("C:/Games/Steam"));
    }

    [Fact]
    public void CollapsesDuplicatedBackslashes()
    {
        Assert.Equal(@"C:\Games\Steam\steam.exe", _service.NormalizeRawPath(@"C:\Games\\Steam\\\steam.exe"));
    }

    [Fact]
    public void ExpandsEnvironmentVariables()
    {
        var expected = Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%") + @"\Steam";

        Assert.Equal(expected, _service.NormalizeRawPath(@"%ProgramFiles(x86)%\Steam"));
    }

    [Fact]
    public void PreservesLeadingDoubleBackslash_ForUncPaths()
    {
        Assert.Equal(@"\\NAS\Games\Steam", _service.NormalizeRawPath(@"\\NAS\Games\\Steam"));
    }

    [Fact]
    public void IsIdempotent()
    {
        const string raw = "  \"%ProgramFiles(x86)%/Steam//bin\"  ";

        var once = _service.NormalizeRawPath(raw);
        var twice = _service.NormalizeRawPath(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void NeverThrows_OnEmptyOrWhitespaceInput()
    {
        Assert.Equal(string.Empty, _service.NormalizeRawPath(string.Empty));
        Assert.Equal(string.Empty, _service.NormalizeRawPath("   "));
    }
}
