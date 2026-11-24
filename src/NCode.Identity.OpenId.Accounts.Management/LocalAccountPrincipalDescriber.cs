// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Management.Principals;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Describes the principal of a local account, named by its principal identifier or by its username. A principal is a
/// local account's when it owns the account's identity in a local connection, and a username names an account of the
/// control-plane tenant's default connection, the connection that the platform's local-account routes serve.
/// </summary>
// See ADR-1465.
internal sealed class LocalAccountPrincipalDescriber(
    LocalAccountStoreCatalog localAccountStoreCatalog,
    ITenantResolver tenantResolver,
    IEffectiveSettingsResolver effectiveSettingsResolver
) : IPrincipalDescriber
{
    private LocalAccountStoreCatalog LocalAccountStoreCatalog { get; } = localAccountStoreCatalog;
    private ITenantResolver TenantResolver { get; } = tenantResolver;
    private IEffectiveSettingsResolver EffectiveSettingsResolver { get; } =
        effectiveSettingsResolver;

    /// <summary>
    /// Describes the local accounts that own an identity, in a local connection, of each requested principal.
    /// </summary>
    /// <param name="storeManager">The store manager that reads the identities, the connections and the
    /// accounts.</param>
    /// <param name="principalIds">The principal identifiers to describe.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A summary per account that an identity of a requested principal names in a local connection, or an empty list,
    /// without reading accounts, when no principal has such an identity.
    /// </returns>
    private async ValueTask<List<PrincipalSummaryResource>> DescribeByPrincipalIdsAsync(
        IStoreManager storeManager,
        IReadOnlyList<string> principalIds,
        CancellationToken cancellationToken
    )
    {
        var summaries = new List<PrincipalSummaryResource>();

        var identityStore = storeManager.GetStore<IFederatedIdentityStore>();
        var identities = await identityStore.GetNamesByPrincipalsAsync(
            principalIds,
            cancellationToken
        );

        var principalIdsByConnection = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.Ordinal
        );
        foreach (var identity in identities)
        {
            if (!principalIdsByConnection.TryGetValue(identity.ConnectionId, out var bySubject))
            {
                bySubject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                principalIdsByConnection[identity.ConnectionId] = bySubject;
            }

            bySubject[identity.Subject] = identity.PrincipalId;
        }

        var connectionStore = storeManager.GetStore<IConnectionStore>();
        foreach (var (connectionId, principalIdBySubject) in principalIdsByConnection)
        {
            var connection = await connectionStore.GetOrDefaultAsync(
                connectionId,
                cancellationToken
            );
            if (connection is null || !ConnectionStrategyNames.IsLocal(connection.Strategy))
            {
                continue;
            }

            var accountStore = LocalAccountStoreCatalog.GetStore(storeManager, connection);
            var localAccountIds = new List<string>(principalIdBySubject.Keys);
            var accounts = await accountStore.GetNamesByIdsAsync(
                localAccountIds,
                cancellationToken
            );
            foreach (var account in accounts)
            {
                if (
                    string.Equals(
                        account.ConnectionId,
                        connection.ConnectionId,
                        StringComparison.Ordinal
                    )
                    && principalIdBySubject.TryGetValue(account.LocalAccountId, out var principalId)
                )
                {
                    var summary = ToSummary(principalId, account);
                    summaries.Add(summary);
                }
            }
        }

        return summaries;
    }

    /// <summary>
    /// Describes the principals of the accounts of the control-plane tenant's default connection that have the
    /// requested usernames.
    /// </summary>
    /// <param name="storeManager">The store manager that reads the tenant, the connection, the accounts and the
    /// identities.</param>
    /// <param name="userNames">The usernames to describe.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A summary per account whose identity is found, or an empty list, without reading identities, when no account
    /// matches, and without reading accounts when the tenant has no default local connection.
    /// </returns>
    private async ValueTask<List<PrincipalSummaryResource>> DescribeByUserNamesAsync(
        IStoreManager storeManager,
        IReadOnlyList<string> userNames,
        CancellationToken cancellationToken
    )
    {
        var summaries = new List<PrincipalSummaryResource>();

        var connection = await FindDefaultConnectionAsync(storeManager, cancellationToken);
        if (connection is null || !ConnectionStrategyNames.IsLocal(connection.Strategy))
        {
            return summaries;
        }

        var accountStore = LocalAccountStoreCatalog.GetStore(storeManager, connection);
        var accounts = await accountStore.GetNamesByUserNamesAsync(
            connection.ConnectionId,
            userNames,
            cancellationToken
        );
        if (accounts.Count == 0)
        {
            return summaries;
        }

        var localAccountIds = new List<string>(accounts.Count);
        foreach (var account in accounts)
        {
            localAccountIds.Add(account.LocalAccountId);
        }

        var identityStore = storeManager.GetStore<IFederatedIdentityStore>();
        var found = await identityStore.GetByConnectionSubjectsAsync(
            connection.ConnectionId,
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
    /// Finds the default connection of the control-plane tenant from the stored tenant and team documents, as the
    /// platform's local-account routes do.
    /// </summary>
    /// <param name="storeManager">The store manager that reads the tenant and the connection.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The connection that the tenant's <c>default_connection</c> setting names, or <c>null</c> when the
    /// tenant or the connection does not exist.</returns>
    private async ValueTask<PersistedConnection?> FindDefaultConnectionAsync(
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        var tenantId = TenantResolver.GetControlPlaneTenantId();
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
        var tenantSettings = EffectiveSettingsResolver.ResolveStored(
            teamSettings,
            OpenIdSettingScopes.Tenant,
            tenantId,
            tenant.Settings.Value
        );
        var defaultName = tenantSettings.GetValue(OpenIdSettingKeys.DefaultConnection);

        var connectionStore = storeManager.GetStore<IConnectionStore>();
        return await connectionStore.GetByNameOrDefaultAsync(defaultName, cancellationToken);
    }

    /// <summary>
    /// Summarizes the principal of a local account.
    /// </summary>
    /// <param name="principalId">The identifier of the principal that owns the account's identity.</param>
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

        var described = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (query.PrincipalIds.Count > 0)
        {
            var byIds = await DescribeByPrincipalIdsAsync(
                storeManager,
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
                storeManager,
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
