// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Reads the control-plane tenant's default connection from the stored tenant and team documents, so the platform's
/// local-account routes and the principal describer name the same connection, whatever tenant the request was served
/// from. It also holds the one step that resolves a tenant's effective settings from its stored documents.
/// </summary>
internal sealed class ControlPlaneDefaultConnectionReader(
    ITenantResolver tenantResolver,
    IEffectiveSettingsResolver effectiveSettingsResolver
)
{
    private ITenantResolver TenantResolver { get; } = tenantResolver;
    private IEffectiveSettingsResolver EffectiveSettingsResolver { get; } =
        effectiveSettingsResolver;

    /// <summary>
    /// Resolves a tenant's effective settings from its stored documents: its team's settings under its own document.
    /// </summary>
    /// <param name="effectiveSettingsResolver">The resolver that merges the stored documents.</param>
    /// <param name="storeManager">The store manager that reads the tenant.</param>
    /// <param name="tenantId">The identifier of the tenant.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tenant's effective settings, or <c>null</c> when the tenant does not exist.</returns>
    public static async ValueTask<IReadOnlySettingCollection?> ReadTenantSettingsAsync(
        IEffectiveSettingsResolver effectiveSettingsResolver,
        IStoreManager storeManager,
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        var tenantStore = storeManager.GetStore<ITenantStore>();
        var tenant = await tenantStore.GetOrDefaultAsync(tenantId, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        var teamSettings = await effectiveSettingsResolver.ResolveTeamAsync(
            tenant.TeamId,
            cancellationToken
        );
        return effectiveSettingsResolver.ResolveStored(
            teamSettings,
            OpenIdSettingScopes.Tenant,
            tenantId,
            tenant.Settings.Value
        );
    }

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
        var tenantSettings = await ReadTenantSettingsAsync(
            EffectiveSettingsResolver,
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
