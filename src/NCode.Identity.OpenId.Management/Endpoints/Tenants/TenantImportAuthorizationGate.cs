// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.GlobalDirectory;

namespace NCode.Identity.OpenId.Management.Endpoints.Tenants;

/// <summary>
/// Authorizes the caller of <c>POST api/tenant-imports</c> for the update operation at the platform node before the
/// route reads its body of up to 64 MiB plus 64 KiB. It runs after <see cref="ManagementCallerGate"/>, so the caller is
/// authenticated. A caller that is not authorized gets <c>403</c>, and <c>503 Service Unavailable</c> with a
/// <c>Retry-After</c> answers a request while the control plane that holds the platform roles is unavailable.
/// </summary>
// See ADR-1520.
internal sealed class TenantImportAuthorizationGate(IAuthorizationService authorizationService)
    : IEndpointGate
{
    private IAuthorizationService AuthorizationService { get; } = authorizationService;

    /// <inheritdoc />
    public async ValueTask<IResult?> InvokeAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var platformNode = BaseApiEndpointHandler.GetPlatformNode(httpContext);

        try
        {
            var authorization = await AuthorizationService.AuthorizeAsync(
                httpContext.User,
                platformNode,
                Operations.Update
            );

            if (authorization.Succeeded)
            {
                return null;
            }
        }
        catch (DirectoryUnavailableException)
        {
            return ControlPlaneUnavailableProblem.Create(httpContext);
        }

        return TypedResults.Problem(
            detail: "The caller is not authorized for this operation.",
            statusCode: StatusCodes.Status403Forbidden
        );
    }
}
