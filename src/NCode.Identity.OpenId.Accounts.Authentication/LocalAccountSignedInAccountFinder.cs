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
internal sealed class LocalAccountSignedInAccountFinder(
    IStoreManagerFactory storeManagerFactory,
    IConnectionResolver connectionResolver,
    LocalAccountStoreCatalog catalog
)
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IConnectionResolver ConnectionResolver { get; } = connectionResolver;
    private LocalAccountStoreCatalog Catalog { get; } = catalog;

    /// <summary>
    /// Finds the local account behind the signed-in end user's principal in a unit of work of its own, which ends
    /// before this method returns, so the caller holds no store while it mails a code.
    /// </summary>
    /// <param name="openIdContext">The resolved <see cref="OpenIdContext"/> that reads the principal's
    /// identifier.</param>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> of the signed-in end user.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>
    /// The <see cref="PersistedLocalAccount"/> with its <see cref="ResolvedConnection"/>, or <see langword="null"/>
    /// when the principal has no identifier, names a connection that is not an enabled local one it has an identity in,
    /// names none and links no or several enabled local-connection identities, or has no account. The caller checks
    /// whether the account is enabled.
    /// </returns>
    public async ValueTask<(
        PersistedLocalAccount Account,
        ResolvedConnection Connection
    )?> FindAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken
    )
    {
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        return await FindAsync(storeManager, openIdContext, principal, cancellationToken);
    }

    /// <summary>
    /// Finds the local account behind the signed-in end user's identity in an enabled local connection: the one in the
    /// connection the principal's connection claim names, or, when it carries no connection claim, the single such
    /// identity.
    /// </summary>
    /// <param name="storeManager">The <see cref="IStoreManager"/> whose stores are read.</param>
    /// <param name="openIdContext">The resolved <see cref="OpenIdContext"/> that reads the principal's
    /// identifier.</param>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> of the signed-in end user.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>
    /// The <see cref="PersistedLocalAccount"/> with its <see cref="ResolvedConnection"/>, or <see langword="null"/>
    /// when the principal has no identifier, names a connection that is not an enabled local one it has an identity in,
    /// names none and links no or several enabled local-connection identities, or has no account. The caller checks
    /// whether the account is enabled.
    /// </returns>
    public async ValueTask<(
        PersistedLocalAccount Account,
        ResolvedConnection Connection
    )?> FindAsync(
        IStoreManager storeManager,
        OpenIdContext openIdContext,
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

        // A principal that names the connection it signed in through acts on that connection's account alone; one that
        // names none and links several local accounts has no single account to act on.
        var signedInConnectionId = principal.FindFirstValue(ConnectionClaimTypes.Connection);
        var localIdentities =
            new List<(PersistedFederatedIdentity Identity, ResolvedConnection Connection)>();
        foreach (var identity in identities)
        {
            var connection = await ConnectionResolver.GetByIdOrDefaultAsync(
                openIdContext,
                identity.ConnectionId,
                cancellationToken
            );
            if (
                connection is null
                || !connection.Connection.IsEnabled
                || !ConnectionStrategyNames.IsLocal(connection.Connection.Strategy)
            )
            {
                continue;
            }

            localIdentities.Add((identity, connection));
        }

        int selectedIndex;
        if (string.IsNullOrEmpty(signedInConnectionId))
        {
            if (localIdentities.Count != 1)
            {
                return null;
            }

            selectedIndex = 0;
        }
        else
        {
            selectedIndex = localIdentities.FindIndex(x =>
                string.Equals(
                    x.Identity.ConnectionId,
                    signedInConnectionId,
                    StringComparison.Ordinal
                )
            );
            if (selectedIndex < 0)
            {
                return null;
            }
        }

        var (localIdentity, localConnection) = localIdentities[selectedIndex];
        var accountStore = Catalog.GetStore(storeManager, localConnection.Connection);
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
}
