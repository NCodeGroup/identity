// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using NCode.Identity.OpenId.Authentication.Tokens.Commands;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.PrincipalResolution;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// Validates a self-issued bearer or DPoP access token for the management audience against the tenant that the
/// request's OpenID environment selects, and resolves the client the caller acts as. It never answers unavailable.
/// </summary>
// See ADR-1466 and ADR-1044.
internal sealed class LocalManagementTokenValidator(
    IClientPrincipalResolver clientPrincipalResolver
) : IManagementTokenValidator
{
    private const string BearerPrefix = "Bearer ";
    private const string DPoPPrefix = "DPoP ";

    private static string[] ManagementAudiences { get; } =
    [OpenIdConstants.SystemResourceServerIdentifiers.Management];

    private IClientPrincipalResolver ClientPrincipalResolver { get; } = clientPrincipalResolver;

    /// <inheritdoc />
    public async ValueTask<ManagementTokenValidation> ValidateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var openIdContext = httpContext.GetOpenIdContextOrDefault();
        if (openIdContext is null || !TryGetBearerToken(httpContext, out var accessToken))
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

    private static bool TryGetBearerToken(HttpContext httpContext, out string token)
    {
        var authorization = httpContext.Request.Headers.Authorization.ToString();

        // Accept both the Bearer scheme and the DPoP scheme (RFC 9449 §7.1).
        foreach (var prefix in (ReadOnlySpan<string>)[BearerPrefix, DPoPPrefix])
        {
            if (authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                token = authorization[prefix.Length..].Trim();
                return token.Length > 0;
            }
        }

        token = string.Empty;
        return false;
    }
}
