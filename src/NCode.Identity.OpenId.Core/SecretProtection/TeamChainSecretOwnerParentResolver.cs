// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Teams;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Secrets.Persistence.Protection;
using NCode.PropertyBag;

namespace NCode.Identity.OpenId.SecretProtection;

/// <summary>
/// Resolves the ancestors of a secret owner from the team tree: a tenant's ancestors are its team, the teams above that
/// team from the nearest, and the platform owner; a team's are the teams above it from the nearest, and the platform
/// owner. An owner that is not a tenant or a team, and a tenant or team that is not stored, has the platform owner as
/// its only ancestor, and the platform owner has none.
/// </summary>
/// <remarks>
/// A tenant is read from the <see cref="IOpenIdTenantCache"/> when it is cached and from the tenant store when it is
/// not, and a team is read from the <see cref="IOpenIdTeamCache"/> when it is cached and from the team store when it is
/// not. A resolution reads the stores through at most one store manager, which it opens only when a read misses its
/// cache.
/// </remarks>
// See ADR-1414.
internal sealed class TeamChainSecretOwnerParentResolver(
    IStoreManagerFactory storeManagerFactory,
    IOpenIdTenantCache tenantCache,
    IOpenIdTeamCache teamCache,
    IAmbientTenantAccessor ambientTenantAccessor
) : ISecretOwnerParentResolver
{
    /// <summary>
    /// The reason that the resolver gives when it opens an unscoped ambient tenant scope to read teams.
    /// </summary>
    private const string UnscopedReason = "secret owner ancestors";

    /// <summary>
    /// Gets the ancestors of an owner that has none.
    /// </summary>
    private static IReadOnlyList<SecretOwner> Empty { get; } = [];

    /// <summary>
    /// Gets the ancestors of an owner whose only ancestor is the platform owner.
    /// </summary>
    private static IReadOnlyList<SecretOwner> PlatformOnly { get; } = [SecretOwner.Platform];

    /// <summary>
    /// Determines whether the secret owner has the specified owner type, compared without regard to case.
    /// </summary>
    /// <param name="owner">The secret owner to check.</param>
    /// <param name="ownerType">The owner type to compare with.</param>
    /// <returns><c>true</c> when the owner has the owner type; otherwise, <c>false</c>.</returns>
    private static bool IsOwnerType(SecretOwner owner, string ownerType) =>
        string.Equals(owner.OwnerType, ownerType, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a team from the team store, with the teams above it.
    /// </summary>
    /// <param name="storeManager">The store manager to read the team with.</param>
    /// <param name="teamId">The identifier of the team to read.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The team's identifier followed by the identifiers of the teams above it, nearest first; empty when the
    /// team is not stored.</returns>
    private static async ValueTask<IReadOnlyList<string>> ReadStoredLineageAsync(
        IStoreManager storeManager,
        string teamId,
        CancellationToken cancellationToken
    )
    {
        var team = await storeManager
            .GetStore<ITeamStore>()
            .GetOrDefaultAsync(teamId, cancellationToken);
        if (team is null)
            return [];

        return [team.TeamId, .. team.AncestorTeamIds.Reverse()];
    }

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IOpenIdTenantCache TenantCache { get; } = tenantCache;
    private IOpenIdTeamCache TeamCache { get; } = teamCache;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<SecretOwner>> GetAncestorsAsync(
        SecretOwner owner,
        CancellationToken cancellationToken
    )
    {
        if (IsOwnerType(owner, SecretOwnerTypes.Platform))
            return Empty;

        IReadOnlyList<string> teamIds;
        if (IsOwnerType(owner, SecretOwnerTypes.Tenant))
        {
            teamIds = await GetTenantTeamIdsAsync(owner.OwnerId, cancellationToken);
        }
        else if (IsOwnerType(owner, SecretOwnerTypes.Team))
        {
            teamIds = await GetTeamAncestorIdsAsync(owner.OwnerId, cancellationToken);
        }
        else
        {
            return PlatformOnly;
        }

        if (teamIds.Count == 0)
            return PlatformOnly;

        return
        [
            .. teamIds.Select(teamId => new SecretOwner(SecretOwnerTypes.Team, teamId).Normalize()),
            SecretOwner.Platform,
        ];
    }

    /// <summary>
    /// Gets a tenant's team and the teams above it, reading the tenant from the tenant cache when it is cached and from
    /// the tenant store when it is not.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The identifier of the tenant's team followed by the identifiers of the teams above it, nearest first;
    /// empty when the tenant or its team is not stored.</returns>
    private async ValueTask<IReadOnlyList<string>> GetTenantTeamIdsAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        var lease = await TenantCache.TryGetAsync(
            tenantId,
            PropertyBagFactory.Create(),
            cancellationToken
        );
        if (lease.IsActive)
        {
            string teamId;
            await using (lease)
            {
                teamId = lease.Value.TeamId;
            }

            return await GetTeamLineageAsync(storeManager: null, teamId, cancellationToken);
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var tenant = await storeManager
            .GetStore<ITenantStore>()
            .GetOrDefaultAsync(tenantId, cancellationToken);
        if (tenant is null)
            return [];

        return await GetTeamLineageAsync(storeManager, tenant.TeamId, cancellationToken);
    }

    /// <summary>
    /// Gets the teams above a team.
    /// </summary>
    /// <param name="teamId">The identifier of the team.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The identifiers of the teams above the team, nearest first; empty when the team is a top-level team or
    /// is not stored.</returns>
    private async ValueTask<IReadOnlyList<string>> GetTeamAncestorIdsAsync(
        string teamId,
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        var lineage = await GetTeamLineageAsync(storeManager: null, teamId, cancellationToken);
        return [.. lineage.Skip(1)];
    }

    /// <summary>
    /// Gets a team and the teams above it, from the team cache when the team is cached and from the team store when it
    /// is not.
    /// </summary>
    /// <param name="storeManager">The store manager to read the team with when it is not cached, or <c>null</c> to
    /// read it with one that is opened for that read and disposed after it.</param>
    /// <param name="teamId">The identifier of the team.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The team's identifier followed by the identifiers of the teams above it, nearest first; empty when the
    /// team is not stored.</returns>
    private async ValueTask<IReadOnlyList<string>> GetTeamLineageAsync(
        IStoreManager? storeManager,
        string teamId,
        CancellationToken cancellationToken
    )
    {
        var cached = await TryGetCachedLineageAsync(teamId, cancellationToken);
        if (cached is not null)
            return cached;

        if (storeManager is not null)
            return await ReadStoredLineageAsync(storeManager, teamId, cancellationToken);

        await using var ownedStoreManager = await StoreManagerFactory.CreateAsync(
            cancellationToken
        );

        return await ReadStoredLineageAsync(ownedStoreManager, teamId, cancellationToken);
    }

    /// <summary>
    /// Gets a team and the teams above it from the team cache.
    /// </summary>
    /// <param name="teamId">The identifier of the team.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The team's identifier followed by the identifiers of the teams above it, nearest first; <c>null</c>
    /// when the team is not cached.</returns>
    private async ValueTask<IReadOnlyList<string>?> TryGetCachedLineageAsync(
        string teamId,
        CancellationToken cancellationToken
    )
    {
        var lease = await TeamCache.TryGetAsync(
            teamId,
            PropertyBagFactory.Create(),
            cancellationToken
        );
        if (!lease.IsActive)
            return null;

        await using (lease)
        {
            // A team path names the teams from the top-level team down to the team itself.
            return [.. TeamPaths.Parse(lease.Value.Path).Reverse()];
        }
    }
}
