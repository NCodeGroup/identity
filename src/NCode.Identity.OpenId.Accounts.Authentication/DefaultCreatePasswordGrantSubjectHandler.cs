// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Accounts.Authentication;

/// <summary>
/// Provides a default implementation of a handler for the <see cref="CreatePasswordGrantSubjectCommand"/> message that
/// builds the subject from a validated local account: the self-issued connection's subject and issuer claims plus the
/// account's profile claims. The local account is a self-issued connection, so principal resolution and linking treat
/// it uniformly with any federated identity. The <c>name</c> claim comes from the account's display name. The
/// principal's metadata bags are not carried here; they are a principal-level concept resolved fresh at issuance by the
/// subject-claims projection handlers.
/// </summary>
// See ADR-1035, ADR-1051, ADR-1055.
internal class DefaultCreatePasswordGrantSubjectHandler(
    LocalAccountSubjectFactory localAccountSubjectFactory
) : ICommandResponseHandler<CreatePasswordGrantSubjectCommand, ClaimsPrincipal>
{
    private LocalAccountSubjectFactory LocalAccountSubjectFactory { get; } =
        localAccountSubjectFactory;

    /// <inheritdoc />
    public async ValueTask<ClaimsPrincipal> HandleAsync(
        CreatePasswordGrantSubjectCommand command,
        CancellationToken cancellationToken
    )
    {
        var (openIdContext, account) = command;

        // The ROPC subject is built fresh with no interactive challenge to stash the tenant and authentication time in
        // auth properties, so the factory stamps both as claims for DefaultValidateSubjectAuthenticationHandler.
        return await LocalAccountSubjectFactory.CreateAsync(
            openIdContext,
            account,
            OpenIdProtocol.GrantTypes.Password,
            cancellationToken
        );
    }
}
