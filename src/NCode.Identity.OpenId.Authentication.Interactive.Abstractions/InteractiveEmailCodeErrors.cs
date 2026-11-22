// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// Contains the known error codes the email-code pages receive in their <c>error</c> query parameter. The vocabulary is
/// open: a backend (<see cref="IInteractiveEmailVerifier"/>, <see cref="IInteractiveSignInCodeChallenge"/>) may report
/// a code of its own, and the page shows a general message for a code it does not know.
/// </summary>
// See ADR-1533.
[PublicAPI]
public static class InteractiveEmailCodeErrors
{
    /// <summary>
    /// Contains the code for an address that is already verified, so no code is sent.
    /// </summary>
    public const string AlreadyVerified = "already_verified";

    /// <summary>
    /// Contains the code for a code that is wrong, expired or already used.
    /// </summary>
    public const string InvalidCode = "invalid_code";

    /// <summary>
    /// Contains the code for an account that has no address to mail.
    /// </summary>
    public const string NoEmail = "no_email";

    /// <summary>
    /// Contains the code for a backend that has no email code to send or verify for the end user.
    /// </summary>
    public const string NotSupported = "not_supported";

    /// <summary>
    /// Contains the code for an account that has already been sent the most codes the tenant allows in an hour.
    /// </summary>
    public const string TooManyCodes = "too_many_codes";
}
