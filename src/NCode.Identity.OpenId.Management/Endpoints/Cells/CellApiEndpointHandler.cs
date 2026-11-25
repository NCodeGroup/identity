// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Identity.AspNetCore;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Contracts.Cells;
using NCode.Identity.Registration.AspNetCore;
using NCode.Identity.Secrets.Persistence;

namespace NCode.Identity.OpenId.Management.Endpoints.Cells;

/*

GET    api/cells (control-plane cell only)
GET    api/cells/{name}
PUT    api/cells/{name}
DELETE api/cells/{name}

*/

/// <summary>
/// Provides the API endpoints that list, read, register and retire the cells of the platform. The directory owns the
/// registry, so the routes are mapped only on the control-plane cell. The list answers any caller that the management
/// caller gate authenticated from a management access token, which it reads from
/// <see cref="IManagementCallerFeature"/>; reading, registering and retiring a cell are authorized at the platform
/// node. No route returns a cell's management URL.
/// </summary>
internal class CellApiEndpointHandler(
    ICellDirectory cellDirectory,
    IAuthorizationService authorizationService,
    ICryptoService cryptoService,
    IPublicJwksReader publicJwksReader,
    IManagementAuditRecorder managementAuditRecorder,
    IOptions<CellOptions> cellOptions
) : BaseApiEndpointHandler, IEndpointProvider
{
    private ICellDirectory CellDirectory { get; } = cellDirectory;
    private IPublicJwksReader PublicJwksReader { get; } = publicJwksReader;
    private IManagementAuditRecorder ManagementAuditRecorder { get; } = managementAuditRecorder;
    private CellOptions CellOptions { get; } = cellOptions.Value;

    /// <inheritdoc />
    protected override IAuthorizationService AuthorizationService { get; } = authorizationService;

    /// <inheritdoc />
    protected override ICryptoService CryptoService { get; } = cryptoService;

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
        endpoints
            .MapGet("/cells/{name}", GetCellAsync)
            .WithTags(OpenIdConstants.EndpointTags.Platform)
            .AddEndpointFilter<ControlPlaneScopeEndpointFilter>()
            .Produces<CellResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        endpoints
            .MapPut("/cells/{name}", RegisterCellAsync)
            .WithTags(OpenIdConstants.EndpointTags.Platform)
            .AddEndpointFilter<ControlPlaneScopeEndpointFilter>()
            .Produces<CellResource>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        endpoints
            .MapDelete("/cells/{name}", RetireCellAsync)
            .WithTags(OpenIdConstants.EndpointTags.Platform)
            .AddEndpointFilter<ControlPlaneScopeEndpointFilter>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
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
            IsRegistered = descriptor.IsRegistered,
            IsRetired = descriptor.IsRetired,
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
            return ManagementProblems.AuthenticationRequired(httpContext);

        try
        {
            var descriptors = await CellDirectory.ListAsync(cancellationToken);
            var cells = descriptors.Select(ToCellResource).ToList();
            return TypedResults.Json(cells);
        }
        catch (DirectoryUnavailableException)
        {
            return DirectoryUnavailableProblem(httpContext);
        }
    }

    /// <summary>
    /// Gets a cell of the platform, whether it is retired or not.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="name">The name of the cell.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The cell.</response>
    /// <response code="404">No cell has the name.</response>
    /// <response code="503">The global directory cannot be reached; the response carries Retry-After.</response>
    [EndpointName("api/cells/get")]
    internal virtual async ValueTask<IResult> GetCellAsync(
        HttpContext httpContext,
        [FromRoute] string name,
        CancellationToken cancellationToken
    )
    {
        var platformNode = GetPlatformNode(httpContext);
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            platformNode,
            Operations.Read,
            cancellationToken
        );
        if (authorizationFailure is not null)
            return authorizationFailure;

        try
        {
            var descriptor = await CellDirectory.GetOrDefaultAsync(name, cancellationToken);
            if (descriptor is null)
                return ManagementProblems.ResourceNotFound();

            var resource = ToCellResource(descriptor);
            return TypedResults.Json(resource);
        }
        catch (DirectoryUnavailableException)
        {
            return DirectoryUnavailableProblem(httpContext);
        }
    }

    /// <summary>
    /// Registers a cell with the global directory, so the cell can authenticate to the directory's token endpoint with
    /// the keys of the request's JSON Web Key Set.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="name">The name of the cell.</param>
    /// <param name="request">The region, kind and public JSON Web Key Set of the cell.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The registered cell.</response>
    /// <response code="400">The region or kind is blank, or the key set is not a valid public JSON Web Key Set.</response>
    /// <response code="409">The directory refuses the registration; the detail names the reason.</response>
    /// <response code="415">The request body is not JSON.</response>
    /// <response code="503">The global directory cannot be reached; the response carries Retry-After.</response>
    [EndpointName("api/cells/register")]
    internal virtual async ValueTask<IResult> RegisterCellAsync(
        HttpContext httpContext,
        [FromRoute] string name,
        [FromBody] RegisterCellRequest request,
        CancellationToken cancellationToken
    )
    {
        var platformNode = GetPlatformNode(httpContext);
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            platformNode,
            Operations.Update,
            cancellationToken
        );
        if (authorizationFailure is not null)
            return authorizationFailure;

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(request.Region))
            errors["region"] = ["The region is required."];

        if (string.IsNullOrWhiteSpace(request.Kind))
            errors["kind"] = ["The kind is required."];
        else if (!CellKindRules.IsValidKind(request.Kind))
            errors["kind"] = [$"The kind must be {CellKindRules.Description}."];

        if (!PublicJwksReader.TryRead(request.Jwks, out _, out var jwksError))
            errors["jwks"] = [jwksError];

        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var registration = new CellRegistration
        {
            Name = name,
            Region = request.Region,
            Kind = request.Kind,
            Jwks = request.Jwks,
        };

        try
        {
            await CellDirectory.RegisterAsync(registration, cancellationToken);

            // A registration the directory accepts stores exactly these values: it refuses the control-plane cell,
            // a retired cell and another region or kind.
            var resource = new CellResource
            {
                Name = name,
                Region = request.Region,
                Kind = request.Kind,
                IsControlPlane = false,
                IsRegistered = true,
                IsRetired = false,
            };
            var resourceValues = ToResourceValues(resource);
            await ManagementAuditRecorder.RecordPlatformChangedAsync(
                httpContext,
                ResourceChangeTypes.Updated,
                resourceValues
            );

            return TypedResults.Json(resource);
        }
        catch (CellDirectoryConflictException exception)
        {
            return ConflictProblem(exception);
        }
        catch (DirectoryUnavailableException)
        {
            return DirectoryUnavailableProblem(httpContext);
        }
    }

    /// <summary>
    /// Retires a cell, so its name is never reissued.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="name">The name of the cell.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="204">The cell is retired.</response>
    /// <response code="409">The cell still holds tenants or is the control-plane cell; the detail names the reason.</response>
    /// <response code="503">The global directory cannot be reached; the response carries Retry-After.</response>
    [EndpointName("api/cells/retire")]
    internal virtual async ValueTask<IResult> RetireCellAsync(
        HttpContext httpContext,
        [FromRoute] string name,
        CancellationToken cancellationToken
    )
    {
        var platformNode = GetPlatformNode(httpContext);
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            platformNode,
            Operations.Delete,
            cancellationToken
        );
        if (authorizationFailure is not null)
            return authorizationFailure;

        try
        {
            await CellDirectory.RetireAsync(name, cancellationToken);
            var retired = new { name };
            var resourceValues = ToResourceValues(retired);
            await ManagementAuditRecorder.RecordPlatformChangedAsync(
                httpContext,
                ResourceChangeTypes.Deleted,
                resourceValues
            );

            return TypedResults.NoContent();
        }
        catch (CellDirectoryConflictException exception)
        {
            return ConflictProblem(exception);
        }
        catch (DirectoryUnavailableException)
        {
            return DirectoryUnavailableProblem(httpContext);
        }
    }

    /// <summary>
    /// Answers <c>409 Conflict</c> with the directory's detail.
    /// </summary>
    /// <param name="exception">The <see cref="CellDirectoryConflictException"/> that the directory raised.</param>
    /// <returns>The <see cref="ProblemHttpResult"/> that names the reason.</returns>
    private static ProblemHttpResult ConflictProblem(CellDirectoryConflictException exception) =>
        TypedResults.Problem(detail: exception.Message, statusCode: StatusCodes.Status409Conflict);

    /// <summary>
    /// Answers <c>503 Service Unavailable</c> with a <c>Retry-After</c> for a request that needs the directory while it
    /// cannot be reached.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <returns>The <see cref="ProblemHttpResult"/> that asks the caller to retry.</returns>
    private static ProblemHttpResult DirectoryUnavailableProblem(HttpContext httpContext)
    {
        var retryAfter = TimeSpan.FromSeconds(GlobalDirectoryProtocol.UnavailableRetryAfterSeconds);
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Detail = "The global directory is unavailable. Retry the request.",
        };
        return RetryAfterProblem.Create(httpContext, retryAfter, problemDetails);
    }
}
