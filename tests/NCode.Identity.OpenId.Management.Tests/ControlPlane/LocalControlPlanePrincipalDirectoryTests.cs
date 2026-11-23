// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Principals;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.ControlPlane;

public sealed class LocalControlPlanePrincipalDirectoryTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private const string RootTenantId = "control-plane";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<ITenantResolver> MockTenantResolver { get; }
    private Mock<IStoreManager> MockStoreManager { get; }

    public LocalControlPlanePrincipalDirectoryTests()
    {
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockAmbientTenantAccessor = MockRepository.Create<IAmbientTenantAccessor>();
        MockTenantResolver = MockRepository.Create<ITenantResolver>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
    }

    private LocalControlPlanePrincipalDirectory CreateDirectory(
        params IPrincipalDescriber[] describers
    ) =>
        new(
            MockStoreManagerFactory.Object,
            MockAmbientTenantAccessor.Object,
            MockTenantResolver.Object,
            describers
        );

    private void SetupScope()
    {
        MockTenantResolver
            .Setup(x => x.GetControlPlaneTenantId())
            .Returns(RootTenantId)
            .Verifiable();

        var scope = MockRepository.Create<IDisposable>();
        scope.Setup(x => x.Dispose()).Verifiable();
        MockAmbientTenantAccessor
            .Setup(x => x.BeginScope(RootTenantId))
            .Returns(scope.Object)
            .Verifiable();

        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
    }

    private static PrincipalSummaryResource CreateSummary(string principalId, string displayName) =>
        new()
        {
            PrincipalId = principalId,
            PrincipalType = PrincipalTypes.LocalAccount,
            DisplayName = displayName,
            UserName = null,
        };

    private Mock<IPrincipalDescriber> CreateDescriber(
        ControlPlanePrincipalQuery query,
        params PrincipalSummaryResource[] summaries
    )
    {
        var describer = MockRepository.Create<IPrincipalDescriber>();
        describer
            .Setup(x => x.FindAsync(MockStoreManager.Object, query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaries)
            .Verifiable();
        return describer;
    }

    [Fact]
    public async Task FindAsync_WithAnEmptyQuery_SkipsTheStore()
    {
        var describer = MockRepository.Create<IPrincipalDescriber>();
        var directory = CreateDirectory(describer.Object);

        var result = await directory.FindAsync(
            new ControlPlanePrincipalQuery(),
            CancellationToken.None
        );

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_MergesTheDescribersAndTheFirstWins()
    {
        SetupScope();
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1", "p2"] };
        var first = CreateDescriber(query, CreateSummary("p1", "First"));
        var second = CreateDescriber(
            query,
            CreateSummary("p1", "Second"),
            CreateSummary("p2", "Other")
        );
        var directory = CreateDirectory(first.Object, second.Object);

        var result = await directory.FindAsync(query, CancellationToken.None);

        Assert.Collection(
            result,
            summary =>
            {
                Assert.Equal("p1", summary.PrincipalId);
                Assert.Equal("First", summary.DisplayName);
            },
            summary =>
            {
                Assert.Equal("p2", summary.PrincipalId);
                Assert.Equal("Other", summary.DisplayName);
            }
        );
    }

    private (
        Mock<IFederatedPrincipalStore> Principals,
        Mock<IFederatedIdentityStore> Identities
    ) SetupFederatedStores()
    {
        var principals = MockRepository.Create<IFederatedPrincipalStore>();
        var identities = MockRepository.Create<IFederatedIdentityStore>();
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedPrincipalStore>())
            .Returns(principals.Object);
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(identities.Object);
        return (principals, identities);
    }

    private static PersistedFederatedPrincipal CreateFederatedPrincipal(string principalId) =>
        new()
        {
            TenantId = RootTenantId,
            PrincipalId = principalId,
            ConcurrencyToken = "token",
        };

    private static PersistedFederatedIdentity CreateFederatedIdentity(
        string principalId,
        string issuer,
        string subject
    ) =>
        new()
        {
            TenantId = RootTenantId,
            FederatedIdentityId = $"{issuer}|{subject}",
            PrincipalId = principalId,
            Issuer = issuer,
            Subject = subject,
            JoinKey = null,
            ConcurrencyToken = "token",
        };

    [Fact]
    public async Task FindAsync_FederatedFallback_DescribesAnUnknownPrincipalByItsFirstIdentity()
    {
        SetupScope();
        var (principals, identities) = SetupFederatedStores();
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1", "p1"] };
        var describer = CreateDescriber(query);
        principals
            .Setup(x => x.GetOrDefaultAsync("p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFederatedPrincipal("p1"))
            .Verifiable();
        identities
            .Setup(x => x.GetByPrincipalAsync("p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                CreateFederatedIdentity("p1", "https://b.example", "a"),
                CreateFederatedIdentity("p1", "https://a.example", "z"),
                CreateFederatedIdentity("p1", "https://a.example", "y"),
            ])
            .Verifiable();
        var directory = CreateDirectory(describer.Object);

        var result = await directory.FindAsync(query, CancellationToken.None);

        var summary = Assert.Single(result);
        Assert.Equal("p1", summary.PrincipalId);
        Assert.Equal(PrincipalTypes.FederatedUser, summary.PrincipalType);
        Assert.Equal("y (https://a.example)", summary.DisplayName);
        Assert.Null(summary.UserName);
    }

    [Fact]
    public async Task FindAsync_FederatedFallback_NamesAPrincipalWithoutIdentitiesByItsId()
    {
        SetupScope();
        var (principals, identities) = SetupFederatedStores();
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1"] };
        var describer = CreateDescriber(query);
        principals
            .Setup(x => x.GetOrDefaultAsync("p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateFederatedPrincipal("p1"))
            .Verifiable();
        identities
            .Setup(x => x.GetByPrincipalAsync("p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .Verifiable();
        var directory = CreateDirectory(describer.Object);

        var result = await directory.FindAsync(query, CancellationToken.None);

        var summary = Assert.Single(result);
        Assert.Equal(PrincipalTypes.FederatedUser, summary.PrincipalType);
        Assert.Equal("p1", summary.DisplayName);
    }

    [Fact]
    public async Task FindAsync_FederatedFallback_SkipsAPrincipalADescriberKnows()
    {
        SetupScope();
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1"] };
        var describer = CreateDescriber(query, CreateSummary("p1", "Alice"));
        var directory = CreateDirectory(describer.Object);

        var result = await directory.FindAsync(query, CancellationToken.None);

        var summary = Assert.Single(result);
        Assert.Equal(PrincipalTypes.LocalAccount, summary.PrincipalType);
    }

    [Fact]
    public async Task FindAsync_FederatedFallback_SkipsAnUnknownPrincipal()
    {
        SetupScope();
        var (principals, _) = SetupFederatedStores();
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["missing"] };
        var describer = CreateDescriber(query);
        principals
            .Setup(x => x.GetOrDefaultAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedFederatedPrincipal?)null)
            .Verifiable();
        var directory = CreateDirectory(describer.Object);

        var result = await directory.FindAsync(query, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_WithoutDescribers_ReturnsNothing()
    {
        SetupScope();
        var directory = CreateDirectory();

        var result = await directory.FindAsync(
            new ControlPlanePrincipalQuery { UserNames = ["alice"] },
            CancellationToken.None
        );

        Assert.Empty(result);
    }
}
