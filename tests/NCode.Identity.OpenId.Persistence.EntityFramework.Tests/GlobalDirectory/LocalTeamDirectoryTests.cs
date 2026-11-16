// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text.Json;
using IdGen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Secrets.Persistence.Protection;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.GlobalDirectory;

public sealed class LocalTeamDirectoryTests : IDisposable
{
    private const string TeamId = "platform";
    private const string OtherTeamId = "acme";

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private IIdGenerator<long> IdGenerator { get; }
    private LocalTeamDirectory Directory { get; }

    public LocalTeamDirectoryTests()
    {
        MockAmbientTenantAccessor = Mocks.Create<IAmbientTenantAccessor>();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        );
        serviceCollection.AddDbContextFactory<OpenIdDbContext>();

        Provider = serviceCollection.BuildServiceProvider();
        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
        IdGenerator = Provider.GetRequiredService<IIdGenerator<long>>();
        Directory = new LocalTeamDirectory(
            Provider.GetRequiredService<IDbContextFactory<OpenIdDbContext>>(),
            IdGenerator,
            MockAmbientTenantAccessor.Object,
            TestPayloadSerializer.Instance
        );
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Provider.Dispose();
        Mocks.VerifyAll();
    }

    private void SetupUnscoped()
    {
        MockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped("directory"))
            .Returns(Mock.Of<IDisposable>())
            .Verifiable();
    }

    private void SeedRetiredTeam(string teamId)
    {
        DbContext.RetiredTeams.Add(
            new RetiredTeamEntity
            {
                Id = IdGenerator.CreateId(),
                TeamId = teamId,
                NormalizedTeamId = teamId.ToLowerInvariant(),
            }
        );
        DbContext.SaveChanges();
    }

    #region ReserveAsync Tests

    [Fact]
    public async Task ReserveAsync_WhenNothingHoldsTheIdentifier_IsReserved()
    {
        SetupUnscoped();
        DbContext.SeedTeam(IdGenerator, OtherTeamId);

        var result = await Directory.ReserveAsync(TeamId, CancellationToken.None);

        Assert.True(result.IsReserved);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheTeamIdIsRetired_ReturnsConflict()
    {
        SetupUnscoped();
        SeedRetiredTeam(TeamId);

        var result = await Directory.ReserveAsync(
            TeamId.ToUpperInvariant(),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Contains("never reissued", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenATeamHoldsTheIdentifier_ReturnsConflict()
    {
        SetupUnscoped();
        DbContext.SeedTeam(IdGenerator, TeamId);

        var result = await Directory.ReserveAsync(
            TeamId.ToUpperInvariant(),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Contains("already has the identifier", result.ConflictDetail);
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_WhenCalled_WritesNothing()
    {
        await Directory.PublishAsync(
            new TeamDocument
            {
                TeamId = TeamId,
                DisplayName = "Platform",
                IsDisabled = false,
                Settings = JsonElement.Parse("{}"),
                RootKeys = ImmutableDictionary<string, TeamRootKeyReference>.Empty,
            },
            CancellationToken.None
        );

        Assert.False(DbContext.ChangeTracker.HasChanges());
    }

    #endregion

    #region ReleaseAsync Tests

    [Fact]
    public async Task ReleaseAsync_WhenCalled_WritesNothingAndLeavesTheIdentifierReservable()
    {
        await Directory.ReleaseAsync(TeamId, CancellationToken.None);

        Assert.False(DbContext.ChangeTracker.HasChanges());

        SetupUnscoped();
        var result = await Directory.ReserveAsync(TeamId, CancellationToken.None);
        Assert.True(result.IsReserved);
    }

    #endregion

    #region RetireAsync Tests

    private OpenIdDbContext CreateFreshContext() =>
        Provider.GetRequiredService<IDbContextFactory<OpenIdDbContext>>().CreateDbContext();

    [Fact]
    public async Task RetireAsync_WhenTheTeamExists_RetiresItsIdentifierWithoutACallersSave()
    {
        SetupUnscoped();
        DbContext.SeedTeam(IdGenerator, TeamId);

        await Directory.RetireAsync(TeamId.ToUpperInvariant(), CancellationToken.None);

        await using var fresh = CreateFreshContext();
        var retired = Assert.Single(await fresh.RetiredTeams.ToListAsync());
        Assert.Equal(TeamId, retired.TeamId);
        Assert.Equal(TeamId, retired.NormalizedTeamId);
    }

    [Fact]
    public async Task RetireAsync_WhenTheTeamRowIsGone_RetiresTheGivenIdentifier()
    {
        SetupUnscoped();

        await Directory.RetireAsync(TeamId, CancellationToken.None);

        await using var fresh = CreateFreshContext();
        var retired = Assert.Single(await fresh.RetiredTeams.ToListAsync());
        Assert.Equal(TeamId, retired.TeamId);
    }

    [Fact]
    public async Task RetireAsync_WhenCalledTwice_RetiresOnce()
    {
        SetupUnscoped();

        await Directory.RetireAsync(TeamId, CancellationToken.None);
        await Directory.RetireAsync(TeamId, CancellationToken.None);

        await using var fresh = CreateFreshContext();
        Assert.Single(await fresh.RetiredTeams.ToListAsync());
    }

    [Fact]
    public async Task RetireAsync_WhenTheIdentifierIsAlreadyRetired_LeavesTheRetiredRowAsItIs()
    {
        SetupUnscoped();
        SeedRetiredTeam(TeamId);
        await using var before = CreateFreshContext();
        var retiredId = Assert.Single(await before.RetiredTeams.ToListAsync()).Id;

        await Directory.RetireAsync(TeamId, CancellationToken.None);

        await using var fresh = CreateFreshContext();
        var retired = Assert.Single(await fresh.RetiredTeams.ToListAsync());
        Assert.Equal(retiredId, retired.Id);
    }

    #endregion

    #region IsTeamIdRetiredAsync Tests

    [Fact]
    public async Task IsTeamIdRetiredAsync_WhenTheIdentifierIsRetired_ReturnsTrue()
    {
        SetupUnscoped();
        SeedRetiredTeam(TeamId);

        var isRetired = await Directory.IsTeamIdRetiredAsync(
            TeamId.ToUpperInvariant(),
            CancellationToken.None
        );

        Assert.True(isRetired);
    }

    [Fact]
    public async Task IsTeamIdRetiredAsync_WhenAnotherIdentifierIsRetired_ReturnsFalse()
    {
        SetupUnscoped();
        SeedRetiredTeam(OtherTeamId);

        var isRetired = await Directory.IsTeamIdRetiredAsync(TeamId, CancellationToken.None);

        Assert.False(isRetired);
    }

    #endregion

    #region GetOrDefaultAsync Tests

    [Fact]
    public async Task GetOrDefaultAsync_WhenTheTeamExists_ReturnsItsDocument()
    {
        SetupUnscoped();
        var team = DbContext.SeedTeam(IdGenerator, TeamId);
        team.DisplayName = "Platform team";
        team.SettingsJson = JsonSerializer.SerializeToElement(
            new Dictionary<string, object> { ["max_tenants"] = 3 }
        );
        await DbContext.SaveChangesAsync();

        var document = await Directory.GetOrDefaultAsync(
            TeamId.ToUpperInvariant(),
            CancellationToken.None
        );

        Assert.NotNull(document);
        Assert.Equal(TeamId, document.TeamId);
        Assert.Equal("Platform team", document.DisplayName);
        Assert.Equal("""{"max_tenants":3}""", document.Settings.GetRawText());
        Assert.Empty(document.RootKeys);
        Assert.Equal(0, document.Sequence);
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenTheTeamHoldsRootKeys_ReturnsThemByRegion()
    {
        SetupUnscoped();
        var team = DbContext.SeedTeam(IdGenerator, TeamId);
        team.RootKeysJson = TeamRootKeysPayload.Write(
            TestPayloadSerializer.Instance,
            new Dictionary<string, OwnerRootKeyRequest>
            {
                ["region-a"] = new("aws-kms", "kms://key-a")
                {
                    AccessIdentity = "arn:aws:iam::123456789012:role/team-a",
                },
                ["region-b"] = new("gcp-kms", "kms://key-b"),
            }
        );
        await DbContext.SaveChangesAsync();

        var document = await Directory.GetOrDefaultAsync(TeamId, CancellationToken.None);

        Assert.NotNull(document);
        Assert.Equal(2, document.RootKeys.Count);
        var regionA = document.RootKeys["region-a"];
        Assert.Equal("aws-kms", regionA.KeyWrapperName);
        Assert.Equal("kms://key-a", regionA.KeyReference);
        Assert.Equal("arn:aws:iam::123456789012:role/team-a", regionA.AccessIdentity);
        var regionB = document.RootKeys["region-b"];
        Assert.Equal("gcp-kms", regionB.KeyWrapperName);
        Assert.Equal("kms://key-b", regionB.KeyReference);
        Assert.Null(regionB.AccessIdentity);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"region-a":{"keyWrapperName":"aws-kms","keyReference":"kms://key-a"}}""")]
    [InlineData("""null""")]
    public async Task GetOrDefaultAsync_WhenTheStoredRootKeysAreNotAnEnvelope_ThrowsJsonException(
        string rootKeysJson
    )
    {
        SetupUnscoped();
        var team = DbContext.SeedTeam(IdGenerator, TeamId);
        team.RootKeysJson = JsonElement.Parse(rootKeysJson);
        await DbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<JsonException>(async () =>
            await Directory.GetOrDefaultAsync(TeamId, CancellationToken.None)
        );
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenTheTeamHasAParent_ReturnsTheParentTeamId()
    {
        SetupUnscoped();
        var parent = DbContext.SeedTeam(IdGenerator, TeamId);
        DbContext.SeedChildTeam(IdGenerator, parent, OtherTeamId);

        var document = await Directory.GetOrDefaultAsync(OtherTeamId, CancellationToken.None);

        Assert.NotNull(document);
        Assert.Equal(TeamId, document.ParentTeamId);
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenTheTeamIsTopLevel_ReturnsNoParentTeamId()
    {
        SetupUnscoped();
        DbContext.SeedTeam(IdGenerator, TeamId);

        var document = await Directory.GetOrDefaultAsync(TeamId, CancellationToken.None);

        Assert.NotNull(document);
        Assert.Null(document.ParentTeamId);
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenTheTeamIsUnknown_ReturnsNull()
    {
        SetupUnscoped();

        var document = await Directory.GetOrDefaultAsync(TeamId, CancellationToken.None);

        Assert.Null(document);
    }

    #endregion

    #region ListChangesAsync Tests

    [Fact]
    public async Task ListChangesAsync_WhenCalled_ReturnsNothing()
    {
        DbContext.SeedTeam(IdGenerator, TeamId);

        var changes = await Directory.ListChangesAsync(0, 100, CancellationToken.None);

        Assert.Empty(changes);
    }

    #endregion
}
