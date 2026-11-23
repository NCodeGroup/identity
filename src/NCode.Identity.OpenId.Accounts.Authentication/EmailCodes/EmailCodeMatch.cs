// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Accounts.DataContracts;

namespace NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;

/// <summary>
/// Represents the flow that a right code matched: the account the code was sent for and the address it was mailed to.
/// </summary>
/// <param name="LocalAccountId">The identifier of the local account the code was sent for.</param>
/// <param name="Email">The email address the code was mailed to.</param>
// See ADR-1533.
internal sealed record EmailCodeMatch(string LocalAccountId, string Email)
{
    /// <summary>
    /// Determines whether the code was sent for the specified account: the flow's account identifier equals the
    /// account's, ordinally, and the address the code was mailed to equals the account's current address, ignoring
    /// case. A code authorizes nothing once the account's address differs from the one it was mailed to.
    /// </summary>
    /// <param name="account">The local account to compare with.</param>
    /// <returns><see langword="true"/> when the code is bound to the account; otherwise,
    /// <see langword="false"/>.</returns>
    // See ADR-1533.
    internal bool IsFor(PersistedLocalAccount account) =>
        string.Equals(LocalAccountId, account.LocalAccountId, StringComparison.Ordinal)
        && string.Equals(Email, account.Email, StringComparison.OrdinalIgnoreCase);
}
