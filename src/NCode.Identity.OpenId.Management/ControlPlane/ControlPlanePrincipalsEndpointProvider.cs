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
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/*

POST control-plane/v1/principals

*/

/// <summary>
/// Provides the endpoint through which the control-plane cell describes its principals to the other cells. It maps
/// nothing unless the cell is the control plane. The route sits beside the management <c>/api</c> tree, never under
/// it, is authenticated by <see cref="ControlPlaneAuthenticationGate"/> before the route binds its body, and its
/// bodies are never logged.
/// </summary>
// See ADR-1411 and ADR-1465.
internal sealed class ControlPlanePrincipalsEndpointProvider : IEndpointProvider
{
    /// <summary>
    /// The path of the principals endpoint.
    /// </summary>
    internal const string PrincipalsPath = "/control-plane/v1/principals";

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<CellOptions>>();
        if (!options.Value.IsControlPlane)
            return;

        endpoints
            .MapPost(PrincipalsPath, FindAsync)
            .AddEndpointGate<RouteHandlerBuilder, ControlPlaneAuthenticationGate>()
            .WithConfidentialBodies()
            .ExcludeFromDescription();
    }

    /// <summary>
    /// Finds the summaries of the principals that the query names.
    /// </summary>
    /// <param name="query">The <see cref="ControlPlanePrincipalQuery"/> that names the principals.</param>
    /// <param name="directory">The <see cref="IControlPlanePrincipalDirectory"/> that answers it.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> of the request.</param>
    /// <returns>The summaries, or a problem when the query names too many.</returns>
    internal static async ValueTask<
        Results<ProblemHttpResult, Ok<CollectionResource<PrincipalSummaryResource>>>
    > FindAsync(
        ControlPlanePrincipalQuery query,
        IControlPlanePrincipalDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (query.PrincipalIds.Count + query.UserNames.Count > ControlPlanePrincipalQuery.MaxNames)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: "A query names at most 100 principals and user names."
            );
        }

        var summaries = await directory.FindAsync(query, cancellationToken);
        var resource = new CollectionResource<PrincipalSummaryResource> { Items = summaries };
        return TypedResults.Ok(resource);
    }
}
