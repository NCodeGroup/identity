// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using NCode.Identity.Jose;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.Secrets.Collections;
using NCode.Identity.Secrets.Keys;

namespace NCode.Identity.OpenId.Client.Tokens;

/// <summary>
/// Provides the signature policy and the signing-key resolution shared by the validation of the JWTs the provider issues
/// to the client.
/// </summary>
// See ADR-1273, ADR-1274 and ADR-1301.
internal static class ProviderSignaturePolicy
{
    /// <summary>
    /// Gets the registered asymmetric signature algorithms that the client permits and the provider advertises,
    /// excluding <c>none</c>. An empty or <see langword="null"/> list adds no restriction from its side.
    /// </summary>
    /// <param name="algorithms">The registered algorithms.</param>
    /// <param name="configured">The algorithms the client registered for the token.</param>
    /// <param name="advertised">The algorithms the provider advertises for the token.</param>
    public static List<string> GetPermittedAlgorithms(
        IAlgorithmCollection algorithms,
        IReadOnlyList<string>? configured,
        IReadOnlyList<string>? advertised
    )
    {
        var result = new List<string>();
        foreach (var algorithm in algorithms)
        {
            if (
                algorithm is not SignatureAlgorithm signatureAlgorithm
                || !signatureAlgorithm.AcceptsOnly<AsymmetricSecretKey>()
                || string.Equals(
                    signatureAlgorithm.Code,
                    AlgorithmCodes.DigitalSignature.None,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                continue;

            var code = signatureAlgorithm.Code;
            if (configured is { Count: > 0 } && !configured.Contains(code, StringComparer.Ordinal))
                continue;

            if (advertised is { Count: > 0 } && !advertised.Contains(code, StringComparer.Ordinal))
                continue;

            result.Add(code);
        }

        return result;
    }

    /// <summary>
    /// Resolves the provider's signing keys for a token, selecting by <c>kid</c> and refreshing the key set once when
    /// the <c>kid</c> is unknown.
    /// </summary>
    public static async ValueTask<IEnumerable<SecretKey>> ResolveKeysAsync(
        IProviderKeySetResolver keySetResolver,
        string authority,
        JoseToken token,
        CancellationToken cancellationToken
    )
    {
        var keys = await keySetResolver.GetSigningKeysAsync(authority, cancellationToken);

        if (!TryGetKeyId(token, out var kid))
            return keys;

        if (keys.TryGetByKeyId(kid, out var key))
            return [key];

        keys = await keySetResolver.RefreshSigningKeysAsync(authority, cancellationToken);
        return keys.TryGetByKeyId(kid, out key) ? [key] : [];
    }

    /// <summary>
    /// Selects the signing keys of a token from the given keys by <c>kid</c>, or all of them when the token names none.
    /// </summary>
    public static ValueTask<IEnumerable<SecretKey>> ResolveGivenKeysAsync(
        ISecretKeyCollection keys,
        JoseToken token,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGetKeyId(token, out var kid))
            return ValueTask.FromResult<IEnumerable<SecretKey>>(keys);

        IEnumerable<SecretKey> selected = keys.TryGetByKeyId(kid, out var key) ? [key] : [];
        return ValueTask.FromResult(selected);
    }

    /// <summary>
    /// Reads the <c>kid</c> header of a token.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="kid">The <c>kid</c> header when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the header holds a non-empty string <c>kid</c>.</returns>
    private static bool TryGetKeyId(JoseToken token, [NotNullWhen(true)] out string? kid)
    {
        kid = null;
        if (
            !token.Header.TryGetProperty(JoseClaimNames.Header.Kid, out var kidElement)
            || kidElement.ValueKind != JsonValueKind.String
        )
            return false;

        kid = kidElement.GetString();
        return kid is { Length: > 0 };
    }

    /// <summary>
    /// Rethrows a validation failure that is a cancellation or a provider failure, preserving its stack trace, because
    /// the validators wrap every failure, including those raised while resolving keys.
    /// </summary>
    public static void RethrowProviderFailure(Exception exception)
    {
        if (exception is OperationCanceledException or OpenIdProviderException)
            ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
