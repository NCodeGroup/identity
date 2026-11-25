// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.Jose;
using NCode.Identity.Secrets.Collections;

namespace NCode.Identity.OpenId.Client;

/// <summary>
/// Describes a JWT access token that another issuer issued, to validate.
/// </summary>
[PublicAPI]
public sealed class IssuerAccessTokenValidationRequest
{
    /// <summary>
    /// Gets the issuer identifier of the provider that issued the access token.
    /// </summary>
    public required string Issuer { get; init; }

    /// <summary>
    /// Gets the audience the access token must name.
    /// </summary>
    public required string Audience { get; init; }

    /// <summary>
    /// Gets the compact JWT access token.
    /// </summary>
    public required string Token { get; init; }

    /// <summary>
    /// Gets the signing keys to verify the access token with, selected by <c>kid</c>. When <see langword="null"/>, the
    /// validator reads the issuer's discovery document and key set instead.
    /// </summary>
    public ISecretKeyCollection? SigningKeys { get; init; }

    /// <summary>
    /// Gets the <c>typ</c> header value the access token must carry. The default is
    /// <see cref="JoseTokenTypes.AccessTokenJwt"/> (RFC 9068).
    /// </summary>
    public string TokenType { get; init; } = JoseTokenTypes.AccessTokenJwt;
}
