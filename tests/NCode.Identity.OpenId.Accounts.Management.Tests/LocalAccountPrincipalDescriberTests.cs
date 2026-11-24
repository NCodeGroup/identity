// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Management.Principals;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Accounts.Management;

public sealed class LocalAccountPrincipalDescriberTests : IDisposable
{
    private const string TenantId = "control-plane";
    private const string TeamId = "team-1";
    private const string ConnectionId = "connection-1";
    private const string OtherConnectionId = "connection-2";
    private const string ConnectionName = "local";

    // An expression tree cannot call a non-array params collection in its expanded form (CS9226).
    private static bool Names(IReadOnlyCollection<string> actual, params string[] expected) =>
        actual.SequenceEqual(expected);

    private static LocalAccountNameProjection CreateAccount(
        string localAccountId,
        string userName,
        string connectionId = ConnectionId
    ) =>
        new()
        {
            LocalAccountId = localAccountId,
            ConnectionId = connectionId,
            UserName = userName,
            DisplayName = "Alice Admin",
        };

    private static PersistedFederatedIdentity CreateIdentity(
        string connectionId,
        string subject,
        string principalId
    ) =>
        new()
        {
            TenantId = TenantId,
            FederatedIdentityId = "identity-" + subject,
            PrincipalId = principalId,
            ConnectionId = connectionId,
            Issuer = null,
            Subject = subject,
            JoinKey = null,
            ConcurrencyToken = "identity-ct",
        };

    private static FederatedIdentityNameProjection CreateIdentityName(
        string connectionId,
        string subject,
        string principalId
    ) =>
        new()
        {
            PrincipalId = principalId,
            ConnectionId = connectionId,
            Issuer = null,
            Subject = subject,
        };

