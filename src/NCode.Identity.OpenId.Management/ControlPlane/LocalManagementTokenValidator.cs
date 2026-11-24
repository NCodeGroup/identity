// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Authentication.Tokens.Commands;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.PrincipalResolution;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// Validates a self-issued bearer or DPoP access token for the management audience against the tenant that the
/// request's OpenID environment selects, and resolves the client the caller acts as. On a platform with a directory
/// it accepts only a DPoP-bound token sent under the <c>DPoP</c> scheme. It never answers unavailable.
/// </summary>
// See ADR-1466 and ADR-1044.
internal sealed class LocalManagementTokenValidator(
    IClientPrincipalResolver clientPrincipalResolver,
    IPlatformTopology platformTopology
) : IManagementTokenValidator
{
    private const string BearerPrefix = "Bearer ";
    private const string DPoPPrefix = "DPoP ";

    private static string[] ManagementAudiences { get; } =
    [OpenIdConstants.SystemResourceServerIdentifiers.Management];

    private IClientPrincipalResolver ClientPrincipalResolver { get; } = clientPrincipalResolver;

    private IPlatformTopology PlatformTopology { get; } = platformTopology;

    /// <inheritdoc />
    public async ValueTask<ManagementTokenValidation> ValidateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var openIdContext = httpContext.GetOpenIdContextOrDefault();
        if (
            openIdContext is null
            || !TryGetBearerToken(httpContext, out var accessToken, out var isDPoP)
        )
            return ManagementTokenValidation.Unauthenticated;

        var requiresDPoP = PlatformTopology.HasDirectory;
        if (requiresDPoP && !isDPoP)
            return ManagementTokenValidation.Unauthenticated;

        var command = new AuthenticateAccessTokenCommand(
            openIdContext,
            accessToken,
            ManagementAudiences
        );
        var disposition = await openIdContext.Mediator.SendAsync<
            AuthenticateAccessTokenCommand,
            AuthenticateAccessTokenDisposition
        >(command, cancellationToken);

        if (!disposition.IsAuthenticated)
            return ManagementTokenValidation.Unauthenticated;

        if (requiresDPoP && !HasConfirmationThumbprint(disposition.Principal))
            return ManagementTokenValidation.Unauthenticated;

        // The call is kept for the answer the request remembers, which the audit recorder reads after the
        // commit, so a change the request commits, such as a machine client deleting itself, does not alter
        // the actor its audit records.
        // See ADR-1466.
        _ = await ClientPrincipalResolver.GetActingClientOrDefaultAsync(
            openIdContext,
            disposition.Principal,
            cancellationToken
        );

        return ManagementTokenValidation.Authenticated(disposition.Principal);
    }

    private static bool HasConfirmationThumbprint(ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirst(JoseClaimNames.Payload.Cnf)?.Value;
        if (string.IsNullOrEmpty(value))
            return false;

        try
        {
            var confirmation = JsonElement.Parse(value);
            return confirmation.ValueKind == JsonValueKind.Object
                && confirmation.TryGetProperty(
                    OpenIdProtocol.ConfirmationMethods.JwkThumbprint,
                    out var thumbprint
                )
                && thumbprint.ValueKind == JsonValueKind.String
                && HasText(thumbprint);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasText(JsonElement element)
    {
        var text = element.GetString();
        return !string.IsNullOrEmpty(text);
    }

    private static bool TryGetBearerToken(
        HttpContext httpContext,
        out string token,
        out bool isDPoP
    )
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
