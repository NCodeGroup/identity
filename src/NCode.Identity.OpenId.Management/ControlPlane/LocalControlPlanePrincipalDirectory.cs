// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Principals;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// Provides the adapter of <see cref="IControlPlanePrincipalDirectory"/> that describes the principals from the cell's
/// own store, in the control plane's tenant, by asking every registered <see cref="IPrincipalDescriber"/>, then
/// describes each requested principal that none knows and the federated principal store holds as
/// <see cref="PrincipalTypes.FederatedUser"/>.
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

        // A principal no describer knows, which the federated principal store holds, signs in only through federated
        // identities.
        var unknownIds = query
            .PrincipalIds.Distinct(StringComparer.Ordinal)
            .Where(principalId => !summaries.ContainsKey(principalId))
            .ToList();
        if (unknownIds.Count > 0)
        {
            var principalStore = storeManager.GetStore<IFederatedPrincipalStore>();
            var identityStore = storeManager.GetStore<IFederatedIdentityStore>();
            foreach (var principalId in unknownIds)
            {
                var principal = await principalStore.GetOrDefaultAsync(
                    principalId,
                    cancellationToken
                );
                if (principal is null)
                {
                    continue;
                }

                var identities = await identityStore.GetByPrincipalAsync(
                    principalId,
                    cancellationToken
                );
                var first = identities
                    .OrderBy(identity => identity.Issuer, StringComparer.Ordinal)
                    .ThenBy(identity => identity.Subject, StringComparer.Ordinal)
                    .FirstOrDefault();
                var displayName = first is null ? principalId : $"{first.Subject} ({first.Issuer})";
                summaries[principalId] = new PrincipalSummaryResource
                {
                    PrincipalId = principalId,
                    PrincipalType = PrincipalTypes.FederatedUser,
                    DisplayName = displayName,
                    UserName = null,
                };
            }
        }

        return [.. summaries.Values];
    }
}