    private static PersistedConnection CreateConnection(
        string connectionId = ConnectionId,
        string strategy = ConnectionStrategyNames.Local
    ) =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = connectionId,
            Name = ConnectionName,
            Strategy = strategy,
            DisplayName = "Username and password",
            IsSystem = false,
            IsEnabled = true,
            Options = JsonSerializer.SerializeToElement(new { store = "default" }),
            Settings = new PersistedConnectionSettings
            {
                TenantId = TenantId,
                ConnectionId = connectionId,
                Value = JsonSerializer.SerializeToElement(new { }),
            },
        };

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ILocalAccountStore> MockAccountStore { get; }
    private Mock<IFederatedIdentityStore> MockIdentityStore { get; }
    private Mock<IConnectionStore> MockConnectionStore { get; }
    private Mock<ITenantStore> MockTenantStore { get; }
    private Mock<IEffectiveSettingsResolver> MockEffectiveSettingsResolver { get; }
    private Mock<ITenantResolver> MockTenantResolver { get; }

    private LocalAccountPrincipalDescriber Describer { get; }
    private bool IsConnectionStoreSetUp { get; set; }

    public LocalAccountPrincipalDescriberTests()
    {
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockAccountStore = MockRepository.Create<ILocalAccountStore>();
        MockIdentityStore = MockRepository.Create<IFederatedIdentityStore>();
        MockConnectionStore = MockRepository.Create<IConnectionStore>();
        MockTenantStore = MockRepository.Create<ITenantStore>();
        MockEffectiveSettingsResolver = MockRepository.Create<IEffectiveSettingsResolver>();
        MockTenantResolver = MockRepository.Create<ITenantResolver>();

        var catalog = new LocalAccountStoreCatalog([
            new LocalAccountStoreRegistration
            {
                Name = "default",
                CreateStore = _ => MockAccountStore.Object,
                CreateTransferStore = _ =>
                    MockRepository.Create<ILocalAccountTransferStore>().Object,
            },
        ]);

        Describer = new LocalAccountPrincipalDescriber(
            catalog,
            new ControlPlaneDefaultConnectionReader(
                MockTenantResolver.Object,
                MockEffectiveSettingsResolver.Object
            )
        );
    }

    private void SetupIdentityStore()
    {
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(MockIdentityStore.Object)
            .Verifiable();
    }

    private void SetupConnectionStore()
    {
        if (IsConnectionStoreSetUp)
        {
            return;
        }

        IsConnectionStoreSetUp = true;
        MockStoreManager
            .Setup(x => x.GetStore<IConnectionStore>())
            .Returns(MockConnectionStore.Object)
            .Verifiable();
    }

    private void SetupConnectionById(PersistedConnection? connection, string connectionId) =>
        MockConnectionStore
            .Setup(x => x.GetOrDefaultAsync(connectionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection)
            .Verifiable();

    private void SetupIdentityNames(
        IReadOnlyList<FederatedIdentityNameProjection> identities,
        params string[] principalIds
    ) =>
        MockIdentityStore
            .Setup(x =>
                x.GetNamesByPrincipalsAsync(
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, principalIds)),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(identities)
            .Verifiable();

    private void SetupAccountsByIds(
        IReadOnlyList<LocalAccountNameProjection> accounts,
        params string[] localAccountIds
    ) =>
        MockAccountStore
            .Setup(x =>
                x.GetNamesByIdsAsync(
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, localAccountIds)),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(accounts)
            .Verifiable();

    // Resolves the control-plane tenant's default connection name from the stored tenant and team documents.
    private void SetupDefaultConnectionName(PersistedConnection? connection)
    {
        MockTenantResolver.Setup(x => x.GetControlPlaneTenantId()).Returns(TenantId).Verifiable();
        MockStoreManager
            .Setup(x => x.GetStore<ITenantStore>())
            .Returns(MockTenantStore.Object)
            .Verifiable();
        var tenant = new PersistedTenantBuilder { TenantId = TenantId, TeamId = TeamId }.Build();
        MockTenantStore
            .Setup(x => x.GetOrDefaultAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant)
            .Verifiable();

        var mockTeamSettings = MockRepository.Create<IReadOnlySettingCollection>();
        MockEffectiveSettingsResolver
            .Setup(x => x.ResolveTeamAsync(TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockTeamSettings.Object)
            .Verifiable();
        var mockTenantSettings = MockRepository.Create<IReadOnlySettingCollection>();
        mockTenantSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.DefaultConnection))
            .Returns(ConnectionName)
            .Verifiable();
        MockEffectiveSettingsResolver
            .Setup(x =>
                x.ResolveStored(
                    mockTeamSettings.Object,
                    OpenIdSettingScopes.Tenant,
                    TenantId,
                    It.IsAny<JsonElement>()
                )
            )
            .Returns(mockTenantSettings.Object)
            .Verifiable();

        SetupConnectionStore();
        MockConnectionStore
            .Setup(x => x.GetByNameOrDefaultAsync(ConnectionName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection)
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
    public async Task FindAsync_WhenAPrincipalHasAnIdentityInALocalConnection_DescribesItsAccount()
    {
        SetupIdentityStore();
        SetupConnectionStore();
        var account = CreateAccount("acc-1", "alice");
        SetupIdentityNames(
            [
                CreateIdentityName(OtherConnectionId, "x", "p2"),
                CreateIdentityName(ConnectionId, "acc-1", "p1"),
            ],
            "p1",
            "p2"
        );
        SetupConnectionById(CreateConnection(), ConnectionId);
        SetupConnectionById(
            CreateConnection(OtherConnectionId, strategy: "other"),
            OtherConnectionId
        );
        SetupAccountsByIds([account], "acc-1");

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
    public async Task FindAsync_WhenTheIdentityIsInAConnectionThatIsNotLocal_DescribesNoAccount()
    {
        SetupIdentityStore();
        SetupConnectionStore();
        SetupIdentityNames([CreateIdentityName(OtherConnectionId, "x", "p2")], "p2");
        SetupConnectionById(
            CreateConnection(OtherConnectionId, strategy: "other"),
            OtherConnectionId
        );

        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p2"] };

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            query,
            CancellationToken.None
        );

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_WhenNoPrincipalHasAnIdentity_ReadsNoAccount()
    {
        SetupIdentityStore();
        SetupConnectionStore();
        SetupIdentityNames([], "p2");

        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p2"] };

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            query,
            CancellationToken.None
        );

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_WhenTheAccountBelongsToAnotherConnection_DoesNotMatchIt()
    {
        SetupIdentityStore();
        SetupConnectionStore();
        SetupIdentityNames([CreateIdentityName(ConnectionId, "acc-1", "p1")], "p1");
        SetupConnectionById(CreateConnection(), ConnectionId);
        SetupAccountsByIds([CreateAccount("acc-1", "alice", OtherConnectionId)], "acc-1");

        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1"] };

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
        SetupIdentityStore();
        SetupDefaultConnectionName(CreateConnection());
        MockAccountStore
            .Setup(x =>
                x.GetNamesByUserNamesAsync(
                    ConnectionId,
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, "alice", "nobody")),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([CreateAccount("acc-1", "alice")])
            .Verifiable();
        SetupIdentitiesBySubject([CreateIdentity(ConnectionId, "acc-1", "p1")], "acc-1");

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
        SetupDefaultConnectionName(CreateConnection());
        MockAccountStore
            .Setup(x =>
                x.GetNamesByUserNamesAsync(
                    ConnectionId,
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, "nobody")),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([])
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
    public async Task FindAsync_WhenTheDefaultConnectionIsMissing_LooksUpNoUserName()
    {
        SetupDefaultConnectionName(connection: null);

        var query = new ControlPlanePrincipalQuery { UserNames = ["alice"] };

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            query,
            CancellationToken.None
        );

        Assert.Empty(result);
    }

    [Fact]
    public async Task FindAsync_WhenTheDefaultConnectionIsNotLocal_LooksUpNoUserName()
    {
        SetupDefaultConnectionName(CreateConnection(strategy: "other"));

        var query = new ControlPlanePrincipalQuery { UserNames = ["alice"] };

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
        SetupIdentityStore();
        SetupConnectionStore();
        var account = CreateAccount("acc-1", "alice");
        SetupIdentityNames([CreateIdentityName(ConnectionId, "acc-1", "p1")], "p1");
        SetupConnectionById(CreateConnection(), ConnectionId);
        SetupAccountsByIds([account], "acc-1");
        SetupDefaultConnectionName(CreateConnection());
        MockAccountStore
            .Setup(x =>
                x.GetNamesByUserNamesAsync(
                    ConnectionId,
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, "alice")),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([account])
            .Verifiable();
        SetupIdentitiesBySubject([CreateIdentity(ConnectionId, "acc-1", "p1")], "acc-1");

        var query = new ControlPlanePrincipalQuery { PrincipalIds = ["p1"], UserNames = ["alice"] };

        var result = await Describer.FindAsync(
            MockStoreManager.Object,
            query,
            CancellationToken.None
        );

        Assert.Single(result);
    }

    #endregion

    private void SetupIdentitiesBySubject(
        IReadOnlyList<PersistedFederatedIdentity> identities,
        params string[] subjects
    ) =>
        MockIdentityStore
            .Setup(x =>
                x.GetByConnectionSubjectsAsync(
                    ConnectionId,
                    It.Is<IReadOnlyCollection<string>>(y => Names(y, subjects)),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(identities)
            .Verifiable();
}
