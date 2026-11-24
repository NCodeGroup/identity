// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Moq;
using NCode.Identity.OpenId.Accounts.Authentication;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

public class LocalAccountSignedInAccountFinderTests : BaseTests
{
    private const string TenantId = "tenant-1";
    private const string PrincipalId = "principal-1";
    private const string FirstConnectionId = "connection-1";
    private const string SecondConnectionId = "connection-2";
    private const string ExternalConnectionId = "connection-ext";
    private const string FirstAccountId = "account-1";
    private const string SecondAccountId = "account-2";

    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IFederatedIdentityStore> MockFederatedIdentityStore { get; }
    private Mock<ILocalAccountStore> MockLocalAccountStore { get; }
    private Mock<IConnectionResolver> MockConnectionResolver { get; }
    private LocalAccountSignedInAccountFinder Finder { get; }

    public LocalAccountSignedInAccountFinderTests()
    {
        var mockStoreManagerFactory = CreateStrictMock<IStoreManagerFactory>();
        MockStoreManager = CreateStrictMock<IStoreManager>();
        MockFederatedIdentityStore = CreateStrictMock<IFederatedIdentityStore>();
        MockLocalAccountStore = CreateStrictMock<ILocalAccountStore>();
        MockConnectionResolver = CreateStrictMock<IConnectionResolver>();
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(MockFederatedIdentityStore.Object);
        Finder = new LocalAccountSignedInAccountFinder(
            mockStoreManagerFactory.Object,
            MockConnectionResolver.Object,
            LocalAccountStoreSetup.CreateCatalog(MockLocalAccountStore)
        );
    }

    #region Scaffolding

    // Creates a local connection that is disabled.
    internal static ResolvedConnection CreateDisabledConnection(
        string tenantId,
        IReadOnlySettingCollection settings,
        string connectionId
    )
    {
        var enabled = LocalAccountStoreSetup.CreateConnection(
            tenantId,
            settings,
            connectionId: connectionId
        );
        var connection = enabled.Connection;
        return new ResolvedConnection
        {
            Connection = new PersistedConnection
            {
                TenantId = connection.TenantId,
                ConnectionId = connection.ConnectionId,
                Name = connection.Name,
                Strategy = connection.Strategy,
                DisplayName = connection.DisplayName,
                IsSystem = connection.IsSystem,
                IsEnabled = false,
                Options = connection.Options,
                Settings = connection.Settings,
            },
            Strategy = enabled.Strategy,
            Settings = enabled.Settings,
        };
    }

    private Mock<OpenIdContext> CreateContext(ClaimsPrincipal principal, string? principalId)
    {
        var mockContext = CreateStrictMock<OpenIdContext>();
        mockContext.Setup(x => x.GetSubjectId(principal)).Returns(principalId);
        return mockContext;
    }

    private static ClaimsPrincipal CreatePrincipal(string? connectionId = null)
    {
        var identity = new ClaimsIdentity("test");
        if (!string.IsNullOrEmpty(connectionId))
        {
            identity.AddClaim(new Claim(ConnectionClaimTypes.Connection, connectionId));
        }

        return new ClaimsPrincipal(identity);
    }

    private static PersistedFederatedIdentity Identity(string connectionId, string subject) =>
        new()
        {
            TenantId = TenantId,
            FederatedIdentityId = $"fed-{connectionId}",
            PrincipalId = PrincipalId,
            ConnectionId = connectionId,
            Issuer = null,
            Subject = subject,
            JoinKey = null,
            ConcurrencyToken = string.Empty,
        };

