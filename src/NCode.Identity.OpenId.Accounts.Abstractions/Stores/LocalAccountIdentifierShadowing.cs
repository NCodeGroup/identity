// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// Describes whether a proposed user name or email address equals the identifier of the other field on another account
/// in the connection, which would make a sign-in identifier name two accounts.
/// </summary>
[PublicAPI]
public sealed class LocalAccountIdentifierShadowing
{
    /// <summary>
    /// Gets a value indicating whether the proposed user name is the email address of another account.
    /// </summary>
    public bool UserNameIsAnEmail { get; init; }

    /// <summary>
    /// Gets a value indicating whether the proposed email address is the user name of another account.
    /// </summary>
    public bool EmailIsAUserName { get; init; }

    /// <summary>
    /// Finds the accounts of a connection that a proposed user name or email address would shadow.
    /// </summary>
    /// <param name="store">The <see cref="ILocalAccountStore"/> of the connection.</param>
    /// <param name="connectionId">The identifier of the connection.</param>
    /// <param name="proposal">The <see cref="LocalAccountIdentifierProposal"/> to check.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="LocalAccountIdentifierShadowing"/> that reports each shadowed field.</returns>
    public static async ValueTask<LocalAccountIdentifierShadowing> FindAsync(
        ILocalAccountStore store,
        string connectionId,
        LocalAccountIdentifierProposal proposal,
        CancellationToken cancellationToken
    )
    {
        var userNameIsAnEmail = false;
        if (!string.IsNullOrWhiteSpace(proposal.UserName))
        {
            var shadowed = await store.GetByEmailOrDefaultAsync(
                connectionId,
                proposal.UserName,
                cancellationToken
            );
            userNameIsAnEmail = IsOtherAccount(shadowed?.LocalAccountId, proposal);
        }

        var emailIsAUserName = false;
        if (!string.IsNullOrWhiteSpace(proposal.Email))
        {
            var shadowed = await store.GetByUserNameOrDefaultAsync(
                connectionId,
                proposal.Email,
                cancellationToken
            );
            emailIsAUserName = IsOtherAccount(shadowed?.LocalAccountId, proposal);
        }

        return new LocalAccountIdentifierShadowing
        {
            UserNameIsAnEmail = userNameIsAnEmail,
            EmailIsAUserName = emailIsAUserName,
        };
    }

    private static bool IsOtherAccount(
        string? foundLocalAccountId,
        LocalAccountIdentifierProposal proposal
    )
    {
        return foundLocalAccountId is not null
            && !string.Equals(
                foundLocalAccountId,
                proposal.LocalAccountId,
                StringComparison.Ordinal
            );
    }
}
