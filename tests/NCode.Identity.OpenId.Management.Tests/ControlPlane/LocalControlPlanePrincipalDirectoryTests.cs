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

    private static PrincipalSummaryResource CreateSummary(
        string principalId,
        string displayName,
        string? userName = null
    ) =>
        new()
        {
            PrincipalId = principalId,
            PrincipalType = PrincipalTypes.LocalAccount,
            DisplayName = displayName,
            UserName = userName,
        };

    private Mock<IPrincipalDescriber> CreateDescriber(
        string[] principalIds,
        string[] userNames,
        params PrincipalSummaryResource[] summaries
    )
    {
        var describer = MockRepository.Create<IPrincipalDescriber>();
        describer
            .Setup(x =>
                x.FindAsync(
                    MockStoreManager.Object,
                    It.Is<ControlPlanePrincipalQuery>(q =>
                        q.PrincipalIds.SequenceEqual(principalIds)
                        && q.UserNames.SequenceEqual(userNames)
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(summaries)
            .Verifiable();
        return describer;
    }

    private Mock<IFederatedPrincipalStore> SetupFederatedPrincipalStore()
    {
        var principals = MockRepository.Create<IFederatedPrincipalStore>();
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedPrincipalStore>())
            .Returns(principals.Object)
            .Verifiable();
        return principals;
    }

    private Mock<IFederatedIdentityStore> SetupFederatedIdentityStore()
    {
        var identities = MockRepository.Create<IFederatedIdentityStore>();
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(identities.Object)
            .Verifiable();
        return identities;
    }

    private static void SetupPrincipalsRead(
        Mock<IFederatedPrincipalStore> store,
        string[] principalIds,
        params PersistedFederatedPrincipal[] found
    ) =>
        store
            .Setup(x =>
                x.GetByIdsAsync(
                    It.Is<IReadOnlyCollection<string>>(c => c.SequenceEqual(principalIds)),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(found)
            .Verifiable();

    private static void SetupIdentitiesRead(
        Mock<IFederatedIdentityStore> store,
        string[] principalIds,
        params PersistedFederatedIdentity[] found
    ) =>
        store
            .Setup(x =>
                x.GetByPrincipalsAsync(
                    It.Is<IReadOnlyCollection<string>>(c => c.SequenceEqual(principalIds)),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(found)
            .Verifiable();

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

    #region FindAsync Tests

    [Fact]
    public async Task FindAsync_WhenTheQueryIsEmpty_OpensNoStore()
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
    public async Task FindAsync_WhenADescriberNamesSomeNames_AsksTheNextOnlyAboutTheRest()
    {
        SetupScope();
        var query = new ControlPlanePrincipalQuery
        {
            PrincipalIds = ["p1", "p2", "P1"],
            UserNames = ["alice"],
        };
        var first = CreateDescriber(["p1", "p2"], ["alice"], CreateSummary("p1", "First", "ALICE"));
        var second = CreateDescriber(["p2"], [], CreateSummary("p2", "Other"));
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

    [Fact]
    public async Task FindAsync_WhenEveryNameIsDescribed_AsksNoFurtherDescriberAndReadsNoStore()
    {
        SetupScope();
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1"] };
        var first = CreateDescriber(["p1"], [], CreateSummary("p1", "Alice"));
        var second = MockRepository.Create<IPrincipalDescriber>();
        var directory = CreateDirectory(first.Object, second.Object);

        var result = await directory.FindAsync(query, CancellationToken.None);

        var summary = Assert.Single(result);
        Assert.Equal(PrincipalTypes.LocalAccount, summary.PrincipalType);
    }

    [Fact]
    public async Task FindAsync_WhenNoDescriberKnowsFederatedPrincipals_ReadsTheFederatedStoresOnce()
    {
        SetupScope();
        var principals = SetupFederatedPrincipalStore();
        var identities = SetupFederatedIdentityStore();
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1", "p2"] };
        var describer = CreateDescriber(["p1", "p2"], []);
        SetupPrincipalsRead(
            principals,
            ["p1", "p2"],
            CreateFederatedPrincipal("p1"),
            CreateFederatedPrincipal("p2")
        );
        SetupIdentitiesRead(
            identities,
            ["p1", "p2"],
            CreateFederatedIdentity("p1", "https://b.example", "a"),
            CreateFederatedIdentity("p2", "https://c.example", "x"),
            CreateFederatedIdentity("p1", "https://a.example", "z"),
            CreateFederatedIdentity("p1", "https://a.example", "y")
        );
        var directory = CreateDirectory(describer.Object);

        var result = await directory.FindAsync(query, CancellationToken.None);

        Assert.Collection(
            result,
            summary =>
            {
                Assert.Equal("p1", summary.PrincipalId);
                Assert.Equal(PrincipalTypes.FederatedUser, summary.PrincipalType);
                Assert.Equal("y (https://a.example)", summary.DisplayName);
                Assert.Null(summary.UserName);
            },
            summary =>
            {
                Assert.Equal("p2", summary.PrincipalId);
                Assert.Equal("x (https://c.example)", summary.DisplayName);
            }
        );
    }

    [Fact]
    public async Task FindAsync_WhenAFederatedPrincipalHasNoIdentity_NamesItByItsId()
    {
        SetupScope();
        var principals = SetupFederatedPrincipalStore();
        var identities = SetupFederatedIdentityStore();
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1"] };
        var describer = CreateDescriber(["p1"], []);
        SetupPrincipalsRead(principals, ["p1"], CreateFederatedPrincipal("p1"));
        SetupIdentitiesRead(identities, ["p1"]);
        var directory = CreateDirectory(describer.Object);

        var result = await directory.FindAsync(query, CancellationToken.None);

        var summary = Assert.Single(result);
        Assert.Equal(PrincipalTypes.FederatedUser, summary.PrincipalType);
        Assert.Equal("p1", summary.DisplayName);
    }

    [Fact]
    public async Task FindAsync_WhenNoStoreHoldsTheUnknownIds_ReadsNoIdentityAndSkipsThem()
    {
        SetupScope();
        var principals = SetupFederatedPrincipalStore();
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["missing"] };
        var describer = CreateDescriber(["missing"], []);
        SetupPrincipalsRead(principals, ["missing"]);
        var directory = CreateDirectory(describer.Object);

        var result = await directory.FindAsync(query, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_WhenThereAreNoDescribersAndOnlyUserNames_ReturnsNothing()
    {
        SetupScope();
        var directory = CreateDirectory();

        var result = await directory.FindAsync(
            new ControlPlanePrincipalQuery { UserNames = ["alice"] },
            CancellationToken.None
        );

        Assert.Empty(result);
    }

    #endregion
}
