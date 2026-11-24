// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Management.Principals;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management;

public sealed class LocalAccountPrincipalDescriberTests : IDisposable
{
    private const string TenantId = "control-plane";

    private static bool Names(IReadOnlyCollection<string> actual, params string[] expected) =>
        actual.SequenceEqual(expected);

    private static LocalAccountNameProjection CreateAccount(
        string localAccountId,
        string userName
    ) =>
        new()
        {
            LocalAccountId = localAccountId,
            UserName = userName,
            DisplayName = "Alice Admin",
        };

    private static PersistedFederatedIdentity CreateIdentity(
        string issuer,
        string subject,
        string principalId
    ) =>
        new()
        {
            TenantId = TenantId,
            FederatedIdentityId = "identity-" + subject,
            PrincipalId = principalId,
            Issuer = issuer,
            Subject = subject,
            JoinKey = null,
            ConcurrencyToken = "identity-ct",
        };

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ILocalAccountStore> MockAccountStore { get; }
    private Mock<IFederatedIdentityStore> MockIdentityStore { get; }

    private LocalAccountPrincipalDescriber Describer { get; } = new();

    public LocalAccountPrincipalDescriberTests()
    {
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockAccountStore = MockRepository.Create<ILocalAccountStore>();
        MockIdentityStore = MockRepository.Create<IFederatedIdentityStore>();
    }

    private void SetupStores()
    {
        MockStoreManager
            .Setup(x => x.GetStore<ILocalAccountStore>())
            .Returns(MockAccountStore.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(MockIdentityStore.Object)
            .Verifiable();
    }

    #region FindAsync Tests

    [Fact]
    public async Task FindAsync_WhenTheQueryIsEmpty_ReadsNoStore()
    {
        var query = new ControlPlanePrincipalQuery();

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            query,
            CancellationToken.None
        );

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_WhenAPrincipalHasASelfIssuedIdentity_DescribesItsAccount()
    {
        SetupStores();
        var account = CreateAccount("acc-1", "alice");
        IReadOnlyList<PersistedFederatedIdentity> identities =
        [
            CreateIdentity("https://other.example", "x", "p2"),
            CreateIdentity(AccountConstants.SelfIssuer, "acc-1", "p1"),
        ];
        IReadOnlyList<LocalAccountNameProjection> accounts = [account];
        MockIdentityStore
            .Setup(x =>
                x.GetByPrincipalsAsync(
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, "p1", "p2")),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(identities)
            .Verifiable();
        MockAccountStore
            .Setup(x =>
                x.GetNamesByIdsAsync(
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, "acc-1")),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(accounts)
            .Verifiable();

        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1", "p2"] };

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            query,
            CancellationToken.None
        );

        var summary = Assert.Single(result);
        Assert.Equal("p1", summary.PrincipalId);
        Assert.Equal(PrincipalTypes.LocalAccount, summary.PrincipalType);
        Assert.Equal("Alice Admin", summary.DisplayName);
        Assert.Equal("alice", summary.UserName);
    }

    [Fact]
    public async Task FindAsync_WhenNoPrincipalHasASelfIssuedIdentity_ReadsNoAccount()
    {
        SetupStores();
        IReadOnlyList<PersistedFederatedIdentity> identities =
        [
            CreateIdentity("https://other.example", "x", "p2"),
        ];
        MockIdentityStore
            .Setup(x =>
                x.GetByPrincipalsAsync(
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, "p2")),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(identities)
            .Verifiable();

        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p2"] };

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            query,
            CancellationToken.None
        );

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_WhenAUserNameNamesAnAccount_DescribesItsPrincipal()
    {
        SetupStores();
        var account = CreateAccount("acc-1", "alice");
        var identity = CreateIdentity(AccountConstants.SelfIssuer, "acc-1", "p1");
        IReadOnlyList<LocalAccountNameProjection> accounts = [account];
        IReadOnlyList<PersistedFederatedIdentity> identities = [identity];
        MockAccountStore
            .Setup(x =>
                x.GetNamesByUserNamesAsync(
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, "alice", "nobody")),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(accounts)
            .Verifiable();
        MockIdentityStore
            .Setup(x =>
                x.GetByIssuerSubjectsAsync(
                    AccountConstants.SelfIssuer,
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, "acc-1")),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(identities)
            .Verifiable();

        var query = new ControlPlanePrincipalQuery { UserNames = ["alice", "nobody"] };

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            query,
            CancellationToken.None
        );

        var summary = Assert.Single(result);
        Assert.Equal("p1", summary.PrincipalId);
        Assert.Equal(PrincipalTypes.LocalAccount, summary.PrincipalType);
        Assert.Equal("alice", summary.UserName);
    }

    [Fact]
    public async Task FindAsync_WhenNoAccountHasTheUserNames_ReadsNoIdentity()
    {
        SetupStores();
        IReadOnlyList<LocalAccountNameProjection> accounts = [];
        MockAccountStore
            .Setup(x =>
                x.GetNamesByUserNamesAsync(
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, "nobody")),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(accounts)
            .Verifiable();

        var query = new ControlPlanePrincipalQuery { UserNames = ["nobody"] };

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            query,
            CancellationToken.None
        );

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_WhenAPrincipalIdAndItsUserNameAreNamed_DescribesItOnce()
    {
        SetupStores();
        var account = CreateAccount("acc-1", "alice");
        var identity = CreateIdentity(AccountConstants.SelfIssuer, "acc-1", "p1");
        IReadOnlyList<PersistedFederatedIdentity> identities = [identity];
        IReadOnlyList<LocalAccountNameProjection> accounts = [account];
        MockIdentityStore
            .Setup(x =>
                x.GetByPrincipalsAsync(
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, "p1")),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(identities)
            .Verifiable();
        MockAccountStore
            .Setup(x =>
                x.GetNamesByIdsAsync(
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, "acc-1")),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(accounts)
            .Verifiable();
        MockAccountStore
            .Setup(x =>
                x.GetNamesByUserNamesAsync(
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, "alice")),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(accounts)
            .Verifiable();
        MockIdentityStore
            .Setup(x =>
                x.GetByIssuerSubjectsAsync(
                    AccountConstants.SelfIssuer,
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, "acc-1")),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(identities)
            .Verifiable();

        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1"], UserNames = ["alice"] };

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            query,
            CancellationToken.None
        );

        Assert.Single(result);
    }

    #endregion
}
