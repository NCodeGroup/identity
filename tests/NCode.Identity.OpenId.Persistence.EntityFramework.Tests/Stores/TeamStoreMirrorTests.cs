// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using IdGen;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

public sealed class TeamStoreMirrorTests : IDisposable
{
    private SqliteConnection Connection { get; }
    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private TeamStore Store { get; }

    public TeamStoreMirrorTests()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options => options.UseSqlite(Connection));

        Provider = serviceCollection.BuildServiceProvider();
        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
        DbContext.Database.EnsureCreated();
        var idGenerator = Provider.GetRequiredService<IIdGenerator<long>>();
        Store = new TeamStore(Mock.Of<IStoreProvider>(), idGenerator, DbContext);
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Provider.Dispose();
        Connection.Dispose();
    }

    private static PersistedTeam CreateTeam(
        string teamId,
        string? parentTeamId,
        long sequence,
        string displayName = "Team",
        string settingsJson = "{}"
    )
    {
        var team = new PersistedTeamBuilder
        {
            TeamId = teamId,
            ParentTeamId = parentTeamId,
            DisplayName = displayName,
            Settings = JsonElement.Parse(settingsJson),
        }.Build();
        team.DirectorySequence = sequence;
        return team;
    }

    private async ValueTask SaveAndClearAsync(CancellationToken cancellationToken)
    {
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();
    }

    private async ValueTask<bool> MirrorAsync(
        PersistedTeam team,
        CancellationToken cancellationToken = default
    )
    {
        var isApplied = await Store.MirrorAsync(team, cancellationToken);
        await SaveAndClearAsync(cancellationToken);
        return isApplied;
    }

    private async ValueTask SeedTreeAsync(CancellationToken cancellationToken = default)
    {
        await MirrorAsync(CreateTeam("acme", null, sequence: 1), cancellationToken);
        await MirrorAsync(CreateTeam("acme-web", "acme", sequence: 2), cancellationToken);
        await MirrorAsync(CreateTeam("acme-web-eu", "acme-web", sequence: 3), cancellationToken);
        await MirrorAsync(CreateTeam("acme-api", "acme", sequence: 4), cancellationToken);
        await MirrorAsync(CreateTeam("acme-x", null, sequence: 5), cancellationToken);
    }

    #region MirrorAsync Tests

    [Fact]
    public async Task MirrorAsync_WhenTheTeamIsNotStored_InsertsIt()
    {
        var isApplied = await MirrorAsync(
            CreateTeam("acme", null, sequence: 4, displayName: "Acme", settingsJson: """{"a":1}""")
        );

        Assert.True(isApplied);
        var team = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(team);
        Assert.Equal("Acme", team.DisplayName);
        Assert.Equal(4, team.DirectorySequence);
        Assert.False(string.IsNullOrEmpty(team.ConcurrencyToken));
        Assert.Equal("""{"a":1}""", team.Settings.Value.GetRawText());
    }

    [Fact]
    public async Task MirrorAsync_WhenTheTeamIsNotStoredAndHasAParent_InsertsItBeneathTheParent()
    {
        await MirrorAsync(CreateTeam("acme", null, sequence: 1));

        await MirrorAsync(CreateTeam("acme-web", "acme", sequence: 2));

        var team = await Store.GetOrDefaultAsync("acme-web", CancellationToken.None);
        Assert.NotNull(team);
        Assert.Equal("acme", team.ParentTeamId);
        Assert.Equal(["acme"], team.AncestorTeamIds);
    }

    [Fact]
    public async Task MirrorAsync_WhenTheParentIsMissing_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Store.MirrorAsync(
                CreateTeam("acme-web", "acme", sequence: 1),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task MirrorAsync_WhenTheSequenceIsNewer_UpdatesTheTeam()
    {
        await MirrorAsync(CreateTeam("acme", null, sequence: 1, displayName: "Before"));

        var isApplied = await MirrorAsync(
            CreateTeam("acme", null, sequence: 2, displayName: "After")
        );

        Assert.True(isApplied);
        var team = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(team);
        Assert.Equal("After", team.DisplayName);
        Assert.Equal(2, team.DirectorySequence);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(4)]
    public async Task MirrorAsync_WhenTheSequenceIsNotNewer_LeavesTheTeamAlone(long sequence)
    {
        await MirrorAsync(CreateTeam("acme", null, sequence: 5, displayName: "Current"));

        var isApplied = await MirrorAsync(
            CreateTeam("acme", null, sequence: sequence, displayName: "Stale")
        );

        Assert.False(isApplied);
        var team = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(team);
        Assert.Equal("Current", team.DisplayName);
        Assert.Equal(5, team.DirectorySequence);
    }

    [Fact]
    public async Task MirrorAsync_WhenTheSettingsChange_RenewsTheSettingsToken()
    {
        await MirrorAsync(CreateTeam("acme", null, sequence: 1, settingsJson: """{"a":1}"""));
        var before = await Store.GetSettingsOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(before);
        DbContext.ChangeTracker.Clear();

        await MirrorAsync(CreateTeam("acme", null, sequence: 2, settingsJson: """{"a":2}"""));

        var after = await Store.GetSettingsOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(after);
        Assert.Equal("""{"a":2}""", after.Value.GetRawText());
        Assert.NotEqual(before.ConcurrencyToken, after.ConcurrencyToken);
    }

    [Fact]
    public async Task MirrorAsync_WhenTheSettingsAreEquivalent_KeepsTheSettingsToken()
    {
        await MirrorAsync(CreateTeam("acme", null, sequence: 1, settingsJson: """{"a":1}"""));
        var before = await Store.GetSettingsOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(before);
        DbContext.ChangeTracker.Clear();

        await MirrorAsync(CreateTeam("acme", null, sequence: 2, settingsJson: """{ "a": 1 }"""));

        var after = await Store.GetSettingsOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(after);
        Assert.Equal(before.ConcurrencyToken, after.ConcurrencyToken);
    }

    [Fact]
    public async Task MirrorAsync_WhenTheTeamMovesToAnotherParent_RewritesTheDescendantPaths()
    {
        await SeedTreeAsync();

        var isApplied = await MirrorAsync(CreateTeam("acme-web", "acme-x", sequence: 6));

        Assert.True(isApplied);
        var lineage = await Store.GetLineageAsync("acme-web-eu", CancellationToken.None);
        Assert.Equal(["acme-x", "acme-web", "acme-web-eu"], lineage.Select(team => team.TeamId));
        var sibling = await Store.GetLineageAsync("acme-api", CancellationToken.None);
        Assert.Equal(["acme", "acme-api"], sibling.Select(team => team.TeamId));
    }

    [Fact]
    public async Task MirrorAsync_WhenTheTeamMovesToTheTopLevel_RewritesTheDescendantPaths()
    {
        await SeedTreeAsync();

        await MirrorAsync(CreateTeam("acme-web", null, sequence: 6));

        var lineage = await Store.GetLineageAsync("acme-web-eu", CancellationToken.None);
        Assert.Equal(["acme-web", "acme-web-eu"], lineage.Select(team => team.TeamId));
    }

    [Fact]
    public async Task MirrorAsync_WhenTheMoveNestsADescendantBeyondTheMaximumDepth_Throws()
    {
        string? parentTeamId = null;
        for (var depth = 1; depth < TeamPaths.MaxDepth; ++depth)
        {
            var teamId = $"team-{depth}";
            await MirrorAsync(CreateTeam(teamId, parentTeamId, sequence: depth));
            parentTeamId = teamId;
        }

        await MirrorAsync(CreateTeam("kid", null, sequence: 20));
        await MirrorAsync(CreateTeam("grandkid", "kid", sequence: 21));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Store.MirrorAsync(
                CreateTeam("kid", parentTeamId, sequence: 22),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task MirrorAsync_WhenTheTeamMovesBeneathADescendant_Throws()
    {
        await SeedTreeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Store.MirrorAsync(
                CreateTeam("acme", "acme-web-eu", sequence: 6),
                CancellationToken.None
            )
        );
    }

    #endregion

    #region GetMaxDirectorySequenceAsync Tests

    [Fact]
    public async Task GetMaxDirectorySequenceAsync_WhenNoTeamIsStored_ReturnsZero()
    {
        var result = await Store.GetMaxDirectorySequenceAsync(CancellationToken.None);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetMaxDirectorySequenceAsync_WhenTeamsAreStored_ReturnsTheHighestSequence()
    {
        await SeedTreeAsync();

        var result = await Store.GetMaxDirectorySequenceAsync(CancellationToken.None);

        Assert.Equal(5, result);
    }

    #endregion
}
