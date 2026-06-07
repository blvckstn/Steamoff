using System.IO;
using Steamoff.Core.Enums;
using Steamoff.Core.Models;
using Steamoff.Infrastructure.Paths;

namespace Steamoff.Tests.Infrastructure;

/// <summary>
/// Exercises the full normalize → resolve-shortcut → file-or-folder
/// resolution chain documented in contracts/path-normalization.md, using a
/// real temp directory tree (no registry/COM access) and a fake shortcut
/// resolver delegate (spec section 10: "`.lnk` via fake resolver").
/// </summary>
public sealed class SteamPathValidatorTests : IDisposable
{
    private readonly string _root;
    private readonly string _steamFolder;
    private readonly string _steamExe;
    private readonly string _emptyFolder;
    private readonly string _notSteamExe;

    public SteamPathValidatorTests()
    {
        _root = Directory.CreateTempSubdirectory("steamoff-pathvalidator-").FullName;

        _steamFolder = Path.Combine(_root, "Steam");
        Directory.CreateDirectory(_steamFolder);
        _steamExe = Path.Combine(_steamFolder, "steam.exe");
        File.WriteAllText(_steamExe, "fake exe");

        _emptyFolder = Path.Combine(_root, "EmptyFolder");
        Directory.CreateDirectory(_emptyFolder);

        _notSteamExe = Path.Combine(_steamFolder, "notsteam.exe");
        File.WriteAllText(_notSteamExe, "fake exe");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static SteamPathValidator CreateValidator(Func<string, string?>? shortcutResolver = null) =>
        new(new PathNormalizationService(), shortcutResolver);

    [Fact]
    public void EmptyCandidate_ReturnsEmptyResult_WithoutTouchingFilesystem()
    {
        var validator = CreateValidator();

        var result = validator.Validate("   ");

        Assert.Equal(PathCheckStatus.Empty, result.Status);
        Assert.Equal(SteamPathCheckResult.Empty.StatusMessageKey, result.StatusMessageKey);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void FolderContainingSteamExe_IsValid_AndPersistsTheFolder()
    {
        var validator = CreateValidator();

        var result = validator.Validate(_steamFolder);

        Assert.Equal(PathCheckStatus.Valid, result.Status);
        Assert.True(result.IsValid);
        Assert.Equal(_steamFolder, result.NormalizedFolderPath);
        Assert.Equal(_steamExe, result.SteamExePath);
        Assert.Equal("settings.steamPath.found", result.StatusMessageKey);
    }

    [Fact]
    public void SteamExePath_ResolvesToItsParentFolder_AsValid()
    {
        var validator = CreateValidator();

        var result = validator.Validate(_steamExe);

        Assert.Equal(PathCheckStatus.Valid, result.Status);
        Assert.Equal(_steamFolder, result.NormalizedFolderPath);
        Assert.Equal(_steamExe, result.SteamExePath);
    }

    [Fact]
    public void QuotedSteamExePath_NormalizesFirst_ThenResolves_AsValid()
    {
        var validator = CreateValidator();

        var result = validator.Validate($"\"{_steamExe}\"");

        Assert.Equal(PathCheckStatus.Valid, result.Status);
        Assert.Equal(_steamFolder, result.NormalizedFolderPath);
        Assert.Equal("settings.steamPath.normalized", result.StatusMessageKey);
    }

    [Fact]
    public void FolderWithoutSteamExe_IsSteamExeNotFound()
    {
        var validator = CreateValidator();

        var result = validator.Validate(_emptyFolder);

        Assert.Equal(PathCheckStatus.SteamExeNotFound, result.Status);
        Assert.False(result.IsValid);
        Assert.Equal(_emptyFolder, result.NormalizedFolderPath);
        Assert.Equal("settings.steamPath.exeNotFound", result.StatusMessageKey);
    }

    [Fact]
    public void FileThatIsNotSteamExe_IsWrongExe()
    {
        var validator = CreateValidator();

        var result = validator.Validate(_notSteamExe);

        Assert.Equal(PathCheckStatus.WrongExe, result.Status);
        Assert.False(result.IsValid);
        Assert.Equal("settings.steamPath.wrongExe", result.StatusMessageKey);
    }

    [Fact]
    public void NonexistentPath_IsPathNotFound()
    {
        var validator = CreateValidator();

        var result = validator.Validate(Path.Combine(_root, "DoesNotExist"));

        Assert.Equal(PathCheckStatus.PathNotFound, result.Status);
        Assert.False(result.IsValid);
        Assert.Equal("settings.steamPath.notExist", result.StatusMessageKey);
    }

    [Fact]
    public void Shortcut_ResolvedByFakeResolver_ToSteamFolder_IsValid()
    {
        var lnkPath = Path.Combine(_root, "Steam.lnk");
        File.WriteAllText(lnkPath, "not a real shortcut");
        var validator = CreateValidator(shortcutResolver: path => path == lnkPath ? _steamExe : null);

        var result = validator.Validate(lnkPath);

        Assert.Equal(PathCheckStatus.Valid, result.Status);
        Assert.Equal(_steamFolder, result.NormalizedFolderPath);
        Assert.Equal(_steamExe, result.SteamExePath);
        Assert.Equal("settings.steamPath.shortcutResolved", result.StatusMessageKey);
    }

    [Fact]
    public void Shortcut_ResolvedByFakeResolver_ToWrongTarget_IsWrongExe()
    {
        var lnkPath = Path.Combine(_root, "WrongTarget.lnk");
        File.WriteAllText(lnkPath, "not a real shortcut");
        var validator = CreateValidator(shortcutResolver: _ => _notSteamExe);

        var result = validator.Validate(lnkPath);

        Assert.Equal(PathCheckStatus.WrongExe, result.Status);
    }

    [Fact]
    public void Shortcut_UnresolvedByFakeResolver_IsShortcutUnresolved()
    {
        var lnkPath = Path.Combine(_root, "Broken.lnk");
        File.WriteAllText(lnkPath, "not a real shortcut");
        var validator = CreateValidator(shortcutResolver: _ => null);

        var result = validator.Validate(lnkPath);

        Assert.Equal(PathCheckStatus.ShortcutUnresolved, result.Status);
        Assert.Equal("settings.steamPath.invalid", result.StatusMessageKey);
    }

    [Fact]
    public void FromInstallation_ValidInstallation_IsValid()
    {
        var validator = CreateValidator();
        var installation = new SteamInstallation
        {
            IsValid = true,
            Path = _steamFolder,
            SteamExePath = _steamExe,
            DiscoverySource = DiscoverySource.Registry
        };

        var result = validator.FromInstallation(installation);

        Assert.Equal(PathCheckStatus.Valid, result.Status);
        Assert.Equal(_steamFolder, result.NormalizedFolderPath);
        Assert.Equal(_steamExe, result.SteamExePath);
        Assert.Equal("settings.steamPath.found", result.StatusMessageKey);
    }

    [Fact]
    public void FromInstallation_InvalidInstallation_IsEmpty_WithNotFoundAutoMessage()
    {
        var validator = CreateValidator();
        var installation = SteamInstallation.NotFound;

        var result = validator.FromInstallation(installation);

        Assert.Equal(PathCheckStatus.Empty, result.Status);
        Assert.Equal("settings.steamPath.notFoundAuto", result.StatusMessageKey);
    }
}
