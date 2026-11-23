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
/// nothing unless the cell is the control plane. The route sits beside the management <c>/api</c> tree, never under it,
/// is authenticated by <see cref="ControlPlaneAuthenticationGate"/> before the route binds its body, and its bodies are
/// never logged.
/// </summary>
// See ADR-1411 and ADR-1465.
internal sealed class ControlPlanePrincipalsEndpointProvider : IEndpointProvider
{
    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<CellOptions>>();
        if (!options.Value.IsControlPlane)
            return;

        endpoints
            .MapPost(GlobalDirectoryProtocol.ControlPlanePrincipalsPath, FindAsync)
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
    /// <returns>The summaries, or a <c>400</c> problem when a list is null, a name is null or blank, or the query names
    /// too many.</returns>
    internal static async ValueTask<
        Results<ProblemHttpResult, Ok<CollectionResource<PrincipalSummaryResource>>>
    > FindAsync(
        ControlPlanePrincipalQuery query,
        IControlPlanePrincipalDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (!AreNonBlankNames(query.PrincipalIds) || !AreNonBlankNames(query.UserNames))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: "A query's 'principalIds' and 'userNames' are lists of non-blank names."
            );
        }

        if (query.PrincipalIds.Count + query.UserNames.Count > ControlPlanePrincipalQuery.MaxNames)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"A query names at most {ControlPlanePrincipalQuery.MaxNames} principal identifiers and user names in all."
            );
        }

        var summaries = await directory.FindAsync(query, cancellationToken);
        var resource = new CollectionResource<PrincipalSummaryResource> { Items = summaries };
        return TypedResults.Ok(resource);
    }

    /// <summary>
    /// Determines whether the list exists and every name in it is neither <see langword="null"/> nor blank. The host's
    /// JSON binding does not enforce nullable annotations, so a body can carry either.
    /// </summary>
    private static bool AreNonBlankNames(IReadOnlyList<string>? names)
    {
        if (names is null)
            return false;

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
        }

        return true;
    }
}
