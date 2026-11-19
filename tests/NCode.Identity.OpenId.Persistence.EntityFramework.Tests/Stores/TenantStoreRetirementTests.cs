// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

/// <summary>
/// Proves that the tenant store writes no retired identifier and no retired issuer: the global directory retires them.
/// </summary>
public sealed class TenantStoreRetirementTests : IDisposable
{
    private const string TenantId = "tenant-1";
    private const string Issuer = "https://op.example/tenant-1";
    private const string NewIssuer = "https://op.example/tenant-1-renamed";

    private ServiceProvider Provider { get; }
    private OpenIdDbContext DbContext { get; }
    private TenantStore Store { get; }

    public TenantStoreRetirementTests()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTestOpenIdDbContext(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        );

        Provider = serviceCollection.BuildServiceProvider();
        DbContext = Provider.GetRequiredService<OpenIdDbContext>();
        var keyGenerator = Provider.GetRequiredService<ISurrogateKeyGenerator>();
        Store = new TenantStore(Mock.Of<IStoreProvider>(), keyGenerator, DbContext);
        DbContext.SeedTeam(Provider.GetRequiredService<ISurrogateKeyGenerator>(), "platform");
    }

    public void Dispose()
    {
        DbContext.Dispose();
        Provider.Dispose();
    }

    private static PersistedTenant CreateTenant(string? issuer) =>
        new PersistedTenantBuilder
        {
            TenantId = TenantId,
            Issuer = issuer,
            DisplayName = "Tenant One",
        }.Build();

    private async ValueTask SeedTenantAsync(string? issuer, CancellationToken cancellationToken)
    {
        await Store.AddAsync(CreateTenant(issuer), cancellationToken);
        await SaveAndClearAsync(cancellationToken);
    }

    private async ValueTask SaveAndClearAsync(CancellationToken cancellationToken)
    {
        await DbContext.SaveChangesAsync(cancellationToken);
        DbContext.ChangeTracker.Clear();
    }

    private async ValueTask ChangeIssuerAsync(string? issuer, CancellationToken cancellationToken)
    {
        var tenant = await Store.GetOrDefaultAsync(TenantId, cancellationToken);
        Assert.NotNull(tenant);
        tenant.Issuer = issuer;
        await Store.UpdateAsync(tenant, cancellationToken);
        await SaveAndClearAsync(cancellationToken);
    }

    private async ValueTask AssertNothingRetiredAsync(CancellationToken cancellationToken)
    {
        Assert.Empty(await DbContext.RetiredTenants.ToListAsync(cancellationToken));
        Assert.Empty(await DbContext.RetiredIssuers.ToListAsync(cancellationToken));
    }

    #region RemoveAsync Tests

    [Fact]
    public async Task RemoveAsync_WhenTenantHasAnIssuer_RemovesTheTenantAndWritesNoRetiredRow()
    {
        await SeedTenantAsync(Issuer, CancellationToken.None);

        var removed = await Store.RemoveAsync(TenantId, CancellationToken.None);
        await SaveAndClearAsync(CancellationToken.None);

        Assert.True(removed);
        Assert.Null(await Store.GetOrDefaultAsync(TenantId, CancellationToken.None));
        await AssertNothingRetiredAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RemoveAsync_WhenTenantHasNoIssuer_RemovesTheTenantAndWritesNoRetiredRow()
    {
        await SeedTenantAsync(issuer: null, CancellationToken.None);

        var removed = await Store.RemoveAsync(TenantId, CancellationToken.None);
        await SaveAndClearAsync(CancellationToken.None);

        Assert.True(removed);
        await AssertNothingRetiredAsync(CancellationToken.None);
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_WhenTheIssuerChanges_StoresTheNewIssuerAndWritesNoRetiredRow()
    {
        await SeedTenantAsync(Issuer, CancellationToken.None);

        await ChangeIssuerAsync(NewIssuer, CancellationToken.None);

        var tenant = await Store.GetOrDefaultAsync(TenantId, CancellationToken.None);
        Assert.NotNull(tenant);
        Assert.Equal(NewIssuer, tenant.Issuer);
        await AssertNothingRetiredAsync(CancellationToken.None);
    }

    [Fact]
    public async Task UpdateAsync_WhenTheIssuerIsCleared_WritesNoRetiredRow()
    {
        await SeedTenantAsync(Issuer, CancellationToken.None);

        await ChangeIssuerAsync(issuer: null, CancellationToken.None);

        await AssertNothingRetiredAsync(CancellationToken.None);
    }

    #endregion
}
