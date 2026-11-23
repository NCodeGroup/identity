// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Buffers;
using NCode.Identity.OpenId.Contexts;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// The <see cref="IInteractiveEmailVerifier"/> of a surface whose backend holds no email address to verify: it reports
/// no address and refuses every send and verification with <see cref="InteractiveEmailCodeErrors.NotSupported"/>. The
/// surface offers no email verification until a backend that holds email addresses replaces it.
/// </summary>
// See ADR-1533.
internal sealed class DefaultInteractiveEmailVerifier : IInteractiveEmailVerifier
{
    /// <inheritdoc />
    public ValueTask<InteractiveEmailStatus?> GetStatusAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult<InteractiveEmailStatus?>(null);

    /// <inheritdoc />
    public ValueTask<InteractiveEmailCodeResult> SendVerificationCodeAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        InteractiveLocale locale,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(InteractiveEmailCodeResult.NotSupported);

    /// <inheritdoc />
    public ValueTask<InteractiveEmailCodeResult> VerifyEmailAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        string flowId,
        Secret<char> code,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(InteractiveEmailCodeResult.NotSupported);
}
