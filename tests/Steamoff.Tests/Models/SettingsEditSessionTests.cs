using Steamoff.Core.Enums;
using Steamoff.Core.Models;

namespace Steamoff.Tests.Models;

public sealed class SettingsEditSessionTests
{
    private static AppSettings CreateSavedSettings() => new()
    {
        Language = "ru",
        IsFirstLaunchCompleted = true,
        SteamPath = @"C:\Games\Steam",
        EnforcementMode = EnforcementMode.ManualToggle,
        AdditionalFolders =
        {
            new FolderBlockTarget { Id = "folder-1", Name = "Extra", Path = @"C:\Games\Extra", Enabled = true }
        }
    };

    [Fact]
    public void Constructor_ClonesOriginalAndDraft_AsIndependentObjects()
    {
        var saved = CreateSavedSettings();
        var session = new SettingsEditSession(saved);

        Assert.NotSame(saved, session.Original);
        Assert.NotSame(saved, session.Draft);
        Assert.NotSame(session.Original, session.Draft);

        Assert.Equal(saved.Language, session.Original.Language);
        Assert.Equal(saved.Language, session.Draft.Language);
    }

    [Fact]
    public void MutatingDraft_DoesNotAffectOriginal_OrTheSourceObject()
    {
        var saved = CreateSavedSettings();
        var session = new SettingsEditSession(saved);

        session.Draft.Language = "en";
        session.Draft.SteamPath = @"D:\OtherSteam";

        Assert.Equal("ru", session.Original.Language);
        Assert.Equal("ru", saved.Language);
        Assert.Equal(@"C:\Games\Steam", session.Original.SteamPath);
        Assert.Equal(@"C:\Games\Steam", saved.SteamPath);
    }

    [Fact]
    public void HasChanges_IsFalse_WhenDraftMatchesOriginal()
    {
        var session = new SettingsEditSession(CreateSavedSettings());

        Assert.False(session.HasChanges);
    }

    [Fact]
    public void HasChanges_BecomesTrue_AfterEditingDraft()
    {
        var session = new SettingsEditSession(CreateSavedSettings());

        session.Draft.Language = "en";

        Assert.True(session.HasChanges);
    }

    [Fact]
    public void CommitDraft_PromotesDraftToOriginal_AndClearsHasChanges()
    {
        var session = new SettingsEditSession(CreateSavedSettings());

        session.Draft.Language = "de";
        session.Draft.EnforcementMode = EnforcementMode.AlwaysBlock;
        session.CommitDraft();

        Assert.Equal("de", session.Original.Language);
        Assert.Equal(EnforcementMode.AlwaysBlock, session.Original.EnforcementMode);
        Assert.False(session.HasChanges);
        Assert.NotSame(session.Original, session.Draft);
    }

    [Fact]
    public void DiscardDraft_RestoresDraftFromOriginal_IncludingLanguageRollback()
    {
        var session = new SettingsEditSession(CreateSavedSettings());

        session.Draft.Language = "fr";
        session.Draft.SteamPath = @"E:\Temp\Steam";
        session.DiscardDraft();

        Assert.Equal("ru", session.Draft.Language);
        Assert.Equal(@"C:\Games\Steam", session.Draft.SteamPath);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void DiscardDraft_AfterCommit_RollsBackToTheCommittedBaseline_NotTheOriginalSavedValue()
    {
        var session = new SettingsEditSession(CreateSavedSettings());

        session.Draft.Language = "en";
        session.CommitDraft();

        session.Draft.Language = "pl";
        session.DiscardDraft();

        Assert.Equal("en", session.Draft.Language);
    }

    [Fact]
    public void Draft_PreservesUserAddedFolders_AcrossClone()
    {
        var session = new SettingsEditSession(CreateSavedSettings());

        Assert.Single(session.Draft.AdditionalFolders);
        Assert.Equal(@"C:\Games\Extra", session.Draft.AdditionalFolders[0].Path);

        session.Draft.AdditionalFolders[0].Enabled = false;

        Assert.True(session.Original.AdditionalFolders[0].Enabled);
    }
}
