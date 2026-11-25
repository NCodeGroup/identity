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
using NCode.Identity.OpenId.Client;
using NCode.Identity.Secrets.Keys;

namespace NCode.Identity.OpenId.GlobalDirectory.Server.Tokens;

/// <summary>
/// Issues the access tokens that authenticate a cell to the directory's API, and validates them. A token is a JWT
/// access token (RFC 9068) that <see cref="DirectoryServerOptions.KeyRing"/>'s active asymmetric key signs: its issuer
/// is <see cref="DirectoryServerOptions.Issuer"/>, its audience is
/// <see cref="GlobalDirectoryProtocol.DirectoryAudience"/>, its scope is
/// <see cref="GlobalDirectoryProtocol.DirectoryScope"/>, and its <c>client_id</c> and <c>sub</c> are the cell's client
/// identifier. It carries no confirmation claim, so it is a bearer token that lives for
/// <see cref="DirectoryServerOptions.TokenLifetime"/>. The directory trusts only the tokens it issues itself.
/// </summary>
// See ADR-1536.
internal sealed class DirectoryTokenIssuer(
    TimeProvider timeProvider,
    IClientKeyRingProvider keyRingProvider,
    IAlgorithmCollectionProvider algorithmCollectionProvider,
    ICredentialSelector credentialSelector,
    IJwtEncoder jwtEncoder,
    IIssuerAccessTokenValidator tokenValidator,
    IOptions<DirectoryServerOptions> options
)
{
    private const int JtiByteCount = 16;

    private static string[] PreferredAlgorithms { get; } =
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
    /// Creates the <c>jti</c> of an access token.
    /// </summary>
    /// <returns>The base64url text, without padding, of <see cref="JtiByteCount"/> random bytes, which are not
    /// sensitive because the token publishes them.</returns>
    private static string CreateJti()
    {
        using var owner = BufferFactory.RentRandom(
            JtiByteCount,
            isSensitive: false,
            out Span<byte> bytes
        );
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>
    /// Gets the client identifier of the cell with the specified name.
    /// </summary>
    /// <param name="cellName">The name of the cell.</param>
    /// <returns>The client identifier, which is the name prefixed with
    /// <see cref="GlobalDirectoryProtocol.CellClientIdPrefix"/>.</returns>
    internal static string GetClientId(string cellName) =>
        GlobalDirectoryProtocol.CellClientIdPrefix + cellName;

    /// <summary>
    /// Gets the registered signature algorithms that accept only asymmetric keys, excluding <c>none</c>.
    /// </summary>
    /// <param name="algorithms">The registered algorithms.</param>
    /// <returns>The codes of the permitted algorithms.</returns>
    internal static List<string> GetAsymmetricSignatureAlgorithms(IAlgorithmCollection algorithms)
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

            result.Add(signatureAlgorithm.Code);
        }

        return result;
    }

    private TimeProvider TimeProvider { get; } = timeProvider;
    private IClientKeyRingProvider KeyRingProvider { get; } = keyRingProvider;
    private IAlgorithmCollectionProvider AlgorithmCollectionProvider { get; } =
        algorithmCollectionProvider;
    private ICredentialSelector CredentialSelector { get; } = credentialSelector;
    private IJwtEncoder JwtEncoder { get; } = jwtEncoder;
    private IIssuerAccessTokenValidator TokenValidator { get; } = tokenValidator;
    private DirectoryServerOptions Options { get; } = options.Value;

    /// <summary>
    /// Issues an access token to the cell with the specified name.
    /// </summary>
    /// <param name="cellName">The name of the cell that the token authenticates.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe.</param>
    /// <returns>The compact JWT access token.</returns>
    /// <exception cref="InvalidOperationException">The key ring has no usable asymmetric signing key.</exception>
    public async ValueTask<string> IssueAsync(string cellName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(cellName);

        var keyRingName = Options.KeyRing;
        var keyRing = KeyRingProvider.GetKeyRing(keyRingName);
        var candidateKeys = keyRing.OfType<AsymmetricSecretKey>().Cast<SecretKey>().ToList();
        var algorithms = AlgorithmCollectionProvider.Collection;
        if (
            !CredentialSelector.TryGetSigningCredentials(
                algorithms,
                PreferredAlgorithms,
                candidateKeys,
                out var credentials
            )
        )
        {
            throw new InvalidOperationException(
                $"The key ring '{keyRingName}' has no usable asymmetric signing key and algorithm."
            );
        }

        var clientId = GetClientId(cellName);
        var now = TimeProvider.GetUtcNow();
        var jti = CreateJti();
        var parameters = new JwtEncodingParameters
        {
            TokenType = JoseTokenTypes.AccessTokenJwt,
            SigningCredentials = credentials,
            Issuer = Options.Issuer,
            Audience = new StringValues(GlobalDirectoryProtocol.DirectoryAudience),
            IssuedAt = now,
            Expires = now + Options.TokenLifetime,
            ExtraPayloadClaims = new Dictionary<string, object>
            {
                [JoseClaimNames.Payload.Sub] = clientId,
                [JoseClaimNames.Payload.Jti] = jti,
                ["client_id"] = clientId,
                ["scope"] = GlobalDirectoryProtocol.DirectoryScope,
            },
        };
        return await JwtEncoder.EncodeAsync(parameters, cancellationToken);
    }

    /// <summary>
    /// Validates an access token that this issuer issued: its type, issuer, audience, lifetime and signature, checked
    /// against the keys of the same key ring that signs the tokens.
    /// </summary>
    /// <param name="token">The compact JWT access token.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe.</param>
    /// <returns>The validation result, which is a failure for a token that is not valid.</returns>
    public ValueTask<IssuerAccessTokenResult> ValidateAsync(
        string token,
        CancellationToken cancellationToken
    )
    {
        var signingKeys = KeyRingProvider.GetKeyRing(Options.KeyRing);
        var request = new IssuerAccessTokenValidationRequest
        {
            Issuer = Options.Issuer,
            Audience = GlobalDirectoryProtocol.DirectoryAudience,
            Token = token,
            SigningKeys = signingKeys,
        };
        return TokenValidator.ValidateAsync(request, cancellationToken);
    }
}
