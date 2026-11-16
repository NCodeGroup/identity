// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NCode.Identity.AspNetCore;
using NCode.Identity.Jose;
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
/// <c>503 Service Unavailable</c> with a <c>Retry-After</c> and does not call the endpoint. The same answer covers an
/// endpoint whose authorization asks <see cref="IControlPlaneAuthority"/> for a role while the control plane is
/// unavailable. For an authenticated caller the filter also asks <see cref="IControlPlaneAuthority"/>, once, whether
/// the caller is a client acting for itself and keeps the answer on <see cref="ManagementActorFeature"/>; an
/// unavailable control plane there keeps "not acting client" and does not fail the request.
/// </summary>
// See ADR-1466 and ADR-1044.
internal sealed class ManagementAuthenticationEndpointFilter(
    IManagementTokenValidator tokenValidator,
    IControlPlaneAuthority controlPlaneAuthority
) : IEndpointFilter
{
    private IManagementTokenValidator TokenValidator { get; } = tokenValidator;
    private IControlPlaneAuthority ControlPlaneAuthority { get; } = controlPlaneAuthority;

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

            var isActingClient = await IsActingClientAsync(caller, httpContext.RequestAborted);
            httpContext.Features.Set(new ManagementActorFeature(isActingClient));
        }
        else
        {
            var anonymousIdentity = new ClaimsIdentity();
            httpContext.User = new ClaimsPrincipal(anonymousIdentity);
            httpContext.Features.Set<IManagementCallerFeature>(null);
            httpContext.Features.Set<ManagementActorFeature>(null);
        }

        try
        {
            return await next(context);
        }
        catch (DirectoryUnavailableException)
        {
            return RetryAfterProblem.Create(
                httpContext,
                TimeSpan.FromSeconds(GlobalDirectoryProtocol.UnavailableRetryAfterSeconds),
                new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Detail =
                        "The control plane that holds the platform and team roles is unavailable.",
                }
            );
        }
    }

    // Only the control plane can tell whether the subject is the principal of the requesting client, and a client that
    // deletes itself is gone once the change commits, so the answer is resolved here, before the handler runs. When the
    // control plane is unavailable the caller is not an acting client, and the authorization handlers answer their own
    // 503.
    private async ValueTask<bool> IsActingClientAsync(
        ClaimsPrincipal caller,
        CancellationToken cancellationToken
    )
    {
        var subject = caller.FindFirstValue(JoseClaimNames.Payload.Sub);
        var clientId = caller.FindFirstValue(JoseClaimNames.Payload.ClientId);
        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(clientId))
        {
            return false;
        }

        try
        {
            var authority = await ControlPlaneAuthority.GetAsync(
                new ControlPlaneAuthorityRequest { Subject = subject, ClientId = clientId },
                cancellationToken
            );

            return authority.IsActingClient;
        }
        catch (DirectoryUnavailableException)
        {
            return false;
        }
    }
}
