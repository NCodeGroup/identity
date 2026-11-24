// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// Builds the keys under which failed password attempts are counted. An account the store found and an identifier an
/// end user submitted have separate key spaces, so a submitted identifier never shares the count of an account whose
/// identifier it equals.
/// </summary>
[PublicAPI]
public static class LocalAccountAttemptKeys
{
    private const string AccountPrefix = "account:";
    private const string IdentifierPrefix = "identifier:";

    /// <summary>
    /// Builds the key of an account the store found.
    /// </summary>
    /// <param name="localAccountId">The identifier of the account.</param>
    /// <returns>The key to count the account's failed attempts under.</returns>
    public static string ForAccount(string localAccountId) => AccountPrefix + localAccountId;

    /// <summary>
    /// Builds the key of an identifier an end user submitted when no account matched it.
    /// </summary>
    /// <param name="submitted">The identifier the end user submitted.</param>
    /// <returns>The key to count the failed attempts under.</returns>
    public static string ForIdentifier(string submitted) => IdentifierPrefix + submitted;
}
