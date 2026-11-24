// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Buffers;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// Resets the password of an end user who forgot it and is not signed in to the interactive surface. This is the
/// pluggable backend seam of the forgot-password and reset-password pages: the surface itself (endpoints, antiforgery,
/// locale) is backend-agnostic and defers the reset to the registered implementation. The reference local-account
/// implementation is provided by the local-account authentication capability; a host whose end users have no password
/// to reset, such as one fronting an external identity provider, keeps the default, which reports that no reset is
/// supported.
/// </summary>
// See ADR-1533.
[PublicAPI]
public interface IInteractivePasswordResetter
{
    /// <summary>
    /// Starts a password reset for the account the identifier names, mailing its verified address a code when it has
    /// one, and returns the flow's handle whether or not it sent anything, so the answer never tells whether an account
    /// exists.
    /// </summary>
    /// <param name="openIdContext">The resolved <see cref="OpenIdContext"/> whose tenant scopes the account
    /// lookup.</param>
    /// <param name="connection">The <see cref="ResolvedConnection"/> whose account the reset is for.</param>
    /// <param name="identifier">The identifier the end user entered.</param>
    /// <param name="locale">The <see cref="InteractiveLocale"/> whose messages word the mail.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>The handle of the reset flow, which the reset page presents with the code.</returns>
    ValueTask<string> SendResetCodeAsync(
        OpenIdContext openIdContext,
        ResolvedConnection connection,
        string identifier,
        InteractiveLocale locale,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Resets the password of the account the flow belongs to after it verifies the code. A backend that sets passwords
    /// judges the new one with the server's password policy before it spends the code, and reports a refusal with
    /// <see cref="InteractivePasswordChangeResult.Refused"/> and the policy's error code; a code that is wrong, expired
    /// or spent is refused with <see cref="InteractiveEmailCodeErrors.InvalidCode"/>; a backend without a password to
    /// reset reports <see cref="InteractivePasswordChangeResult.NotSupported"/>.
    /// </summary>
    /// <param name="openIdContext">The resolved <see cref="OpenIdContext"/> whose tenant scopes the account
    /// lookup.</param>
    /// <param name="flowId">The handle that <see cref="SendResetCodeAsync"/> returned.</param>
    /// <param name="code">The code the end user received by mail. The caller owns and disposes it.</param>
    /// <param name="newPassword">The password the end user chose. The caller owns and disposes it.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous
    /// operation.</param>
    /// <returns>
    /// The <see cref="InteractivePasswordChangeResult"/> that reports the reset, or the error code that refused it.
    /// </returns>
    ValueTask<InteractivePasswordChangeResult> ResetPasswordAsync(
        OpenIdContext openIdContext,
        string flowId,
        Secret<char> code,
        Secret<char> newPassword,
        CancellationToken cancellationToken
    );
}
