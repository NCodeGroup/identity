// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NCode.Buffers;
using NCode.Identity.Jose;
using NCode.Identity.Jose.Algorithms;
using NCode.Identity.Jose.Credentials;
using NCode.Identity.Jwt.Encoding;
using NCode.Identity.OpenId.Client.Options;
using NCode.Identity.Secrets.Keys;

namespace NCode.Identity.OpenId.Client.Authentication;

/// <summary>
/// Provides an implementation of <see cref="IClientAuthenticator"/> for the <c>private_key_jwt</c> method, which sends a
/// short-lived JWT client assertion signed with an asymmetric key of the client (RFC 7523 §2.2, OpenID Connect Core §9).
/// </summary>
internal class PrivateKeyJwtClientAuthenticator(
    TimeProvider timeProvider,
    IClientKeyRingProvider keyRingProvider,
    IAlgorithmCollectionProvider algorithmCollectionProvider,
    ICredentialSelector credentialSelector,
    IJwtEncoder jwtEncoder,
    IOptions<OpenIdClientOptions> options
) : IClientAuthenticator
{
    private const string NoneAlgorithm = "none";
    private const int JtiByteCount = 16;

    private static TimeSpan AssertionLifetime { get; } = TimeSpan.FromSeconds(60);

    private static string[] DefaultPreferredAlgorithms { get; } =
    [
        AlgorithmCodes.DigitalSignature.RsaSsaPssSha256,
        AlgorithmCodes.DigitalSignature.EcdsaSha256,
        AlgorithmCodes.DigitalSignature.RsaSsaPssSha384,
        AlgorithmCodes.DigitalSignature.EcdsaSha384,
        AlgorithmCodes.DigitalSignature.RsaSsaPssSha512,
        AlgorithmCodes.DigitalSignature.EcdsaSha512,
        AlgorithmCodes.DigitalSignature.RsaSha256,
        AlgorithmCodes.DigitalSignature.RsaSha384,
        AlgorithmCodes.DigitalSignature.RsaSha512,
    ];

    /// <summary>
    /// Creates the <c>jti</c> of a client assertion.
    /// </summary>
    /// <returns>The base64url text, without padding, of <see cref="JtiByteCount"/> random bytes, which are not
    /// sensitive because the assertion publishes them.</returns>
    private static string CreateJti()
    {
        using var owner = BufferFactory.RentRandom(
            JtiByteCount,
            isSensitive: false,
            out Span<byte> bytes
        );
        return Base64Url.EncodeToString(bytes);
    }

    private TimeProvider TimeProvider { get; } = timeProvider;
    private IClientKeyRingProvider KeyRingProvider { get; } = keyRingProvider;
    private IAlgorithmCollectionProvider AlgorithmCollectionProvider { get; } =
        algorithmCollectionProvider;
    private ICredentialSelector CredentialSelector { get; } = credentialSelector;
    private IJwtEncoder JwtEncoder { get; } = jwtEncoder;
    private OpenIdClientOptions Options { get; } = options.Value;

    /// <inheritdoc />
    public string MethodName => OpenIdProtocol.ClientAuthenticationMethods.PrivateKeyJwt;

    /// <inheritdoc />
    public bool UsesMutualTls => false;

    /// <inheritdoc />
    public async ValueTask AuthenticateAsync(
        ClientAuthenticationContext context,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        var registration = context.Registration;
        var clientId = registration.ClientId;
        var keyRingName = registration.KeyRing;
        if (string.IsNullOrEmpty(keyRingName))
            throw new InvalidOperationException(
                $"The client '{clientId}' has no key ring for the '{MethodName}' method."
            );

        var audience =
            context.Metadata.Issuer
            ?? throw new InvalidOperationException(
                $"The provider metadata has no issuer to use as the audience of the assertion of the client '{clientId}'."
            );

        var candidateKeys = KeyRingProvider
            .GetKeyRing(keyRingName)
            .OfType<AsymmetricSecretKey>()
            .Cast<SecretKey>()
            .ToList();

        var preferredAlgorithms = BuildPreferredAlgorithms(
            context.Metadata.TokenEndpointAuthSigningAlgValuesSupported
        );
        if (
            !CredentialSelector.TryGetSigningCredentials(
                AlgorithmCollectionProvider.Collection,
                preferredAlgorithms,
                candidateKeys,
                out var credentials
            )
        )
            throw new InvalidOperationException(
                $"The key ring '{keyRingName}' of the client '{clientId}' has no usable asymmetric signing key and algorithm."
            );

        var now = TimeProvider.GetUtcNow();
        var encodingParameters = new JwtEncodingParameters
        {
            SigningCredentials = credentials,
            Issuer = clientId,
            Audience = new StringValues(audience),
            IssuedAt = now,
            NotBefore = now,
            Expires = now + AssertionLifetime,
            ExtraPayloadClaims = new Dictionary<string, object>
            {
                [JoseClaimNames.Payload.Sub] = clientId,
                [JoseClaimNames.Payload.Jti] = CreateJti(),
            },
        };
        var assertion = await JwtEncoder.EncodeAsync(encodingParameters, cancellationToken);

        var clientAssertionTypeParameter = new KeyValuePair<string, string>(
            OpenIdProtocol.Parameters.ClientAssertionType,
            OpenIdProtocol.ClientAssertionTypes.JwtBearer
        );
        context.Parameters.Add(clientAssertionTypeParameter);
        var clientAssertionParameter = new KeyValuePair<string, string>(
            OpenIdProtocol.Parameters.ClientAssertion,
            assertion
        );
        context.Parameters.Add(clientAssertionParameter);
        var clientIdParameter = new KeyValuePair<string, string>(
            OpenIdProtocol.Parameters.ClientId,
            clientId
        );
        context.Parameters.Add(clientIdParameter);
    }

    private List<string> BuildPreferredAlgorithms(List<string>? supported)
    {
        IReadOnlyList<string> preferred = Options.ClientAssertionSigningAlgorithms
            is { Count: > 0 } configured
            ? configured
            : DefaultPreferredAlgorithms;

        if (supported is null || supported.Count == 0)
            return [.. preferred];

        var result = new List<string>();
        foreach (var algorithm in preferred)
        {
            if (supported.Contains(algorithm, StringComparer.Ordinal))
                result.Add(algorithm);
        }

        foreach (var algorithm in supported)
        {
            if (
                !string.Equals(algorithm, NoneAlgorithm, StringComparison.Ordinal)
                && !result.Contains(algorithm, StringComparer.Ordinal)
            )
                result.Add(algorithm);
        }

        return result;
    }
}
