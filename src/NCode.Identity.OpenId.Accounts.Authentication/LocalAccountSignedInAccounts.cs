// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Accounts.Authentication;

/// <summary>
/// Finds the local account of the end user who is signed in to the interactive surface.
/// </summary>
// See ADR-1230.
internal static class LocalAccountSignedInAccounts
{
    /// <summary>
    /// Finds the local account behind the signed-in end user's principal in a unit of work of its own, which ends
    /// before this method returns, so the caller holds no store while it mails a code.
    /// </summary>
    /// <param name="storeManagerFactory">The <see cref="IStoreManagerFactory"/> that creates the unit of work.</param>
    /// <param name="openIdContext">The resolved <see cref="OpenIdContext"/> that reads the principal's
    /// identifier.</param>
    /// <param name="connectionResolver">The <see cref="IConnectionResolver"/> that resolves each identity's
    /// connection.</param>
    /// <param name="catalog">The <see cref="LocalAccountStoreCatalog"/> that names a connection's store.</param>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> of the signed-in end user.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>
    /// The <see cref="PersistedLocalAccount"/> with its <see cref="ResolvedConnection"/>, or <see langword="null"/>
    /// when the principal has no identifier, links no or several local-connection identities, or has no account. The
    /// caller checks whether the account is enabled.
    /// </returns>
    public static async ValueTask<(
        PersistedLocalAccount Account,
        ResolvedConnection Connection
    )?> FindAsync(
        IStoreManagerFactory storeManagerFactory,
        OpenIdContext openIdContext,
        IConnectionResolver connectionResolver,
        LocalAccountStoreCatalog catalog,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await storeManagerFactory.CreateAsync(cancellationToken);

        return await FindAsync(
            storeManager,
            openIdContext,
            connectionResolver,
            catalog,
            principal,
            cancellationToken
        );
    }

    /// <summary>
    /// Finds the local account behind the single local-connection identity of the signed-in end user's principal.
    /// </summary>
    /// <param name="storeManager">The <see cref="IStoreManager"/> whose stores are read.</param>
    /// <param name="openIdContext">The resolved <see cref="OpenIdContext"/> that reads the principal's
    /// identifier.</param>
    /// <param name="connectionResolver">The <see cref="IConnectionResolver"/> that resolves each identity's
    /// connection.</param>
    /// <param name="catalog">The <see cref="LocalAccountStoreCatalog"/> that names a connection's store.</param>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> of the signed-in end user.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>
    /// The <see cref="PersistedLocalAccount"/> with its <see cref="ResolvedConnection"/>, or <see langword="null"/>
    /// when the principal has no identifier, links no or several local-connection identities, or has no account. The
    /// caller checks whether the account is enabled.
    /// </returns>
    public static async ValueTask<(
        PersistedLocalAccount Account,
        ResolvedConnection Connection
    )?> FindAsync(
        IStoreManager storeManager,
        OpenIdContext openIdContext,
        IConnectionResolver connectionResolver,
        LocalAccountStoreCatalog catalog,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken
    )
    {
        var principalId = openIdContext.GetSubjectId(principal);
        if (string.IsNullOrEmpty(principalId))
        {
            return null;
        }

        var identityStore = storeManager.GetStore<IFederatedIdentityStore>();

        var identities = await identityStore.GetByPrincipalAsync(principalId, cancellationToken);

        // A principal that links several local accounts has no single account to act on.
        var localIdentities =
            new List<(PersistedFederatedIdentity Identity, ResolvedConnection Connection)>();
        foreach (var identity in identities)
        {
            var connection = await connectionResolver.GetByIdOrDefaultAsync(
                openIdContext,
                identity.ConnectionId,
                cancellationToken
            );
            if (connection is null || !IsLocal(connection))
            {
                continue;
            }

            localIdentities.Add((identity, connection));
        }

        if (localIdentities.Count != 1)
        {
            return null;
        }

        var (localIdentity, localConnection) = localIdentities[0];
        var accountStore = catalog.GetStore(storeManager, localConnection.Connection);
        var account = await accountStore.GetByIdOrDefaultAsync(
            localIdentity.Subject,
            cancellationToken
        );
        if (account is null)
        {
            return null;
        }

        return (account, localConnection);
    }

    /// <summary>
    /// Determines whether the connection's strategy is the <c>local</c> strategy.
    /// </summary>
    /// <param name="connection">The <see cref="ResolvedConnection"/> to test.</param>
    /// <returns><see langword="true"/> when the connection holds local accounts.</returns>
    public static bool IsLocal(ResolvedConnection connection)
    {
        return string.Equals(
            connection.Connection.Strategy,
            ConnectionStrategyNames.Local,
            StringComparison.Ordinal
        );
    }
}
