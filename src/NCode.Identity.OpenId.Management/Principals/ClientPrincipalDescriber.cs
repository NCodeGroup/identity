// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.Principals;

/// <summary>
/// Describes the principal of a client that acts for itself, named by its client identifier. It ignores user names.
/// </summary>
// See ADR-1465.
internal sealed class ClientPrincipalDescriber(IAmbientTenantAccessor ambientTenantAccessor)
    : IPrincipalDescriber
{
    /// <summary>
    /// The reason of the unscoped scope in which the describer reads the clients of principals.
    /// </summary>
    internal const string UnscopedReason = "principal description";

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PrincipalSummaryResource>> FindAsync(
        IStoreManager storeManager,
        ControlPlanePrincipalQuery query,
        CancellationToken cancellationToken
    )
    {
        var summaries = new List<PrincipalSummaryResource>();
        var clientStore = storeManager.GetStore<IClientStore>();

        foreach (var principalId in query.PrincipalIds.Distinct(StringComparer.Ordinal))
        {
            PersistedClient? client;

            // A principal names at most one client in any tenant, so it is read across tenants. See ADR-1018.
            using (AmbientTenantAccessor.BeginUnscoped(UnscopedReason))
            {
                client = await clientStore.GetByPrincipalOrDefaultAsync(
                    principalId,
                    cancellationToken
                );
            }

            if (client is null)
            {
                continue;
            }

            var summary = new PrincipalSummaryResource
            {
                PrincipalId = principalId,
                PrincipalType = PrincipalTypes.Client,
                DisplayName = client.ClientId,
                UserName = null,
            };
            summaries.Add(summary);
        }

        return summaries;
    }
}
