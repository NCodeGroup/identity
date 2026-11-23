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
        if (query.PrincipalIds.Count == 0)
        {
            return [];
        }

        var clientStore = storeManager.GetStore<IClientStore>();
        var clients = await ReadClientsAsync(clientStore, query.PrincipalIds, cancellationToken);
        var summaries = new List<PrincipalSummaryResource>(clients.Count);

        foreach (var client in clients)
        {
            var summary = new PrincipalSummaryResource
            {
                PrincipalId = client.PrincipalId,
                PrincipalType = PrincipalTypes.Client,
                DisplayName = client.ClientId,
                UserName = null,
            };
            summaries.Add(summary);
        }

        return summaries;
    }

    /// <summary>
    /// Reads the clients that hold any of the principals across tenants, inside an unscoped ambient scope, because a
    /// principal names at most one client in any tenant.
    /// </summary>
    /// <param name="clientStore">The store that reads the clients.</param>
    /// <param name="principalIds">The identifiers of the principals whose clients to read.</param>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The clients that hold any of the principals.</returns>
    private async ValueTask<IReadOnlyList<PersistedClient>> ReadClientsAsync(
        IClientStore clientStore,
        IReadOnlyList<string> principalIds,
        CancellationToken cancellationToken
    )
    {
        // A principal names at most one client in any tenant, so the principals are read across tenants. See ADR-1018.
        using var unscoped = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);
        return await clientStore.GetByPrincipalsAsync(principalIds, cancellationToken);
    }
}
