// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.Protection;

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
        var keyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        Store = new TeamStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            DbContext,
            TestPayloadSerializer.Instance
        );
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
        string settingsJson = "{}",
        IReadOnlyDictionary<string, OwnerRootKeyRequest>? rootKeys = null,
        bool isDisabled = false
    )
    {
        var team = new PersistedTeamBuilder
        {
            TeamId = teamId,
            ParentTeamId = parentTeamId,
            DisplayName = displayName,
            IsDisabled = isDisabled,
            Settings = JsonElement.Parse(settingsJson),
        }.Build();
        team.DirectorySequence = sequence;
        if (rootKeys is not null)
            team.RootKeys = rootKeys;
        return team;
    }

    private static Dictionary<string, OwnerRootKeyRequest> CreateTwoRegionRootKeys() =>
        new(StringComparer.Ordinal)
        {
            ["region-a"] = new("aws-kms", "kms://key-a")
            {
                AccessIdentity = "arn:aws:iam::123456789012:role/team-a",
            },
            ["region-b"] = new("gcp-kms", "kms://key-b"),
        };

    private static void AssertTwoRegionRootKeys(
        IReadOnlyDictionary<string, OwnerRootKeyRequest> rootKeys
    )
    {
        Assert.Equal(2, rootKeys.Count);
        var regionA = rootKeys["region-a"];
        Assert.Equal("aws-kms", regionA.KeyWrapperName);
        Assert.Equal("kms://key-a", regionA.KeyReference);
        Assert.Equal("arn:aws:iam::123456789012:role/team-a", regionA.AccessIdentity);
        var regionB = rootKeys["region-b"];
        Assert.Equal("gcp-kms", regionB.KeyWrapperName);
        Assert.Equal("kms://key-b", regionB.KeyReference);
        Assert.Null(regionB.AccessIdentity);
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

    /// <summary>
    /// Reads the root keys of a team as its row holds them.
    /// </summary>
    private async ValueTask<JsonElement> ReadRootKeysJsonAsync(
        string normalizedTeamId,
        CancellationToken cancellationToken
    ) =>
        await DbContext
            .Teams.AsNoTracking()
            .Where(entity => entity.NormalizedTeamId == normalizedTeamId)
            .Select(entity => entity.RootKeysJson)
            .SingleAsync(cancellationToken);

    /// <summary>
    /// Replaces the root keys that a team's row holds with the given JSON, as a damaged or foreign row holds them.
    /// </summary>
    private async ValueTask ReplaceRootKeysJsonAsync(
        string normalizedTeamId,
        string rootKeysJson,
        CancellationToken cancellationToken
    )
    {
        var entity = await DbContext.Teams.SingleAsync(
            team => team.NormalizedTeamId == normalizedTeamId,
            cancellationToken
        );
        entity.RootKeysJson = JsonElement.Parse(rootKeysJson);
        await SaveAndClearAsync(cancellationToken);
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

    [Fact]
    public async Task MirrorAsync_WhenTheDocumentIsDisabledOverAnEnabledTeam_DisablesTheTeam()
    {
        await MirrorAsync(CreateTeam("acme", null, sequence: 1));

        var isApplied = await MirrorAsync(CreateTeam("acme", null, sequence: 2, isDisabled: true));

        Assert.True(isApplied);
        var team = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(team);
        Assert.True(team.IsDisabled);
    }

    [Fact]
    public async Task MirrorAsync_WhenTheDocumentIsEnabledOverADisabledTeam_EnablesTheTeam()
    {
        await MirrorAsync(CreateTeam("acme", null, sequence: 1, isDisabled: true));

        await MirrorAsync(CreateTeam("acme", null, sequence: 2));

        var team = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(team);
        Assert.False(team.IsDisabled);
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

    #region Root Keys Tests

    [Fact]
    public async Task AddAsync_WhenTheTeamHoldsRootKeys_RoundTripsTheMapByRegion()
    {
        var team = CreateTeam("acme", null, sequence: 0, rootKeys: CreateTwoRegionRootKeys());

        await Store.AddAsync(team, CancellationToken.None);
        await SaveAndClearAsync(CancellationToken.None);

        var stored = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(stored);
        AssertTwoRegionRootKeys(stored.RootKeys);
    }

    [Fact]
    public async Task AddAsync_WhenTheTeamHoldsRootKeys_StoresThemAsAVersionedEnvelope()
    {
        var team = CreateTeam("acme", null, sequence: 0, rootKeys: CreateTwoRegionRootKeys());

        await Store.AddAsync(team, CancellationToken.None);
        await SaveAndClearAsync(CancellationToken.None);

        var rootKeysJson = await ReadRootKeysJsonAsync("acme", CancellationToken.None);
        Assert.Equal(1, rootKeysJson.GetProperty("$ver").GetInt32());
        var rootKeys = rootKeysJson.GetProperty("$payload").GetProperty("rootKeys");
        Assert.Equal(2, rootKeys.GetPropertyCount());
        var regionA = rootKeys.GetProperty("region-a");
        Assert.Equal("aws-kms", regionA.GetProperty("keyWrapperName").GetString());
        Assert.Equal("kms://key-a", regionA.GetProperty("keyReference").GetString());
        Assert.Equal(
            "arn:aws:iam::123456789012:role/team-a",
            regionA.GetProperty("accessIdentity").GetString()
        );
        Assert.Equal(
            "gcp-kms",
            rootKeys.GetProperty("region-b").GetProperty("keyWrapperName").GetString()
        );
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"region-a":{"keyWrapperName":"aws-kms","keyReference":"kms://key-a"}}""")]
    [InlineData("""null""")]
    public async Task GetOrDefaultAsync_WhenTheStoredRootKeysAreNotAnEnvelope_ThrowsJsonException(
        string rootKeysJson
    )
    {
        await Store.AddAsync(CreateTeam("acme", null, sequence: 0), CancellationToken.None);
        await SaveAndClearAsync(CancellationToken.None);
        await ReplaceRootKeysJsonAsync("acme", rootKeysJson, CancellationToken.None);

        await Assert.ThrowsAsync<JsonException>(async () =>
            await Store.GetOrDefaultAsync("acme", CancellationToken.None)
        );
    }

    [Theory]
    [InlineData("""{"$ver":1,"$payload":{}}""")]
    [InlineData("""{"$ver":1,"$payload":{"rootKeys":null}}""")]
    [InlineData(
        """{"$ver":1,"$payload":{"rootKeys":{"region-a":{"keyReference":"kms://key-a"}}}}"""
    )]
    [InlineData(
        """{"$ver":1,"$payload":{"rootKeys":{"region-a":{"keyWrapperName":"aws-kms","keyReference":null}}}}"""
    )]
    [InlineData("""{"$ver":1,"$payload":{"rootKeys":{"region-a":null}}}""")]
    public async Task GetOrDefaultAsync_WhenTheStoredRootKeysLackAMemberTheStoreWrites_ThrowsJsonException(
        string rootKeysJson
    )
    {
        await Store.AddAsync(CreateTeam("acme", null, sequence: 0), CancellationToken.None);
        await SaveAndClearAsync(CancellationToken.None);
        await ReplaceRootKeysJsonAsync("acme", rootKeysJson, CancellationToken.None);

        await Assert.ThrowsAsync<JsonException>(async () =>
            await Store.GetOrDefaultAsync("acme", CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenTheTeamHoldsNoRootKeys_ReturnsAnEmptyMap()
    {
        await Store.AddAsync(CreateTeam("acme", null, sequence: 0), CancellationToken.None);
        await SaveAndClearAsync(CancellationToken.None);

        var stored = await Store.GetOrDefaultAsync("acme", CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Empty(stored.RootKeys);
    }

    [Fact]
    public async Task UpdateRootKeysAsync_WhenTheTokenIsCurrent_WritesOnlyTheMap()
    {
        await MirrorAsync(CreateTeam("acme", null, sequence: 1, displayName: "Acme"));
        var team = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(team);
        var previousToken = team.ConcurrencyToken;
        team.DisplayName = "Not written";
        team.RootKeys = CreateTwoRegionRootKeys();

        await Store.UpdateRootKeysAsync(team, CancellationToken.None);
        await SaveAndClearAsync(CancellationToken.None);

        var stored = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(stored);
        AssertTwoRegionRootKeys(stored.RootKeys);
        Assert.Equal("Acme", stored.DisplayName);
        Assert.NotEqual(previousToken, stored.ConcurrencyToken);
    }

    [Fact]
    public async Task UpdateRootKeysAsync_WhenTheTokenIsStale_Throws()
    {
        await MirrorAsync(CreateTeam("acme", null, sequence: 1));
        var team = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(team);
        team.ConcurrencyToken = "stale";
        team.RootKeys = CreateTwoRegionRootKeys();

        await Assert.ThrowsAsync<StoreConcurrencyException>(async () =>
            await Store.UpdateRootKeysAsync(team, CancellationToken.None)
        );
    }

    [Fact]
    public async Task UpdateAsync_WhenTheTeamHoldsRootKeys_KeepsTheMap()
    {
        await MirrorAsync(
            CreateTeam("acme", null, sequence: 1, rootKeys: CreateTwoRegionRootKeys())
        );
        var team = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(team);
        team.DisplayName = "Renamed";
        team.RootKeys = new Dictionary<string, OwnerRootKeyRequest>();

        await Store.UpdateAsync(team, CancellationToken.None);
        await SaveAndClearAsync(CancellationToken.None);

        var stored = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal("Renamed", stored.DisplayName);
        AssertTwoRegionRootKeys(stored.RootKeys);
    }

    [Fact]
    public async Task MirrorAsync_WhenTheTeamIsNotStored_InsertsItsRootKeys()
    {
        await MirrorAsync(
            CreateTeam("acme", null, sequence: 1, rootKeys: CreateTwoRegionRootKeys())
        );

        var stored = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(stored);
        AssertTwoRegionRootKeys(stored.RootKeys);
    }

    [Fact]
    public async Task MirrorAsync_WhenTheRootKeysChange_WritesTheNewMap()
    {
        await MirrorAsync(CreateTeam("acme", null, sequence: 1));

        await MirrorAsync(
            CreateTeam("acme", null, sequence: 2, rootKeys: CreateTwoRegionRootKeys())
        );

        var stored = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(stored);
        AssertTwoRegionRootKeys(stored.RootKeys);
    }

    [Fact]
    public async Task MirrorAsync_WhenTheDocumentHoldsNoRootKeys_ClearsTheMap()
    {
        await MirrorAsync(
            CreateTeam("acme", null, sequence: 1, rootKeys: CreateTwoRegionRootKeys())
        );

        await MirrorAsync(CreateTeam("acme", null, sequence: 2));

        var stored = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Empty(stored.RootKeys);
    }

    #endregion

    #region DisableRetiredAsync Tests

    [Fact]
    public async Task DisableRetiredAsync_WhenTheRowIsOlder_DisablesItAndRecordsTheSequence()
    {
        await MirrorAsync(CreateTeam("acme", null, sequence: 1, displayName: "Acme"));
        var before = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(before);
        DbContext.ChangeTracker.Clear();

        var isDisabled = await Store.DisableRetiredAsync("acme", 5, CancellationToken.None);
        await SaveAndClearAsync(CancellationToken.None);

        Assert.True(isDisabled);
        var team = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(team);
        Assert.True(team.IsDisabled);
        Assert.Equal(5, team.DirectorySequence);
        Assert.Equal("Acme", team.DisplayName);
        Assert.NotEqual(before.ConcurrencyToken, team.ConcurrencyToken);
    }

    [Fact]
    public async Task DisableRetiredAsync_WhenTheRowCarriesTheSequence_ReturnsFalse()
    {
        await MirrorAsync(CreateTeam("acme", null, sequence: 5));

        var isDisabled = await Store.DisableRetiredAsync("acme", 5, CancellationToken.None);
        await SaveAndClearAsync(CancellationToken.None);

        Assert.False(isDisabled);
        var team = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(team);
        Assert.False(team.IsDisabled);
        Assert.Equal(5, team.DirectorySequence);
    }

    [Fact]
    public async Task DisableRetiredAsync_WhenTheRowIsNewer_ReturnsFalse()
    {
        await MirrorAsync(CreateTeam("acme", null, sequence: 7));

        var isDisabled = await Store.DisableRetiredAsync("acme", 5, CancellationToken.None);
        await SaveAndClearAsync(CancellationToken.None);

        Assert.False(isDisabled);
        var team = await Store.GetOrDefaultAsync("acme", CancellationToken.None);
        Assert.NotNull(team);
        Assert.False(team.IsDisabled);
        Assert.Equal(7, team.DirectorySequence);
    }

    [Fact]
    public async Task DisableRetiredAsync_WhenTheTeamIsMissing_ReturnsFalse()
    {
        var isDisabled = await Store.DisableRetiredAsync("acme", 5, CancellationToken.None);

        Assert.False(isDisabled);
        Assert.Null(await Store.GetOrDefaultAsync("acme", CancellationToken.None));
    }

    #endregion
}
