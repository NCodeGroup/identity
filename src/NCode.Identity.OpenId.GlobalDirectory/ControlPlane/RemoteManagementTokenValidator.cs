// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NCode.Identity.Jose;
using NCode.Identity.Jose.DPoP;
using NCode.Identity.OpenId.Client;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.SharedState;

namespace NCode.Identity.OpenId.GlobalDirectory.ControlPlane;

/// <summary>
/// Validates a bearer or DPoP access token for the management audience against the control-plane cell's issuer, so a
/// cell that is not the control plane authenticates a caller without a local lookup. The token is validated through the
/// issuer's discovery document and signing keys. A token bound to a key by <c>cnf.jkt</c> must come with one DPoP proof
/// for the request, made with that key, whose <c>jti</c> has not been used before (RFC 9449). The principal is built
/// from the token's claims. An issuer that cannot be reached answers unavailable, and nothing else does.
/// </summary>
internal sealed class RemoteManagementTokenValidator(
    IIssuerAccessTokenValidator accessTokenValidator,
    IDPoPProofService proofService,
    IExpiringKeySet replayKeys,
    IOptions<CellOptions> cellOptions
) : IManagementTokenValidator
{
    private const string BearerPrefix = "Bearer ";
    private const string DPoPPrefix = "DPoP ";
    private const string AuthenticationType = "ControlPlaneBearer";
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
            identity.AddClaim(new Claim(JoseClaimNames.Payload.Sub, subject));

        if (clientId is not null)
            identity.AddClaim(new Claim(JoseClaimNames.Payload.ClientId, clientId));

        var scope = GetString(payload, JoseClaimNames.Payload.Scope);
        if (scope is not null)
            identity.AddClaim(new Claim(JoseClaimNames.Payload.Scope, scope));

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
    /// Reads the access token from the <c>Authorization</c> header of a request, under either the Bearer scheme or the
    /// DPoP scheme.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> of the request.</param>
    /// <param name="token">The access token, or an empty string when the header carries none.</param>
    /// <param name="isDPoP"><c>true</c> when the token is sent under the DPoP scheme; otherwise, <c>false</c>.</param>
    /// <returns><c>true</c> when the header carries a non-empty token; otherwise, <c>false</c>.</returns>
    private static bool TryGetToken(HttpContext httpContext, out string token, out bool isDPoP)
    {
        var authorization = httpContext.Request.Headers.Authorization.ToString();

        // Accept both the Bearer scheme and the DPoP scheme (RFC 9449 §7.1).
        foreach (var prefix in (ReadOnlySpan<string>)[BearerPrefix, DPoPPrefix])
        {
            if (authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                token = authorization[prefix.Length..].Trim();
                isDPoP = string.Equals(prefix, DPoPPrefix, StringComparison.Ordinal);
                return token.Length > 0;
            }
        }

        token = string.Empty;
        isDPoP = false;
        return false;
    }

    private IIssuerAccessTokenValidator AccessTokenValidator { get; } = accessTokenValidator;
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
            string.IsNullOrEmpty(issuer) || !TryGetToken(httpContext, out var token, out var isDPoP)
        )
            return ManagementTokenValidation.Unauthenticated;

        var result = await AccessTokenValidator.ValidateAsync(
            issuer,
            OpenIdConstants.SystemResourceServerIdentifiers.Management,
            token,
            cancellationToken
        );

        if (result.IsUnavailable)
            return ManagementTokenValidation.Unavailable(
                TimeSpan.FromSeconds(GlobalDirectoryProtocol.IssuerUnavailableRetryAfterSeconds)
            );

        if (!result.IsValid || result.Payload.ValueKind != JsonValueKind.Object)
            return ManagementTokenValidation.Unauthenticated;

        var payload = result.Payload;
        var thumbprint = GetConfirmationThumbprint(payload);

        // A token without a key binding is a bearer token, which the DPoP scheme must not carry (RFC 9449 §7.1).
        if (thumbprint is null)
        {
            if (isDPoP)
                return ManagementTokenValidation.Unauthenticated;
        }
        else if (
            !isDPoP || !await IsProofValidAsync(httpContext, token, thumbprint, cancellationToken)
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
    /// Validates the DPoP proof that accompanies an access token bound to a key: the request carries exactly one
    /// proof, the proof is made for the cell's configured management URL and the request's method and path, it is bound
    /// to the access token and to the key that the token confirms, and it has not been seen within its window.
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
