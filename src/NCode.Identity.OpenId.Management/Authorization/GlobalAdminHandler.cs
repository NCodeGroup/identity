// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Management.ControlPlane;

namespace NCode.Identity.OpenId.Management.Authorization;

/// <summary>
/// An <see cref="AuthorizationHandler{TRequirement}"/> that automatically succeeds any
/// <see cref="IAuthorizationRequirement"/> when <see cref="IControlPlaneAuthority"/> answers that the caller, named by
/// the <c>sub</c> and <c>client_id</c> claims of the user, holds the <see cref="BuiltInRoles.GlobalAdmin"/> role at the
/// platform node. This handler provides a global bypass for administrators across all authorization requirements. A
/// caller without either claim is not an administrator.
/// </summary>
// See ADR-1044 and ADR-1417.
internal sealed class GlobalAdminHandler(
    IControlPlaneAuthority controlPlaneAuthority,
    IHttpContextAccessor httpContextAccessor
) : AuthorizationHandler<IAuthorizationRequirement>
{
    private IControlPlaneAuthority ControlPlaneAuthority { get; } = controlPlaneAuthority;

    // ASP.NET Core authorization handlers are invoked by the framework without the request's cancellation token in
    // hand, so the token is read from the request being authorized.
    private IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        IAuthorizationRequirement requirement
    )
    {
        var subject = context.User.FindFirstValue(JoseClaimNames.Payload.Sub);
        var clientId = context.User.FindFirstValue(JoseClaimNames.Payload.ClientId);
        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(clientId))
        {
            return;
        }

        var authority = await ControlPlaneAuthority.GetAsync(
            new ControlPlaneAuthorityRequest { Subject = subject, ClientId = clientId },
            HttpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None
        );

        if (authority.IsGlobalAdmin)
        {
            context.Succeed(requirement);
        }
    }
}
