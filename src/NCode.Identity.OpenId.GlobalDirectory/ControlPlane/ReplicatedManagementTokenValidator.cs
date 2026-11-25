// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NCode.Identity.Jose;
using NCode.Identity.Jose.DPoP;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.Secrets.Collections;
using NCode.Identity.SharedState;

namespace NCode.Identity.OpenId.GlobalDirectory.ControlPlane;

/// <summary>
/// Validates a DPoP-bound access token for the management audience against the management keys that the cell holds in
/// its replica, so a cell that is not the control plane authenticates a caller without a local lookup and without
/// calling the control plane. The token is verified with the replicated keys, never through a discovery document or key
/// set. A token must be sent under the DPoP scheme, must be bound to a key by <c>cnf.jkt</c> and must come with one
/// DPoP proof for the request, made with that key, whose <c>jti</c> has not been used before (RFC 9449). The principal
/// is built from the token's claims. A replica that holds no key answers unavailable, and nothing else does.
/// </summary>
internal sealed class ReplicatedManagementTokenValidator(
    IIssuerAccessTokenValidator accessTokenValidator,
    IControlPlaneReplica replica,
    ISecretKeyCollectionFactory keyCollectionFactory,
    IDPoPProofService proofService,
    IExpiringKeySet replayKeys,
    IOptions<CellOptions> cellOptions
) : IManagementTokenValidator
{
    private const string AuthenticationType = "ControlPlaneDPoP";
    private const string ConfirmationKeyThumbprint = "jkt";

    /// <summary>
    /// The kind of record that the management DPoP replay keys hold.
    /// </summary>
    private const string ReplayPurpose = "management_dpop_jti";

    private const int ProofLifetimeSeconds = 60;

    private static TimeSpan ProofLifetime { get; } = TimeSpan.FromSeconds(ProofLifetimeSeconds);

    private static string[] AllowedAlgorithms { get; } =
    ["ES256", "ES384", "ES512", "PS256", "PS384", "PS512", "EdDSA"];

    /// <summary>
    /// Gets the thumbprint of the key that an access token confirms.
    /// </summary>
    /// <param name="payload">The payload of the access token.</param>
    /// <returns>The confirmation key thumbprint, or <c>null</c> when the token carries no key binding.</returns>
    private static string? GetConfirmationThumbprint(JsonElement payload)
    {
        if (
            payload.TryGetProperty(JoseClaimNames.Payload.Cnf, out var confirmation)
            && confirmation.ValueKind == JsonValueKind.Object
        )
        {
            return GetString(confirmation, ConfirmationKeyThumbprint);
        }

        return null;
    }

    /// <summary>
    /// Creates the principal of a validated access token from its subject, client identifier and scope claims.
    /// </summary>
    /// <param name="payload">The payload of the access token.</param>
    /// <returns>
    /// The <see cref="ClaimsPrincipal"/>, or <c>null</c> when the payload has no subject and no client identifier.
    /// </returns>
    private static ClaimsPrincipal? CreatePrincipal(JsonElement payload)
    {
        var subject = GetString(payload, JoseClaimNames.Payload.Sub);
        var clientId = GetString(payload, JoseClaimNames.Payload.ClientId);
        if (subject is null && clientId is null)
            return null;

        var identity = new ClaimsIdentity(AuthenticationType);

        if (subject is not null)
        {
            var subjectClaim = new Claim(JoseClaimNames.Payload.Sub, subject);
            identity.AddClaim(subjectClaim);
        }

        if (clientId is not null)
        {
            var clientIdClaim = new Claim(JoseClaimNames.Payload.ClientId, clientId);
            identity.AddClaim(clientIdClaim);
        }

        var scope = GetString(payload, JoseClaimNames.Payload.Scope);
        if (scope is not null)
        {
            var scopeClaim = new Claim(JoseClaimNames.Payload.Scope, scope);
            identity.AddClaim(scopeClaim);
        }

        var issuerKindClaim = new Claim(
            ManagementIssuerKinds.ClaimType,
            ManagementIssuerKinds.ControlPlane
        );
        identity.AddClaim(issuerKindClaim);

        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Gets a non-empty string property of a JSON object.
    /// </summary>
    /// <param name="element">The JSON object to read.</param>
    /// <param name="name">The name of the property to read.</param>
    /// <returns>The value, or <c>null</c> when the property is absent, is not a string or is empty.</returns>
    private static string? GetString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        return null;
    }

    /// <summary>
    /// Reads the <c>kid</c> of the header of a compact JWT.
    /// </summary>
    /// <param name="token">The compact JWT.</param>
    /// <returns>The key identifier, or <c>null</c> when the header carries none or cannot be read.</returns>
    private static string? GetKeyId(string token)
    {
        var separator = token.IndexOf('.');
        if (separator <= 0)
            return null;

        try
        {
            var encoded = token[..separator];
            var bytes = StrictBase64Url.Decode(encoded);
            var header = JsonElement.Parse(bytes);
            return header.ValueKind == JsonValueKind.Object
                ? GetString(header, JoseClaimNames.Header.Kid)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private IIssuerAccessTokenValidator AccessTokenValidator { get; } = accessTokenValidator;
    private IControlPlaneReplica Replica { get; } = replica;
    private ISecretKeyCollectionFactory KeyCollectionFactory { get; } = keyCollectionFactory;
    private IDPoPProofService ProofService { get; } = proofService;
    private IExpiringKeySet ReplayKeys { get; } = replayKeys;
    private CellOptions CellOptions { get; } = cellOptions.Value;

    /// <inheritdoc />
    public async ValueTask<ManagementTokenValidation> ValidateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var issuer = CellOptions.ControlPlane.Issuer;
        if (
            string.IsNullOrEmpty(issuer)
            || !ManagementAuthorizationHeader.TryReadToken(
                httpContext,
                OpenIdProtocol.TokenTypes.DPoP,
                out var token
            )
        )
        {
            return ManagementTokenValidation.Unauthenticated;
        }

        var managementKeys = await Replica.GetManagementKeysAsync(cancellationToken);
        if (!managementKeys.HasKeys)
        {
            var retryAfter = TimeSpan.FromSeconds(
                GlobalDirectoryProtocol.UnavailableRetryAfterSeconds
            );
            return ManagementTokenValidation.Unavailable(retryAfter);
        }

        var keyId = GetKeyId(token);
        if (keyId is not null && managementKeys.RevokedKeyIds.Contains(keyId))
            return ManagementTokenValidation.Unauthenticated;

        var signingKeys = KeyCollectionFactory.Create(managementKeys.Keys);
        var validationRequest = new IssuerAccessTokenValidationRequest
        {
            Issuer = issuer,
            Audience = OpenIdConstants.SystemResourceServerIdentifiers.Management,
            Token = token,
            SigningKeys = signingKeys,
        };

        var result = await AccessTokenValidator.ValidateAsync(validationRequest, cancellationToken);
        if (!result.IsValid || result.Payload.ValueKind != JsonValueKind.Object)
            return ManagementTokenValidation.Unauthenticated;

        // A token without a key binding is a bearer token, which a management request never carries.
        var payload = result.Payload;
        var thumbprint = GetConfirmationThumbprint(payload);
        if (
            thumbprint is null
            || !await IsProofValidAsync(httpContext, token, thumbprint, cancellationToken)
        )
        {
            return ManagementTokenValidation.Unauthenticated;
        }

        var principal = CreatePrincipal(payload);
        return principal is null
            ? ManagementTokenValidation.Unauthenticated
            : ManagementTokenValidation.Authenticated(principal);
    }

    /// <summary>
    /// Validates the DPoP proof that accompanies an access token bound to a key: the request carries exactly one proof,
    /// the proof is made for the cell's configured management URL and the request's method and path, it is bound to the
    /// access token and to the key that the token confirms, and it has not been seen within its window.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> of the request that carries the proof.</param>
    /// <param name="accessToken">The access token that the proof is bound to.</param>
    /// <param name="thumbprint">The thumbprint of the key that the access token confirms.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <returns><c>true</c> when the proof is valid and accepted for the first time; otherwise, <c>false</c>.</returns>
    private async ValueTask<bool> IsProofValidAsync(
        HttpContext httpContext,
        string accessToken,
        string thumbprint,
        CancellationToken cancellationToken
    )
    {
        var request = httpContext.Request;

        var headers = request.Headers[OpenIdProtocol.DPoP.HeaderName];
        if (headers.Count != 1)
            return false;

        var proofJwt = headers[0];
        if (string.IsNullOrEmpty(proofJwt))
            return false;

        // The URL comes from the cell's configured management URL, never from the request's Host, so a proof made for
        // one cell does not pass in another.
        if (!AbsoluteUri.TryParse(CellOptions.ManagementUrl, out var managementUri))
            return false;

        var parameters = new DPoPProofValidationParameters
        {
            HttpMethod = request.Method,
            HttpUri =
                $"{managementUri.GetLeftPart(UriPartial.Authority)}{request.PathBase}{request.Path}",
            AllowedAlgorithms = AllowedAlgorithms,
            Lifetime = ProofLifetime,
            AccessToken = accessToken,
        };

        if (!ProofService.TryValidateProof(proofJwt, parameters, out var proof, out _))
            return false;

        if (!string.Equals(proof.JwkThumbprint, thumbprint, StringComparison.Ordinal))
            return false;

        // The key is a digest of the length-prefixed thumbprint and jti, so a caller's jti cannot lengthen it.
        var key = ExpiringKeys.Create(
            ReplayPurpose,
            CellOptions.Name,
            proof.JwkThumbprint,
            proof.Jti
        );

        // A proof is accepted once within its window (RFC 9449 §11.1).
        return await ReplayKeys.TryAddAsync(key, proof.IssuedAt + ProofLifetime, cancellationToken);
    }
}