    private static PersistedLocalAccount Account(string connectionId, string accountId) =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = connectionId,
            LocalAccountId = accountId,
            UserName = accountId,
            DisplayName = accountId,
            Email = null,
            EmailVerified = false,
            IsEnabled = true,
            Claims = [],
            ConcurrencyToken = string.Empty,
        };

    private void SetupIdentities(params PersistedFederatedIdentity[] identities) =>
        MockFederatedIdentityStore
            .Setup(x => x.GetByPrincipalAsync(PrincipalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(identities);

    private void SetupConnection(
        string connectionId,
        string strategy = ConnectionStrategyNames.Local,
        bool isEnabled = true
    )
    {
        var settings = CreateStrictMock<IReadOnlySettingCollection>().Object;
        var connection = isEnabled
            ? LocalAccountStoreSetup.CreateConnection(TenantId, settings, strategy, connectionId)
            : CreateDisabledConnection(TenantId, settings, connectionId);
        MockConnectionResolver
            .Setup(x =>
                x.GetByIdOrDefaultAsync(
                    It.IsAny<OpenIdContext>(),
                    connectionId,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(connection);
    }

    private void SetupAccount(string connectionId, string accountId) =>
        MockLocalAccountStore
            .Setup(x => x.GetByIdOrDefaultAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Account(connectionId, accountId));

    #endregion

    #region FindAsync Tests

    [Fact]
    public async Task FindAsync_WhenNoPrincipalIdentifier_ReturnsNull()
    {
        var principal = CreatePrincipal();
        var mockContext = CreateContext(principal, principalId: null);

        var result = await Finder.FindAsync(
            MockStoreManager.Object,
            mockContext.Object,
            principal,
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task FindAsync_WhenSeveralLocalIdentitiesAndNoConnectionClaim_ReturnsNull()
    {
        var principal = CreatePrincipal();
        var mockContext = CreateContext(principal, PrincipalId);
        SetupIdentities(
            Identity(FirstConnectionId, FirstAccountId),
            Identity(SecondConnectionId, SecondAccountId)
        );
        SetupConnection(FirstConnectionId);
        SetupConnection(SecondConnectionId);

        var result = await Finder.FindAsync(
            MockStoreManager.Object,
            mockContext.Object,
            principal,
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FindAsync_WhenSeveralLocalIdentitiesAndAConnectionClaim_ReturnsTheClaimedAccount(
        bool claimedFirst
    )
    {
        var principal = CreatePrincipal(SecondConnectionId);
        var mockContext = CreateContext(principal, PrincipalId);
        var first = Identity(FirstConnectionId, FirstAccountId);
        var second = Identity(SecondConnectionId, SecondAccountId);
        SetupIdentities(claimedFirst ? [second, first] : [first, second]);
        SetupConnection(FirstConnectionId);
        SetupConnection(SecondConnectionId);
        SetupAccount(SecondConnectionId, SecondAccountId);

        var result = await Finder.FindAsync(
            MockStoreManager.Object,
            mockContext.Object,
            principal,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal(SecondAccountId, result.Value.Account.LocalAccountId);
        Assert.Equal(SecondConnectionId, result.Value.Connection.Connection.ConnectionId);
    }

    [Fact]
    public async Task FindAsync_WhenOneLocalIdentityBesideANonLocalIdentity_ReturnsTheLocalAccount()
    {
        var principal = CreatePrincipal();
        var mockContext = CreateContext(principal, PrincipalId);
        SetupIdentities(
            Identity(ExternalConnectionId, "external-subject"),
            Identity(FirstConnectionId, FirstAccountId)
        );
        SetupConnection(ExternalConnectionId, strategy: "external");
        SetupConnection(FirstConnectionId);
        SetupAccount(FirstConnectionId, FirstAccountId);

        var result = await Finder.FindAsync(
            MockStoreManager.Object,
            mockContext.Object,
            principal,
            CancellationToken.None
        );

        Assert.NotNull(result);
        Assert.Equal(FirstAccountId, result.Value.Account.LocalAccountId);
    }

    [Fact]
    public async Task FindAsync_WhenTheLocalConnectionIsDisabled_ReturnsNull()
    {
        var principal = CreatePrincipal();
        var mockContext = CreateContext(principal, PrincipalId);
        SetupIdentities(Identity(FirstConnectionId, FirstAccountId));
        SetupConnection(FirstConnectionId, isEnabled: false);

        var result = await Finder.FindAsync(
            MockStoreManager.Object,
            mockContext.Object,
            principal,
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task FindAsync_WhenTheClaimedConnectionIsDisabled_ReturnsNullBesideAnotherLocalIdentity()
    {
        var principal = CreatePrincipal(FirstConnectionId);
        var mockContext = CreateContext(principal, PrincipalId);
        SetupIdentities(
            Identity(FirstConnectionId, FirstAccountId),
            Identity(SecondConnectionId, SecondAccountId)
        );
        SetupConnection(FirstConnectionId, isEnabled: false);
        SetupConnection(SecondConnectionId);

        var result = await Finder.FindAsync(
            MockStoreManager.Object,
            mockContext.Object,
            principal,
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task FindAsync_WhenTheClaimedConnectionHasNoIdentity_ReturnsNullBesideAnotherLocalIdentity()
    {
        var principal = CreatePrincipal(SecondConnectionId);
        var mockContext = CreateContext(principal, PrincipalId);
        SetupIdentities(Identity(FirstConnectionId, FirstAccountId));
        SetupConnection(FirstConnectionId);

        var result = await Finder.FindAsync(
            MockStoreManager.Object,
            mockContext.Object,
            principal,
            CancellationToken.None
        );

        Assert.Null(result);
    }

    #endregion
}
