// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public sealed class DefaultDirectoryStoreReplicatedDocumentTests : DefaultDirectoryStoreTestBase
{
    private static ReplicatedDocument CreateDocument(
        string kind,
        string key,
        long revision,
        string contentJson = """{"assignments":[],"clients":[]}"""
    ) =>
        new()
        {
            Kind = kind,
            Key = key,
            Revision = revision,
            Content = JsonElement.Parse(contentJson),
        };

    private ValueTask<IReadOnlyList<DirectoryChange>> ListAllChangesAsync(
        CancellationToken cancellationToken
    ) => Teams.ListChangesAsync(0, 100, cancellationToken);

    [Fact]
    public async Task PublishAsync_WhenNothingIsStored_StoresTheDocumentAndAnswersItsRevision()
    {
        var document = CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 3);

        var revision = await Replicated.PublishAsync(document, CancellationToken.None);

        Assert.Equal(3, revision);
        var stored = await Replicated.GetOrDefaultAsync(
            ReplicatedDocumentKinds.PlatformAuthority,
            "",
            CancellationToken.None
        );
        Assert.NotNull(stored);
        Assert.Equal(3, stored.Revision);
        Assert.Equal("""{"assignments":[],"clients":[]}""", stored.Content?.GetRawText());
    }

    [Fact]
    public async Task PublishAsync_WhenTheRevisionIsHigher_ReplacesTheDocument()
    {
        await Replicated.PublishAsync(
            CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 1),
            CancellationToken.None
        );

        var revision = await Replicated.PublishAsync(
            CreateDocument(
                ReplicatedDocumentKinds.PlatformAuthority,
                "",
                2,
                """{"assignments":[{"principalId":"p","roleName":"GlobalAdmin"}],"clients":[]}"""
            ),
            CancellationToken.None
        );

        Assert.Equal(2, revision);
        var stored = await Replicated.GetOrDefaultAsync(
            ReplicatedDocumentKinds.PlatformAuthority,
            "",
            CancellationToken.None
        );
        Assert.NotNull(stored);
        Assert.Contains("GlobalAdmin", stored.Content?.GetRawText());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(4)]
    public async Task PublishAsync_WhenTheRevisionIsEqualOrLower_AnswersTheStoredRevisionUnchanged(
        long revision
    )
    {
        await Replicated.PublishAsync(
            CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 5),
            CancellationToken.None
        );

        var answer = await Replicated.PublishAsync(
            CreateDocument(
                ReplicatedDocumentKinds.PlatformAuthority,
                "",
                revision,
                """{"assignments":[{"principalId":"other","roleName":"GlobalAdmin"}],"clients":[]}"""
            ),
            CancellationToken.None
        );

        Assert.Equal(5, answer);
        var stored = await Replicated.GetOrDefaultAsync(
            ReplicatedDocumentKinds.PlatformAuthority,
            "",
            CancellationToken.None
        );
        Assert.NotNull(stored);
        Assert.Equal(5, stored.Revision);
        Assert.DoesNotContain("other", stored.Content?.GetRawText());
        var changes = await ListAllChangesAsync(CancellationToken.None);
        Assert.Single(changes);
    }

    [Fact]
    public async Task ListChangesAsync_WhenADocumentIsPublishedTwice_KeepsOnlyItsLatestChange()
    {
        await Replicated.PublishAsync(
            CreateDocument(ReplicatedDocumentKinds.ManagementKeys, "", 1),
            CancellationToken.None
        );
        await Replicated.PublishAsync(
            CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 1),
            CancellationToken.None
        );
        await Replicated.PublishAsync(
            CreateDocument(ReplicatedDocumentKinds.ManagementKeys, "", 2),
            CancellationToken.None
        );

        var changes = await ListAllChangesAsync(CancellationToken.None);

        Assert.Equal(2, changes.Count);
        Assert.Equal(2, changes[0].Sequence);
        Assert.Equal(ReplicatedDocumentKinds.PlatformAuthority, changes[0].Replicated?.Kind);
        Assert.Equal(3, changes[1].Sequence);
        Assert.Equal(ReplicatedDocumentKinds.ManagementKeys, changes[1].Replicated?.Kind);
        Assert.Equal(2, changes[1].Replicated?.Revision);
        Assert.Null(changes[1].Platform);
        Assert.Null(changes[1].TeamId);
    }

    [Fact]
    public async Task PublishAsync_WhenThePlatformDocumentIsPublished_KeepsTheReplicatedChanges()
    {
        await Replicated.PublishAsync(
            CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 1),
            CancellationToken.None
        );

        await PublishPlatformAsync(maxTenants: 3);

        var changes = await ListAllChangesAsync(CancellationToken.None);
        Assert.Equal(2, changes.Count);
        Assert.NotNull(changes[0].Replicated);
        Assert.NotNull(changes[1].Platform);
    }

    [Fact]
    public async Task RetireAsync_WhenTheTeamHasAnAuthorityDocument_RetiresTheDocument()
    {
        await PublishTeamAsync("team-1");
        await Replicated.PublishAsync(
            CreateDocument(ReplicatedDocumentKinds.TeamAuthority, "team-1", 4),
            CancellationToken.None
        );

        await Teams.RetireAsync("team-1", CancellationToken.None);

        var stored = await Replicated.GetOrDefaultAsync(
            ReplicatedDocumentKinds.TeamAuthority,
            "team-1",
            CancellationToken.None
        );
        Assert.NotNull(stored);
        Assert.Null(stored.Content);
        Assert.Equal(4, stored.Revision);
        var changes = await ListAllChangesAsync(CancellationToken.None);
        var replicated = Assert.Single(changes, change => change.Replicated is not null);
        Assert.Null(replicated.Replicated?.Content);
        Assert.Contains(changes, change => change.TeamId == "team-1" && change.Document is null);
    }

    [Fact]
    public async Task RetireAsync_WhenTheTeamHasNoAuthorityDocument_StoresNoDocument()
    {
        await PublishTeamAsync("team-1");

        await Teams.RetireAsync("team-1", CancellationToken.None);

        Assert.Null(
            await Replicated.GetOrDefaultAsync(
                ReplicatedDocumentKinds.TeamAuthority,
                "team-1",
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenTheKeyDiffersInCase_FindsTheDocument()
    {
        await Replicated.PublishAsync(
            CreateDocument(ReplicatedDocumentKinds.TeamAuthority, "Team-1", 1),
            CancellationToken.None
        );

        var stored = await Replicated.GetOrDefaultAsync(
            ReplicatedDocumentKinds.TeamAuthority,
            "TEAM-1",
            CancellationToken.None
        );

        Assert.NotNull(stored);
    }

    [Fact]
    public async Task ListRevisionsAsync_ListsEveryDocumentWithoutContent()
    {
        await Replicated.PublishAsync(
            CreateDocument(ReplicatedDocumentKinds.ManagementKeys, "", 7),
            CancellationToken.None
        );
        await Replicated.PublishAsync(
            CreateDocument(ReplicatedDocumentKinds.PlatformAuthority, "", 2),
            CancellationToken.None
        );

        var revisions = await Replicated.ListRevisionsAsync(CancellationToken.None);

        Assert.Equal(2, revisions.Count);
        Assert.All(revisions, revision => Assert.Null(revision.Content));
        Assert.Equal(
            7,
            revisions.Single(r => r.Kind == ReplicatedDocumentKinds.ManagementKeys).Revision
        );
        Assert.Equal(
            2,
            revisions.Single(r => r.Kind == ReplicatedDocumentKinds.PlatformAuthority).Revision
        );
    }

    [Fact]
    public async Task PublishAsync_WhenTheContentIsOversized_Throws()
    {
        var padding = new string('a', DirectoryLimits.MaxDocumentBytes);
        var document = CreateDocument(
            ReplicatedDocumentKinds.ManagementKeys,
            "",
            1,
            $$"""{"padding":"{{padding}}"}"""
        );

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Replicated.PublishAsync(document, CancellationToken.None)
        );

        Assert.Null(
            await Replicated.GetOrDefaultAsync(
                ReplicatedDocumentKinds.ManagementKeys,
                "",
                CancellationToken.None
            )
        );
    }
}
