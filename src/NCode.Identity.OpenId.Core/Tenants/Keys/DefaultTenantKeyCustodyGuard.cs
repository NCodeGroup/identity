// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using NCode.Identity.OpenId.Logging;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Tenants.Keys;

/// <summary>
/// Provides a default implementation of <see cref="ITenantKeyCustodyGuard"/> that reads the tenant's key-custody fence
/// from the cell's own database. A tenant the cell holds may change; a tenant whose keys are leaving or arriving, or
/// that the cell does not hold, may not, so a change is deferred and never made on a guess. It never reads the global
/// directory.
/// </summary>
// See ADR-1404, ADR-1411 and ADR-1514.
internal sealed class DefaultTenantKeyCustodyGuard(
    IStoreManagerFactory storeManagerFactory,
    ILogger<DefaultTenantKeyCustodyGuard> logger
) : ITenantKeyCustodyGuard
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private ILogger<DefaultTenantKeyCustodyGuard> Logger { get; } = logger;

    /// <inheritdoc />
    public async ValueTask<bool> MayChangeKeysAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var tenantStore = storeManager.GetStore<ITenantStore>();
        var tenant = await tenantStore.GetOrDefaultAsync(tenantId, cancellationToken);

        if (tenant is null)
        {
            Logger.TenantKeysNotHeld(tenantId);
            return false;
        }

        if (string.Equals(tenant.KeyCustody, TenantKeyCustody.Held, StringComparison.Ordinal))
        {
            return true;
        }

        Logger.TenantKeysFenced(tenantId, tenant.KeyCustody, tenant.KeyCustodyCell);
        return false;
    }
}
