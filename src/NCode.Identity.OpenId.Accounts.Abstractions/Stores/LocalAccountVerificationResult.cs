// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using NCode.Identity.OpenId.Accounts.DataContracts;

namespace NCode.Identity.OpenId.Accounts.Stores;

/// <summary>
/// Contains the outcome of one verification of a local account's credential through
/// <see cref="ILocalAccountStore"/>.
/// </summary>
[PublicAPI]
public sealed class LocalAccountVerificationResult
{
    /// <summary>
    /// Gets the key that the limit on failed password attempts counts under:
    /// <see cref="LocalAccountAttemptKeys.ForAccount"/> of the found account's identifier, or
    /// <see cref="LocalAccountAttemptKeys.ForIdentifier"/> of the submitted identifier when no account matches. A store
    /// sets it through those two methods, so a key carries its prefix.
    /// </summary>
    public required string AccountKey { get; init; }

    /// <summary>
    /// Gets the account whose password verified, whether or not it is enabled, or <c>null</c>.
    /// </summary>
    public PersistedLocalAccount? Account { get; init; }

    /// <summary>
    /// Gets a value indicating whether the store's caller refused the password, so the store verified it against its
    /// dummy hash and returned no account.
    /// </summary>
    public bool IsRefused { get; init; }

    /// <summary>
    /// Gets a value indicating whether the password verified and <see cref="Account"/> is not <c>null</c>.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Account))]
    public bool IsVerified => Account is not null;
}
