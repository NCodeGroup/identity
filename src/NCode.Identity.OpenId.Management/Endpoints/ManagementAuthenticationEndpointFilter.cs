// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.ControlPlane;

namespace NCode.Identity.OpenId.Management.Endpoints;

/// <summary>
/// The management authentication endpoint filter. It runs after <see cref="ManagementEnvironmentEndpointFilter"/> has
/// materialized the OpenID request environment, asks <see cref="IManagementTokenValidator"/> to validate the management
/// access token of the request, and publishes, on every management request, the principal a valid management access
/// token authenticates on <see cref="HttpContext.User"/> and as <see cref="IManagementCallerFeature"/>, for the
/// endpoints' resource-based authorization to read. Any other request is anonymous whatever principal an authentication
/// scheme set, and carries no feature; the imperative authorization returns <c>401</c>/<c>403</c>. When the validator
/// cannot validate the token because the control plane that validates it is unavailable, the filter answers
/// <c>503 Service Unavailable</c> with a <c>Retry-After</c> and does not call the endpoint. The same answer covers an endpoint
/// whose authorization asks <see cref="IControlPlaneAuthority"/> for a role while the control plane is unavailable.
/// </summary>
// See ADR-1466 and ADR-1044.
internal sealed class ManagementAuthenticationEndpointFilter(
    IManagementTokenValidator tokenValidator
) : IEndpointFilter
{
    private IManagementTokenValidator TokenValidator { get; } = tokenValidator;

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        var httpContext = context.HttpContext;

        var validation = await TokenValidator.ValidateAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (validation.IsUnavailable)
        {
            return RetryAfterProblem.Create(
                httpContext,
                validation.RetryAfter,
                new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Detail = "The control plane that validates management tokens is unavailable.",
                }
            );
        }

        if (validation.Principal is { } caller)
        {
            httpContext.User = caller;
            var callerFeature = new DefaultManagementCallerFeature(caller);
            httpContext.Features.Set<IManagementCallerFeature>(callerFeature);
        }
        else
        {
            var anonymousIdentity = new ClaimsIdentity();
            httpContext.User = new ClaimsPrincipal(anonymousIdentity);
            httpContext.Features.Set<IManagementCallerFeature>(null);
        }

        try
        {
            return await next(context);
        }
        catch (DirectoryUnavailableException)
        {
            return RetryAfterProblem.Create(
                httpContext,
                TimeSpan.FromSeconds(5),
                new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Detail =
                        "The control plane that holds the platform and team roles is unavailable.",
                }
            );
        }
    }
}
