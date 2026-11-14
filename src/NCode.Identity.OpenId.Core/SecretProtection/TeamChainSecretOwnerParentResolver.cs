// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Persistence;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Teams;
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
/// A team is read from the <see cref="IOpenIdTeamCache"/> when it is cached and from the team store when it is not.
/// </remarks>
// See ADR-1414.
internal sealed class TeamChainSecretOwnerParentResolver(
    IStoreManagerFactory storeManagerFactory,
    IOpenIdTeamCache teamCache,
    IAmbientTenantAccessor ambientTenantAccessor
) : ISecretOwnerParentResolver
{
    private const string UnscopedReason = "secret owner ancestors";

    private static IReadOnlyList<SecretOwner> Empty { get; } = [];

    private static IReadOnlyList<SecretOwner> PlatformOnly { get; } = [SecretOwner.Platform];

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
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

    private static bool IsOwnerType(SecretOwner owner, string ownerType) =>
        string.Equals(owner.OwnerType, ownerType, StringComparison.OrdinalIgnoreCase);

    // The tenant's team, then the teams above it, nearest first.
    private async ValueTask<IReadOnlyList<string>> GetTenantTeamIdsAsync(
        string tenantId,
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var tenant = await storeManager
            .GetStore<ITenantStore>()
            .GetOrDefaultAsync(tenantId, cancellationToken);
        if (tenant is null)
            return [];

        return await GetTeamLineageAsync(storeManager, tenant.TeamId, cancellationToken);
    }

    // The teams above the team, nearest first.
    private async ValueTask<IReadOnlyList<string>> GetTeamAncestorIdsAsync(
        string teamId,
        CancellationToken cancellationToken
    )
    {
        using var _ = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);

        var cached = await TryGetCachedLineageAsync(teamId, cancellationToken);
        if (cached is not null)
            return [.. cached.Skip(1)];

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var lineage = await ReadStoredLineageAsync(storeManager, teamId, cancellationToken);
        return [.. lineage.Skip(1)];
    }

    // The team followed by the teams above it, nearest first; empty when the team is not stored.
    private async ValueTask<IReadOnlyList<string>> GetTeamLineageAsync(
        IStoreManager storeManager,
        string teamId,
        CancellationToken cancellationToken
    )
    {
        var cached = await TryGetCachedLineageAsync(teamId, cancellationToken);
        return cached ?? await ReadStoredLineageAsync(storeManager, teamId, cancellationToken);
    }

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
}
