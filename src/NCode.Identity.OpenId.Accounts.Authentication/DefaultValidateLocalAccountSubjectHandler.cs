// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Identity.OpenId.Accounts.Stores;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Errors;
using NCode.Identity.OpenId.Messages;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration.AspNetCore;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Accounts.Authentication;

/// <summary>
/// Provides a <see cref="ValidateSubjectStandingCommand"/> handler that refuses an end user whose local account is
/// disabled or no longer exists, so no session, authorization code or refresh token of such an account issues a token.
/// A disabled local account refuses its principal whatever connection it signed in through. A subject the local
/// accounts cannot judge, because it has no local account and was not authenticated by one, is left to the other
/// handlers.
/// </summary>
// See ADR-1229.
internal class DefaultValidateLocalAccountSubjectHandler(
    IStoreManagerFactory storeManagerFactory,
    IConnectionResolver connectionResolver,
    LocalAccountStoreCatalog catalog
) : ICommandHandler<ValidateSubjectStandingCommand>
{
    /// <summary>
    /// Refuses the subject with an access-denied error unless another handler already set one.
    /// </summary>
    /// <param name="openIdContext">The resolved <see cref="OpenIdContext"/> whose error factory builds the
    /// error.</param>
    /// <param name="operationDisposition">The disposition that receives the error.</param>
    /// <param name="message">The error description.</param>
    private static void Refuse(
        OpenIdContext openIdContext,
        OperationDisposition<IOpenIdError> operationDisposition,
        string message
    )
    {
        operationDisposition.Error ??= openIdContext.ErrorFactory.AccessDenied(message);
    }

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IConnectionResolver ConnectionResolver { get; } = connectionResolver;
    private LocalAccountStoreCatalog Catalog { get; } = catalog;

    /// <inheritdoc />
    public async ValueTask HandleAsync(
        ValidateSubjectStandingCommand command,
        CancellationToken cancellationToken
    )
    {
        var (openIdContext, subjectAuthentication, operationDisposition) = command;

        // short-circuit if we already have an error
        if (operationDisposition.HasError)
        {
            return;
        }

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var identities = await storeManager
            .GetStore<IFederatedIdentityStore>()
            .GetByPrincipalAsync(subjectAuthentication.PrincipalId, cancellationToken);

        if (identities.Count == 0)
        {
            var authenticatedLocally = await AuthenticatedLocallyAsync(
                openIdContext,
                subjectAuthentication,
                cancellationToken
            );
            if (authenticatedLocally)
            {
                Refuse(
                    openIdContext,
                    operationDisposition,
                    "The end-user's account no longer exists."
                );
            }

            return;
        }

        foreach (var identity in identities)
        {
            var connection = await ConnectionResolver.GetByIdOrDefaultAsync(
                openIdContext,
                identity.ConnectionId,
                cancellationToken
            );
            if (
                connection is null
                || !ConnectionStrategyNames.IsLocal(connection.Connection.Strategy)
            )
            {
                continue;
            }

            var accountStore = Catalog.GetStore(storeManager, connection.Connection);
            var account = await accountStore.GetByIdOrDefaultAsync(
                identity.Subject,
                cancellationToken
            );
            if (account is not { IsEnabled: true })
            {
                Refuse(
                    openIdContext,
                    operationDisposition,
                    "The end-user's account is disabled or no longer exists."
                );
                return;
            }
        }
    }

    /// <summary>
    /// Determines whether a local connection authenticated the subject, which a principal without an identity is then a
    /// deleted local account's.
    /// </summary>
    /// <param name="openIdContext">The resolved <see cref="OpenIdContext"/> that resolves the connection.</param>
    /// <param name="subjectAuthentication">The <see cref="SubjectAuthentication"/> whose connection claim names the
    /// connection that signed the subject in.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns><see langword="true"/> when the connection claim names a local connection.</returns>
    private async ValueTask<bool> AuthenticatedLocallyAsync(
        OpenIdContext openIdContext,
        SubjectAuthentication subjectAuthentication,
        CancellationToken cancellationToken
    )
    {
        var connectionId = subjectAuthentication.Subject.FindFirstValue(
            ConnectionClaimTypes.Connection
        );
        if (string.IsNullOrEmpty(connectionId))
        {
            return false;
        }

        var connection = await ConnectionResolver.GetByIdOrDefaultAsync(
            openIdContext,
            connectionId,
            cancellationToken
        );
        return connection is not null
            && ConnectionStrategyNames.IsLocal(connection.Connection.Strategy);
    }
}
