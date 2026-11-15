// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public sealed class DefaultDirectoryStoreTeamTests : DefaultDirectoryStoreTestBase
{
    #region ReserveAsync Tests

    [Fact]
    public async Task ReserveAsync_WhenTheIdentifierIsFree_ReservesTheTeam()
    {
        var result = await Teams.ReserveAsync("team-1", CancellationToken.None);

        Assert.True(result.IsReserved);
        Assert.Null(await Teams.GetOrDefaultAsync("team-1", CancellationToken.None));
    }

    [Fact]
    public async Task ReserveAsync_WhenTheIdentifierIsHeld_ConflictsWithTheHeldIdentifierText()
    {
        Assert.True((await Teams.ReserveAsync("team-1", CancellationToken.None)).IsReserved);

        var result = await Teams.ReserveAsync("TEAM-1", CancellationToken.None);

        Assert.False(result.IsReserved);
        Assert.Equal("Another team already has the identifier 'TEAM-1'.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheIdentifierIsRetired_ConflictsWithTheDeletedTeamText()
    {
        await Teams.RetireAsync("team-1", CancellationToken.None);

        var result = await Teams.ReserveAsync("Team-1", CancellationToken.None);

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The team identifier 'Team-1' belonged to a deleted team and is never reissued.",
            result.ConflictDetail
        );
    }

    #endregion

    #region ReleaseAsync Tests

    [Fact]
    public async Task ReleaseAsync_WhenTheTeamIsReserved_FreesTheIdentifier()
    {
        Assert.True((await Teams.ReserveAsync("team-1", CancellationToken.None)).IsReserved);

        await Teams.ReleaseAsync("team-1", CancellationToken.None);

        Assert.True((await Teams.ReserveAsync("team-1", CancellationToken.None)).IsReserved);
    }

    [Fact]
    public async Task ReleaseAsync_WhenTheTeamIsPublished_KeepsTheTeam()
    {
        await PublishTeamAsync("team-1");

        await Teams.ReleaseAsync("team-1", CancellationToken.None);

        Assert.NotNull(await Teams.GetOrDefaultAsync("team-1", CancellationToken.None));
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_WhenTheTeamIsReserved_StoresTheDocumentWithItsSequence()
    {
        Assert.True((await Teams.ReserveAsync("team-1", CancellationToken.None)).IsReserved);
        var document = new TeamDocument
        {
            TeamId = "team-1",
            DisplayName = "Team 1",
            IsDisabled = false,
            ParentTeamId = "team-0",
            Settings = JsonElement.Parse(SettingsWithLimit(3)),
            RootKeys = ImmutableDictionary<string, TeamRootKeyReference>
                .Empty.Add(
                    "region-a",
                    new TeamRootKeyReference("aws-kms", "kms://key-a")
                    {
                        AccessIdentity = "arn:aws:iam::123456789012:role/team-a",
                    }
                )
                .Add("region-b", new TeamRootKeyReference("gcp-kms", "kms://key-b")),
            Sequence = 99,
        };

        await Teams.PublishAsync(document, CancellationToken.None);

        var stored = await Teams.GetOrDefaultAsync("TEAM-1", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("team-1", stored.TeamId);
        Assert.Equal("Team 1", stored.DisplayName);
        Assert.Equal("team-0", stored.ParentTeamId);
        Assert.Equal(SettingsWithLimit(3), stored.Settings.GetRawText());
        Assert.Equal(["region-a", "region-b"], stored.RootKeys.Keys.Order(StringComparer.Ordinal));
        var regionA = stored.RootKeys["region-a"];
        Assert.Equal("aws-kms", regionA.KeyWrapperName);
        Assert.Equal("kms://key-a", regionA.KeyReference);
        Assert.Equal("arn:aws:iam::123456789012:role/team-a", regionA.AccessIdentity);
        var regionB = stored.RootKeys["region-b"];
        Assert.Equal("gcp-kms", regionB.KeyWrapperName);
        Assert.Equal("kms://key-b", regionB.KeyReference);
        Assert.Null(regionB.AccessIdentity);
        Assert.Equal(1, stored.Sequence);
    }

    [Fact]
    public async Task PublishAsync_WhenTheTeamIsPublishedAgain_ReplacesTheDocumentWithANewSequence()
    {
        await PublishTeamAsync("team-1");
        await PublishTeamAsync("team-2");

        await Teams.PublishAsync(
            new TeamDocument
            {
                TeamId = "team-1",
                DisplayName = "Renamed",
                IsDisabled = false,
            },
            CancellationToken.None
        );

        var stored = await Teams.GetOrDefaultAsync("team-1", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("Renamed", stored.DisplayName);
        Assert.Equal(3, stored.Sequence);
    }

    [Fact]
    public async Task PublishAsync_WhenAChildIsPublishedBeforeItsParent_RepathsTheChildAndItsSubtreeBeneathTheParent()
    {
        await PublishTeamAsync("team-1", parentTeamId: "team-0");
        await PublishTeamAsync("team-2", parentTeamId: "team-1");
        Assert.Equal("/team-1/", await ReadTeamPathAsync("team-1"));

        await PublishTeamAsync("team-0");

        Assert.Equal("/team-0/", await ReadTeamPathAsync("team-0"));
        Assert.Equal("/team-0/team-1/", await ReadTeamPathAsync("team-1"));
        Assert.Equal("/team-0/team-1/team-2/", await ReadTeamPathAsync("team-2"));
    }

    [Fact]
    public async Task PublishAsync_WhenTheParentIsPublishedAgain_KeepsTheChildPath()
    {
        await PublishTeamAsync("team-0");
        await PublishTeamAsync("team-1", parentTeamId: "team-0");

        await PublishTeamAsync("team-0");

        Assert.Equal("/team-0/team-1/", await ReadTeamPathAsync("team-1"));
    }

    [Fact]
    public async Task PublishAsync_WhenTheTeamIsRetired_KeepsItRetired()
    {
        await Teams.RetireAsync("team-1", CancellationToken.None);

        await PublishTeamAsync("team-1");

        Assert.Null(await Teams.GetOrDefaultAsync("team-1", CancellationToken.None));
        Assert.True(await Teams.IsTeamIdRetiredAsync("team-1", CancellationToken.None));
        var changes = await Teams.ListChangesAsync(0, 10, CancellationToken.None);
        Assert.Single(changes);
    }

    [Fact]
    public async Task PublishAsync_WhenTheTeamWasPublishedBefore_KeepsOnlyItsLatestChange()
    {
        await PublishTeamAsync("team-1");
        await PublishTeamAsync("team-2");

        await PublishTeamAsync("TEAM-1", maxTenants: 2);

        var changes = await Teams.ListChangesAsync(0, 10, CancellationToken.None);
        Assert.Equal([2L, 3L], changes.Select(change => change.Sequence));
        Assert.Equal("team-2", changes[0].TeamId);
        Assert.Equal("TEAM-1", changes[1].TeamId);
        Assert.Equal(SettingsWithLimit(2), changes[1].Document?.Settings.GetRawText());
    }

    #endregion

    #region RetireAsync Tests

    [Fact]
    public async Task RetireAsync_WhenTheTeamIsPublished_RetiresTheIdentifierAndDropsTheDocument()
    {
        await PublishTeamAsync("team-1");

        await Teams.RetireAsync("TEAM-1", CancellationToken.None);

        Assert.Null(await Teams.GetOrDefaultAsync("team-1", CancellationToken.None));
        Assert.True(await Teams.IsTeamIdRetiredAsync("team-1", CancellationToken.None));
    }

    [Fact]
    public async Task RetireAsync_WhenTheTeamIsUnknown_RetiresTheIdentifier()
    {
        await Teams.RetireAsync("team-1", CancellationToken.None);

        Assert.True(await Teams.IsTeamIdRetiredAsync("team-1", CancellationToken.None));
    }

    [Fact]
    public async Task RetireAsync_WhenTheTeamIsRetiredTwice_AddsOneChange()
    {
        await Teams.RetireAsync("team-1", CancellationToken.None);

        await Teams.RetireAsync("team-1", CancellationToken.None);

        var changes = await Teams.ListChangesAsync(0, 10, CancellationToken.None);
        Assert.Single(changes);
    }

    [Fact]
    public async Task RetireAsync_WhenTheTeamWasPublished_KeepsOnlyTheRetire()
    {
        await PublishTeamAsync("team-1");
        await PublishTeamAsync("team-2");

        await Teams.RetireAsync("TEAM-1", CancellationToken.None);

        var changes = await Teams.ListChangesAsync(0, 10, CancellationToken.None);
        Assert.Equal([2L, 3L], changes.Select(change => change.Sequence));
        Assert.Equal("team-2", changes[0].TeamId);
        Assert.NotNull(changes[0].Document);
        Assert.Equal("TEAM-1", changes[1].TeamId);
        Assert.Null(changes[1].Document);
    }

    #endregion

    #region IsTeamIdRetiredAsync Tests

    [Fact]
    public async Task IsTeamIdRetiredAsync_WhenTheTeamIsPublished_ReturnsFalse()
    {
        await PublishTeamAsync("team-1");

        var retired = await Teams.IsTeamIdRetiredAsync("team-1", CancellationToken.None);

        Assert.False(retired);
    }

    #endregion

    #region GetOrDefaultAsync Tests

    [Fact]
    public async Task GetOrDefaultAsync_WhenTheTeamIsUnknown_ReturnsNull()
    {
        var document = await Teams.GetOrDefaultAsync("unknown", CancellationToken.None);

        Assert.Null(document);
    }

    #endregion

    #region ListChangesAsync Tests

    [Fact]
    public async Task ListChangesAsync_WhenTeamsAndThePlatformChange_ReturnsTheChangesInSequence()
    {
        await PublishTeamAsync("team-1", parentTeamId: "team-0");
        await PublishTeamAsync("team-2");
        await PublishPlatformAsync(maxTenants: 4);
        await Teams.RetireAsync("team-2", CancellationToken.None);

        var changes = await Teams.ListChangesAsync(0, 10, CancellationToken.None);

        Assert.Equal(3, changes.Count);

        Assert.Equal(1, changes[0].Sequence);
        Assert.Equal("team-1", changes[0].TeamId);
        Assert.NotNull(changes[0].Document);
        Assert.Equal(1, changes[0].Document?.Sequence);
        Assert.Equal("team-0", changes[0].Document?.ParentTeamId);
        Assert.Null(changes[0].Platform);

        Assert.Equal(3, changes[1].Sequence);
        Assert.Null(changes[1].TeamId);
        Assert.Null(changes[1].Document);
        Assert.NotNull(changes[1].Platform);
        Assert.Equal(3, changes[1].Platform?.Sequence);
        Assert.Equal(SettingsWithLimit(4), changes[1].Platform?.Settings.GetRawText());

        Assert.Equal(4, changes[2].Sequence);
        Assert.Equal("team-2", changes[2].TeamId);
        Assert.Null(changes[2].Document);
        Assert.Null(changes[2].Platform);
    }

    [Fact]
    public async Task ListChangesAsync_WhenPublishesARetireAndAPlatformPublishInterleave_ReturnsEachLatestChangeInSequence()
    {
        await PublishTeamAsync("team-1");
        await PublishTeamAsync("team-2");
        await Teams.RetireAsync("team-1", CancellationToken.None);
        await PublishPlatformAsync(maxTenants: 4);
        await PublishTeamAsync("team-2", maxTenants: 2);
        await PublishPlatformAsync(maxTenants: 5);
        await PublishTeamAsync("team-3");

        var changes = await Teams.ListChangesAsync(0, 10, CancellationToken.None);

        Assert.Equal([3L, 5L, 6L, 7L], changes.Select(change => change.Sequence));
        Assert.Equal(["team-1", "team-2", null, "team-3"], changes.Select(change => change.TeamId));
        Assert.Null(changes[0].Document);
        Assert.Equal(SettingsWithLimit(2), changes[1].Document?.Settings.GetRawText());
        Assert.Equal(SettingsWithLimit(5), changes[2].Platform?.Settings.GetRawText());
    }

    [Fact]
    public async Task ListChangesAsync_WhenAfterIsSet_ReturnsOnlyLaterChanges()
    {
        await PublishTeamAsync("team-1");
        await PublishTeamAsync("team-2");
        await PublishTeamAsync("team-3");

        var changes = await Teams.ListChangesAsync(1, 10, CancellationToken.None);

        Assert.Equal([2L, 3L], changes.Select(change => change.Sequence));
    }

    [Fact]
    public async Task ListChangesAsync_WhenLimitIsSet_ReturnsAtMostLimitChanges()
    {
        await PublishTeamAsync("team-1");
        await PublishTeamAsync("team-2");
        await PublishTeamAsync("team-3");

        var changes = await Teams.ListChangesAsync(0, 2, CancellationToken.None);

        Assert.Equal([1L, 2L], changes.Select(change => change.Sequence));
    }

    [Fact]
    public async Task ListChangesAsync_WhenLimitIsNegative_ReturnsNothing()
    {
        await PublishTeamAsync("team-1");

        var changes = await Teams.ListChangesAsync(0, -1, CancellationToken.None);

        Assert.Empty(changes);
    }

    [Fact]
    public async Task ListChangesAsync_WhenNothingChanged_ReturnsNothing()
    {
        var changes = await Teams.ListChangesAsync(0, 10, CancellationToken.None);

        Assert.Empty(changes);
    }

    #endregion
}
