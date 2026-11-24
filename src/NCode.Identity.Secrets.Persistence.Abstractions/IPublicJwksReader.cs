// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;
using NCode.Identity.Secrets.Keys;

namespace NCode.Identity.Secrets.Persistence;

/// <summary>
/// Provides the ability to read a public JSON Web Key Set into verification-only <see cref="SecretKey"/> instances. A
/// set that carries private or symmetric members, a key without a <c>kid</c>, or two keys with one <c>kid</c> is
/// refused whole, so a caller never trusts a partial set.
/// </summary>
[PublicAPI]
public interface IPublicJwksReader
{
    /// <summary>
    /// Reads the public keys of the JSON Web Key Set <paramref name="jwks"/>.
    /// </summary>
    /// <param name="jwks">The JSON Web Key Set: an object with a non-empty <c>keys</c> array.</param>
    /// <param name="keys">When this method returns <see langword="true"/>, the keys in the order of the set.</param>
    /// <param name="error">When this method returns <see langword="false"/>, the reason the set is refused.</param>
    /// <returns><see langword="true"/> when every key of the set is a valid public key; otherwise,
    /// <see langword="false"/>.</returns>
    bool TryRead(
        JsonElement jwks,
        [NotNullWhen(true)] out IReadOnlyList<SecretKey>? keys,
        [NotNullWhen(false)] out string? error
    );
}
