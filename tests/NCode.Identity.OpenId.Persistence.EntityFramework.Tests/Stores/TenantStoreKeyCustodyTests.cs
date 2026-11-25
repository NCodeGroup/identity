// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

// Validates the key-custody fence a tenant row carries (ADR-1536).
public sealed class TenantStoreKeyCustodyTests : IDisposable
{
    private const string TenantId = "tenant-1";

    private SqliteConnection Connection { get; }
    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private TenantStore Store { get; }

    public TenantStoreKeyCustodyTests()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options => options.UseSqlite(Connection));
        Provider = serviceCollection.BuildServiceProvider();

        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
        DbContext.Database.EnsureCreated();

        var keyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        DbContext.SeedTeam(keyGenerator, "platform");

        Store = new TenantStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            TestPageCursorProtector.Instance,
            DbContext
        );
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Provider.Dispose();
        Connection.Dispose();
    }

    private async ValueTask SeedTenantAsync(
        PersistedTenant tenant,
        CancellationToken cancellationToken
    )
    {
        await Store.AddAsync(tenant, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task AddAsync_WithDefaults_ThenGet_IsHeldWithoutCell()
    {
        var cancellationToken = CancellationToken.None;
        await SeedTenantAsync(
            new PersistedTenantBuilder { TenantId = TenantId }.Build(),
            cancellationToken
        );

        var tenant = await Store.GetOrDefaultAsync(TenantId, cancellationToken);

        Assert.NotNull(tenant);
        Assert.Equal(TenantKeyCustody.Held, tenant.KeyCustody);
        Assert.Null(tenant.KeyCustodyCell);
    }

    [Fact]
    public async Task AddAsync_WithIncomingCustody_RoundTrips()
    {
        var cancellationToken = CancellationToken.None;
        var persisted = new PersistedTenantBuilder { TenantId = TenantId }.Build();
        persisted.KeyCustody = TenantKeyCustody.Incoming;
        persisted.KeyCustodyCell = "cell-a";
        await SeedTenantAsync(persisted, cancellationToken);

        var tenant = await Store.GetOrDefaultAsync(TenantId, cancellationToken);

        Assert.NotNull(tenant);
        Assert.Equal(TenantKeyCustody.Incoming, tenant.KeyCustody);
        Assert.Equal("cell-a", tenant.KeyCustodyCell);
    }

    [Fact]
    public async Task SetKeyCustodyAsync_SetsFenceAndCell_AfterSave()
    {
        var cancellationToken = CancellationToken.None;
        await SeedTenantAsync(
            new PersistedTenantBuilder { TenantId = TenantId }.Build(),
            cancellationToken
        );

        var found = await Store.SetKeyCustodyAsync(
            TenantId,
            TenantKeyCustody.Outgoing,
            "cell-b",
            cancellationToken
        );
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();

        Assert.True(found);
        var tenant = await Store.GetOrDefaultAsync(TenantId, cancellationToken);
        Assert.NotNull(tenant);
        Assert.Equal(TenantKeyCustody.Outgoing, tenant.KeyCustody);
        Assert.Equal("cell-b", tenant.KeyCustodyCell);
    }

    [Fact]
    public async Task SetKeyCustodyAsync_BackToHeld_ClearsCell()
    {
        var cancellationToken = CancellationToken.None;
        await SeedTenantAsync(
            new PersistedTenantBuilder { TenantId = TenantId }.Build(),
            cancellationToken
        );
        await Store.SetKeyCustodyAsync(
            TenantId,
            TenantKeyCustody.Outgoing,
            "cell-b",
            cancellationToken
        );
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();

        await Store.SetKeyCustodyAsync(TenantId, TenantKeyCustody.Held, null, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();

        var tenant = await Store.GetOrDefaultAsync(TenantId, cancellationToken);
        Assert.NotNull(tenant);
        Assert.Equal(TenantKeyCustody.Held, tenant.KeyCustody);
        Assert.Null(tenant.KeyCustodyCell);
    }

    [Fact]
    public async Task SetKeyCustodyAsync_UnknownTenant_ReturnsFalse()
    {
        var cancellationToken = CancellationToken.None;

        var found = await Store.SetKeyCustodyAsync(
            "missing",
            TenantKeyCustody.Held,
            null,
            cancellationToken
        );

        Assert.False(found);
    }
}
