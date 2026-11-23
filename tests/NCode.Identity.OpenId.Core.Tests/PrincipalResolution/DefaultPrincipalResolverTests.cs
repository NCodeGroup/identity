// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Moq;
using NCode.Identity.Cryptography;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Core.Tests;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.PrincipalResolution;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Core.PrincipalResolution;

public sealed class DefaultPrincipalResolverTests : BaseTests
{
    private const string PrincipalId = "principal-1";
    private const string TenantId = "tenant-1";
    private const string ClientId = "client-1";
    private const string Issuer = "https://issuer.example";
    private const string ConnectionId = "connection-1";
    private const string OtherConnectionId = "connection-2";
    private const string Subject = "upstream-subject-1";
    private const string GeneratedId = "generated-id";

    // Only a resolution that provisions reads the tenant id.
    private OpenIdContext CreateOpenIdContext(bool provisions = false)
    {
        var context = CreateStrictMock<OpenIdContext>();
        if (provisions)
        {
            var tenant = CreateStrictMock<OpenIdTenant>();
            tenant.Setup(x => x.TenantId).Returns(TenantId).Verifiable();
            context.Setup(x => x.Tenant).Returns(tenant.Object).Verifiable();
        }

        // The resolver derives the subject through the tenant-aware context seam (ADR-1053).
        context
            .Setup(x => x.GetSubjectId(It.IsAny<ClaimsPrincipal>()))
            .Returns((ClaimsPrincipal user) => user.FindFirstValue("sub"))
            .Verifiable();
        return context.Object;
    }

    private static ResolvedConnection CreateConnection(
        string connectionId,
        bool isEnabled = true
    ) =>
        new()
        {
            Connection = new PersistedConnection
            {
                TenantId = TenantId,
                ConnectionId = connectionId,
                Name = connectionId,
                Strategy = ConnectionStrategyNames.Local,
                DisplayName = connectionId,
                IsSystem = false,
                IsEnabled = isEnabled,
                Options = JsonElements.EmptyObject,
                Settings = new PersistedConnectionSettings
                {
                    TenantId = TenantId,
                    ConnectionId = connectionId,
                    Value = JsonElements.EmptyObject,
                },
            },
            Strategy = new Mock<IConnectionStrategy>().Object,
            Settings = new Mock<IReadOnlySettingCollection>().Object,
        };

