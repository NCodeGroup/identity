// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.AspNetCore;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Authentication.Tokens.Commands;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// The authentication endpoint filter of the control-plane authority API that the control-plane cell serves to the
/// other cells. It materializes the control-plane environment for the request, authenticates the bearer or DPoP access
/// token of the request for the control-plane audience, and requires the <c>control-plane</c> scope and a
/// <c>client_id</c> that names a cell's machine client. A request without a valid token is answered
/// <c>401 Unauthorized</c> and a request whose token lacks the scope or names another client is answered
/// <c>403 Forbidden</c>, never allowed. A request that passes runs inside an unscoped ambient scope, because the
/// roles it reads are the control plane's own rows.
/// </summary>
// See ADR-1411 and ADR-1466.
internal sealed class ControlPlaneAuthenticationEndpointFilter(
    IOpenIdContextFactory openIdContextFactory,
    IAmbientTenantAccessor ambientTenantAccessor
) : IEndpointFilter
{
    /// <summary>
    /// The scope that an access token for the control plane carries.
    /// </summary>
    internal const string Scope = "control-plane";

    /// <summary>
    /// The prefix of the <c>client_id</c> of a cell's machine client.
    /// </summary>
    internal const string CellClientIdPrefix = "cell-";

    /// <summary>
    /// The reason that the filter gives the unscoped scope it opens.
    /// </summary>
    internal const string UnscopedReason = "control-plane authority";

    private const string BearerPrefix = "Bearer ";
    private const string DPoPPrefix = "DPoP ";

    private static string[] ControlPlaneAudiences { get; } =
    [OpenIdConstants.SystemResourceServerIdentifiers.ControlPlane];

    private IOpenIdContextFactory OpenIdContextFactory { get; } = openIdContextFactory;

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        var httpContext = context.HttpContext;

        // With RouteHandlerOptions.ThrowOnBadRequest off, minimal APIs set 400 for a body that fails to bind and still
        // run the filters, never the route.
        if (httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            return TypedResults.Empty;
        }

        var mediator = httpContext.RequestServices.GetRequiredService<IMediator>();

        try
        {
            // The factory publishes the context on the request (an IOpenIdContextFeature) for the filter to read.
            await OpenIdContextFactory.CreateForControlPlaneAsync(
                httpContext,
                mediator,
                httpContext.RequestAborted
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
        >(command, httpContext.RequestAborted);

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

        using var scope = AmbientTenantAccessor.BeginUnscoped(UnscopedReason);
        return await next(context);
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
                if (string.Equals(candidate, Scope, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private static bool IsCellClient(ClaimsPrincipal principal)
    {
        var clientId = principal.FindFirst(JoseClaimNames.Payload.ClientId)?.Value;
        return clientId is not null
            && clientId.Length > CellClientIdPrefix.Length
            && clientId.StartsWith(CellClientIdPrefix, StringComparison.Ordinal);
    }
}
