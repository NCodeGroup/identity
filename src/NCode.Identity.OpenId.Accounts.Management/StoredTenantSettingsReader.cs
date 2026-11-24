// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Resolves a tenant's effective settings from its stored tenant and team documents, whatever tenant the request was
/// served from.
/// </summary>
internal sealed class StoredTenantSettingsReader(
    IEffectiveSettingsResolver effectiveSettingsResolver
)
{
    private IEffectiveSettingsResolver EffectiveSettingsResolver { get; } =
        effectiveSettingsResolver;

    /// <summary>
    /// Resolves a tenant's effective settings from its stored documents: its team's settings under its own document.
    /// </summary>
    /// <param name="storeManager">The store manager that reads the tenant.</param>
    /// <param name="tenantId">The identifier of the tenant.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tenant's effective settings, or <c>null</c> when the tenant does not exist.</returns>
    public async ValueTask<IReadOnlySettingCollection?> ReadAsync(
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

        var teamSettings = await EffectiveSettingsResolver.ResolveTeamAsync(
            tenant.TeamId,
            cancellationToken
        );
        return EffectiveSettingsResolver.ResolveStored(
            teamSettings,
            OpenIdSettingScopes.Tenant,
            tenantId,
            tenant.Settings.Value
        );
    }
}
