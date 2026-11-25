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
    /// Validates an access token that the request's issuer issued for the request's audience, which the token must
    /// name. When <see cref="IssuerAccessTokenValidationRequest.SigningKeys"/> is set, the validator never reads the
    /// issuer's discovery document or key set and resolves the signing key from those keys by <c>kid</c>.
    /// </summary>
    /// <param name="request">The access token to validate.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe.</param>
    ValueTask<IssuerAccessTokenResult> ValidateAsync(
        IssuerAccessTokenValidationRequest request,
        CancellationToken cancellationToken
    );
}
