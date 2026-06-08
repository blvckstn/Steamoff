using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Steamoff.Tests.Release;

/// <summary>
/// Covers the release script contract: manifest JSON shape/round-trip,
/// README-RUN.txt content presence, the process-safety path-matching predicate
/// (exercised in isolation via the script's <c>-TestProcessPath</c> self-test
/// hook), and the exit-code-on-failure contract (script-level smoke run
/// against a copy with no <c>Steamoff.slnx</c>).
///
/// <para>
/// These are necessarily script-invocation/file-presence tests rather than
/// pure unit tests — <c>build-release.ps1</c> is PowerShell, not C#, and the
/// brief asks to fix on top of the existing architecture rather than port the
/// pipeline into the app. They run the real script (or its self-test hook) as
/// a subprocess and assert on its observable contract, mirroring how I5 is
/// described ("script-level smoke, run in isolation").
/// </para>
/// </summary>
public sealed class ReleaseScriptTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ScriptPath = Path.Combine(RepoRoot, "build-release.ps1");
    private static readonly string ReleaseRoot = Path.Combine(RepoRoot, "src", "Steamoff.App", "release");

    [Fact]
    public void ReleaseManifest_HasTheContractShape_AndRoundTripsThroughJson()
    {
        var manifestPath = Path.Combine(ReleaseRoot, "release-manifest.json");
        AssertReleaseArtifactExists(manifestPath);

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;

        Assert.Equal("Steamoff", root.GetProperty("appName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("version").GetString()));
        Assert.True(DateTimeOffset.TryParse(root.GetProperty("builtAt").GetString(), out _), "builtAt must be ISO-8601 with offset.");
        Assert.Equal("Release", root.GetProperty("configuration").GetString());
        Assert.Equal("win-x64", root.GetProperty("runtime").GetString());

        var outputs = root.GetProperty("outputs");
        Assert.Equal(2, outputs.GetArrayLength());

        var expected = new[]
        {
            ("Steamoff-with-dotnet-runtime", "self-contained", true, @"Steamoff-with-dotnet-runtime\Steamoff.exe"),
            ("Steamoff-without-dotnet-runtime", "framework-dependent", false, @"Steamoff-without-dotnet-runtime\Steamoff.exe")
        };

        for (var i = 0; i < expected.Length; i++)
        {
            var (name, type, includesRuntime, path) = expected[i];
            var output = outputs[i];

            Assert.Equal(name, output.GetProperty("name").GetString());
            Assert.Equal(type, output.GetProperty("type").GetString());
            Assert.Equal(includesRuntime, output.GetProperty("includesDotnetRuntime").GetBoolean());
            Assert.Equal(path, output.GetProperty("path").GetString());
            Assert.True(output.GetProperty("sizeBytes").GetInt64() > 0);
            Assert.Matches("^[0-9A-F]{64}$", output.GetProperty("sha256").GetString() ?? string.Empty);
        }
    }

    [Theory]
    [InlineData("Steamoff-with-dotnet-runtime", "самодостаточная сборка", "от имени администратора")]
    [InlineData("Steamoff-without-dotnet-runtime", "облегчённая сборка", "от имени администратора")]
    public void ReadmeRun_IsPresent_AndContainsTheVariantSpecificGuidance(string variantFolder, params string[] expectedSubstrings)
    {
        var readmePath = Path.Combine(ReleaseRoot, variantFolder, "README-RUN.txt");
        AssertReleaseArtifactExists(readmePath);

        var content = File.ReadAllText(readmePath);

        Assert.False(string.IsNullOrWhiteSpace(content));
        foreach (var expected in expectedSubstrings)
        {
            Assert.Contains(expected, content, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(@"src\Steamoff.App\release\Steamoff-with-dotnet-runtime\Steamoff.exe", true)]
    [InlineData(@"src\Steamoff.App\bin\Release\net8.0-windows\Steamoff.App.exe", true)]
    [InlineData(@"src\Steamoff.App\publish-2026-01-01\Steamoff.exe", true)]
    [InlineData(@"src\Steamoff.App\publishing-tools\Steamoff.exe", false)]
    [InlineData(@"obj\Release\Steamoff.exe", false)]
    public void ProcessPathGuard_AcceptsOnlyPathsUnderTheThreeManagedTrees(string relativePath, bool expectedManaged)
    {
        var candidate = Path.Combine(RepoRoot, relativePath);

        var result = InvokeScriptSelfTest(candidate);

        Assert.Equal(expectedManaged, result);
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe")]
    [InlineData(@"C:\Program Files (x86)\Steam\bin\steamservice.exe")]
    [InlineData(@"C:\Users\someone\Downloads\Steamoff.exe")]
    public void ProcessPathGuard_RejectsPathsOutsideTheRepo_IncludingAnythingThatLooksLikeSteam(string foreignPath)
    {
        Assert.False(InvokeScriptSelfTest(foreignPath));
    }

    [Fact]
    public void Pipeline_FailsFastWithNonZeroExitAndAnErrorLogLine_WhenNotRunFromTheRepoRoot()
    {
        // "Run in isolation": copy the script into an empty temp directory (no
        // Steamoff.slnx) and invoke it there — step 1 ("verify CWD is the repo
        // root") must reject it before touching anything else, with exit 1.
        var isolationDir = Path.Combine(Path.GetTempPath(), "steamoff-release-script-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolationDir);
        try
        {
            var isolatedScript = Path.Combine(isolationDir, "build-release.ps1");
            File.Copy(ScriptPath, isolatedScript);

            var (exitCode, stdout) = RunPowerShell(isolatedScript, arguments: null, workingDirectory: isolationDir);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("ОШИБКА", stdout);
            Assert.Contains("verify-root", stdout);
            Assert.False(Directory.Exists(Path.Combine(isolationDir, "src")), "A failed verify-root step must not create any release output.");
        }
        finally
        {
            Directory.Delete(isolationDir, recursive: true);
        }
    }

    private static bool InvokeScriptSelfTest(string candidatePath)
    {
        var (exitCode, stdout) = RunPowerShell(ScriptPath, $"-TestProcessPath \"{candidatePath}\"", workingDirectory: RepoRoot);
        Assert.Equal(0, exitCode);

        var trimmed = stdout.Trim();
        return trimmed.Equals("True", StringComparison.OrdinalIgnoreCase);
    }

    private static (int ExitCode, string StdOut) RunPowerShell(string scriptPath, string? arguments, string workingDirectory)
    {
        var argumentList = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"" + (arguments is null ? string.Empty : " " + arguments);

        var psi = new ProcessStartInfo("powershell.exe", argumentList)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout + stderr);
    }

    /// <summary>
    /// Asserts the artifact exists rather than skipping when absent — the brief's
    /// own requirement #5 ("the final build must always be saved to release\")
    /// makes "the artifact is there" itself part of the contract this suite checks,
    /// not an optional precondition. Run <c>.\build-release.ps1</c> once to produce
    /// these artifacts before running this suite in a fresh checkout.
    /// </summary>
    private static void AssertReleaseArtifactExists(string path) =>
        Assert.True(File.Exists(path), $"Expected release artifact missing: {path}. Run .\\build-release.ps1 to produce it (release\\ must always contain the latest build per the project brief).");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Steamoff.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root (Steamoff.slnx) from " + AppContext.BaseDirectory);
    }
}
