// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/*

POST control-plane/v1/authority

*/

/// <summary>
/// Provides the endpoint through which the control-plane cell tells the other cells which platform and team roles a
/// caller holds. It maps nothing unless the cell is the control plane. The route sits beside the management
/// <c>/api</c> tree, never under it, because the filters of that tree treat a request as a management caller; it is
/// authenticated by <see cref="ControlPlaneAuthenticationEndpointFilter"/> and its bodies are never logged.
/// </summary>
// See ADR-1411 and ADR-1466.
internal sealed class ControlPlaneAuthorityEndpointProvider : IEndpointProvider
{
    /// <summary>
    /// The path of the authority endpoint.
    /// </summary>
    internal const string AuthorityPath = "/control-plane/v1/authority";

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<CellOptions>>();
        if (!options.Value.IsControlPlane)
            return;

        endpoints
            .MapPost(AuthorityPath, GetAuthorityAsync)
            .AddEndpointFilter<ControlPlaneAuthenticationEndpointFilter>()
            .WithConfidentialBodies()
            .ExcludeFromDescription();
    }

    /// <summary>
    /// Gets the platform and team roles that the caller of the request holds.
    /// </summary>
    /// <param name="request">The <see cref="ControlPlaneAuthorityRequest"/> that names the caller.</param>
    /// <param name="controlPlaneAuthority">The <see cref="IControlPlaneAuthority"/> that answers it.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> of the request.</param>
    /// <returns>The <see cref="ControlPlaneAuthority"/> of the caller.</returns>
    internal static async ValueTask<Ok<ControlPlaneAuthority>> GetAuthorityAsync(
        ControlPlaneAuthorityRequest request,
        IControlPlaneAuthority controlPlaneAuthority,
        CancellationToken cancellationToken
    )
    {
        var authority = await controlPlaneAuthority.GetAsync(request, cancellationToken);
        return TypedResults.Ok(authority);
    }
}