    private IConnectionResolver CreateConnectionResolver(params ResolvedConnection[] connections)
    {
        var resolver = CreateStrictMock<IConnectionResolver>();
        foreach (var connection in connections)
        {
            resolver
                .Setup(x =>
                    x.GetByIdOrDefaultAsync(
                        It.IsAny<OpenIdContext>(),
                        connection.ConnectionId,
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync(connection)
                .Verifiable();
        }

        return resolver.Object;
    }

    private static Claim ConnectionClaim(string connectionId = ConnectionId) =>
        new(ConnectionClaimTypes.Connection, connectionId);

    private static ClaimsPrincipal CreateUser(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    private static PersistedFederatedPrincipal Principal(string principalId) =>
        new()
        {
            TenantId = TenantId,
            PrincipalId = principalId,
            ConcurrencyToken = string.Empty,
        };

    private static PersistedFederatedIdentity Identity(
        string principalId,
        string connectionId = ConnectionId
    ) =>
        new()
        {
            TenantId = TenantId,
            FederatedIdentityId = "identity-1",
            PrincipalId = principalId,
            ConnectionId = connectionId,
            Issuer = Issuer,
            Subject = Subject,
            JoinKey = null,
            ConcurrencyToken = string.Empty,
        };

    private static PersistedClient CreateClient(bool isDisabled) =>
        new PersistedClientBuilder
        {
            TenantId = TenantId,
            ClientId = ClientId,
            PrincipalId = PrincipalId,
            IsDisabled = isDisabled,
        }.Build();

    private IClientPrincipalResolver CreateClientResolver(PersistedClient? client)
    {
        var resolver = CreateStrictMock<IClientPrincipalResolver>();
        resolver
            .Setup(x =>
                x.GetActingClientOrDefaultAsync(
                    It.IsAny<OpenIdContext>(),
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<IStoreManager>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(client)
            .Verifiable();
        return resolver.Object;
    }

    private DefaultPrincipalResolver CreateActingClientResolver(PersistedClient client) =>
        new(
            CreateStrictMock<IStoreManagerFactory>().Object,
            CreateStrictMock<ICryptoService>().Object,
            CreateStrictMock<IFederatedIdentityLinkingPolicy>().Object,
            CreateClientResolver(client),
            CreateStrictMock<IConnectionResolver>().Object
        );

    private IStoreManager CreateStoreManagerWithoutPrincipal()
    {
        var manager = CreateStrictMock<IStoreManager>();
        var principalStore = CreateStrictMock<IFederatedPrincipalStore>();
        manager
            .Setup(x => x.GetStore<IFederatedPrincipalStore>())
            .Returns(principalStore.Object)
            .Verifiable();
        principalStore
            .Setup(x => x.GetOrDefaultAsync(PrincipalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedFederatedPrincipal?)null)
            .Verifiable();
        return manager.Object;
    }

    [Fact]
    public async Task ResolvePrincipalIdOrDefaultAsync_WhenSubjectIsPrincipalId_ReturnsIt()
    {
        var manager = CreateStrictMock<IStoreManager>();
        var principalStore = CreateStrictMock<IFederatedPrincipalStore>();

        manager
            .Setup(x => x.GetStore<IFederatedPrincipalStore>())
            .Returns(principalStore.Object)
            .Verifiable();
        principalStore
            .Setup(x => x.GetOrDefaultAsync(PrincipalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Principal(PrincipalId))
            .Verifiable();

        var resolver = new DefaultPrincipalResolver(
            CreateStrictMock<IStoreManagerFactory>().Object,
            CreateStrictMock<ICryptoService>().Object,
            CreateStrictMock<IFederatedIdentityLinkingPolicy>().Object,
            CreateStrictMock<IClientPrincipalResolver>().Object,
            CreateStrictMock<IConnectionResolver>().Object
        );

        var result = await resolver.ResolvePrincipalIdOrDefaultAsync(
            CreateOpenIdContext(),
            CreateUser(new Claim("sub", PrincipalId)),
            manager.Object,
            CancellationToken.None
        );

        Assert.Equal(PrincipalId, result);
    }

    [Fact]
    public async Task ResolvePrincipalIdOrDefaultAsync_WhenUpstreamIdentity_ReturnsOwningPrincipal()
    {
        var manager = CreateStrictMock<IStoreManager>();
        var principalStore = CreateStrictMock<IFederatedPrincipalStore>();
        var identityStore = CreateStrictMock<IFederatedIdentityStore>();

        manager
            .Setup(x => x.GetStore<IFederatedPrincipalStore>())
            .Returns(principalStore.Object)
            .Verifiable();
        manager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(identityStore.Object)
            .Verifiable();
        principalStore
            .Setup(x => x.GetOrDefaultAsync(Subject, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedFederatedPrincipal?)null)
            .Verifiable();
        identityStore
            .Setup(x =>
                x.GetByConnectionSubjectAsync(ConnectionId, Subject, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(Identity(PrincipalId))
            .Verifiable();

        var resolver = new DefaultPrincipalResolver(
            CreateStrictMock<IStoreManagerFactory>().Object,
            CreateStrictMock<ICryptoService>().Object,
            CreateStrictMock<IFederatedIdentityLinkingPolicy>().Object,
            CreateClientResolver(null),
            CreateConnectionResolver(CreateConnection(ConnectionId))
        );

        var result = await resolver.ResolvePrincipalIdOrDefaultAsync(
            CreateOpenIdContext(),
            CreateUser(new Claim("sub", Subject), new Claim("iss", Issuer), ConnectionClaim()),
            manager.Object,
            CancellationToken.None
        );

        Assert.Equal(PrincipalId, result);
    }

    [Fact]
    public async Task ResolvePrincipalIdOrDefaultAsync_WhenNoSubjectClaim_ReturnsNull()
    {
        var manager = CreateStrictMock<IStoreManager>();

        var resolver = new DefaultPrincipalResolver(
            CreateStrictMock<IStoreManagerFactory>().Object,
            CreateStrictMock<ICryptoService>().Object,
            CreateStrictMock<IFederatedIdentityLinkingPolicy>().Object,
            CreateStrictMock<IClientPrincipalResolver>().Object,
            CreateStrictMock<IConnectionResolver>().Object
        );

        var result = await resolver.ResolvePrincipalIdOrDefaultAsync(
            CreateOpenIdContext(),
            CreateUser(),
            manager.Object,
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolvePrincipalIdOrDefaultAsync_WhenNoConnectionClaim_ReturnsNull()
    {
        var manager = CreateStrictMock<IStoreManager>();
        var principalStore = CreateStrictMock<IFederatedPrincipalStore>();

        manager
            .Setup(x => x.GetStore<IFederatedPrincipalStore>())
            .Returns(principalStore.Object)
            .Verifiable();
        principalStore
            .Setup(x => x.GetOrDefaultAsync(Subject, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedFederatedPrincipal?)null)
            .Verifiable();

        var resolver = new DefaultPrincipalResolver(
            CreateStrictMock<IStoreManagerFactory>().Object,
            CreateStrictMock<ICryptoService>().Object,
            CreateStrictMock<IFederatedIdentityLinkingPolicy>().Object,
            CreateClientResolver(null),
            CreateStrictMock<IConnectionResolver>().Object
        );

        var result = await resolver.ResolvePrincipalIdOrDefaultAsync(
            CreateOpenIdContext(),
            CreateUser(new Claim("sub", Subject)),
            manager.Object,
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolvePrincipalIdAsync_WhenAlreadyProvisioned_DoesNotProvision()
    {
        var manager = CreateStrictMock<IStoreManager>();
        var principalStore = CreateStrictMock<IFederatedPrincipalStore>();

        manager
            .Setup(x => x.GetStore<IFederatedPrincipalStore>())
            .Returns(principalStore.Object)
            .Verifiable();
        principalStore
            .Setup(x => x.GetOrDefaultAsync(PrincipalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Principal(PrincipalId))
            .Verifiable();

        var resolver = new DefaultPrincipalResolver(
            CreateStrictMock<IStoreManagerFactory>().Object,
            CreateStrictMock<ICryptoService>().Object,
            CreateStrictMock<IFederatedIdentityLinkingPolicy>().Object,
            CreateStrictMock<IClientPrincipalResolver>().Object,
            CreateStrictMock<IConnectionResolver>().Object
        );

        var result = await resolver.ResolvePrincipalIdAsync(
            CreateOpenIdContext(),
            CreateUser(new Claim("sub", PrincipalId)),
            manager.Object,
            CancellationToken.None
        );

        Assert.Equal(PrincipalId, result);
    }

    [Fact]
    public async Task ResolvePrincipalIdAsync_WhenLinked_AttachesIdentityToLinkedPrincipal()
    {
        var manager = CreateStrictMock<IStoreManager>();
        var principalStore = CreateStrictMock<IFederatedPrincipalStore>();
        var identityStore = CreateStrictMock<IFederatedIdentityStore>();
        var crypto = CreateStrictMock<ICryptoService>();
        var linkingPolicy = CreateStrictMock<IFederatedIdentityLinkingPolicy>();

        manager
            .Setup(x => x.GetStore<IFederatedPrincipalStore>())
            .Returns(principalStore.Object)
            .Verifiable();
        manager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(identityStore.Object)
            .Verifiable();
        principalStore
            .Setup(x => x.GetOrDefaultAsync(Subject, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedFederatedPrincipal?)null)
            .Verifiable();
        identityStore
            .Setup(x =>
                x.GetByConnectionSubjectAsync(ConnectionId, Subject, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((PersistedFederatedIdentity?)null)
            .Verifiable();
        linkingPolicy
            .Setup(x =>
                x.ResolveLinkAsync(
                    It.IsAny<OpenIdContext>(),
                    It.Is<ResolvedConnection>(c => c.ConnectionId == ConnectionId),
                    It.IsAny<ClaimsPrincipal>(),
                    manager.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new FederatedIdentityLinkDecision
                {
                    LinkedPrincipalId = PrincipalId,
                    JoinKey = "user@example.com",
                }
            )
            .Verifiable();
        crypto
            .Setup(x => x.GenerateKey(16, BinaryEncodings.Base64Url))
            .Returns(GeneratedId)
            .Verifiable();

        PersistedFederatedIdentity? captured = null;
        identityStore
            .Setup(x =>
                x.AddAsync(It.IsAny<PersistedFederatedIdentity>(), It.IsAny<CancellationToken>())
            )
            .Callback(
                (PersistedFederatedIdentity identity, CancellationToken _) => captured = identity
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var resolver = new DefaultPrincipalResolver(
            CreateStrictMock<IStoreManagerFactory>().Object,
            crypto.Object,
            linkingPolicy.Object,
            CreateClientResolver(null),
            CreateConnectionResolver(CreateConnection(ConnectionId))
        );

        var result = await resolver.ResolvePrincipalIdAsync(
            CreateOpenIdContext(provisions: true),
            CreateUser(new Claim("sub", Subject), new Claim("iss", Issuer), ConnectionClaim()),
            manager.Object,
            CancellationToken.None
        );

        Assert.Equal(PrincipalId, result);
        Assert.NotNull(captured);
        Assert.Equal(PrincipalId, captured.PrincipalId);
        Assert.Equal("user@example.com", captured.JoinKey);
        Assert.Equal(ConnectionId, captured.ConnectionId);
        // A linked identity never provisions a new principal.
        principalStore.Verify(
            x => x.AddAsync(It.IsAny<PersistedFederatedPrincipal>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ResolvePrincipalIdAsync_WhenNotLinked_ProvisionsNewPrincipalAndIdentity()
    {
        var manager = CreateStrictMock<IStoreManager>();
        var principalStore = CreateStrictMock<IFederatedPrincipalStore>();
        var identityStore = CreateStrictMock<IFederatedIdentityStore>();
        var crypto = CreateStrictMock<ICryptoService>();
        var linkingPolicy = CreateStrictMock<IFederatedIdentityLinkingPolicy>();

        manager
            .Setup(x => x.GetStore<IFederatedPrincipalStore>())
            .Returns(principalStore.Object)
            .Verifiable();
        manager
            .Setup(x => x.GetStore<IFederatedIdentityStore>())
            .Returns(identityStore.Object)
            .Verifiable();
        principalStore
            .Setup(x => x.GetOrDefaultAsync(Subject, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedFederatedPrincipal?)null)
            .Verifiable();
        identityStore
            .Setup(x =>
                x.GetByConnectionSubjectAsync(ConnectionId, Subject, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync((PersistedFederatedIdentity?)null)
            .Verifiable();
        linkingPolicy
            .Setup(x =>
                x.ResolveLinkAsync(
                    It.IsAny<OpenIdContext>(),
                    It.Is<ResolvedConnection>(c => c.ConnectionId == ConnectionId),
                    It.IsAny<ClaimsPrincipal>(),
                    manager.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new FederatedIdentityLinkDecision { JoinKey = null })
            .Verifiable();
        crypto
            .Setup(x => x.GenerateKey(16, BinaryEncodings.Base64Url))
            .Returns(GeneratedId)
            .Verifiable();

        PersistedFederatedPrincipal? capturedPrincipal = null;
        principalStore
            .Setup(x =>
                x.AddAsync(It.IsAny<PersistedFederatedPrincipal>(), It.IsAny<CancellationToken>())
            )
            .Callback(
                (PersistedFederatedPrincipal principal, CancellationToken _) =>
                    capturedPrincipal = principal
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        PersistedFederatedIdentity? capturedIdentity = null;
        identityStore
            .Setup(x =>
                x.AddAsync(It.IsAny<PersistedFederatedIdentity>(), It.IsAny<CancellationToken>())
            )
            .Callback(
                (PersistedFederatedIdentity identity, CancellationToken _) =>
                    capturedIdentity = identity
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        var resolver = new DefaultPrincipalResolver(
            CreateStrictMock<IStoreManagerFactory>().Object,
            crypto.Object,
            linkingPolicy.Object,
            CreateClientResolver(null),
            CreateConnectionResolver(CreateConnection(ConnectionId))
        );

        var result = await resolver.ResolvePrincipalIdAsync(
            CreateOpenIdContext(provisions: true),
            CreateUser(new Claim("sub", Subject), new Claim("iss", Issuer), ConnectionClaim()),
            manager.Object,
            CancellationToken.None
        );

        Assert.Equal(GeneratedId, result);
        Assert.NotNull(capturedPrincipal);
        Assert.Equal(GeneratedId, capturedPrincipal.PrincipalId);
        Assert.Equal(TenantId, capturedPrincipal.TenantId);
        Assert.NotNull(capturedIdentity);
        Assert.Equal(GeneratedId, capturedIdentity.PrincipalId);
        Assert.Equal(TenantId, capturedIdentity.TenantId);
        Assert.Equal(ConnectionId, capturedIdentity.ConnectionId);
        Assert.Equal(Issuer, capturedIdentity.Issuer);
        Assert.Equal(Subject, capturedIdentity.Subject);
    }

    [Fact]
    public async Task ResolvePrincipalIdAsync_WhenNoConnectionClaim_FallsBackToRawSubject()
    {
        var manager = CreateStrictMock<IStoreManager>();
        var principalStore = CreateStrictMock<IFederatedPrincipalStore>();

        manager
            .Setup(x => x.GetStore<IFederatedPrincipalStore>())
            .Returns(principalStore.Object)
            .Verifiable();
        principalStore
            .Setup(x => x.GetOrDefaultAsync(Subject, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedFederatedPrincipal?)null)
            .Verifiable();

        var resolver = new DefaultPrincipalResolver(
            CreateStrictMock<IStoreManagerFactory>().Object,
            CreateStrictMock<ICryptoService>().Object,
            CreateStrictMock<IFederatedIdentityLinkingPolicy>().Object,
            CreateClientResolver(null),
            CreateStrictMock<IConnectionResolver>().Object
        );

        // Subject present but no connection claim: cannot form a durable identity, so the subject value is used as-is.
        var result = await resolver.ResolvePrincipalIdAsync(
            CreateOpenIdContext(),
            CreateUser(new Claim("sub", Subject)),
            manager.Object,
            CancellationToken.None
        );

        Assert.Equal(Subject, result);
    }

    [Fact]
    public async Task ResolvePrincipalIdOrDefaultAsync_WhenTheConnectionIsDisabled_ReturnsNull()
    {
        var manager = CreateStrictMock<IStoreManager>();
        var principalStore = CreateStrictMock<IFederatedPrincipalStore>();

        manager
            .Setup(x => x.GetStore<IFederatedPrincipalStore>())
            .Returns(principalStore.Object)
            .Verifiable();
        principalStore
            .Setup(x => x.GetOrDefaultAsync(Subject, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedFederatedPrincipal?)null)
            .Verifiable();

        var resolver = new DefaultPrincipalResolver(
            CreateStrictMock<IStoreManagerFactory>().Object,
            CreateStrictMock<ICryptoService>().Object,
            CreateStrictMock<IFederatedIdentityLinkingPolicy>().Object,
            CreateClientResolver(null),
            CreateConnectionResolver(CreateConnection(ConnectionId, isEnabled: false))
        );

        var result = await resolver.ResolvePrincipalIdOrDefaultAsync(
            CreateOpenIdContext(),
            CreateUser(new Claim("sub", Subject), ConnectionClaim()),
            manager.Object,
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolvePrincipalIdAsync_WhenTheConnectionIsDisabled_DoesNotProvision()
    {
        var manager = CreateStrictMock<IStoreManager>();
        var principalStore = CreateStrictMock<IFederatedPrincipalStore>();

        manager
            .Setup(x => x.GetStore<IFederatedPrincipalStore>())
            .Returns(principalStore.Object)
            .Verifiable();
        principalStore
            .Setup(x => x.GetOrDefaultAsync(Subject, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedFederatedPrincipal?)null)
            .Verifiable();

        var resolver = new DefaultPrincipalResolver(
            CreateStrictMock<IStoreManagerFactory>().Object,
            CreateStrictMock<ICryptoService>().Object,
            CreateStrictMock<IFederatedIdentityLinkingPolicy>().Object,
            CreateClientResolver(null),
            CreateConnectionResolver(CreateConnection(ConnectionId, isEnabled: false))
        );

        var result = await resolver.ResolvePrincipalIdAsync(
            CreateOpenIdContext(),
            CreateUser(new Claim("sub", Subject), ConnectionClaim()),
            manager.Object,
            CancellationToken.None
        );

        Assert.Equal(Subject, result);
    }

    [Fact]
    public async Task ResolvePrincipalIdOrDefaultAsync_WhenTheSameSubjectIsInTwoConnections_ResolvesEachItsOwnIdentity()
    {
        const string otherPrincipalId = "principal-2";
        var manager = CreateStrictMock<IStoreManager>();
        var principalStore = CreateStrictMock<IFederatedPrincipalStore>();
        var identityStore = CreateStrictMock<IFederatedIdentityStore>();

        manager.Setup(x => x.GetStore<IFederatedPrincipalStore>()).Returns(principalStore.Object);
        manager.Setup(x => x.GetStore<IFederatedIdentityStore>()).Returns(identityStore.Object);
        principalStore
            .Setup(x => x.GetOrDefaultAsync(Subject, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedFederatedPrincipal?)null);
        identityStore
            .Setup(x =>
                x.GetByConnectionSubjectAsync(ConnectionId, Subject, It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(Identity(PrincipalId))
            .Verifiable();
        identityStore
            .Setup(x =>
                x.GetByConnectionSubjectAsync(
                    OtherConnectionId,
                    Subject,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Identity(otherPrincipalId, OtherConnectionId))
            .Verifiable();

        var resolver = new DefaultPrincipalResolver(
            CreateStrictMock<IStoreManagerFactory>().Object,
            CreateStrictMock<ICryptoService>().Object,
            CreateStrictMock<IFederatedIdentityLinkingPolicy>().Object,
            CreateClientResolver(null),
            CreateConnectionResolver(
                CreateConnection(ConnectionId),
                CreateConnection(OtherConnectionId)
            )
        );

        var first = await resolver.ResolvePrincipalIdOrDefaultAsync(
            CreateOpenIdContext(),
            CreateUser(new Claim("sub", Subject), ConnectionClaim()),
            manager.Object,
            CancellationToken.None
        );
        var second = await resolver.ResolvePrincipalIdOrDefaultAsync(
            CreateOpenIdContext(),
            CreateUser(new Claim("sub", Subject), ConnectionClaim(OtherConnectionId)),
            manager.Object,
            CancellationToken.None
        );

        Assert.Equal(PrincipalId, first);
        Assert.Equal(otherPrincipalId, second);
    }

    [Fact]
    public async Task ResolvePrincipalIdOrDefaultAsync_WhenAClientActsForItself_ReturnsItsPrincipal()
    {
        var resolver = CreateActingClientResolver(CreateClient(isDisabled: false));

        var result = await resolver.ResolvePrincipalIdOrDefaultAsync(
            CreateOpenIdContext(),
            CreateUser(new Claim("sub", PrincipalId), new Claim("client_id", ClientId)),
            CreateStoreManagerWithoutPrincipal(),
            CancellationToken.None
        );

        Assert.Equal(PrincipalId, result);
    }

    [Fact]
    public async Task ResolvePrincipalIdOrDefaultAsync_WhenTheActingClientIsDisabled_ReturnsNull()
    {
        var resolver = CreateActingClientResolver(CreateClient(isDisabled: true));

        var result = await resolver.ResolvePrincipalIdOrDefaultAsync(
            CreateOpenIdContext(),
            CreateUser(new Claim("sub", PrincipalId), new Claim("client_id", ClientId)),
            CreateStoreManagerWithoutPrincipal(),
            CancellationToken.None
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolvePrincipalIdAsync_WhenTheActingClientIsDisabled_ReturnsEmptyWithoutProvisioning()
    {
        var resolver = CreateActingClientResolver(CreateClient(isDisabled: true));

        var result = await resolver.ResolvePrincipalIdAsync(
            CreateOpenIdContext(),
            CreateUser(new Claim("sub", PrincipalId), new Claim("client_id", ClientId)),
            CreateStoreManagerWithoutPrincipal(),
            CancellationToken.None
        );

        Assert.Equal(string.Empty, result);
    }
}
