// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NCode.Identity.AspNetCore;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Authentication.Tokens.Commands;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// The authentication gate of the control-plane authority API that the control-plane cell serves to the other cells. It
/// runs before the route binds its body, materializes the control-plane environment for the request, authenticates the
/// bearer or DPoP access token of the request for the control-plane audience, and requires the <c>control-plane</c>
/// scope and a <c>client_id</c> that names a cell's machine client. A request without a valid token is answered
/// <c>401 Unauthorized</c> and a request whose token lacks the scope or names another client is answered
/// <c>403 Forbidden</c>, never allowed. A request refused here costs no more than its headers.
/// </summary>
// See ADR-1520, ADR-1411 and ADR-1466.
internal sealed class ControlPlaneAuthenticationGate(
    IOpenIdContextFactory openIdContextFactory,
    IMediator mediator
) : IEndpointGate
{
    private const string BearerPrefix = "Bearer ";
    private const string DPoPPrefix = "DPoP ";

    private static string[] ControlPlaneAudiences { get; } =
    [GlobalDirectoryProtocol.ControlPlaneAudience];

    private IOpenIdContextFactory OpenIdContextFactory { get; } = openIdContextFactory;

    private IMediator Mediator { get; } = mediator;

    /// <inheritdoc />
    public async ValueTask<IResult?> InvokeAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // The factory publishes the context on the request (an IOpenIdContextFeature) for the gate to read.
            await OpenIdContextFactory.CreateForControlPlaneAsync(
                httpContext,
                Mediator,
                cancellationToken
            );
        }
        catch (HttpResultException exception)
        {
            return exception.HttpResult;
        }

        var openIdContext = httpContext.GetOpenIdContextOrDefault();
        if (openIdContext is null || !TryGetBearerToken(httpContext, out var accessToken))
            return Unauthorized(httpContext);

        var command = new AuthenticateAccessTokenCommand(
            openIdContext,
            accessToken,
            ControlPlaneAudiences
        );
        var disposition = await openIdContext.Mediator.SendAsync<
            AuthenticateAccessTokenCommand,
            AuthenticateAccessTokenDisposition
        >(command, cancellationToken);

        if (!disposition.IsAuthenticated)
            return Unauthorized(httpContext);

        var principal = disposition.Principal;
        if (!HasScope(principal) || !IsCellClient(principal))
        {
            return TypedResults.Problem(
                title: "Forbidden",
                detail: "The access token must carry the control-plane scope and name a cell's machine client.",
                statusCode: StatusCodes.Status403Forbidden
            );
        }

        return null;
    }

    private static ProblemHttpResult Unauthorized(HttpContext httpContext)
    {
        httpContext.Response.Headers.WWWAuthenticate = "Bearer";
        return TypedResults.Problem(
            title: "Unauthorized",
            detail: "A valid access token for the control plane is required.",
            statusCode: StatusCodes.Status401Unauthorized
        );
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

    private static bool HasScope(ClaimsPrincipal principal)
    {
        foreach (var claim in principal.FindAll(JoseClaimNames.Payload.Scope))
        {
            foreach (var candidate in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (
                    string.Equals(
                        candidate,
                        GlobalDirectoryProtocol.ControlPlaneScope,
                        StringComparison.Ordinal
                    )
                )
                    return true;
            }
        }

        return false;
    }

    private static bool IsCellClient(ClaimsPrincipal principal)
    {
        var clientId = principal.FindFirst(JoseClaimNames.Payload.ClientId)?.Value;
        return clientId is not null
            && clientId.Length > GlobalDirectoryProtocol.CellClientIdPrefix.Length
            && clientId.StartsWith(
                GlobalDirectoryProtocol.CellClientIdPrefix,
                StringComparison.Ordinal
            );
    }
}
