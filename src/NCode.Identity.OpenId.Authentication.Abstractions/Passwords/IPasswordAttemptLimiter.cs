// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;

namespace NCode.Identity.OpenId.Authentication.Passwords;

/// <summary>
/// Limits the failed password attempts of an account of a connection, so a password can be guessed only a limited number of times.
/// Every path that verifies a password passes <see cref="IsLimitReachedAsync"/> to the store's verification, which
/// refuses a password of an account at its limit by verifying it against the dummy hash, and answers it as it answers
/// a wrong password.
/// </summary>
// See ADR-1237.
[PublicAPI]
public interface IPasswordAttemptLimiter
{
    /// <summary>
    /// Determines whether the account that the account key names has reached the connection's limit of failed
    /// attempts, so a password presented for it is refused and verified against the store's dummy hash instead of the
    /// account's hash.
    /// </summary>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> instance associated with the current request.</param>
    /// <param name="connection">The connection that the password is presented to; its settings set the limit.</param>
    /// <param name="accountKey">
    /// The account key: the account's identifier, or the submitted identifier when no account matches.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe.</param>
    /// <returns>
    /// The <see cref="ValueTask{TResult}"/> that represents the asynchronous operation, containing <c>true</c> when the
    /// account has reached the limit; otherwise, <c>false</c>.
    /// </returns>
    ValueTask<bool> IsLimitReachedAsync(
        OpenIdContext openIdContext,
        ResolvedConnection connection,
        string accountKey,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Records one failed password attempt, a refusal at the limit included, for the account that the account key
    /// names.
    /// </summary>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> instance associated with the current request.</param>
    /// <param name="connection">The connection that the password is presented to; its settings set the window.</param>
    /// <param name="accountKey">
    /// The account key: the account's identifier, or the submitted identifier when no account matches.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask RecordFailureAsync(
        OpenIdContext openIdContext,
        ResolvedConnection connection,
        string accountKey,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Clears the failed attempts of the account that the account key names, after a verified password or after a
    /// password that an administrator sets.
    /// </summary>
    /// <param name="tenantId">The identifier of the tenant that the account belongs to.</param>
    /// <param name="connectionId">The identifier of the connection that the account belongs to.</param>
    /// <param name="accountKey">
    /// The account key: the account's identifier, or the submitted identifier when no account matches.
    /// </param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe.</param>
    /// <returns>The <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask ResetAsync(
        string tenantId,
        string connectionId,
        string accountKey,
        CancellationToken cancellationToken
    );
}
