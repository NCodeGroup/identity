// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.DataContracts;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

// A tenant's secrets are tenant-scoped like every other tenant-owned row: the tenant entity itself stays unscoped
// (so it can be discovered by id during resolution), but its secrets are isolated by the ambient-tenant filter. In
// practice tenant resolution and tenant management read secrets with no ambient scope; these tests assert the filter.
public sealed class TenantStoreTenantScopingTests : IDisposable
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private const string SecretB = "secret-b";

    private ServiceProvider Provider { get; }
    private TestAmbientTenantAccessor AmbientTenantAccessor { get; } = new();
    private OpenIdDbContext DbContext { get; }
    private TenantStore Store { get; }

    public TenantStoreTenantScopingTests()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<ISurrogateKeyGenerator>(
            new SequentialSurrogateKeyGenerator()
        );
        serviceCollection.AddSingleton<IPageCursorProtector>(TestPageCursorProtector.Instance);

        Provider = serviceCollection.BuildServiceProvider();

        var options = new DbContextOptionsBuilder<OpenIdDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseApplicationServiceProvider(Provider)
            .Options;

        DbContext = new OpenIdDbContext(
            options,
            AmbientTenantAccessor,
            [new CoreModelContributor()]
        );
        Store = new TenantStore(
            Mock.Of<IStoreProvider>(),
            Provider.GetRequiredService<ISurrogateKeyGenerator>(),
            TestPageCursorProtector.Instance,
            DbContext
        );
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Provider.Dispose();
    }

    private static PersistedSecret CreateSecret(string secretId) =>
        new PersistedSecretBuilder
        {
            SecretId = secretId,
            Use = "sig",
            Algorithm = "RS256",
            EncodedValue = "encoded-value",
        }.Build();

    private async ValueTask SeedTenantAsync(
        string tenantId,
        PersistedSecret[] secrets,
        CancellationToken cancellationToken
    )
    {
        DbContext.SeedTeam(Provider.GetRequiredService<ISurrogateKeyGenerator>(), "platform");

        var tenant = new PersistedTenantBuilder { TenantId = tenantId, Secrets = secrets }.Build();

        await Store.AddAsync(tenant, cancellationToken);
    }

    private async ValueTask SeedAsync(CancellationToken cancellationToken)
    {
        await SeedTenantAsync(TenantA, [], cancellationToken);
        await SeedTenantAsync(TenantB, [CreateSecret(SecretB)], cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task GetSecretOrDefaultAsync_WhenScopedToOtherTenant_ReturnsNull()
    {
        await SeedAsync(CancellationToken.None);

        AmbientTenantAccessor.TenantId = TenantA;

        var crossTenant = await Store.GetSecretOrDefaultAsync(
            TenantB,
            SecretB,
            CancellationToken.None
        );

        Assert.Null(crossTenant);
    }

    [Fact]
    public async Task GetSecretOrDefaultAsync_WhenUnscoped_ReturnsSecret()
    {
        await SeedAsync(CancellationToken.None);

        using var scope = AmbientTenantAccessor.BeginUnscoped("test");

        var secret = await Store.GetSecretOrDefaultAsync(TenantB, SecretB, CancellationToken.None);

        Assert.NotNull(secret);
        Assert.Equal(SecretB, secret.SecretId);
    }

    [Fact]
    public async Task GetOrDefaultAsync_WhenScopedToOtherTenant_LoadsTenantButFiltersItsSecrets()
    {
        await SeedAsync(CancellationToken.None);

        AmbientTenantAccessor.TenantId = TenantA;

        // The tenant entity is unscoped (discoverable by id), but its secrets are tenant-scoped and filtered out.
        var tenantB = await Store.GetOrDefaultAsync(TenantB, CancellationToken.None);

        Assert.NotNull(tenantB);
        Assert.Empty(tenantB.Secrets.Value);
    }

    [Fact]
    public async Task RemoveAsync_WhenTheScopeIsATenantScope_Throws()
    {
        await SeedAsync(CancellationToken.None);

        using var scope = AmbientTenantAccessor.BeginScope(TenantA);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Store.RemoveAsync(TenantA, CancellationToken.None)
        );

        Assert.StartsWith("TenantStore.RemoveAsync reads across tenants", exception.Message);
    }

    [Fact]
    public async Task RemoveFromCellAsync_WhenTheScopeIsATenantScope_Throws()
    {
        await SeedAsync(CancellationToken.None);

        using var scope = AmbientTenantAccessor.BeginScope(TenantA);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Store.RemoveFromCellAsync(TenantA, CancellationToken.None)
        );

        Assert.StartsWith(
            "TenantStore.RemoveFromCellAsync reads across tenants",
            exception.Message
        );
    }

    private sealed class TestAmbientTenantAccessor : IAmbientTenantAccessor
    {
        public string? TenantId { get; set; }

        public bool IsScoped => TenantId is not null;

        public bool IsUnscoped { get; private set; }

        public string? UnscopedReason { get; private set; }

        public IDisposable BeginScope(string tenantId)
        {
            TenantId = tenantId;
            return new Scope(() => TenantId = null);
        }

        public IDisposable BeginUnscoped(string reason)
        {
            IsUnscoped = true;
            UnscopedReason = reason;
            return new Scope(() =>
            {
                IsUnscoped = false;
                UnscopedReason = null;
            });
        }

        private sealed class Scope(Action restore) : IDisposable
        {
            public void Dispose() => restore();
        }
    }
}
