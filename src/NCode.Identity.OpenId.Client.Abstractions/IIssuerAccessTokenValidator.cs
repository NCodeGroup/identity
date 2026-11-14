// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Client;

/// <summary>
/// Validates a JWT access token (RFC 9068) that another issuer issued, using the issuer's discovery document and
/// signing keys.
/// </summary>
/// <remarks>
/// An invalid token is returned as data, where <see cref="IssuerAccessTokenResult.FailureCode"/> names why it is
/// invalid and <see cref="IssuerAccessTokenResult.FailureReason"/> describes it. An issuer whose discovery document or
/// signing keys cannot be retrieved is the <see cref="TokenValidationFailureCodes.IssuerUnavailable"/> failure, never a
/// valid result.
/// </remarks>
[PublicAPI]
public interface IIssuerAccessTokenValidator
{
    /// <summary>
    /// Validates an access token that the specified issuer issued for the specified audience.
    /// </summary>
    /// <param name="issuer">The issuer identifier of the provider that issued the access token.</param>
    /// <param name="audience">The audience the access token must name.</param>
    /// <param name="token">The compact JWT access token.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe.</param>
    ValueTask<IssuerAccessTokenResult> ValidateAsync(
        string issuer,
        string audience,
        string token,
        CancellationToken cancellationToken
    );
}
