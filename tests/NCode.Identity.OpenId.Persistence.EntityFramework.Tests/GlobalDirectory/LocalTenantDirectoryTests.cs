// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using IdGen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.EntityFramework.Core.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.Tenants;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.GlobalDirectory;

public sealed class LocalTenantDirectoryTests : IDisposable
{
    private const string TeamId = "platform";
    private const string TenantId = "tenant-1";
    private const string OtherTenantId = "tenant-2";
    private const string Issuer = "https://op.example/tenant-1";
    private const string OtherIssuer = "https://op.example/tenant-2";
    private const string FreeIssuer = "https://op.example/tenant-3";
    private const string CellName = "cell-a";
    private const string CellRegion = "region-a";

    private MockRepository Mocks { get; } = new(MockBehavior.Strict);
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private IIdGenerator<long> IdGenerator { get; }
    private LocalTenantDirectory Directory { get; }

    public LocalTenantDirectoryTests()
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
        Directory = new LocalTenantDirectory(
            Provider.GetRequiredService<IDbContextFactory<OpenIdDbContext>>(),
            IdGenerator,
            MockAmbientTenantAccessor.Object,
            Options.Create(new CellOptions { Name = CellName, Region = CellRegion })
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

    private TeamEntity SeedTeamWithLimit(string teamId, int? maxTenants)
    {
        var team = DbContext.SeedTeam(IdGenerator, teamId);
        if (maxTenants is { } limit)
        {
            team.SettingsJson = JsonSerializer.SerializeToElement(
                new Dictionary<string, object> { ["max_tenants"] = limit }
            );
            DbContext.SaveChanges();
        }

        return team;
    }

    private void SeedPlatformWithLimit(int maxTenants)
    {
        DbContext.Platforms.Add(
            new PlatformEntity
            {
                Id = IdGenerator.CreateId(),
                Key = OpenIdResourceIds.Platform,
                ConcurrencyToken = string.Empty,
                SettingsConcurrencyToken = string.Empty,
                SettingsJson = JsonSerializer.SerializeToElement(
                    new Dictionary<string, object> { ["max_tenants"] = maxTenants }
                ),
            }
        );
        DbContext.SaveChanges();
    }

    private void SeedTenantWithIssuer(TeamEntity team, string tenantId, string? issuer)
    {
        DbContext
            .Set<TenantEntity>()
            .Add(
                new TenantEntity
                {
                    Id = IdGenerator.CreateId(),
                    TeamId = team.Id,
                    Team = team,
                    TenantId = tenantId,
                    NormalizedTenantId = tenantId.ToLowerInvariant(),
                    Issuer = issuer,
                    NormalizedIssuer = issuer?.ToLowerInvariant(),
                    NormalizedIssuerHost = null,
                    MtlsEndpointAliasAuthority = null,
                    NormalizedMtlsEndpointAliasHost = null,
                    ConcurrencyToken = string.Empty,
                    SettingsConcurrencyToken = string.Empty,
                    SecretsConcurrencyToken = string.Empty,
                    IsDisabled = false,
                    DisplayName = tenantId,
                    SettingsJson = JsonSerializer.SerializeToElement(
                        new Dictionary<string, object>()
                    ),
                    Secrets = [],
                }
            );
        DbContext.SaveChanges();
    }

    private void SeedRetiredTenant(string tenantId)
    {
        DbContext.RetiredTenants.Add(
            new RetiredTenantEntity
            {
                Id = IdGenerator.CreateId(),
                TenantId = tenantId,
                NormalizedTenantId = tenantId.ToLowerInvariant(),
            }
        );
        DbContext.SaveChanges();
    }

    private void SeedRetiredIssuer(string issuer, string tenantId)
    {
        DbContext.RetiredIssuers.Add(
            new RetiredIssuerEntity
            {
                Id = IdGenerator.CreateId(),
                Issuer = issuer,
                NormalizedIssuer = issuer.ToLowerInvariant(),
                TenantId = tenantId,
            }
        );
        DbContext.SaveChanges();
    }

    private static TenantReservation CreateReservation(
        string tenantId = TenantId,
        string issuer = Issuer,
        string? teamId = null
    ) =>
        new()
        {
            TenantId = tenantId,
            Issuer = issuer,
            TeamId = teamId,
            Cell = CellName,
        };

    #region ReserveAsync Tests

    [Fact]
    public async Task ReserveAsync_WhenNothingHoldsTheIdentifiers_IsReserved()
    {
        SetupUnscoped();
        SeedTenantWithIssuer(
            SeedTeamWithLimit(TeamId, maxTenants: null),
            OtherTenantId,
            OtherIssuer
        );

        var result = await Directory.ReserveAsync(
            CreateReservation(issuer: FreeIssuer, teamId: TeamId),
            CancellationToken.None
        );

        Assert.True(result.IsReserved);
        Assert.Null(result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenTenantIdIsRetired_ReturnsConflict()
    {
        SetupUnscoped();
        SeedRetiredTenant(TenantId);

        var result = await Directory.ReserveAsync(
            CreateReservation(tenantId: TenantId.ToUpperInvariant(), issuer: FreeIssuer),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Contains("never reissued", result.ConflictDetail);
        Assert.Contains(TenantId.ToUpperInvariant(), result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenTenantHoldsTheIdentifier_ReturnsConflict()
    {
        SetupUnscoped();
        SeedTenantWithIssuer(SeedTeamWithLimit(TeamId, maxTenants: null), TenantId, Issuer);

        var result = await Directory.ReserveAsync(
            CreateReservation(tenantId: TenantId.ToUpperInvariant(), issuer: FreeIssuer),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Contains("already has the identifier", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenAnotherTenantHoldsTheIssuer_ReturnsConflict()
    {
        SetupUnscoped();
        SeedTenantWithIssuer(
            SeedTeamWithLimit(TeamId, maxTenants: null),
            OtherTenantId,
            OtherIssuer
        );

        var result = await Directory.ReserveAsync(
            CreateReservation(issuer: OtherIssuer.ToUpperInvariant()),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Contains("already has the issuer", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenAnotherTenantRetiredTheIssuer_ReturnsConflict()
    {
        SetupUnscoped();
        SeedRetiredIssuer(OtherIssuer, OtherTenantId);

        var result = await Directory.ReserveAsync(
            CreateReservation(issuer: OtherIssuer.ToUpperInvariant()),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Contains("belonged to another tenant", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheSameTenantRetiredTheIssuer_IsReserved()
    {
        SetupUnscoped();
        SeedRetiredIssuer(Issuer, TenantId.ToUpperInvariant());

        var result = await Directory.ReserveAsync(CreateReservation(), CancellationToken.None);

        Assert.True(result.IsReserved);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheTeamHoldsItsMaximum_ReturnsConflict()
    {
        SetupUnscoped();
        var team = SeedTeamWithLimit(TeamId, maxTenants: 1);
        SeedTenantWithIssuer(team, OtherTenantId, OtherIssuer);

        var result = await Directory.ReserveAsync(
            CreateReservation(issuer: FreeIssuer, teamId: TeamId),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'platform' holds its maximum of 1 tenants.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheTeamIsBelowItsMaximum_IsReserved()
    {
        SetupUnscoped();
        var team = SeedTeamWithLimit(TeamId, maxTenants: 2);
        SeedTenantWithIssuer(team, OtherTenantId, OtherIssuer);

        var result = await Directory.ReserveAsync(
            CreateReservation(issuer: FreeIssuer, teamId: TeamId),
            CancellationToken.None
        );

        Assert.True(result.IsReserved);
    }

    [Fact]
    public async Task ReserveAsync_WhenAnAncestorTeamHoldsItsMaximumThroughItsDescendants_ReturnsConflict()
    {
        SetupUnscoped();
        var parent = SeedTeamWithLimit(TeamId, maxTenants: 1);
        var child = DbContext.SeedChildTeam(IdGenerator, parent, "platform-web");
        SeedTenantWithIssuer(child, OtherTenantId, OtherIssuer);

        var result = await Directory.ReserveAsync(
            CreateReservation(issuer: FreeIssuer, teamId: "platform-web"),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'platform' holds its maximum of 1 tenants.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenThePlatformLimitBoundsATeamWithoutItsOwn_ReturnsConflict()
    {
        SetupUnscoped();
        SeedPlatformWithLimit(1);
        var team = SeedTeamWithLimit(TeamId, maxTenants: null);
        SeedTenantWithIssuer(team, OtherTenantId, OtherIssuer);

        var result = await Directory.ReserveAsync(
            CreateReservation(issuer: FreeIssuer, teamId: TeamId),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'platform' holds its maximum of 1 tenants.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenThePlatformLimitIsLowerThanTheTeamsOwn_ReturnsConflict()
    {
        SetupUnscoped();
        SeedPlatformWithLimit(1);
        var team = SeedTeamWithLimit(TeamId, maxTenants: 5);
        SeedTenantWithIssuer(team, OtherTenantId, OtherIssuer);

        var result = await Directory.ReserveAsync(
            CreateReservation(issuer: FreeIssuer, teamId: TeamId),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'platform' holds its maximum of 1 tenants.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheTeamLimitIsLowerThanThePlatformLimit_ReturnsConflict()
    {
        SetupUnscoped();
        SeedPlatformWithLimit(5);
        var team = SeedTeamWithLimit(TeamId, maxTenants: 1);
        SeedTenantWithIssuer(team, OtherTenantId, OtherIssuer);

        var result = await Directory.ReserveAsync(
            CreateReservation(issuer: FreeIssuer, teamId: TeamId),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Equal("The team 'platform' holds its maximum of 1 tenants.", result.ConflictDetail);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheTeamIsBelowThePlatformLimit_IsReserved()
    {
        SetupUnscoped();
        SeedPlatformWithLimit(2);
        var team = SeedTeamWithLimit(TeamId, maxTenants: null);
        SeedTenantWithIssuer(team, OtherTenantId, OtherIssuer);

        var result = await Directory.ReserveAsync(
            CreateReservation(issuer: FreeIssuer, teamId: TeamId),
            CancellationToken.None
        );

        Assert.True(result.IsReserved);
    }

    [Fact]
    public async Task ReserveAsync_WhenTheTeamLimitIsUnreadable_ReturnsConflict()
    {
        SetupUnscoped();
        var team = SeedTeamWithLimit(TeamId, maxTenants: null);
        team.SettingsJson = JsonSerializer.SerializeToElement(
            new Dictionary<string, object> { ["max_tenants"] = "3" }
        );
        DbContext.SaveChanges();

        var result = await Directory.ReserveAsync(
            CreateReservation(issuer: FreeIssuer, teamId: TeamId),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The team 'platform' has a max_tenants setting the directory cannot read.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenThePlatformLimitIsNegative_ReturnsConflict()
    {
        SetupUnscoped();
        SeedPlatformWithLimit(-1);
        SeedTeamWithLimit(TeamId, maxTenants: null);

        var result = await Directory.ReserveAsync(
            CreateReservation(issuer: FreeIssuer, teamId: TeamId),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Equal(
            "The platform has a max_tenants setting the directory cannot read.",
            result.ConflictDetail
        );
    }

    [Fact]
    public async Task ReserveAsync_WhenTheTeamIsUnknown_IsReserved()
    {
        SetupUnscoped();

        var result = await Directory.ReserveAsync(
            CreateReservation(teamId: "missing"),
            CancellationToken.None
        );

        Assert.True(result.IsReserved);
    }

    #endregion

    #region PlaceAsync Tests

    [Fact]
    public async Task PlaceAsync_WhenCalled_IsReservedAndWritesNothing()
    {
        var result = await Directory.PlaceAsync(
            new TenantPlacement
            {
                TenantId = TenantId,
                Issuer = Issuer,
                Cell = CellName,
                Region = CellRegion,
            },
            CancellationToken.None
        );

        Assert.True(result.IsReserved);
        Assert.False(DbContext.ChangeTracker.HasChanges());
    }

    #endregion

    #region ReleaseAsync Tests

    [Fact]
    public async Task ReleaseAsync_WhenCalled_WritesNothing()
    {
        await Directory.ReleaseAsync(TenantId, CancellationToken.None);

        Assert.False(DbContext.ChangeTracker.HasChanges());
    }

    #endregion

    #region OfferMoveAsync Tests

    [Fact]
    public async Task OfferMoveAsync_WhenCalled_ConflictsBecauseTheCellRunsOnItsOwnAndWritesNothing()
    {
        var result = await Directory.OfferMoveAsync(
            TenantId,
            CellName,
            "cell-b",
            CancellationToken.None
        );

        Assert.False(result.IsOffered);
        Assert.Equal(
            $"The cell '{CellName}' runs on its own, so it has no other cell to offer the tenant '{TenantId}' to.",
            result.ConflictDetail
        );
        Assert.False(DbContext.ChangeTracker.HasChanges());
    }

    #endregion

    #region MoveAsync Tests

    [Fact]
    public async Task MoveAsync_WhenCalled_ConflictsBecauseTheCellRunsOnItsOwnAndWritesNothing()
    {
        var result = await Directory.MoveAsync(
            TenantId,
            CellName,
            "cell-b",
            CancellationToken.None
        );

        Assert.False(result.IsMoved);
        Assert.Equal(
            $"The cell '{CellName}' runs on its own, so it has no other cell to move the tenant '{TenantId}' to.",
            result.ConflictDetail
        );
        Assert.False(DbContext.ChangeTracker.HasChanges());
    }

    #endregion

    #region RetireAsync Tests

    private OpenIdDbContext CreateFreshContext() =>
        Provider.GetRequiredService<IDbContextFactory<OpenIdDbContext>>().CreateDbContext();

    private static TenantRetirement CreateRetirement(
        string tenantId = TenantId,
        string? issuer = Issuer
    ) => new() { TenantId = tenantId, Issuer = issuer };

    [Fact]
    public async Task RetireAsync_WhenTheRetirementHasAnIssuer_RetiresTheIdentifierAndTheIssuerWithoutACallersSave()
    {
        SetupUnscoped();

        await Directory.RetireAsync(
            CreateRetirement(TenantId.ToUpperInvariant()),
            CancellationToken.None
        );

        await using var fresh = CreateFreshContext();
        var retiredTenant = Assert.Single(await fresh.RetiredTenants.ToListAsync());
        Assert.Equal(TenantId.ToUpperInvariant(), retiredTenant.TenantId);
        Assert.Equal(TenantId, retiredTenant.NormalizedTenantId);
        var retiredIssuer = Assert.Single(await fresh.RetiredIssuers.ToListAsync());
        Assert.Equal(Issuer, retiredIssuer.Issuer);
        Assert.Equal(TenantId.ToUpperInvariant(), retiredIssuer.TenantId);
    }

    [Fact]
    public async Task RetireAsync_WhenTheRetirementHasNoIssuer_RetiresOnlyTheIdentifier()
    {
        SetupUnscoped();

        await Directory.RetireAsync(CreateRetirement(issuer: null), CancellationToken.None);

        await using var fresh = CreateFreshContext();
        Assert.Single(await fresh.RetiredTenants.ToListAsync());
        Assert.Empty(await fresh.RetiredIssuers.ToListAsync());
    }

    [Fact]
    public async Task RetireAsync_WhenTheTenantRowIsGone_RetiresFromTheRecordAlone()
    {
        SetupUnscoped();

        await Directory.RetireAsync(CreateRetirement(), CancellationToken.None);

        await using var fresh = CreateFreshContext();
        var retiredTenant = Assert.Single(await fresh.RetiredTenants.ToListAsync());
        Assert.Equal(TenantId, retiredTenant.TenantId);
        var retiredIssuer = Assert.Single(await fresh.RetiredIssuers.ToListAsync());
        Assert.Equal(Issuer, retiredIssuer.Issuer);
    }

    [Fact]
    public async Task RetireAsync_WhenATenantRowStillHoldsTheIdentifier_RetiresTheRecordsIssuerAndNotTheRowsIssuer()
    {
        SetupUnscoped();
        SeedTenantWithIssuer(SeedTeamWithLimit(TeamId, maxTenants: null), TenantId, OtherIssuer);

        await Directory.RetireAsync(CreateRetirement(issuer: Issuer), CancellationToken.None);

        await using var fresh = CreateFreshContext();
        var retiredIssuer = Assert.Single(await fresh.RetiredIssuers.ToListAsync());
        Assert.Equal(Issuer, retiredIssuer.Issuer);
    }

    [Fact]
    public async Task RetireAsync_WhenCalledTwice_RetiresOnce()
    {
        SetupUnscoped();

        await Directory.RetireAsync(CreateRetirement(), CancellationToken.None);
        await Directory.RetireAsync(CreateRetirement(), CancellationToken.None);

        await using var fresh = CreateFreshContext();
        Assert.Single(await fresh.RetiredTenants.ToListAsync());
        Assert.Single(await fresh.RetiredIssuers.ToListAsync());
    }

    [Fact]
    public async Task RetireAsync_WhenTheIdentifierIsAlreadyRetired_LeavesTheRetiredRowAsItIs()
    {
        SetupUnscoped();
        SeedRetiredTenant(TenantId);
        await using var before = CreateFreshContext();
        var retiredId = Assert.Single(await before.RetiredTenants.ToListAsync()).Id;

        await Directory.RetireAsync(CreateRetirement(), CancellationToken.None);

        await using var fresh = CreateFreshContext();
        var retiredTenant = Assert.Single(await fresh.RetiredTenants.ToListAsync());
        Assert.Equal(retiredId, retiredTenant.Id);
    }

    #endregion

    #region ChangeIssuerAsync Tests

    private static TenantIssuerChange CreateChange(
        string? previousIssuer = Issuer,
        string? issuer = FreeIssuer,
        string tenantId = TenantId
    ) =>
        new()
        {
            TenantId = tenantId,
            PreviousIssuer = previousIssuer,
            Issuer = issuer,
        };

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheIssuerIsFree_IsReservedAndRetiresThePreviousIssuer()
    {
        SetupUnscoped();
        SeedTenantWithIssuer(SeedTeamWithLimit(TeamId, maxTenants: null), TenantId, Issuer);

        var result = await Directory.ChangeIssuerAsync(CreateChange(), CancellationToken.None);

        Assert.True(result.IsReserved);
        await using var fresh = CreateFreshContext();
        var retiredIssuer = Assert.Single(await fresh.RetiredIssuers.ToListAsync());
        Assert.Equal(Issuer, retiredIssuer.Issuer);
        Assert.Equal(TenantId, retiredIssuer.TenantId);
        Assert.Empty(await fresh.RetiredTenants.ToListAsync());
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenAnotherTenantHoldsTheIssuer_ReturnsConflictAndRetiresNothing()
    {
        SetupUnscoped();
        var team = SeedTeamWithLimit(TeamId, maxTenants: null);
        SeedTenantWithIssuer(team, TenantId, Issuer);
        SeedTenantWithIssuer(team, OtherTenantId, OtherIssuer);

        var result = await Directory.ChangeIssuerAsync(
            CreateChange(issuer: OtherIssuer.ToUpperInvariant()),
            CancellationToken.None
        );

        Assert.False(result.IsReserved);
        Assert.Contains("already has the issuer", result.ConflictDetail);
        await using var fresh = CreateFreshContext();
        Assert.Empty(await fresh.RetiredIssuers.ToListAsync());
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenAnotherTenantRetiredTheIssuer_ReturnsConflict()
    {
        SetupUnscoped();
        SeedRetiredIssuer(FreeIssuer, OtherTenantId);

        var result = await Directory.ChangeIssuerAsync(CreateChange(), CancellationToken.None);

        Assert.False(result.IsReserved);
        Assert.Contains("belonged to another tenant", result.ConflictDetail);
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheTenantTakesBackAnIssuerItRetired_IsReserved()
    {
        SetupUnscoped();
        SeedTenantWithIssuer(SeedTeamWithLimit(TeamId, maxTenants: null), TenantId, OtherIssuer);
        SeedRetiredIssuer(Issuer, TenantId.ToUpperInvariant());

        var result = await Directory.ChangeIssuerAsync(
            CreateChange(previousIssuer: OtherIssuer, issuer: Issuer),
            CancellationToken.None
        );

        Assert.True(result.IsReserved);
        await using var fresh = CreateFreshContext();
        var retiredIssuers = (await fresh.RetiredIssuers.ToListAsync())
            .Select(entity => entity.Issuer)
            .Order()
            .ToList();
        Assert.Equal([Issuer, OtherIssuer], retiredIssuers);
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheTenantTakesTheIssuerItHolds_IsReservedAndRetiresNothing()
    {
        SetupUnscoped();
        SeedTenantWithIssuer(SeedTeamWithLimit(TeamId, maxTenants: null), TenantId, Issuer);

        var result = await Directory.ChangeIssuerAsync(
            CreateChange(previousIssuer: Issuer, issuer: Issuer.ToUpperInvariant()),
            CancellationToken.None
        );

        Assert.True(result.IsReserved);
        await using var fresh = CreateFreshContext();
        Assert.Empty(await fresh.RetiredIssuers.ToListAsync());
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenTheTenantTakesNoIssuer_RetiresThePreviousIssuer()
    {
        SetupUnscoped();

        var result = await Directory.ChangeIssuerAsync(
            CreateChange(issuer: null),
            CancellationToken.None
        );

        Assert.True(result.IsReserved);
        await using var fresh = CreateFreshContext();
        Assert.Equal(Issuer, Assert.Single(await fresh.RetiredIssuers.ToListAsync()).Issuer);
    }

    [Fact]
    public async Task ChangeIssuerAsync_WhenThePreviousIssuerIsAlreadyRetired_KeepsASingleRetiredRow()
    {
        SetupUnscoped();
        SeedRetiredIssuer(Issuer, TenantId);

        var result = await Directory.ChangeIssuerAsync(CreateChange(), CancellationToken.None);

        Assert.True(result.IsReserved);
        await using var fresh = CreateFreshContext();
        Assert.Single(await fresh.RetiredIssuers.ToListAsync());
    }

    #endregion

    #region GetPlacementOrDefaultAsync Tests

    [Fact]
    public async Task GetPlacementOrDefaultAsync_WhenTheTenantExists_ReturnsItsPlacementInThisCell()
    {
        SetupUnscoped();
        SeedTenantWithIssuer(SeedTeamWithLimit(TeamId, maxTenants: null), TenantId, Issuer);

        var placement = await Directory.GetPlacementOrDefaultAsync(
            TenantId.ToUpperInvariant(),
            CancellationToken.None
        );

        Assert.NotNull(placement);
        Assert.Equal(TenantId, placement.TenantId);
        Assert.Equal(Issuer, placement.Issuer);
        Assert.Equal(CellName, placement.Cell);
        Assert.Equal(CellRegion, placement.Region);
    }

    [Fact]
    public async Task GetPlacementOrDefaultAsync_WhenTheTenantHasNoIssuer_ReturnsAnEmptyIssuer()
    {
        SetupUnscoped();
        SeedTenantWithIssuer(SeedTeamWithLimit(TeamId, maxTenants: null), TenantId, issuer: null);

        var placement = await Directory.GetPlacementOrDefaultAsync(
            TenantId,
            CancellationToken.None
        );

        Assert.NotNull(placement);
        Assert.Equal(string.Empty, placement.Issuer);
    }

    [Fact]
    public async Task GetPlacementOrDefaultAsync_WhenTheTenantIsUnknown_ReturnsNull()
    {
        SetupUnscoped();

        var placement = await Directory.GetPlacementOrDefaultAsync(
            TenantId,
            CancellationToken.None
        );

        Assert.Null(placement);
    }

    #endregion

    #region IsTenantIdRetiredAsync Tests

    [Fact]
    public async Task IsTenantIdRetiredAsync_WhenTheIdentifierIsRetired_ReturnsTrue()
    {
        SetupUnscoped();
        SeedRetiredTenant(TenantId);

        var isRetired = await Directory.IsTenantIdRetiredAsync(
            TenantId.ToUpperInvariant(),
            CancellationToken.None
        );

        Assert.True(isRetired);
    }

    [Fact]
    public async Task IsTenantIdRetiredAsync_WhenAnotherIdentifierIsRetired_ReturnsFalse()
    {
        SetupUnscoped();
        SeedRetiredTenant(OtherTenantId);

        var isRetired = await Directory.IsTenantIdRetiredAsync(TenantId, CancellationToken.None);

        Assert.False(isRetired);
    }

    #endregion

    #region GetRetiredIssuerHolderOrDefaultAsync Tests

    [Fact]
    public async Task GetRetiredIssuerHolderOrDefaultAsync_WhenTheIssuerIsRetired_ReturnsTheHolder()
    {
        SetupUnscoped();
        SeedRetiredIssuer(Issuer, TenantId);

        var holder = await Directory.GetRetiredIssuerHolderOrDefaultAsync(
            Issuer.ToUpperInvariant(),
            CancellationToken.None
        );

        Assert.Equal(TenantId, holder);
    }

    [Fact]
    public async Task GetRetiredIssuerHolderOrDefaultAsync_WhenTheIssuerIsNotRetired_ReturnsNull()
    {
        SetupUnscoped();
        SeedRetiredIssuer(OtherIssuer, OtherTenantId);

        var holder = await Directory.GetRetiredIssuerHolderOrDefaultAsync(
            Issuer,
            CancellationToken.None
        );

        Assert.Null(holder);
    }

    #endregion
}
