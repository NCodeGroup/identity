// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Security.Cryptography;
using NCode.Identity.OpenId.Contexts;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// The <see cref="IInteractivePasswordResetter"/> of a surface whose backend holds no password to reset: it sends no
/// code, answers the forgot-password page with an unguessable handle as any backend does, and reports
/// <see cref="InteractivePasswordChangeResult.NotSupported"/> for every reset. The surface offers no password reset
/// until a backend that holds passwords and email addresses replaces it.
/// </summary>
// See ADR-1533.
internal sealed class DefaultInteractivePasswordResetter : IInteractivePasswordResetter
{
    private const int HandleByteCount = 32;

    /// <inheritdoc />
    public ValueTask<string> SendResetCodeAsync(
        OpenIdContext openIdContext,
        string userName,
        InteractiveLocale locale,
        CancellationToken cancellationToken
    )
    {
        Span<byte> bytes = stackalloc byte[HandleByteCount];
        RandomNumberGenerator.Fill(bytes);
        var handle = Base64Url.EncodeToString(bytes);
        return ValueTask.FromResult(handle);
    }

    /// <inheritdoc />
    public ValueTask<InteractivePasswordChangeResult> ResetPasswordAsync(
        OpenIdContext openIdContext,
        string flowId,
        string code,
        string newPassword,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(InteractivePasswordChangeResult.NotSupported);
}
