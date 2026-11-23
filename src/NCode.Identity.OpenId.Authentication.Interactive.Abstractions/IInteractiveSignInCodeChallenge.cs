// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using JetBrains.Annotations;
using NCode.Buffers;
using NCode.Identity.OpenId.Contexts;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// Challenges an end user, whose password is verified, with a code mailed to the account's address as the second factor
/// of the sign-in. This is the pluggable backend seam of the sign-in code page: the surface itself (the pending sign-in
/// cookie, antiforgery, endpoints) is backend-agnostic and defers the challenge to the registered implementation. The
/// reference local-account implementation is provided by the local-account authentication capability; a host whose end
/// users have no address to mail keeps the default, which reports that no challenge is supported.
/// </summary>
// See ADR-1533.
[PublicAPI]
public interface IInteractiveSignInCodeChallenge
{
    /// <summary>
    /// Mails the address of the account the principal belongs to a code that completes the sign-in, and reports the
    /// handle of the flow.
    /// </summary>
    /// <param name="openIdContext">The resolved <see cref="OpenIdContext"/> whose tenant scopes the account
    /// lookup.</param>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> of the end user whose password is verified, as the
    /// pending sign-in cookie carries it.</param>
    /// <param name="locale">The <see cref="InteractiveLocale"/> whose messages word the mail.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>
    /// The <see cref="InteractiveEmailCodeResult"/> that carries the flow's handle, or the error code that refused the
    /// send, such as <see cref="InteractiveEmailCodeErrors.NoEmail"/> or
    /// <see cref="InteractiveEmailCodeErrors.TooManyCodes"/>.
    /// </returns>
    ValueTask<InteractiveEmailCodeResult> SendSignInCodeAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        InteractiveLocale locale,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Completes the second factor of the sign-in after it verifies the code.
    /// </summary>
    /// <param name="openIdContext">The resolved <see cref="OpenIdContext"/> whose tenant scopes the account
    /// lookup.</param>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> of the end user whose password is verified, as the
    /// pending sign-in cookie carries it.</param>
    /// <param name="flowId">The handle that <see cref="SendSignInCodeAsync"/> reported.</param>
    /// <param name="code">The code the end user received by mail. The caller owns and disposes it.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>
    /// The <see cref="InteractiveEmailCodeResult"/> that reports the second factor with
    /// <see cref="InteractiveEmailCodeResult.Verified"/>, or the error code that refused it, such as
    /// <see cref="InteractiveEmailCodeErrors.InvalidCode"/>.
    /// </returns>
    ValueTask<InteractiveEmailCodeResult> VerifySignInCodeAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        string flowId,
        Secret<char> code,
        CancellationToken cancellationToken
    );
}
