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

        MockStoreManager
            .Setup(x => x.GetStore<ILocalAccountStore>())
            .Returns(MockAccountStore.Object)
            .Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(MockIdentityStore.Object)
            .Verifiable();
    }

    private static PersistedLocalAccount CreateAccount(string localAccountId, string userName) =>
        new()
        {
            TenantId = TenantId,
            LocalAccountId = localAccountId,
            DisplayName = "Alice Admin",
            UserName = userName,
            Email = null,
            EmailVerified = false,
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = "account-ct",
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

    [Fact]
    public async Task FindAsync_WithAPrincipalId_DescribesTheAccountOfItsSelfIssuedIdentity()
    {
        var account = CreateAccount("acc-1", "alice");
        IReadOnlyList<PersistedFederatedIdentity> identities =
        [
            CreateIdentity("https://other.example", "x", "p1"),
            CreateIdentity(AccountConstants.SelfIssuer, "acc-1", "p1"),
        ];
        MockIdentityStore
            .Setup(x => x.GetByPrincipalAsync("p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(identities)
            .Verifiable();
        MockAccountStore
            .Setup(x => x.GetByIdOrDefaultAsync("acc-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account)
            .Verifiable();

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            new ControlPlanePrincipalQuery { PrincipalIds = ["p1", "p1"] },
            CancellationToken.None
        );

        var summary = Assert.Single(result);
        Assert.Equal("p1", summary.PrincipalId);
        Assert.Equal(PrincipalTypes.LocalAccount, summary.PrincipalType);
        Assert.Equal("Alice Admin", summary.DisplayName);
        Assert.Equal("alice", summary.UserName);
    }

    [Fact]
    public async Task FindAsync_WithAUserName_DescribesTheAccountAndItsPrincipal()
    {
        var account = CreateAccount("acc-1", "alice");
        var identity = CreateIdentity(AccountConstants.SelfIssuer, "acc-1", "p1");
        MockAccountStore
            .Setup(x => x.GetByUserNameOrDefaultAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account)
            .Verifiable();
        MockIdentityStore
            .Setup(x =>
                x.GetByIssuerSubjectAsync(
                    AccountConstants.SelfIssuer,
                    "acc-1",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(identity)
            .Verifiable();

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            new ControlPlanePrincipalQuery { UserNames = ["alice"] },
            CancellationToken.None
        );

        var summary = Assert.Single(result);
        Assert.Equal("p1", summary.PrincipalId);
        Assert.Equal(PrincipalTypes.LocalAccount, summary.PrincipalType);
        Assert.Equal("alice", summary.UserName);
    }

    [Fact]
    public async Task FindAsync_WithAPrincipalIdAndItsUserName_DescribesItOnce()
    {
        var account = CreateAccount("acc-1", "alice");
        var identity = CreateIdentity(AccountConstants.SelfIssuer, "acc-1", "p1");
        IReadOnlyList<PersistedFederatedIdentity> identities = [identity];
        MockIdentityStore
            .Setup(x => x.GetByPrincipalAsync("p1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(identities)
            .Verifiable();
        MockAccountStore
            .Setup(x => x.GetByIdOrDefaultAsync("acc-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account)
            .Verifiable();
        MockAccountStore
            .Setup(x => x.GetByUserNameOrDefaultAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(account)
            .Verifiable();
        MockIdentityStore
            .Setup(x =>
                x.GetByIssuerSubjectAsync(
                    AccountConstants.SelfIssuer,
                    "acc-1",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(identity)
            .Verifiable();

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            new ControlPlanePrincipalQuery { PrincipalIds = ["p1"], UserNames = ["alice"] },
            CancellationToken.None
        );

        Assert.Single(result);
    }

    [Fact]
    public async Task FindAsync_WithAPrincipalWithoutASelfIssuedIdentity_SkipsIt()
    {
        IReadOnlyList<PersistedFederatedIdentity> identities =
        [
            CreateIdentity("https://other.example", "x", "p2"),
        ];
        MockIdentityStore
            .Setup(x => x.GetByPrincipalAsync("p2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(identities)
            .Verifiable();

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            new ControlPlanePrincipalQuery { PrincipalIds = ["p2"] },
            CancellationToken.None
        );

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_WithAnUnknownUserName_SkipsIt()
    {
        MockAccountStore
            .Setup(x => x.GetByUserNameOrDefaultAsync("nobody", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedLocalAccount?)null)
            .Verifiable();

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            new ControlPlanePrincipalQuery { UserNames = ["nobody"] },
            CancellationToken.None
        );

        Assert.Empty(result);
    }
}
