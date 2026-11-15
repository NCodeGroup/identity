// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Contracts.Cells;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Management.Endpoints.Cells;

/*

GET api/cells (control-plane cell only)

*/

/// <summary>
/// Provides the API endpoint that lists the cells of the platform, so a management client learns each cell's name,
/// region and kind. The directory owns the list, so the route is mapped only on the control-plane cell. It answers any
/// caller that the management authentication filter authenticated from a management access token, which it reads from
/// <see cref="IManagementCallerFeature"/>, and it never returns a cell's management URL.
/// </summary>
internal class CellApiEndpointHandler(
    ICellDirectory cellDirectory,
    IOptions<CellOptions> cellOptions
) : IEndpointProvider
{
    private ICellDirectory CellDirectory { get; } = cellDirectory;
    private CellOptions CellOptions { get; } = cellOptions.Value;

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        // The directory's cell list is served by the control-plane cell, so no other cell maps the route.
        if (!CellOptions.IsControlPlane)
        {
            return;
        }

        endpoints
            .MapGet("/cells", ListCellsAsync)
            .WithTags(OpenIdConstants.EndpointTags.Platform)
            .AddEndpointFilter<ControlPlaneScopeEndpointFilter>()
            .Produces<IReadOnlyList<CellResource>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// Maps a <see cref="CellDescriptor"/> to its <see cref="CellResource"/> representation, which leaves out the
    /// cell's management URL.
    /// </summary>
    /// <param name="descriptor">The <see cref="CellDescriptor"/> to map.</param>
    /// <returns>The mapped <see cref="CellResource"/>.</returns>
    internal virtual CellResource ToCellResource(CellDescriptor descriptor) =>
        new()
        {
            Name = descriptor.Name,
            Region = descriptor.Region,
            Kind = descriptor.Kind,
            IsControlPlane = descriptor.IsControlPlane,
        };

    /// <summary>
    /// Lists the cells of the platform.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The cells of the platform.</response>
    /// <response code="503">The global directory cannot be reached; the response carries Retry-After.</response>
    [EndpointName("api/cells/list")]
    internal virtual async ValueTask<IResult> ListCellsAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (httpContext.Features.Get<IManagementCallerFeature>() is null)
            return ManagementProblems.AuthenticationRequired();

        try
        {
            var descriptors = await CellDirectory.ListAsync(cancellationToken);
            return TypedResults.Json(descriptors.Select(ToCellResource).ToList());
        }
        catch (DirectoryUnavailableException)
        {
            return DirectoryUnavailableProblem(httpContext);
        }
    }

    /// <summary>
    /// Answers <c>503 Service Unavailable</c> with a <c>Retry-After</c> for a request that needs the directory while it
    /// cannot be reached.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <returns>The <see cref="ProblemHttpResult"/> that asks the caller to retry.</returns>
    private static ProblemHttpResult DirectoryUnavailableProblem(HttpContext httpContext) =>
        RetryAfterProblem.Create(
            httpContext,
            TimeSpan.FromSeconds(GlobalDirectoryProtocol.UnavailableRetryAfterSeconds),
            new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = "The global directory is unavailable. Retry the request.",
            }
        );
}
