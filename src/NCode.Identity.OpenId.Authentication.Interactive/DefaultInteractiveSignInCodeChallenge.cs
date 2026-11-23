// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using NCode.Buffers;
using NCode.Identity.OpenId.Contexts;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// The <see cref="IInteractiveSignInCodeChallenge"/> of a surface whose backend holds no email address to mail: it
/// refuses every send and verification with <see cref="InteractiveEmailCodeErrors.NotSupported"/>. The surface offers
/// no email second factor until a backend that holds email addresses replaces it.
/// </summary>
// See ADR-1533.
internal sealed class DefaultInteractiveSignInCodeChallenge : IInteractiveSignInCodeChallenge
{
    private static InteractiveEmailCodeResult NotSupported { get; } =
        InteractiveEmailCodeResult.Refused(InteractiveEmailCodeErrors.NotSupported);

    /// <inheritdoc />
    public ValueTask<InteractiveEmailCodeResult> SendSignInCodeAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        InteractiveLocale locale,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(NotSupported);

    /// <inheritdoc />
    public ValueTask<InteractiveEmailCodeResult> VerifySignInCodeAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        string flowId,
        Secret<char> code,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(NotSupported);
}
