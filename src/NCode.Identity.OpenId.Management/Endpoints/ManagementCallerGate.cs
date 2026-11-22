// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NCode.Identity.AspNetCore;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.Endpoints;

/// <summary>
/// The gate of the management <c>/api</c> surface. It runs before a route binds its parameters, materializes the OpenID
/// request environment for the control-plane tenant — the root tenant, or a static strategy's own tenant — whatever
/// tenant the request's host or path addresses, authenticates the management access token of the request with
/// <see cref="IManagementTokenValidator"/>, answers <c>401</c> to a request without a valid one and
/// <c>503 Service Unavailable</c> with a <c>Retry-After</c> while the control plane that validates tokens is
/// unavailable, and publishes the caller. The principal a valid management access token authenticates is set on
/// <see cref="HttpContext.User"/> and as <see cref="IManagementCallerFeature"/>, for the endpoints' resource-based
/// authorization to read, whatever principal an authentication scheme set. The gate does <b>not</b> open a tenant
/// scope: control-plane endpoints act on unscoped platform/team/tenant rows, and tenant-bound endpoints open their
/// scope from the route's <c>tenantId</c> via the <see cref="TenantScopeEndpointFilter"/> on the tenant-plane group, so
/// the route's tenant only scopes the data. For an authenticated caller the gate also asks
/// <see cref="IControlPlaneAuthority"/>, once, whether the caller is a client acting for itself and keeps the answer on
/// <see cref="ManagementActorFeature"/>; an unavailable control plane there keeps "not acting client" and does not fail
/// the request. A request refused here costs no more than its headers.
/// </summary>
// See ADR-1520, ADR-1044, ADR-1191 and ADR-1466.
internal sealed class ManagementCallerGate(
    IOpenIdContextFactory openIdContextFactory,
    IMediator mediator,
    IManagementTokenValidator tokenValidator,
    IControlPlaneAuthority controlPlaneAuthority
) : IEndpointGate
{
    private IOpenIdContextFactory OpenIdContextFactory { get; } = openIdContextFactory;
    private IMediator Mediator { get; } = mediator;
    private IManagementTokenValidator TokenValidator { get; } = tokenValidator;
    private IControlPlaneAuthority ControlPlaneAuthority { get; } = controlPlaneAuthority;

    /// <inheritdoc />
    public async ValueTask<IResult?> InvokeAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // The factory publishes the context on the request (an IOpenIdContextFeature) for endpoints to retrieve.
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

        var validation = await TokenValidator.ValidateAsync(httpContext, cancellationToken);
        if (validation.IsUnavailable)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = "The control plane that validates management tokens is unavailable.",
            };
            return RetryAfterProblem.Create(httpContext, validation.RetryAfter, problemDetails);
        }

        if (validation.Principal is not { } caller)
        {
            return ManagementProblems.AuthenticationRequired();
        }

        httpContext.User = caller;
        var callerFeature = new DefaultManagementCallerFeature(caller);
        httpContext.Features.Set<IManagementCallerFeature>(callerFeature);

        var isActingClient = await IsActingClientAsync(caller, cancellationToken);
        var actorFeature = new ManagementActorFeature(isActingClient);
        httpContext.Features.Set(actorFeature);

        return null;
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

        var request = new ControlPlaneAuthorityRequest { Subject = subject, ClientId = clientId };

        try
        {
            var authority = await ControlPlaneAuthority.GetAsync(request, cancellationToken);

            return authority.IsActingClient;
        }
        catch (DirectoryUnavailableException)
        {
            return false;
        }
    }
}
