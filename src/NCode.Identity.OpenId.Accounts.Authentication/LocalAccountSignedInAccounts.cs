// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Contexts;
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
    /// Finds the local account behind the single self-issued identity of the signed-in end user's principal.
    /// </summary>
    /// <param name="storeManager">The <see cref="IStoreManager"/> whose stores are read.</param>
    /// <param name="openIdContext">The resolved <see cref="OpenIdContext"/> that reads the principal's
    /// identifier.</param>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> of the signed-in end user.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>
    /// The <see cref="PersistedLocalAccount"/>, or <see langword="null"/> when the principal has no identifier, links
    /// no or several self-issued identities, or has no account. The caller checks whether the account is enabled.
    /// </returns>
    public static async ValueTask<PersistedLocalAccount?> FindAsync(
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

        // A principal that links several local accounts has no single account to act on.
        var selfIssuedIdentities = identities
            .Where(identity =>
                string.Equals(
                    identity.Issuer,
                    AccountConstants.SelfIssuer,
                    StringComparison.Ordinal
                )
            )
            .ToList();
        if (selfIssuedIdentities.Count != 1)
        {
            return null;
        }

        var accountStore = storeManager.GetStore<ILocalAccountStore>();
        return await accountStore.GetByIdOrDefaultAsync(
            selfIssuedIdentities[0].Subject,
            cancellationToken
        );
    }
}
