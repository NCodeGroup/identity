// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Accounts.Authentication;

/// <summary>
/// Provides a default implementation of a handler for the <see cref="CreatePasswordGrantSubjectCommand"/> message that
/// builds the subject from a validated local account: its subject claim, the connection claims plus the account's
/// profile claims. Principal resolution and linking treat the account's connection uniformly with any other. The
/// <c>name</c> claim comes from the account's display name. The
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
        var (openIdContext, connection, account) = command;

        // The ROPC subject is built fresh with no interactive challenge to stash the tenant and authentication time in
        // auth properties, so the factory stamps both as claims for DefaultValidateSubjectAuthenticationHandler.
        return await LocalAccountSubjectFactory.CreateAsync(
            openIdContext,
            connection,
            account,
            OpenIdProtocol.GrantTypes.Password,
            cancellationToken
        );
    }
}
