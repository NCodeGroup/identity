// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Reads the control-plane tenant's default connection from the stored tenant and team documents, so the platform's
/// local-account routes and the principal describer name the same connection, whatever tenant the request was served
/// from.
/// </summary>
internal sealed class ControlPlaneDefaultConnectionReader(
    ITenantResolver tenantResolver,
    StoredTenantSettingsReader storedTenantSettingsReader
)
{
    private ITenantResolver TenantResolver { get; } = tenantResolver;
    private StoredTenantSettingsReader StoredTenantSettingsReader { get; } =
        storedTenantSettingsReader;

    /// <summary>
    /// Finds the connection that the control-plane tenant's <c>default_connection</c> setting names.
    /// </summary>
    /// <param name="storeManager">The store manager that reads the tenant and the connection.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The connection, or <c>null</c> when the tenant or the connection does not exist.</returns>
    public async ValueTask<PersistedConnection?> FindAsync(
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var tenantId = TenantResolver.GetControlPlaneTenantId();
        var tenantSettings = await StoredTenantSettingsReader.ReadAsync(
            storeManager,
            tenantId,
            cancellationToken
        );
        if (tenantSettings is null)
        {
            return null;
        }

        var defaultName = tenantSettings.GetValue(OpenIdSettingKeys.DefaultConnection);
        var connectionStore = storeManager.GetStore<IConnectionStore>();
        return await connectionStore.GetByNameOrDefaultAsync(defaultName, cancellationToken);
    }
}
