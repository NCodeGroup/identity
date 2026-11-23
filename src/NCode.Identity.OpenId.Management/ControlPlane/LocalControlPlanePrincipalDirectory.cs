// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Principals;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// Provides the adapter of <see cref="IControlPlanePrincipalDirectory"/> that describes the principals from the cell's
/// own store, in the control plane's tenant, by asking every registered <see cref="IPrincipalDescriber"/> only about
/// the names still undescribed, then describes each requested principal that none knows and the federated principal
/// store holds as <see cref="PrincipalTypes.FederatedUser"/>.
/// </summary>
// See ADR-1411 and ADR-1465.
internal sealed class LocalControlPlanePrincipalDirectory(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    ITenantResolver tenantResolver,
    IEnumerable<IPrincipalDescriber> principalDescribers
) : IControlPlanePrincipalDirectory
{
    /// <summary>
    /// Keeps the first of each name, in request order, compared ignoring case as the stores match names.
    /// </summary>
    /// <param name="names">The requested names.</param>
    /// <returns>The distinct names, in request order.</returns>
    private static List<string> GetDistinct(IReadOnlyList<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinct = new List<string>(names.Count);
        foreach (var name in names)
        {
            if (seen.Add(name))
            {
                distinct.Add(name);
            }
        }

        return distinct;
    }

    /// <summary>
    /// Keeps the principal identifiers that no summary describes.
    /// </summary>
    /// <param name="names">The principal identifiers still undescribed.</param>
    /// <param name="summaries">The summaries found so far, keyed by principal identifier.</param>
    /// <returns>The principal identifiers that no summary describes.</returns>
    private static List<string> GetRemaining(
        List<string> names,
        Dictionary<string, PrincipalSummaryResource> summaries
    )
    {
        var remaining = new List<string>(names.Count);
        foreach (var name in names)
        {
            if (!summaries.ContainsKey(name))
            {
                remaining.Add(name);
            }
        }

        return remaining;
    }

    /// <summary>
    /// Keeps the usernames that no summary names.
    /// </summary>
    /// <param name="names">The usernames still undescribed.</param>
    /// <param name="described">The usernames that the summaries found so far name.</param>
    /// <returns>The usernames that no summary names.</returns>
    private static List<string> GetRemaining(List<string> names, HashSet<string> described)
    {
        var remaining = new List<string>(names.Count);
        foreach (var name in names)
        {
            if (!described.Contains(name))
            {
                remaining.Add(name);
            }
        }

        return remaining;
    }

    /// <summary>
    /// Describes the principals that the federated principal store holds, each named by its first federated identity or
    /// by its identifier.
    /// </summary>
    /// <param name="storeManager">The store manager that reads the principals and their identities.</param>
    /// <param name="principalIds">The principal identifiers that no describer knows.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>One summary for each principal the store holds, or none when it holds none.</returns>
    private static async ValueTask<List<PrincipalSummaryResource>> DescribeFederatedAsync(
        IStoreManager storeManager,
        List<string> principalIds,
        CancellationToken cancellationToken
    )
    {
        var principalStore = storeManager.GetStore<IFederatedPrincipalStore>();
        var principals = await principalStore.GetByIdsAsync(principalIds, cancellationToken);
        if (principals.Count == 0)
        {
            return [];
        }

        var foundIds = new List<string>(principals.Count);
        foreach (var principal in principals)
        {
            foundIds.Add(principal.PrincipalId);
        }

        var identityStore = storeManager.GetStore<IFederatedIdentityStore>();
        var identities = await identityStore.GetByPrincipalsAsync(foundIds, cancellationToken);

        // A principal is named by its first identity, ordered by issuer, then subject.
        var firstIdentities = new Dictionary<string, PersistedFederatedIdentity>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var identity in identities)
        {
            if (
                firstIdentities.TryGetValue(identity.PrincipalId, out var first)
                && !IsBefore(identity, first)
            )
            {
                continue;
            }

            firstIdentities[identity.PrincipalId] = identity;
        }

        var summaries = new List<PrincipalSummaryResource>(principals.Count);
        foreach (var principal in principals)
        {
            var displayName = firstIdentities.TryGetValue(principal.PrincipalId, out var first)
                ? $"{first.Subject} ({first.Issuer})"
                : principal.PrincipalId;
            var summary = new PrincipalSummaryResource
            {
                PrincipalId = principal.PrincipalId,
                PrincipalType = PrincipalTypes.FederatedUser,
                DisplayName = displayName,
                UserName = null,
            };
            summaries.Add(summary);
        }

        return summaries;
    }

    /// <summary>
    /// Orders two federated identities by issuer, then subject, ordinally.
    /// </summary>
    /// <param name="left">The identity to place.</param>
    /// <param name="right">The identity to compare with.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> sorts before <paramref name="right"/>.</returns>
    private static bool IsBefore(PersistedFederatedIdentity left, PersistedFederatedIdentity right)
    {
        var byIssuer = string.CompareOrdinal(left.Issuer, right.Issuer);
        return byIssuer != 0
            ? byIssuer < 0
            : string.CompareOrdinal(left.Subject, right.Subject) < 0;
    }

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private ITenantResolver TenantResolver { get; } = tenantResolver;
    private ImmutableArray<IPrincipalDescriber> Describers { get; } = [.. principalDescribers];

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PrincipalSummaryResource>> FindAsync(
        ControlPlanePrincipalQuery query,
        CancellationToken cancellationToken
    )
    {
        var remainingIds = GetDistinct(query.PrincipalIds);
        var remainingUserNames = GetDistinct(query.UserNames);
        if (remainingIds.Count == 0 && remainingUserNames.Count == 0)
        {
            return [];
        }

        var rootTenantId = TenantResolver.GetControlPlaneTenantId();

        using var scope = AmbientTenantAccessor.BeginScope(rootTenantId);
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        // The first describer to know a principal names it, and each describer is asked only about the names still
        // undescribed.
        var summaries = new Dictionary<string, PrincipalSummaryResource>(
            StringComparer.OrdinalIgnoreCase
        );
        var describedUserNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var describer in Describers)
        {
            if (remainingIds.Count == 0 && remainingUserNames.Count == 0)
            {
                break;
            }

            var remaining = new ControlPlanePrincipalQuery
            {
                PrincipalIds = remainingIds,
                UserNames = remainingUserNames,
            };
            var found = await describer.FindAsync(storeManager, remaining, cancellationToken);
            foreach (var summary in found)
            {
                summaries.TryAdd(summary.PrincipalId, summary);
                if (summary.UserName is not null)
                {
                    describedUserNames.Add(summary.UserName);
                }
            }

            remainingIds = GetRemaining(remainingIds, summaries);
            remainingUserNames = GetRemaining(remainingUserNames, describedUserNames);
        }

        // A principal no describer knows, which the federated principal store holds, signs in only through federated
        // identities.
        if (remainingIds.Count > 0)
        {
            var federated = await DescribeFederatedAsync(
                storeManager,
                remainingIds,
                cancellationToken
            );

            // The describers' summaries win, so a principal they described keeps its summary.
            foreach (var summary in federated)
            {
                summaries.TryAdd(summary.PrincipalId, summary);
            }
        }

        return [.. summaries.Values];
    }
}
