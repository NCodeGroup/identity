// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Principals;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// Provides the adapter of <see cref="IControlPlanePrincipalDirectory"/> that describes the principals from the cell's
/// own store, in the control plane's tenant, by asking every registered <see cref="IPrincipalDescriber"/>.
/// </summary>
// See ADR-1411 and ADR-1465.
internal sealed class LocalControlPlanePrincipalDirectory(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    ITenantResolver tenantResolver,
    IEnumerable<IPrincipalDescriber> describers
) : IControlPlanePrincipalDirectory
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private ITenantResolver TenantResolver { get; } = tenantResolver;
    private IEnumerable<IPrincipalDescriber> Describers { get; } = describers;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PrincipalSummaryResource>> FindAsync(
        ControlPlanePrincipalQuery query,
        CancellationToken cancellationToken
    )
    {
        if (query.PrincipalIds.Count == 0 && query.UserNames.Count == 0)
        {
            return [];
        }

        var rootTenantId = TenantResolver.GetControlPlaneTenantId();

        using var scope = AmbientTenantAccessor.BeginScope(rootTenantId);
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        // The first describer to know a principal names it.
        var summaries = new Dictionary<string, PrincipalSummaryResource>(StringComparer.Ordinal);
        foreach (var describer in Describers)
        {
            var found = await describer.FindAsync(storeManager, query, cancellationToken);
            foreach (var summary in found)
            {
                summaries.TryAdd(summary.PrincipalId, summary);
            }
        }

        return [.. summaries.Values];
    }
}
