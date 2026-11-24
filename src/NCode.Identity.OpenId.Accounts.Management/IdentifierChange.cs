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
    public static IdentifierChange ForCreate(LocalAccountIdentifierProposal proposal) =>
        new(proposal, null, true, true);

    public static IdentifierChange ForUpdate(
        LocalAccountIdentifierProposal proposal,
        PersistedLocalAccount current,
        bool userNameChanged,
        bool emailChanged
    ) => new(proposal, current, userNameChanged, emailChanged);
}
