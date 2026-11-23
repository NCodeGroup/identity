// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

public sealed class ConnectionStoreTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string ConnectionId = "conn-1";

    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private TenantStore TenantStore { get; }
    private ConnectionStore Store { get; }

    public ConnectionStoreTests()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        );

        Provider = serviceCollection.BuildServiceProvider();
        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
        var keyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        TenantStore = new TenantStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            TestPageCursorProtector.Instance,
            DbContext
        );
        Store = new ConnectionStore(
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
    }

    private static PersistedConnection CreateConnection(
        string connectionId = ConnectionId,
        string name = "Local"
    ) =>
        new()
        {
            TenantId = TenantId,
            ConnectionId = connectionId,
            Name = name,
            Strategy = "local",
            DisplayName = "Username and password",
            IsEnabled = true,
            Options = JsonSerializer.SerializeToElement(new { store = "default" }),
            Settings = new PersistedConnectionSettings
            {
                TenantId = TenantId,
                ConnectionId = connectionId,
                Value = JsonSerializer.SerializeToElement(new Dictionary<string, object>()),
            },
        };

    private async ValueTask SeedTenantAsync(CancellationToken cancellationToken)
    {
        DbContext.SeedTeam(Provider.GetRequiredService<ISurrogateKeyGenerator>(), "platform");
        var tenant = new PersistedTenantBuilder
        {
            TenantId = TenantId,
            DisplayName = "Tenant One",
        }.Build();

        await TenantStore.AddAsync(tenant, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();
    }

    private async ValueTask AddAndSaveAsync(
        PersistedConnection connection,
        CancellationToken cancellationToken
    )
    {
        await Store.AddAsync(connection, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task AddAsync_RoundtripsById()
    {
        await SeedTenantAsync(CancellationToken.None);
        await AddAndSaveAsync(CreateConnection(), CancellationToken.None);

        var reloaded = await Store.GetOrDefaultAsync(ConnectionId, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(TenantId, reloaded.TenantId);
        Assert.Equal("Local", reloaded.Name);
        Assert.Equal("local", reloaded.Strategy);
        Assert.Equal("Username and password", reloaded.DisplayName);
        Assert.True(reloaded.IsEnabled);
        Assert.Equal("default", reloaded.Options.GetProperty("store").GetString());
        Assert.False(string.IsNullOrEmpty(reloaded.ConcurrencyToken));
        Assert.False(string.IsNullOrEmpty(reloaded.Settings.ConcurrencyToken));
    }

    [Fact]
    public async Task GetByNameOrDefaultAsync_IsCaseInsensitive()
    {
        await SeedTenantAsync(CancellationToken.None);
        await AddAndSaveAsync(CreateConnection(), CancellationToken.None);

        var found = await Store.GetByNameOrDefaultAsync("LOCAL", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(ConnectionId, found.ConnectionId);
    }

    [Fact]
    public async Task GetByNameOrDefaultAsync_SeesAConnectionAddedBeforeSave()
    {
        await SeedTenantAsync(CancellationToken.None);

        await Store.AddAsync(CreateConnection(), CancellationToken.None);
        var found = await Store.GetByNameOrDefaultAsync("local", CancellationToken.None);
        var foundById = await Store.GetOrDefaultAsync(ConnectionId, CancellationToken.None);

        Assert.NotNull(found);
        Assert.NotNull(foundById);
    }

    [Fact]
    public async Task GetByNameOrDefaultAsync_WhenMissing_ReturnsNull()
    {
        await SeedTenantAsync(CancellationToken.None);

        var found = await Store.GetByNameOrDefaultAsync("missing", CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task GetAllAsync_OrdersByName()
    {
        await SeedTenantAsync(CancellationToken.None);
        await Store.AddAsync(CreateConnection("conn-b", "beta"), CancellationToken.None);
        await Store.AddAsync(CreateConnection("conn-c", "gamma"), CancellationToken.None);
        await Store.AddAsync(CreateConnection("conn-a", "alpha"), CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var all = await Store.GetAllAsync(CancellationToken.None);

        Assert.Equal(["alpha", "beta", "gamma"], all.Select(connection => connection.Name));
    }

    [Fact]
    public async Task GetPageAsync_PagesThroughConnections()
    {
        await SeedTenantAsync(CancellationToken.None);
        await Store.AddAsync(CreateConnection("conn-a", "alpha"), CancellationToken.None);
        await Store.AddAsync(CreateConnection("conn-b", "beta"), CancellationToken.None);
        await Store.AddAsync(CreateConnection("conn-c", "gamma"), CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var first = await Store.GetPageAsync(new PageRequest { Limit = 2 }, CancellationToken.None);
        var second = await Store.GetPageAsync(
            new PageRequest { Limit = 2, Cursor = first.NextCursor },
            CancellationToken.None
        );

        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        Assert.Single(second.Items);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesMutableMembers()
    {
        await SeedTenantAsync(CancellationToken.None);
        await AddAndSaveAsync(CreateConnection(), CancellationToken.None);

        var loaded = await Store.GetOrDefaultAsync(ConnectionId, CancellationToken.None);
        Assert.NotNull(loaded);
        loaded.DisplayName = "Renamed";
        loaded.IsEnabled = false;
        await Store.UpdateAsync(loaded, CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var reloaded = await Store.GetOrDefaultAsync(ConnectionId, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal("Renamed", reloaded.DisplayName);
        Assert.False(reloaded.IsEnabled);
    }

    [Fact]
    public async Task UpdateAsync_WhenConcurrencyMismatch_Throws()
    {
        await SeedTenantAsync(CancellationToken.None);
        await AddAndSaveAsync(CreateConnection(), CancellationToken.None);

        var loaded = await Store.GetOrDefaultAsync(ConnectionId, CancellationToken.None);
        Assert.NotNull(loaded);
        loaded.ConcurrencyToken = "stale";

        await Assert.ThrowsAsync<StoreConcurrencyException>(async () =>
            await Store.UpdateAsync(loaded, CancellationToken.None)
        );
    }

    [Fact]
    public async Task UpdateSettingsAsync_UpdatesValueAndBumpsToken()
    {
        await SeedTenantAsync(CancellationToken.None);
        await AddAndSaveAsync(CreateConnection(), CancellationToken.None);

        var loaded = await Store.GetOrDefaultAsync(ConnectionId, CancellationToken.None);
        Assert.NotNull(loaded);
        var settings = loaded.Settings;
        var originalToken = settings.ConcurrencyToken;
        settings.Value = JsonSerializer.SerializeToElement(
            new Dictionary<string, object> { ["password_min_length"] = 12 }
        );

        await Store.UpdateSettingsAsync(settings, CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        Assert.NotEqual(originalToken, settings.ConcurrencyToken);
        var reloaded = await Store.GetOrDefaultAsync(ConnectionId, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal(12, reloaded.Settings.Value.GetProperty("password_min_length").GetInt32());
    }

    [Fact]
    public async Task UpdateSettingsAsync_WhenConcurrencyMismatch_Throws()
    {
        await SeedTenantAsync(CancellationToken.None);
        await AddAndSaveAsync(CreateConnection(), CancellationToken.None);

        var loaded = await Store.GetOrDefaultAsync(ConnectionId, CancellationToken.None);
        Assert.NotNull(loaded);
        var settings = loaded.Settings;
        settings.ConcurrencyToken = "stale";

        await Assert.ThrowsAsync<StoreConcurrencyException>(async () =>
            await Store.UpdateSettingsAsync(settings, CancellationToken.None)
        );
    }

    [Fact]
    public async Task RemoveAsync_RemovesTheConnection()
    {
        await SeedTenantAsync(CancellationToken.None);
        await AddAndSaveAsync(CreateConnection(), CancellationToken.None);

        await Store.RemoveAsync(ConnectionId, CancellationToken.None);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var reloaded = await Store.GetOrDefaultAsync(ConnectionId, CancellationToken.None);
        Assert.Null(reloaded);
    }

    [Fact]
    public void Model_RejectsADuplicateNamePerTenantThroughAUniqueIndex()
    {
        var entityType = DbContext.Model.FindEntityType(typeof(ConnectionEntity));

        Assert.NotNull(entityType);
        var index = Assert.Single(
            entityType.GetIndexes(),
            candidate =>
                candidate.IsUnique
                && candidate
                    .Properties.Select(property => property.Name)
                    .SequenceEqual([
                        nameof(ConnectionEntity.TenantId),
                        nameof(ConnectionEntity.NormalizedName),
                    ])
        );
        Assert.True(index.IsUnique);
    }
}
