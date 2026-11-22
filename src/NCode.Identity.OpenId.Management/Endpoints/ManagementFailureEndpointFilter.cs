// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Http;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.GlobalDirectory;

namespace NCode.Identity.OpenId.Management.Endpoints;

/// <summary>
/// The outermost endpoint filter of the management <c>/api</c> surface, after <see cref="ManagementCallerGate"/> has
/// authenticated the caller. It stops a request whose binding failed, answers an <see cref="HttpResultException"/>
/// raised beneath it with its result, and answers a <see cref="DirectoryUnavailableException"/> with the
/// <c>503 Service Unavailable</c> of <see cref="ControlPlaneUnavailableProblem"/>.
/// </summary>
// See ADR-1520 and ADR-1044.
internal sealed class ManagementFailureEndpointFilter : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        var httpContext = context.HttpContext;

        // With RouteHandlerOptions.ThrowOnBadRequest off, minimal APIs set 400 for a route, query or header value that
        // fails to bind and still run the filters, never the route.
        if (httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            return TypedResults.Empty;
        }

        try
        {
            return await next(context);
        }
        catch (HttpResultException exception)
        {
            return exception.HttpResult;
        }
        catch (DirectoryUnavailableException)
        {
            return ControlPlaneUnavailableProblem.Create(httpContext);
        }
    }
}
