// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

public sealed class FederatedIdentityStoreTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string PrincipalId = "principal-1";
    private const string ConnectionId = "connection-1";
    private const string OtherConnectionId = "connection-2";
    private const string Subject = "upstream-subject-1";

    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private FederatedPrincipalStore PrincipalStore { get; }
    private ConnectionStore ConnectionStore { get; }
    private FederatedIdentityStore Store { get; }

    public FederatedIdentityStoreTests()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        );

        Provider = serviceCollection.BuildServiceProvider();
        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
        var keyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        PrincipalStore = new FederatedPrincipalStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            TestPageCursorProtector.Instance,
            DbContext
        );
        ConnectionStore = new ConnectionStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            TestPageCursorProtector.Instance,
            DbContext
        );
        Store = new FederatedIdentityStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            TestPageCursorProtector.Instance,
            DbContext
        );
        DbContext.SeedTenant(keyGenerator, TenantId);
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Provider.Dispose();
    }

    private static PersistedFederatedIdentity CreateIdentity(
        string federatedIdentityId,
        string principalId,
        string connectionId,
        string subject,
        string? joinKey = null
    ) =>
        new()
        {
            TenantId = TenantId,
            FederatedIdentityId = federatedIdentityId,
            PrincipalId = principalId,
            ConnectionId = connectionId,
            Issuer = null,
            Subject = subject,
            JoinKey = joinKey,
            ConcurrencyToken = string.Empty,
        };

    private async ValueTask SeedConnectionAsync(
        string connectionId,
        string name,
        CancellationToken cancellationToken
    )
    {
        await ConnectionStore.AddAsync(
            new PersistedConnection
            {
                TenantId = TenantId,
                ConnectionId = connectionId,
                Name = name,
                Strategy = "local",
                DisplayName = name,
                IsSystem = false,
                IsEnabled = true,
                Options = JsonSerializer.SerializeToElement(new { store = "default" }),
                Settings = new PersistedConnectionSettings
                {
                    TenantId = TenantId,
                    ConnectionId = connectionId,
                    Value = JsonSerializer.SerializeToElement(new Dictionary<string, object>()),
                },
            },
            cancellationToken
        );
        await DbContext.SaveChangesAsync(cancellationToken);
    }

    private async ValueTask SeedPrincipalAsync(
        string principalId,
        CancellationToken cancellationToken
    )
    {
        if (!await DbContext.Connections.AnyAsync(cancellationToken))
        {
            await SeedConnectionAsync(ConnectionId, "first", cancellationToken);
            await SeedConnectionAsync(OtherConnectionId, "second", cancellationToken);
        }

        await PrincipalStore.AddAsync(
            new PersistedFederatedPrincipal
            {
                TenantId = TenantId,
                PrincipalId = principalId,
                ConcurrencyToken = string.Empty,
            },
            cancellationToken
        );

        // The principal must be persisted before its identities are added (an in-memory query does not observe
        // unsaved inserts).
        await DbContext.SaveChangesAsync(cancellationToken);
    }

    [Fact]
    public async Task AddAsync_WhenPersisted_RoundTripsByFederatedIdentityId()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);

        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, ConnectionId, Subject),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetOrDefaultAsync("identity-1", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("identity-1", loaded.FederatedIdentityId);
        Assert.Equal(PrincipalId, loaded.PrincipalId);
        Assert.Equal(ConnectionId, loaded.ConnectionId);
        Assert.Equal(Subject, loaded.Subject);
        Assert.NotEmpty(loaded.ConcurrencyToken);
    }

    [Fact]
    public async Task AddAsync_WhenPrincipalMissing_Throws()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Store.AddAsync(
                CreateIdentity("identity-1", "missing-principal", ConnectionId, Subject),
                CancellationToken.None
            )
        );

        Assert.Contains("missing-principal", exception.Message);
    }

    [Fact]
    public async Task GetByConnectionSubjectAsync_WhenMatch_ResolvesToOwningPrincipal()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, ConnectionId, Subject),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetByConnectionSubjectAsync(
            ConnectionId,
            Subject,
            CancellationToken.None
        );

        Assert.NotNull(loaded);
        Assert.Equal(PrincipalId, loaded.PrincipalId);
        Assert.Equal("identity-1", loaded.FederatedIdentityId);
    }

    [Fact]
    public async Task GetByConnectionSubjectAsync_WhenNoMatch_ReturnsNull()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);

        var loaded = await Store.GetByConnectionSubjectAsync(
            ConnectionId,
            Subject,
            CancellationToken.None
        );

        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetByConnectionSubjectsAsync_WhenSomeSubjectsMatch_ReturnsOnlyTheMatchingIdentitiesOfTheConnection()
    {
        const string secondPrincipalId = "principal-2";
        const string secondSubject = "upstream-subject-2";
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        await SeedPrincipalAsync(secondPrincipalId, CancellationToken.None);
        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, ConnectionId, Subject),
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateIdentity("identity-2", secondPrincipalId, ConnectionId, secondSubject),
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateIdentity("identity-3", PrincipalId, OtherConnectionId, Subject),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetByConnectionSubjectsAsync(
            ConnectionId,
            [Subject, secondSubject, "missing"],
            CancellationToken.None
        );

        Assert.Equal(2, loaded.Count);
        Assert.Equal(
            PrincipalId,
            Assert
                .Single(loaded, identity => identity.FederatedIdentityId == "identity-1")
                .PrincipalId
        );
        Assert.Equal(
            secondPrincipalId,
            Assert
                .Single(loaded, identity => identity.FederatedIdentityId == "identity-2")
                .PrincipalId
        );
    }

    [Fact]
    public async Task GetByConnectionSubjectsAsync_WhenTheSubjectsDifferInCase_MatchesThem()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, ConnectionId, Subject),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetByConnectionSubjectsAsync(
            ConnectionId,
            [Subject.ToUpperInvariant()],
            CancellationToken.None
        );

        var identity = Assert.Single(loaded);
        Assert.Equal(PrincipalId, identity.PrincipalId);
    }

    [Fact]
    public async Task GetByConnectionSubjectsAsync_WhenNoSubjects_ReturnsNone()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, ConnectionId, Subject),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetByConnectionSubjectsAsync(
            ConnectionId,
            [],
            CancellationToken.None
        );

        Assert.Empty(loaded);
    }

    [Fact]
    public async Task GetByPrincipalAsync_ReturnsAllIdentitiesOfThePrincipal()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, ConnectionId, Subject),
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateIdentity("identity-2", PrincipalId, OtherConnectionId, "subject-2"),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetByPrincipalAsync(PrincipalId, CancellationToken.None);

        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, identity => identity.FederatedIdentityId == "identity-1");
        Assert.Contains(loaded, identity => identity.FederatedIdentityId == "identity-2");
        Assert.All(loaded, identity => Assert.Equal(PrincipalId, identity.PrincipalId));
    }

    [Fact]
    public async Task GetByJoinKeyAsync_WhenMatch_ReturnsIdentitiesWithThatJoinKey()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, ConnectionId, Subject, "user@example.com"),
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateIdentity(
                "identity-2",
                PrincipalId,
                OtherConnectionId,
                "subject-2",
                "other@example.com"
            ),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var loaded = await Store.GetByJoinKeyAsync("USER@example.com", CancellationToken.None);

        Assert.Single(loaded);
        Assert.Equal("identity-1", loaded[0].FederatedIdentityId);
        Assert.Equal("user@example.com", loaded[0].JoinKey);
    }

    [Fact]
    public async Task AddAsync_WhenTheSameSubjectIsInTwoConnections_KeepsBothIdentities()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, ConnectionId, Subject),
            CancellationToken.None
        );
        await Store.AddAsync(
            CreateIdentity("identity-2", PrincipalId, OtherConnectionId, Subject),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        var first = await Store.GetByConnectionSubjectAsync(
            ConnectionId,
            Subject,
            CancellationToken.None
        );
        var second = await Store.GetByConnectionSubjectAsync(
            OtherConnectionId,
            Subject,
            CancellationToken.None
        );

        Assert.Equal("identity-1", first?.FederatedIdentityId);
        Assert.Equal("identity-2", second?.FederatedIdentityId);
    }

    [Fact]
    public async Task AddAsync_WhenTheConnectionIsMissing_Throws()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Store.AddAsync(
                CreateIdentity("identity-1", PrincipalId, "missing-connection", Subject),
                CancellationToken.None
            )
        );

        Assert.Contains("missing-connection", exception.Message);
    }

    [Fact]
    public async Task AnyByConnectionAsync_ReportsOnlyTheConnectionsWithIdentities()
    {
        await SeedPrincipalAsync(PrincipalId, CancellationToken.None);
        await Store.AddAsync(
            CreateIdentity("identity-1", PrincipalId, ConnectionId, Subject),
            CancellationToken.None
        );
        await DbContext.SaveChangesAsync();

        Assert.True(await Store.AnyByConnectionAsync(ConnectionId, CancellationToken.None));
        Assert.False(await Store.AnyByConnectionAsync(OtherConnectionId, CancellationToken.None));
    }
}
