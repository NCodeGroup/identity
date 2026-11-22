// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using JetBrains.Annotations;
using NCode.Identity.OpenId.Contexts;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// Verifies the email address of the end user who is signed in to the interactive surface. This is the pluggable
/// backend seam of the email page: the surface itself (cookie scheme, session, antiforgery, endpoints) is
/// backend-agnostic and defers the verification to the registered implementation. The reference local-account
/// implementation is provided by the local-account authentication capability; a host whose end users have no address to
/// verify keeps the default, which reports that no verification is supported.
/// </summary>
// See ADR-1533.
[PublicAPI]
public interface IInteractiveEmailVerifier
{
    /// <summary>
    /// Gets the address of the signed-in end user and whether it is verified.
    /// </summary>
    /// <param name="openIdContext">The resolved <see cref="OpenIdContext"/> whose tenant scopes the account
    /// lookup.</param>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> of the signed-in end user, as the cookie session
    /// carries it.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>
    /// The <see cref="InteractiveEmailStatus"/> of the address, or <see langword="null"/> when the end user has none or
    /// the backend has no address to verify.
    /// </returns>
    ValueTask<InteractiveEmailStatus?> GetStatusAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Mails the signed-in end user's address a code that verifies it, and reports the handle of the flow.
    /// </summary>
    /// <param name="openIdContext">The resolved <see cref="OpenIdContext"/> whose tenant scopes the account
    /// lookup.</param>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> of the signed-in end user, as the cookie session
    /// carries it.</param>
    /// <param name="locale">The <see cref="InteractiveLocale"/> whose messages word the mail.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>
    /// The <see cref="InteractiveEmailCodeResult"/> that carries the flow's handle, or the error code that refused the
    /// send, such as <see cref="InteractiveEmailCodeErrors.AlreadyVerified"/>,
    /// <see cref="InteractiveEmailCodeErrors.NoEmail"/> or <see cref="InteractiveEmailCodeErrors.TooManyCodes"/>.
    /// </returns>
    ValueTask<InteractiveEmailCodeResult> SendVerificationCodeAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        InteractiveLocale locale,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Marks the signed-in end user's address verified after it verifies the code.
    /// </summary>
    /// <param name="openIdContext">The resolved <see cref="OpenIdContext"/> whose tenant scopes the account
    /// lookup.</param>
    /// <param name="principal">The <see cref="ClaimsPrincipal"/> of the signed-in end user, as the cookie session
    /// carries it.</param>
    /// <param name="flowId">The handle that <see cref="SendVerificationCodeAsync"/> reported.</param>
    /// <param name="code">The code the end user received by mail.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>
    /// The <see cref="InteractiveEmailCodeResult"/> that reports the verification with
    /// <see cref="InteractiveEmailCodeResult.Verified"/>, or the error code that refused it, such as
    /// <see cref="InteractiveEmailCodeErrors.InvalidCode"/>.
    /// </returns>
    ValueTask<InteractiveEmailCodeResult> VerifyEmailAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        string flowId,
        string code,
        CancellationToken cancellationToken
    );
}
