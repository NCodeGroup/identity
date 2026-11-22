// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Accounts.DataContracts;
using NCode.Identity.OpenId.Authentication.Interactive;
using NCode.Identity.OpenId.Contexts;

namespace NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;

/// <summary>
/// Sends one-time codes to a local account's email address and checks the codes that come back, for every purpose in
/// <see cref="EmailCodePurposes"/>. A flow is named by a handle that is unguessable and bound to one account; the code
/// is kept only as a hash, and a flow ends after too many wrong codes or when it is spent, so a code is used once.
/// </summary>
// See ADR-1533.
internal interface ILocalAccountEmailCodes
{
    /// <summary>
    /// Starts a flow for <paramref name="account"/> and mails it a code.
    /// </summary>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> of the tenant the account belongs to.</param>
    /// <param name="purpose">The purpose of the code, one of the <see cref="EmailCodePurposes"/>.</param>
    /// <param name="account">The account to mail, which has an email address.</param>
    /// <param name="locale">The <see cref="InteractiveLocale"/> whose messages word the mail.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The handle of the flow; or <see langword="null"/>, sending nothing, when the account has had as many codes of
    /// the purpose in the past hour as the tenant allows.
    /// </returns>
    ValueTask<string?> SendAsync(
        OpenIdContext openIdContext,
        string purpose,
        PersistedLocalAccount account,
        InteractiveLocale locale,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Starts a decoy flow that does the remote work of <see cref="SendAsync"/> except composing and queuing mail, so a
    /// request that mails nothing costs the shared stores what a request that mails costs. It increments a sends
    /// counter keyed by the purpose and a hash of <paramref name="identifier"/>, never an account's counter, and it
    /// stores a flow whose code hash is that of random bytes, so no code verifies.
    /// </summary>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> of the tenant the request belongs to.</param>
    /// <param name="purpose">The purpose of the code, one of the <see cref="EmailCodePurposes"/>.</param>
    /// <param name="identifier">The value the request named, such as a username, which is hashed into the counter's
    /// key.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>The handle of the decoy flow, which looks like the handle of a real flow.</returns>
    ValueTask<string> SendDecoyAsync(
        OpenIdContext openIdContext,
        string purpose,
        string identifier,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Checks a code against a flow, counting a wrong code against the flow and ending the flow at the fifth. A right
    /// code keeps the flow, so the caller spends it with <see cref="ConsumeAsync"/> after its other checks pass.
    /// </summary>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> of the tenant the flow belongs to.</param>
    /// <param name="purpose">The purpose of the code, one of the <see cref="EmailCodePurposes"/>.</param>
    /// <param name="flowId">The handle that <see cref="SendAsync"/> returned.</param>
    /// <param name="code">The code the end user presented.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// The account the flow was started for and the address the code was mailed to; or <see langword="null"/> when the
    /// flow is unknown, expired or ended, or the code is wrong.
    /// </returns>
    ValueTask<EmailCodeMatch?> VerifyAsync(
        OpenIdContext openIdContext,
        string purpose,
        string flowId,
        string code,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Spends a flow with one atomic take, so its code is used once. The caller takes the flow after every check has
    /// passed and before the work the code authorizes.
    /// </summary>
    /// <param name="openIdContext">The <see cref="OpenIdContext"/> of the tenant the flow belongs to.</param>
    /// <param name="purpose">The purpose of the code, one of the <see cref="EmailCodePurposes"/>.</param>
    /// <param name="flowId">The handle that <see cref="SendAsync"/> returned.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// <see langword="true"/> when this call took the flow; or <see langword="false"/> when the flow was gone because
    /// another request took it or it expired.
    /// </returns>
    ValueTask<bool> ConsumeAsync(
        OpenIdContext openIdContext,
        string purpose,
        string flowId,
        CancellationToken cancellationToken
    );
}
