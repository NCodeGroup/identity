// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Jose;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.Jwt.Validation;

namespace NCode.Identity.OpenId.Client.Tokens;

/// <summary>
/// Provides a default implementation of <see cref="IIssuerAccessTokenValidator"/> that reads the issuer's discovery
/// document, resolves the issuer's signing keys from its key set and validates the JWT access token (RFC 9068): its
/// <c>typ</c>, issuer, audience, lifetime and signature under an asymmetric algorithm. An access token is never
/// encrypted or compressed, and a failure to reach the issuer is never reported as valid.
/// </summary>
internal class DefaultIssuerAccessTokenValidator(
    IAlgorithmCollectionProvider algorithmCollectionProvider,
    IProviderMetadataResolver metadataResolver,
    IProviderKeySetResolver keySetResolver,
    IJwtValidator jwtValidator
) : IIssuerAccessTokenValidator
{
    private IAlgorithmCollectionProvider AlgorithmCollectionProvider { get; } =
        algorithmCollectionProvider;
    private IProviderMetadataResolver MetadataResolver { get; } = metadataResolver;
    private IProviderKeySetResolver KeySetResolver { get; } = keySetResolver;
    private IJwtValidator JwtValidator { get; } = jwtValidator;

    /// <inheritdoc />
    public async ValueTask<IssuerAccessTokenResult> ValidateAsync(
        string issuer,
        string audience,
        string token,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(issuer);
        ArgumentException.ThrowIfNullOrEmpty(audience);

        if (string.IsNullOrEmpty(token))
            return Fail(TokenValidationFailureCodes.InvalidToken, "The access token is empty.");

        try
        {
            var metadata = await MetadataResolver.GetMetadataAsync(issuer, cancellationToken);
            if (!string.Equals(metadata.Issuer, issuer, StringComparison.Ordinal))
                return Fail(
                    TokenValidationFailureCodes.InvalidIssuer,
                    "The issuer of the discovery document is not the expected issuer."
                );

            var algorithms = ProviderSignaturePolicy.GetPermittedAlgorithms(
                AlgorithmCollectionProvider.Collection,
                null,
                null
            );
            if (algorithms.Count == 0)
                return Fail(
                    TokenValidationFailureCodes.AlgorithmNotPermitted,
                    "No asymmetric signature algorithm is permitted for the access token."
                );

            var parameters = new JwtValidationParameters
            {
                ClockSkew = ProviderJwtValidator.ClockSkew,
                ValidSignatureAlgorithms = algorithms,
                ValidKeyManagementAlgorithms = [],
                ValidContentEncryptionAlgorithms = [],
                ValidCompressionAlgorithms = [],
                ResolveValidationKeysAsync = (jose, _, _, ct) =>
                    ProviderSignaturePolicy.ResolveKeysAsync(KeySetResolver, issuer, jose, ct),
            }
                .ValidateTokenType(JoseTokenTypes.AccessTokenJwt)
                .ValidateIssuer(issuer)
                .ValidateAudience(audience)
                .ValidateTokenLifeTime();

            var result = await JwtValidator.ValidateAsync(token, parameters, cancellationToken);
            if (!result.IsValid)
            {
                var exception = result.Exception;
                ProviderSignaturePolicy.RethrowProviderFailure(exception);

                if (exception is JwtDecodeException or JwtSecretKeyNotFoundException)
                    return Fail(
                        TokenValidationFailureCodes.InvalidSignature,
                        "The access token signature could not be verified with the issuer's signing keys."
                    );

                return Fail(
                    result.FailureCode,
                    $"The access token is invalid: {exception.Message}"
                );
            }

            return new IssuerAccessTokenResult(result.DecodedJwt.Payload, issuer);
        }
        catch (OpenIdProviderException exception)
        {
            return Fail(
                TokenValidationFailureCodes.IssuerUnavailable,
                $"The discovery document or signing keys of the issuer could not be retrieved: {exception.Message}"
            );
        }
    }

    private static IssuerAccessTokenResult Fail(string failureCode, string reason) =>
        new(failureCode, reason);
}
