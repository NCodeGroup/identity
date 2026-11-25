// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Endpoints;
using NCode.Identity.OpenId.Management.Principals;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.ControlPlane;

public sealed class LocalControlPlanePrincipalDirectoryTests : IDisposable
{
    private const string RootTenantId = "control-plane";

    private static void SetupPrincipalsRead(
        Mock<IFederatedPrincipalStore> store,
        string[] principalIds,
        params IEnumerable<string> foundIds
    )
    {
        IReadOnlyList<string> found = [.. foundIds];
        store
            .Setup(x =>
                x.GetExistingIdsAsync(
                    It.Is<IReadOnlyCollection<string>>(c => c.SequenceEqual(principalIds)),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(found)
            .Verifiable();
    }

    private static void SetupIdentitiesRead(
        Mock<IFederatedIdentityStore> store,
        string[] principalIds,
        params IEnumerable<FederatedIdentityNameProjection> foundIdentities
    )
    {
        IReadOnlyList<FederatedIdentityNameProjection> found = [.. foundIdentities];
        store
            .Setup(x =>
                x.GetNamesByPrincipalsAsync(
                    It.Is<IReadOnlyCollection<string>>(c => c.SequenceEqual(principalIds)),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(found)
            .Verifiable();
    }

    private static FederatedIdentityNameProjection CreateFederatedIdentity(
        string principalId,
        string connectionId,
        string subject,
        string? issuer
    ) =>
        new()
        {
            PrincipalId = principalId,
            ConnectionId = connectionId,
            Issuer = issuer,
            Subject = subject,
        };

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

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
        params IEnumerable<IPrincipalDescriber> describers
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

    private Mock<IPrincipalDescriber> CreateDescriber(
        string[] principalIds,
        string[] userNames,
        params IEnumerable<PrincipalSummaryResource> summaries
    )
    {
        IReadOnlyList<PrincipalSummaryResource> found = [.. summaries];
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
            .ReturnsAsync(found)
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

    #region DescribesEveryPrincipal Tests

    [Fact]
    public void DescribesEveryPrincipal_Always_IsTrue()
    {
        var directory = CreateDirectory();

        Assert.True(directory.DescribesEveryPrincipal);
    }

    #endregion

    #region FindAsync Tests

    [Fact]
    public async Task FindAsync_WhenTheQueryIsEmpty_OpensNoStore()
    {
        var describer = MockRepository.Create<IPrincipalDescriber>();
        var directory = CreateDirectory(describer.Object);

        var query = new ControlPlanePrincipalQuery();

        var result = await directory.FindAsync(query, CancellationToken.None);

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
        var firstSummary = PrincipalDirectoryMocks.CreateSummary(
            "p1",
            PrincipalTypes.LocalAccount,
            "First",
            "ALICE"
        );
        var secondSummary = PrincipalDirectoryMocks.CreateSummary(
            "p2",
            PrincipalTypes.LocalAccount,
            "Other"
        );
        var first = CreateDescriber(["p1", "p2"], ["alice"], firstSummary);
        var second = CreateDescriber(["p2"], [], secondSummary);
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
        var aliceSummary = PrincipalDirectoryMocks.CreateSummary(
            "p1",
            PrincipalTypes.LocalAccount,
            "Alice"
        );
        var first = CreateDescriber(["p1"], [], aliceSummary);
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
        var identityB = CreateFederatedIdentity("p1", "connection-b", "a", "https://b.example");
        var identityC = CreateFederatedIdentity("p2", "connection-c", "x", "https://c.example");
        var identityZ = CreateFederatedIdentity("p1", "connection-a", "z", "https://a.example");
        var identityY = CreateFederatedIdentity("p1", "connection-a", "y", "https://a.example");
        SetupPrincipalsRead(principals, ["p1", "p2"], "p1", "p2");
        SetupIdentitiesRead(identities, ["p1", "p2"], identityB, identityC, identityZ, identityY);
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
    public async Task FindAsync_WhenAFederatedPrincipalsLaterIdentitySortsAfterItsFirst_KeepsTheFirst()
    {
        SetupScope();
        var principals = SetupFederatedPrincipalStore();
        var identities = SetupFederatedIdentityStore();
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1"] };
        var describer = CreateDescriber(["p1"], []);
        var firstIdentity = CreateFederatedIdentity("p1", "connection-a", "a", "https://a.example");
        var laterIdentity = CreateFederatedIdentity("p1", "connection-b", "a", "https://b.example");
        SetupPrincipalsRead(principals, ["p1"], "p1");
        SetupIdentitiesRead(identities, ["p1"], firstIdentity, laterIdentity);
        var directory = CreateDirectory(describer.Object);

        var result = await directory.FindAsync(query, CancellationToken.None);

        var summary = Assert.Single(result);
        Assert.Equal("a (https://a.example)", summary.DisplayName);
    }

    [Fact]
    public async Task FindAsync_WhenADescriberKnowsOnlySomeUserNames_AsksTheNextAboutTheRest()
    {
        SetupScope();
        var query = new ControlPlanePrincipalQuery { UserNames = ["alice", "bob"] };
        var aliceSummary = PrincipalDirectoryMocks.CreateSummary(
            "p1",
            PrincipalTypes.LocalAccount,
            "Alice",
            "alice"
        );
        var bobSummary = PrincipalDirectoryMocks.CreateSummary(
            "p2",
            PrincipalTypes.LocalAccount,
            "Bob",
            "bob"
        );
        var first = CreateDescriber([], ["alice", "bob"], aliceSummary);
        var second = CreateDescriber([], ["bob"], bobSummary);
        var directory = CreateDirectory(first.Object, second.Object);

        var result = await directory.FindAsync(query, CancellationToken.None);

        var principalIds = result.Select(summary => summary.PrincipalId);
        Assert.Equal(["p1", "p2"], principalIds);
    }

    [Fact]
    public async Task FindAsync_WhenTheFirstIdentityHasNoIssuer_NamesItBySubjectAlone()
    {
        SetupScope();
        var principals = SetupFederatedPrincipalStore();
        var identities = SetupFederatedIdentityStore();
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1"] };
        var describer = CreateDescriber(["p1"], []);
        var identity = CreateFederatedIdentity("p1", "connection-a", "alice", issuer: null);
        SetupPrincipalsRead(principals, ["p1"], "p1");
        SetupIdentitiesRead(identities, ["p1"], identity);
        var directory = CreateDirectory(describer.Object);

        var result = await directory.FindAsync(query, CancellationToken.None);

        var summary = Assert.Single(result);
        Assert.Equal("alice", summary.DisplayName);
    }

    [Fact]
    public async Task FindAsync_WhenAFederatedPrincipalHasNoIdentity_NamesItByItsId()
    {
        SetupScope();
        var principals = SetupFederatedPrincipalStore();
        var identities = SetupFederatedIdentityStore();
        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1"] };
        var describer = CreateDescriber(["p1"], []);
        SetupPrincipalsRead(principals, ["p1"], "p1");
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

        var query = new ControlPlanePrincipalQuery { UserNames = ["alice"] };

        var result = await directory.FindAsync(query, CancellationToken.None);

        Assert.Empty(result);
    }

    #endregion
}
