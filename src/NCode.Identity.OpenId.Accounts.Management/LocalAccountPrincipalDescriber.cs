// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Management.Principals;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Describes the principal of a local account, named by its principal identifier or by its username. A principal is a
/// local account's when it owns the account's self-issued identity.
/// </summary>
// See ADR-1465.
internal sealed class LocalAccountPrincipalDescriber : IPrincipalDescriber
{
    /// <summary>
    /// Describes the local accounts that own the self-issued identity of each requested principal.
    /// </summary>
    /// <param name="accountStore">The store of local accounts.</param>
    /// <param name="identityStore">The store of federated identities.</param>
    /// <param name="principalIds">The principal identifiers to describe.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A summary per account that a self-issued identity of a requested principal names, or an empty list, without
    /// reading accounts, when no principal has a self-issued identity.
    /// </returns>
    private static async ValueTask<List<PrincipalSummaryResource>> DescribeByPrincipalIdsAsync(
        ILocalAccountStore accountStore,
        IFederatedIdentityStore identityStore,
        IReadOnlyList<string> principalIds,
        CancellationToken cancellationToken
    )
    {
        var summaries = new List<PrincipalSummaryResource>();

        var identities = await identityStore.GetNamesByPrincipalsAsync(
            principalIds,
            cancellationToken
        );

        var principalIdBySubject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var identity in identities)
        {
            if (
                string.Equals(
                    identity.Issuer,
                    AccountConstants.SelfIssuer,
                    StringComparison.Ordinal
                )
            )
            {
                principalIdBySubject[identity.Subject] = identity.PrincipalId;
            }
        }

        if (principalIdBySubject.Count == 0)
        {
            return summaries;
        }

        var localAccountIds = new List<string>(principalIdBySubject.Keys);
        var accounts = await accountStore.GetNamesByIdsAsync(localAccountIds, cancellationToken);
        foreach (var account in accounts)
        {
            if (principalIdBySubject.TryGetValue(account.LocalAccountId, out var principalId))
            {
                var summary = ToSummary(principalId, account);
                summaries.Add(summary);
            }
        }

        return summaries;
    }

    /// <summary>
    /// Describes the principals of the local accounts that have the requested usernames.
    /// </summary>
    /// <param name="accountStore">The store of local accounts.</param>
    /// <param name="identityStore">The store of federated identities.</param>
    /// <param name="userNames">The usernames to describe.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A summary per account whose self-issued identity is found, or an empty list, without reading identities, when no
    /// account matches.
    /// </returns>
    private static async ValueTask<List<PrincipalSummaryResource>> DescribeByUserNamesAsync(
        ILocalAccountStore accountStore,
        IFederatedIdentityStore identityStore,
        IReadOnlyList<string> userNames,
        CancellationToken cancellationToken
    )
    {
        var summaries = new List<PrincipalSummaryResource>();

        var accounts = await accountStore.GetNamesByUserNamesAsync(userNames, cancellationToken);
        if (accounts.Count == 0)
        {
            return summaries;
        }

        var localAccountIds = new List<string>(accounts.Count);
        foreach (var account in accounts)
        {
            localAccountIds.Add(account.LocalAccountId);
        }

        var found = await identityStore.GetByIssuerSubjectsAsync(
            AccountConstants.SelfIssuer,
            localAccountIds,
            cancellationToken
        );
        var identityBySubject = new Dictionary<string, PersistedFederatedIdentity>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var identity in found)
        {
            identityBySubject[identity.Subject] = identity;
        }

        foreach (var account in accounts)
        {
            if (identityBySubject.TryGetValue(account.LocalAccountId, out var identity))
            {
                var summary = ToSummary(identity.PrincipalId, account);
                summaries.Add(summary);
            }
        }

        return summaries;
    }

    /// <summary>
    /// Summarizes the principal of a local account.
    /// </summary>
    /// <param name="principalId">The identifier of the principal that owns the account's self-issued identity.</param>
    /// <param name="account">The local account.</param>
    /// <returns>The summary, named by the account's display name and username.</returns>
    private static PrincipalSummaryResource ToSummary(
        string principalId,
        LocalAccountNameProjection account
    ) =>
        new()
        {
            PrincipalId = principalId,
            PrincipalType = PrincipalTypes.LocalAccount,
            DisplayName = account.DisplayName,
            UserName = account.UserName,
        };

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PrincipalSummaryResource>> FindAsync(
        IStoreManager storeManager,
        ControlPlanePrincipalQuery query,
        CancellationToken cancellationToken
    )
    {
        var summaries = new List<PrincipalSummaryResource>();

        if (query.PrincipalIds.Count == 0 && query.UserNames.Count == 0)
        {
            return summaries;
        }

        var accountStore = storeManager.GetStore<ILocalAccountStore>();
        var identityStore = storeManager.GetStore<IFederatedIdentityStore>();
        var described = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (query.PrincipalIds.Count > 0)
        {
            var byIds = await DescribeByPrincipalIdsAsync(
                accountStore,
                identityStore,
                query.PrincipalIds,
                cancellationToken
            );
            foreach (var summary in byIds)
            {
                if (described.Add(summary.PrincipalId))
                {
                    summaries.Add(summary);
                }
            }
        }

        if (query.UserNames.Count > 0)
        {
            var byNames = await DescribeByUserNamesAsync(
                accountStore,
                identityStore,
                query.UserNames,
                cancellationToken
            );
            foreach (var summary in byNames)
            {
                if (described.Add(summary.PrincipalId))
                {
                    summaries.Add(summary);
                }
            }
        }

        return summaries;
    }
}
