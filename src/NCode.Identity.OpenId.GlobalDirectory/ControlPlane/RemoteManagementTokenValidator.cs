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
    private const string ManagementAudience = "urn:ncode:management";
    private const string AuthenticationType = "ControlPlaneBearer";
    private const string ConfirmationKeyThumbprint = "jkt";
    private const string ReplayKeyPrefix = "dpop-jti:";
    private const int RetryAfterSeconds = 5;
    private const int ProofLifetimeSeconds = 60;

    private static TimeSpan ProofLifetime { get; } = TimeSpan.FromSeconds(ProofLifetimeSeconds);

    private static string[] AllowedAlgorithms { get; } =
    ["ES256", "ES384", "ES512", "PS256", "PS384", "PS512", "EdDSA"];

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
            ManagementAudience,
            token,
            cancellationToken
        );

        if (result.IsUnavailable)
            return ManagementTokenValidation.Unavailable(TimeSpan.FromSeconds(RetryAfterSeconds));

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
        if (!Uri.TryCreate(CellOptions.ManagementUrl, UriKind.Absolute, out var managementUri))
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

        // A proof is accepted once within its window (RFC 9449 §11.1).
        return await ReplayKeys.TryAddAsync(
            $"{ReplayKeyPrefix}{proof.JwkThumbprint}:{proof.Jti}",
            proof.IssuedAt + ProofLifetime,
            cancellationToken
        );
    }

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

    private static string? GetString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        return null;
    }

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
}
