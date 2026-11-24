// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Accounts.Stores;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// The identifiers a request proposes, the account they apply to and which of them the request changes.
/// </summary>
/// <param name="Proposal">The proposed identifiers.</param>
/// <param name="Current">The account being updated, or <c>null</c> when an account is created.</param>
/// <param name="UserNameChanged">Whether the request changes the username.</param>
/// <param name="EmailChanged">Whether the request changes the email address.</param>
internal readonly record struct IdentifierChange(
    LocalAccountIdentifierProposal Proposal,
    PersistedLocalAccount? Current,
    bool UserNameChanged,
    bool EmailChanged
)
{
    /// <summary>
    /// Describes the creation of an account, which sets both identifiers.
    /// </summary>
    /// <param name="proposal">The proposed identifiers.</param>
    /// <returns>An <see cref="IdentifierChange"/> with no current account that changes both identifiers.</returns>
    public static IdentifierChange ForCreate(LocalAccountIdentifierProposal proposal) =>
        new(proposal, null, true, true);

    /// <summary>
    /// Describes the update of an account, comparing each proposed identifier with the stored one by ordinal equality.
    /// </summary>
    /// <param name="proposal">The proposed identifiers.</param>
    /// <param name="current">The account being updated.</param>
    /// <returns>
    /// An <see cref="IdentifierChange"/> whose <see cref="UserNameChanged"/> and <see cref="EmailChanged"/> are
    /// <c>true</c> for each identifier that differs from the one <paramref name="current"/> stores.
    /// </returns>
    public static IdentifierChange ForUpdate(
        LocalAccountIdentifierProposal proposal,
        PersistedLocalAccount current
    )
    {
        var userNameChanged = !string.Equals(
            proposal.UserName,
            current.UserName,
            StringComparison.Ordinal
        );
        var emailChanged = !string.Equals(proposal.Email, current.Email, StringComparison.Ordinal);
        return new IdentifierChange(proposal, current, userNameChanged, emailChanged);
    }
}
