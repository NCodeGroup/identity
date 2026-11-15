// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using Moq.Protected;
using NCode.Disposables;
using NCode.Identity.OpenId.Core.Tests;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.SecretProtection;
using NCode.Identity.OpenId.Teams;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.PropertyBag;
using Xunit;

namespace NCode.Identity.OpenId.Core.SecretProtection;

public sealed class TeamChainSecretOwnerParentResolverTests : BaseTests
{
    private const string UnscopedReason = "secret owner ancestors";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITenantStore> MockTenantStore { get; }
    private Mock<ITeamStore> MockTeamStore { get; }
    private Mock<IOpenIdTenantCache> MockTenantCache { get; }
    private Mock<IOpenIdTeamCache> MockTeamCache { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private TeamChainSecretOwnerParentResolver Resolver { get; }

    public TeamChainSecretOwnerParentResolverTests()
    {
        MockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockTenantStore = CreateStrictMock<ITenantStore>();
        MockTeamStore = CreateStrictMock<ITeamStore>();
        MockTenantCache = CreateStrictMock<IOpenIdTenantCache>();
        MockTeamCache = CreateStrictMock<IOpenIdTeamCache>();
        MockAmbientTenantAccessor = CreateStrictMock<IAmbientTenantAccessor>();

        Resolver = new TeamChainSecretOwnerParentResolver(
            MockStoreManagerFactory.Object,
            MockTenantCache.Object,
            MockTeamCache.Object,
            MockAmbientTenantAccessor.Object
        );
    }

    #region GetAncestorsAsync Tests

    [Fact]
    public async Task GetAncestorsAsync_ThePlatform_HasNoAncestors()
    {
        // Strict mocks: the platform owner reads nothing, so any read would fail the test.
        var ancestors = await Resolver.GetAncestorsAsync(
            SecretOwner.Platform,
            CancellationToken.None
        );

        Assert.Empty(ancestors);
    }

    [Fact]
    public async Task GetAncestorsAsync_AnUnknownOwnerType_IsThePlatformOnly()
    {
        var ancestors = await Resolver.GetAncestorsAsync(
            new SecretOwner("service", "billing"),
            CancellationToken.None
        );

        Assert.Equal([SecretOwner.Platform], ancestors);
    }

    [Fact]
    public async Task GetAncestorsAsync_ATenantUnderANestedTeam_IsItsTeamThenTheTeamsAboveThenThePlatform()
    {
        SetupUnscoped();
        SetupTenantCacheMiss("tenant-a");
        SetupStoreManager();
        SetupTenant("tenant-a", teamId: "acme-web");
        SetupCacheMiss("acme-web");
        SetupStoredTeam("acme-web", ancestorTeamIds: ["acme", "acme-eu"]);

        var ancestors = await Resolver.GetAncestorsAsync(
            SecretOwner.ForTenant("tenant-a"),
            CancellationToken.None
        );

        Assert.Equal(
            [
                new SecretOwner(SecretOwnerTypes.Team, "acme-web"),
                new SecretOwner(SecretOwnerTypes.Team, "acme-eu"),
                new SecretOwner(SecretOwnerTypes.Team, "acme"),
                SecretOwner.Platform,
            ],
            ancestors
        );
    }

    [Fact]
    public async Task GetAncestorsAsync_ATenantWhoseTeamIsCached_ReadsTheTeamPathFromTheCache()
    {
        SetupUnscoped();
        SetupTenantCacheMiss("tenant-a");
        SetupStoreManager();
        SetupTenant("tenant-a", teamId: "acme-web");
        var team = CreateMockTeam("/acme/acme-web/");
        await using var owner = AsyncSharedReference.Create(team.Object);
        SetupCacheHit("acme-web", owner.AddReference());

        var ancestors = await Resolver.GetAncestorsAsync(
            SecretOwner.ForTenant("tenant-a"),
            CancellationToken.None
        );

        Assert.Equal(
            [
                new SecretOwner(SecretOwnerTypes.Team, "acme-web"),
                new SecretOwner(SecretOwnerTypes.Team, "acme"),
                SecretOwner.Platform,
            ],
            ancestors
        );
    }

    [Fact]
    public async Task GetAncestorsAsync_WhenTheTenantIsCached_DoesNotReadTheTenantStore()
    {
        // Strict mocks: no store manager setup, so creating one would fail the test.
        SetupUnscoped();
        var tenant = CreateMockTenant("acme-web");
        await using var tenantOwner = AsyncSharedReference.Create(tenant.Object);
        SetupTenantCacheHit("tenant-a", tenantOwner.AddReference());
        var team = CreateMockTeam("/acme/acme-web/");
        await using var teamOwner = AsyncSharedReference.Create(team.Object);
        SetupCacheHit("acme-web", teamOwner.AddReference());

        var ancestors = await Resolver.GetAncestorsAsync(
            SecretOwner.ForTenant("tenant-a"),
            CancellationToken.None
        );

        Assert.Equal(
            [
                new SecretOwner(SecretOwnerTypes.Team, "acme-web"),
                new SecretOwner(SecretOwnerTypes.Team, "acme"),
                SecretOwner.Platform,
            ],
            ancestors
        );
    }

    [Fact]
    public async Task GetAncestorsAsync_ATenantThatIsNotStored_IsThePlatformOnly()
    {
        SetupUnscoped();
        SetupTenantCacheMiss("tenant-a");
        SetupStoreManager();
        SetupTenant("tenant-a", teamId: null);

        var ancestors = await Resolver.GetAncestorsAsync(
            SecretOwner.ForTenant("tenant-a"),
            CancellationToken.None
        );

        Assert.Equal([SecretOwner.Platform], ancestors);
    }

    [Fact]
    public async Task GetAncestorsAsync_ANestedTeam_IsTheTeamsAboveItThenThePlatform()
    {
        SetupUnscoped();
        SetupCacheMiss("acme-web");
        SetupStoreManager();
        SetupStoredTeam("acme-web", ancestorTeamIds: ["acme", "acme-eu"]);

        var ancestors = await Resolver.GetAncestorsAsync(
            new SecretOwner(SecretOwnerTypes.Team, "acme-web"),
            CancellationToken.None
        );

        Assert.Equal(
            [
                new SecretOwner(SecretOwnerTypes.Team, "acme-eu"),
                new SecretOwner(SecretOwnerTypes.Team, "acme"),
                SecretOwner.Platform,
            ],
            ancestors
        );
    }

    [Fact]
    public async Task GetAncestorsAsync_ATeamThatIsCached_ReadsTheTeamPathFromTheCache()
    {
        SetupUnscoped();
        var team = CreateMockTeam("/acme/acme-eu/acme-web/");
        await using var owner = AsyncSharedReference.Create(team.Object);
        SetupCacheHit("acme-web", owner.AddReference());

        var ancestors = await Resolver.GetAncestorsAsync(
            new SecretOwner(SecretOwnerTypes.Team, "acme-web"),
            CancellationToken.None
        );

        Assert.Equal(
            [
                new SecretOwner(SecretOwnerTypes.Team, "acme-eu"),
                new SecretOwner(SecretOwnerTypes.Team, "acme"),
                SecretOwner.Platform,
            ],
            ancestors
        );
    }

    [Fact]
    public async Task GetAncestorsAsync_ATopLevelTeam_IsThePlatformOnly()
    {
        SetupUnscoped();
        SetupCacheMiss("acme");
        SetupStoreManager();
        SetupStoredTeam("acme", ancestorTeamIds: []);

        var ancestors = await Resolver.GetAncestorsAsync(
            new SecretOwner(SecretOwnerTypes.Team, "acme"),
            CancellationToken.None
        );

        Assert.Equal([SecretOwner.Platform], ancestors);
    }

    [Fact]
    public async Task GetAncestorsAsync_ATeamThatIsNotStored_IsThePlatformOnly()
    {
        SetupUnscoped();
        SetupCacheMiss("acme");
        SetupStoreManager();
        SetupStoredTeam("acme", ancestorTeamIds: null);

        var ancestors = await Resolver.GetAncestorsAsync(
            new SecretOwner(SecretOwnerTypes.Team, "acme"),
            CancellationToken.None
        );

        Assert.Equal([SecretOwner.Platform], ancestors);
    }

    [Fact]
    public async Task GetAncestorsAsync_AMixedCaseTeam_NormalizesTheTeamOwners()
    {
        SetupUnscoped();
        SetupTenantCacheMiss("Tenant-A");
        SetupStoreManager();
        SetupTenant("Tenant-A", teamId: "Acme-Web");
        SetupCacheMiss("Acme-Web");
        SetupStoredTeam("Acme-Web", ancestorTeamIds: ["Acme"]);

        var ancestors = await Resolver.GetAncestorsAsync(
            SecretOwner.ForTenant("Tenant-A"),
            CancellationToken.None
        );

        Assert.Equal(
            [
                new SecretOwner(SecretOwnerTypes.Team, "acme-web"),
                new SecretOwner(SecretOwnerTypes.Team, "acme"),
                SecretOwner.Platform,
            ],
            ancestors
        );
    }

    #endregion

    private static Mock<OpenIdTenant> CreateMockTenant(string teamId)
    {
        var tenant = new Mock<OpenIdTenant>(MockBehavior.Strict);
        tenant.Setup(x => x.TeamId).Returns(teamId).Verifiable();
        tenant.Protected().Setup<ValueTask>("DisposeAsyncCore").Returns(ValueTask.CompletedTask);
        return tenant;
    }

    private static Mock<OpenIdTeam> CreateMockTeam(string path)
    {
        var team = new Mock<OpenIdTeam>(MockBehavior.Strict);
        team.Setup(x => x.Path).Returns(path).Verifiable();
        team.Protected().Setup<ValueTask>("DisposeAsyncCore").Returns(ValueTask.CompletedTask);
        return team;
    }

    private void SetupUnscoped() =>
        MockAmbientTenantAccessor
            .Setup(x => x.BeginUnscoped(UnscopedReason))
            .Returns(CreateLooseMock<IDisposable>().Object)
            .Verifiable();

    private void SetupStoreManager()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
    }

