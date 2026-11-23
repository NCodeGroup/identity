// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Buffers;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

/// <summary>
/// Matches a <see cref="Secret{T}"/> argument in a mock setup or verification. A setup expression cannot hold the span
/// that <c>Reveal</c> returns, so the comparison runs in a method.
/// </summary>
internal static class SecretArguments
{
    /// <summary>
    /// Determines whether a secret holds the expected text.
    /// </summary>
    /// <param name="secret">The secret an invocation received.</param>
    /// <param name="expected">The text the secret should hold.</param>
    /// <returns><see langword="true"/> when the secret holds <paramref name="expected"/>.</returns>
    public static bool Reveals(Secret<char> secret, string expected) =>
        secret.FixedTimeEquals(expected);
}
