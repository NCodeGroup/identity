// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.EntityFramework.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework;

public sealed class OpenIdDbContextTests : IDisposable
{
    private SqliteConnection Connection { get; }
    private ServiceProvider? ScopeProvider { get; set; }
    private string? AmbientTenantId { get; set; }
    private bool AmbientIsUnscoped { get; set; }

    public OpenIdDbContextTests()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();
    }

    public void Dispose()
    {
        ScopeProvider?.Dispose();
        Connection.Dispose();
    }

    private async ValueTask<OpenIdDbContext> CreateSeededDbContextAsync(
        CancellationToken cancellationToken
    )
    {
        var accessor = new Mock<IAmbientTenantAccessor>(MockBehavior.Strict);
        accessor.Setup(x => x.TenantId).Returns(() => AmbientTenantId).Verifiable();
        accessor.Setup(x => x.IsUnscoped).Returns(() => AmbientIsUnscoped).Verifiable();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(accessor.Object);
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        );

        ScopeProvider = serviceCollection.BuildServiceProvider();
        var dbContext = ScopeProvider.GetRequiredService<OpenIdDbContext>();
        var keyGenerator = ScopeProvider.GetRequiredService<ISurrogateKeyGenerator>();
        var tenantStore = new TenantStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            TestPageCursorProtector.Instance,
            dbContext
        );
        var clientStore = new ClientStore(
            Mock.Of<IStoreProvider>(),
            keyGenerator,
            TestPageCursorProtector.Instance,
            dbContext,
            TestCryptoServices.CreateResourceIdGenerator()
        );

        AmbientIsUnscoped = true;
        dbContext.SeedTeam(keyGenerator, "platform");
        foreach (var tenantId in new[] { "tenant-1", "tenant-2" })
        {
            await tenantStore.AddAsync(
                new PersistedTenantBuilder { TenantId = tenantId, DisplayName = tenantId }.Build(),
                cancellationToken
            );
            await dbContext.SaveChangesAsync(cancellationToken);
            await clientStore.AddAsync(
                new PersistedClientBuilder
                {
                    TenantId = tenantId,
                    ClientId = $"client-{tenantId}",
                }.Build(),
                cancellationToken
            );
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        AmbientIsUnscoped = false;

        return dbContext;
    }

    #region OnConfiguring Tests

    [Fact]
    public void OnConfiguring_WhenTheProviderIsRelational_SplitsQueries()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options => options.UseSqlite(Connection));

        using var provider = serviceCollection.BuildServiceProvider();
        using var dbContext = provider.GetRequiredService<OpenIdDbContext>();

        var relational = RelationalOptionsExtension.Extract(
            dbContext.GetService<IDbContextOptions>()
        );

        Assert.Equal(QuerySplittingBehavior.SplitQuery, relational.QuerySplittingBehavior);
    }

    [Fact]
    public void OnConfiguring_WhenTheHostChoseAQuerySplittingBehavior_KeepsIt()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseSqlite(
                Connection,
                sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery)
            )
        );

        using var provider = serviceCollection.BuildServiceProvider();
        using var dbContext = provider.GetRequiredService<OpenIdDbContext>();

        var relational = RelationalOptionsExtension.Extract(
            dbContext.GetService<IDbContextOptions>()
        );

        Assert.Equal(QuerySplittingBehavior.SingleQuery, relational.QuerySplittingBehavior);
    }

    #endregion

    #region Tenant Scope Filter Tests

    [Fact]
    public async Task Query_UnsetScope_ReturnsNoTenantRows()
    {
        var dbContext = await CreateSeededDbContextAsync(CancellationToken.None);

        var clients = await dbContext.Set<ClientEntity>().ToListAsync();

        Assert.Empty(clients);
    }

    [Fact]
    public async Task Query_TenantScope_ReturnsOnlyThatTenant()
    {
        var dbContext = await CreateSeededDbContextAsync(CancellationToken.None);
        AmbientTenantId = "Tenant-2";

        var clients = await dbContext.Set<ClientEntity>().ToListAsync();

        var client = Assert.Single(clients);
        Assert.Equal("tenant-2", client.NormalizedTenantId);
    }

    [Fact]
    public async Task Query_Unscoped_ReturnsAllTenants()
    {
        var dbContext = await CreateSeededDbContextAsync(CancellationToken.None);
        AmbientIsUnscoped = true;

        var clients = await dbContext.Set<ClientEntity>().ToListAsync();

        Assert.Equal(2, clients.Count);
    }

    #endregion

    #region EnsureUnscoped Tests

    [Fact]
    public async Task EnsureUnscoped_WhenTheScopeIsATenantScope_ThrowsNamingTheOperation()
    {
        var dbContext = await CreateSeededDbContextAsync(CancellationToken.None);
        AmbientTenantId = "tenant-1";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            dbContext.EnsureUnscoped("TestStore.ReadAsync")
        );

        Assert.Equal(
            "TestStore.ReadAsync reads across tenants and runs only in an unscoped scope (IAmbientTenantAccessor.BeginUnscoped).",
            exception.Message
        );
    }

    [Fact]
    public async Task EnsureUnscoped_WhenTheScopeIsUnset_Throws()
    {
        var dbContext = await CreateSeededDbContextAsync(CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() =>
            dbContext.EnsureUnscoped("TestStore.ReadAsync")
        );
    }

    [Fact]
    public async Task EnsureUnscoped_WhenTheScopeIsUnscoped_DoesNotThrow()
    {
        var dbContext = await CreateSeededDbContextAsync(CancellationToken.None);
        AmbientIsUnscoped = true;

        Assert.Null(Record.Exception(() => dbContext.EnsureUnscoped("TestStore.ReadAsync")));
    }

    #endregion
}