    private void SetupTenant(string tenantId, string? teamId)
    {
        MockStoreManager
            .Setup(x => x.GetStore<ITenantStore>())
            .Returns(MockTenantStore.Object)
            .Verifiable();
        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(teamId is null ? null : CreateTenant(tenantId, teamId))
            .Verifiable();
    }

    private void SetupStoredTeam(string teamId, IReadOnlyList<string>? ancestorTeamIds)
    {
        MockStoreManager
            .Setup(x => x.GetStore<ITeamStore>())
            .Returns(MockTeamStore.Object)
            .Verifiable();
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync(teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                ancestorTeamIds is null
                    ? null
                    : new PersistedTeamBuilder
                    {
                        TeamId = teamId,
                        AncestorTeamIds = ancestorTeamIds,
                    }.Build()
            )
            .Verifiable();
    }

    private void SetupTenantCacheMiss(string tenantId)
    {
        AsyncSharedReferenceLease<OpenIdTenant> empty = default;
        MockTenantCache
            .Setup(x =>
                x.TryGetAsync(tenantId, It.IsAny<IPropertyBag>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(empty)
            .Verifiable();
    }

    private void SetupTenantCacheHit(
        string tenantId,
        AsyncSharedReferenceLease<OpenIdTenant> lease
    ) =>
        MockTenantCache
            .Setup(x =>
                x.TryGetAsync(tenantId, It.IsAny<IPropertyBag>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(lease)
            .Verifiable();

    private void SetupCacheMiss(string teamId)
    {
        AsyncSharedReferenceLease<OpenIdTeam> empty = default;
        MockTeamCache
            .Setup(x =>
                x.TryGetAsync(teamId, It.IsAny<IPropertyBag>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(empty)
            .Verifiable();
    }

    private void SetupCacheHit(string teamId, AsyncSharedReferenceLease<OpenIdTeam> lease) =>
        MockTeamCache
            .Setup(x =>
                x.TryGetAsync(teamId, It.IsAny<IPropertyBag>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(lease)
            .Verifiable();

    private static PersistedTenant CreateTenant(string tenantId, string teamId) =>
        new PersistedTenantBuilder { TenantId = tenantId, TeamId = teamId }.Build();
}
