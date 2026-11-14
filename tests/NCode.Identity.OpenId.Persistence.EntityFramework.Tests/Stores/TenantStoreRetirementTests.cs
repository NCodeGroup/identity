// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using IdGen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Stores;

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
        var idGenerator = Provider.GetRequiredService<IIdGenerator<long>>();
        Store = new TenantStore(Mock.Of<IStoreProvider>(), idGenerator, DbContext);
        DbContext.SeedTeam(Provider.GetRequiredService<IIdGenerator<long>>(), "platform");
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

    private async ValueTask<bool> IsTenantIdRetiredAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var normalizedTenantId = tenantId.ToLowerInvariant();
        return await DbContext.RetiredTenants.AnyAsync(
            entity => entity.NormalizedTenantId == normalizedTenantId,
            cancellationToken
        );
    }

    private async ValueTask<string?> GetRetiredIssuerHolderOrDefaultAsync(
        string issuer,
        CancellationToken cancellationToken
    )
    {
        var normalizedIssuer = issuer.ToLowerInvariant();
        return await DbContext
            .RetiredIssuers.Where(entity => entity.NormalizedIssuer == normalizedIssuer)
            .Select(entity => entity.TenantId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    #region RemoveAsync Tests

    [Fact]
    public async Task RemoveAsync_WhenTenantHasAnIssuer_RetiresTheIdentifierAndTheIssuer()
    {
        await SeedTenantAsync(Issuer, CancellationToken.None);

        var removed = await Store.RemoveAsync(TenantId, CancellationToken.None);
        await SaveAndClearAsync(CancellationToken.None);

        Assert.True(removed);
        Assert.True(
            await IsTenantIdRetiredAsync(TenantId.ToUpperInvariant(), CancellationToken.None)
        );
        Assert.Equal(
            TenantId,
            await GetRetiredIssuerHolderOrDefaultAsync(
                Issuer.ToUpperInvariant(),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task RemoveAsync_WhenTenantHasNoIssuer_RetiresOnlyTheIdentifier()
    {
        await SeedTenantAsync(issuer: null, CancellationToken.None);

        await Store.RemoveAsync(TenantId, CancellationToken.None);
        await SaveAndClearAsync(CancellationToken.None);

        Assert.True(await IsTenantIdRetiredAsync(TenantId, CancellationToken.None));
        Assert.Empty(await DbContext.RetiredIssuers.ToListAsync());
    }

    [Fact]
    public async Task RemoveAsync_WhenTheIssuerIsAlreadyRetired_KeepsASingleRetiredIssuer()
    {
        await SeedTenantAsync(Issuer, CancellationToken.None);
        await ChangeIssuerAsync(NewIssuer, CancellationToken.None);
        await ChangeIssuerAsync(Issuer, CancellationToken.None);

        await Store.RemoveAsync(TenantId, CancellationToken.None);
        await SaveAndClearAsync(CancellationToken.None);

        var retiredIssuers = await DbContext
            .RetiredIssuers.OrderBy(entity => entity.Issuer)
            .Select(entity => entity.Issuer)
            .ToListAsync();
        Assert.Equal([Issuer, NewIssuer], retiredIssuers);
    }

    #endregion

    #region UpdateAsync Tests

    [Fact]
    public async Task UpdateAsync_WhenTheIssuerChanges_RetiresThePreviousIssuer()
    {
        await SeedTenantAsync(Issuer, CancellationToken.None);

        await ChangeIssuerAsync(NewIssuer, CancellationToken.None);

        Assert.Equal(
            TenantId,
            await GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
        Assert.Null(await GetRetiredIssuerHolderOrDefaultAsync(NewIssuer, CancellationToken.None));
        Assert.False(await IsTenantIdRetiredAsync(TenantId, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_WhenTheIssuerIsCleared_RetiresThePreviousIssuer()
    {
        await SeedTenantAsync(Issuer, CancellationToken.None);

        await ChangeIssuerAsync(issuer: null, CancellationToken.None);

        Assert.Equal(
            TenantId,
            await GetRetiredIssuerHolderOrDefaultAsync(Issuer, CancellationToken.None)
        );
    }

    [Fact]
    public async Task UpdateAsync_WhenOnlyTheIssuerCaseChanges_RetiresNothing()
    {
        await SeedTenantAsync(Issuer, CancellationToken.None);

        await ChangeIssuerAsync(Issuer.ToUpperInvariant(), CancellationToken.None);

        Assert.Empty(await DbContext.RetiredIssuers.ToListAsync());
    }

    #endregion
}
