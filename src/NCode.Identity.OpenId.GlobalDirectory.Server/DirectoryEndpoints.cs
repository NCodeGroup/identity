// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.GlobalDirectory.Server.Stores;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Maps the global directory API under <c>/directory/v1/</c>: the tenant, team, platform and cell routes that the
/// remote adapters of <c>NCode.Identity.OpenId.GlobalDirectory</c> call. Every route requires a bearer access token
/// for the audience <see cref="GlobalDirectoryProtocol.DirectoryAudience"/> with the scope
/// <see cref="GlobalDirectoryProtocol.DirectoryScope"/>, checked before the route runs, its bodies are confidential so
/// HTTP logging never writes them, and its failures are RFC 9457 problem documents. Every write is bound to the
/// calling cell: the platform and team writes need the control-plane cell
/// (<see cref="DirectoryServerOptions.ControlPlaneCell"/>), and a tenant write needs the cell that holds the tenant.
/// </summary>
// See ADR-1411. DirectoryAuthenticationEndpointFilter checks the token.
[PublicAPI]
public static class DirectoryEndpoints
{
    private const string RoutePrefix = "directory/v1";

    private const int DefaultChangesLimit = 100;
    private const int MaxChangesLimit = 1000;

    private const string TenantWriteForbiddenDetail =
        "Only the cell that holds a tenant, or the control-plane cell, writes it.";

    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to map the routes on.</param>
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the global directory API under <c>/directory/v1/</c>. The routes resolve
        /// <see cref="ITenantDirectory"/>, <see cref="ITeamDirectory"/>, <see cref="IPlatformDirectory"/> and
        /// <see cref="ICellDirectory"/> from the request services.
        /// </summary>
        /// <returns>The <see cref="RouteGroupBuilder"/> of the directory API.</returns>
        public RouteGroupBuilder MapDirectory()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(RoutePrefix)
                .AddEndpointFilter<DirectoryAuthenticationEndpointFilter>()
                .WithConfidentialBodies()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

            MapTenants(group.MapGroup("tenants"));
            MapTeams(group.MapGroup("teams"));
            MapPlatform(group.MapGroup("platform"));
            MapCells(group.MapGroup("cells"));

            return group;
        }
    }

    private static void MapTenants(RouteGroupBuilder group)
    {
        group
            .MapPost("reserve", ReserveTenantAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group
            .MapPost("place", PlaceTenantAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group
            .MapPost("{tenantId}/release", ReleaseTenantAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        group
            .MapPost("{tenantId}/move", MoveTenantAsync)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group
            .MapPost("retire", RetireTenantAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group
            .MapPost("change-issuer", ChangeTenantIssuerAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group
            .MapGet("{tenantId}", GetTenantPlacementAsync)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group
            .MapGet("retired-ids/{tenantId}", GetRetiredTenantIdAsync)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group
            .MapGet("retired-issuers", GetRetiredIssuerAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapTeams(RouteGroupBuilder group)
    {
        group
            .MapPost("{teamId}/reserve", ReserveTeamAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group
            .MapPut("{teamId}/document", PublishTeamAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group
            .MapPost("{teamId}/release", ReleaseTeamAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        group
            .MapPost("{teamId}/retire", RetireTeamAsync)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        group
            .MapGet("{teamId}/retired", GetRetiredTeamIdAsync)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group
            .MapGet("{teamId}/document", GetTeamDocumentAsync)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("changes", ListChangesAsync).ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static void MapPlatform(RouteGroupBuilder group)
    {
        group
            .MapPut("document", PublishPlatformAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group
            .MapGet("document", GetPlatformDocumentAsync)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapCells(RouteGroupBuilder group)
    {
        group
            .MapPut("{name}", RegisterCellAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group.MapGet("", ListCellsAsync);
        group.MapGet("{name}", GetCellAsync).ProducesProblem(StatusCodes.Status404NotFound);
    }

    #region Tenants

    private static async ValueTask<IResult> ReserveTenantAsync(
        TenantReservation reservation,
        HttpContext httpContext,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (
            IsBlank(reservation.TenantId)
            || IsBlank(reservation.Issuer)
            || IsBlank(reservation.Cell)
        )
        {
            return BadRequest("The tenant, its issuer and its cell are required.");
        }

        // A cell reserves a tenant only for itself.
        if (!IsCaller(httpContext, reservation.Cell))
            return Forbidden("A cell reserves a tenant only for itself.");

        var result = await directory.ReserveAsync(reservation, cancellationToken);
        return ToResult(result);
    }

    private static async ValueTask<IResult> PlaceTenantAsync(
        TenantPlacement placement,
        HttpContext httpContext,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (
            IsBlank(placement.TenantId)
            || IsBlank(placement.Issuer)
            || IsBlank(placement.Cell)
            || IsBlank(placement.Region)
        )
        {
            return BadRequest("The tenant, its issuer, its cell and its region are required.");
        }

        // A cell places a tenant only in itself.
        if (!IsCaller(httpContext, placement.Cell))
            return Forbidden("A cell places a tenant only in itself.");

        var result = await directory.PlaceAsync(placement, cancellationToken);
        return ToResult(result);
    }

    private static async ValueTask<IResult> ReleaseTenantAsync(
        string tenantId,
        HttpContext httpContext,
        IDirectoryTenantWriter writer,
        CancellationToken cancellationToken
    )
    {
        if (DirectoryCaller.GetCell(httpContext.User) is not { } callerCell)
            return Forbidden(TenantWriteForbiddenDetail);

        var result = await writer.ReleaseAsync(tenantId, callerCell, cancellationToken);
        return ToResult(result);
    }

    private static async ValueTask<IResult> MoveTenantAsync(
        string tenantId,
        string? fromCell,
        string? toCell,
        HttpContext httpContext,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(fromCell) || IsBlank(toCell))
            return UnprocessableEntity("The source cell and the target cell are required.");

        // The target cell asks for the move: the machine client that calls is `cell-{toCell}`.
        if (!IsCaller(httpContext, toCell))
            return UnprocessableEntity("Only the target cell moves a tenant to itself.");

        var result = await directory.MoveAsync(tenantId, fromCell, toCell, cancellationToken);
        return result.IsMoved
            ? Results.Ok()
            : Results.Problem(
                title: "Conflict",
                detail: result.ConflictDetail,
                statusCode: StatusCodes.Status409Conflict
            );
    }

    private static async ValueTask<IResult> RetireTenantAsync(
        TenantRetirement retirement,
        HttpContext httpContext,
        IDirectoryTenantWriter writer,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(retirement.TenantId))
            return BadRequest("The tenant is required.");

        if (DirectoryCaller.GetCell(httpContext.User) is not { } callerCell)
            return Forbidden(TenantWriteForbiddenDetail);

        var result = await writer.RetireAsync(retirement, callerCell, cancellationToken);
        return ToResult(result);
    }

    private static async ValueTask<IResult> ChangeTenantIssuerAsync(
        TenantIssuerChange change,
        HttpContext httpContext,
        IDirectoryTenantWriter writer,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(change.TenantId))
            return BadRequest("The tenant is required.");

        if (DirectoryCaller.GetCell(httpContext.User) is not { } callerCell)
            return Forbidden(TenantWriteForbiddenDetail);

        var result = await writer.ChangeIssuerAsync(change, callerCell, cancellationToken);
        return ToResult(result);
    }

    private static async ValueTask<IResult> GetTenantPlacementAsync(
        string tenantId,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        var placement = await directory.GetPlacementOrDefaultAsync(tenantId, cancellationToken);
        return placement is null
            ? NotFound($"The directory holds no placement for the tenant '{tenantId}'.")
            : Results.Ok(placement);
    }

    private static async ValueTask<IResult> GetRetiredTenantIdAsync(
        string tenantId,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        var isRetired = await directory.IsTenantIdRetiredAsync(tenantId, cancellationToken);
        return isRetired
            ? Results.Ok()
            : NotFound($"The directory holds no retired tenant '{tenantId}'.");
    }

    private static async ValueTask<IResult> GetRetiredIssuerAsync(
        string? issuer,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(issuer))
            return BadRequest("The issuer is required.");

        var holder = await directory.GetRetiredIssuerHolderOrDefaultAsync(
            issuer,
            cancellationToken
        );
        return holder is null
            ? NotFound($"The directory holds no retired issuer '{issuer}'.")
            : Results.Ok(holder);
    }

    #endregion

    #region Teams

    private static async ValueTask<IResult> ReserveTeamAsync(
        string teamId,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (ForbidUnlessControlPlane(httpContext, options.Value) is { } forbidden)
            return forbidden;

        var result = await directory.ReserveAsync(teamId, cancellationToken);
        return ToResult(result);
    }

    private static async ValueTask<IResult> PublishTeamAsync(
        string teamId,
        TeamDocument document,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (ForbidUnlessControlPlane(httpContext, options.Value) is { } forbidden)
            return forbidden;

        if (IsBlank(document.TeamId) || IsBlank(document.DisplayName))
            return BadRequest("The team and its display name are required.");

        if (!string.Equals(teamId, document.TeamId, StringComparison.OrdinalIgnoreCase))
            return BadRequest("The team of the document is not the team of the route.");

        await directory.PublishAsync(document, cancellationToken);
        return Results.Ok();
    }

    private static async ValueTask<IResult> ReleaseTeamAsync(
        string teamId,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (ForbidUnlessControlPlane(httpContext, options.Value) is { } forbidden)
            return forbidden;

        await directory.ReleaseAsync(teamId, cancellationToken);
        return Results.Ok();
    }

    private static async ValueTask<IResult> RetireTeamAsync(
        string teamId,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (ForbidUnlessControlPlane(httpContext, options.Value) is { } forbidden)
            return forbidden;

        await directory.RetireAsync(teamId, cancellationToken);
        return Results.Ok();
    }

    private static async ValueTask<IResult> GetRetiredTeamIdAsync(
        string teamId,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        var isRetired = await directory.IsTeamIdRetiredAsync(teamId, cancellationToken);
        return isRetired
            ? Results.Ok()
            : NotFound($"The directory holds no retired team '{teamId}'.");
    }

    private static async ValueTask<IResult> GetTeamDocumentAsync(
        string teamId,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        var document = await directory.GetOrDefaultAsync(teamId, cancellationToken);
        return document is null
            ? NotFound($"The directory holds no document for the team '{teamId}'.")
            : Results.Ok(document);
    }

    private static async ValueTask<IResult> ListChangesAsync(
        ITeamDirectory directory,
        CancellationToken cancellationToken,
        long after = 0,
        int limit = DefaultChangesLimit
    )
    {
        var boundedAfter = Math.Max(after, 0);
        var boundedLimit = Math.Clamp(limit, 1, MaxChangesLimit);

        var changes = await directory.ListChangesAsync(
            boundedAfter,
            boundedLimit,
            cancellationToken
        );
        return Results.Ok(changes);
    }

    #endregion

    #region Platform

    private static async ValueTask<IResult> PublishPlatformAsync(
        PlatformDocument document,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        IPlatformDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (ForbidUnlessControlPlane(httpContext, options.Value) is { } forbidden)
            return forbidden;

        await directory.PublishAsync(document, cancellationToken);
        return Results.Ok();
    }

    private static async ValueTask<IResult> GetPlatformDocumentAsync(
        IPlatformDirectory directory,
        CancellationToken cancellationToken
    )
    {
        var document = await directory.GetOrDefaultAsync(cancellationToken);
        return document is null
            ? NotFound("The directory holds no platform document.")
            : Results.Ok(document);
    }

    #endregion

    #region Cells

    private static async ValueTask<IResult> RegisterCellAsync(
        string name,
        CellDescriptor descriptor,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        ICellDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (
            IsBlank(descriptor.Name)
            || IsBlank(descriptor.Region)
            || IsBlank(descriptor.Kind)
            || IsBlank(descriptor.ManagementUrl)
        )
        {
            return BadRequest(
                "The cell, its region, its kind and its management URL are required."
            );
        }

        if (!string.Equals(name, descriptor.Name, StringComparison.Ordinal))
            return BadRequest("The cell of the descriptor is not the cell of the route.");

        // A cell registers only itself: the machine client that calls is `cell-{name}`.
        if (!IsCaller(httpContext, name))
            return Forbidden("A cell registers only itself.");

        // Only the control-plane cell registers itself as the control plane.
        if (
            descriptor.IsControlPlane
            && !string.Equals(name, options.Value.ControlPlaneCell, StringComparison.Ordinal)
        )
        {
            return Forbidden("Only the control-plane cell registers itself as the control plane.");
        }

        await directory.RegisterAsync(descriptor, cancellationToken);
        return Results.Ok();
    }

    private static async ValueTask<IResult> ListCellsAsync(
        ICellDirectory directory,
        CancellationToken cancellationToken
    )
    {
        var cells = await directory.ListAsync(cancellationToken);
        return Results.Ok(cells);
    }

    private static async ValueTask<IResult> GetCellAsync(
        string name,
        ICellDirectory directory,
        CancellationToken cancellationToken
    )
    {
        var cell = await directory.GetOrDefaultAsync(name, cancellationToken);
        return cell is null ? NotFound($"The directory holds no cell '{name}'.") : Results.Ok(cell);
    }

    #endregion

    private static bool IsBlank([NotNullWhen(false)] string? value) =>
        string.IsNullOrWhiteSpace(value);

    private static bool IsCaller(HttpContext httpContext, string cell) =>
        string.Equals(DirectoryCaller.GetCell(httpContext.User), cell, StringComparison.Ordinal);

    private static IResult? ForbidUnlessControlPlane(
        HttpContext httpContext,
        DirectoryServerOptions options
    ) =>
        DirectoryCaller.IsControlPlane(httpContext.User, options)
            ? null
            : Forbidden("Only the control-plane cell writes the platform and the teams.");

    private static IResult Forbidden(string detail) =>
        Results.Problem(
            title: "Forbidden",
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden
        );

    private static IResult NotFound(string detail) =>
        Results.Problem(
            title: "Not Found",
            detail: detail,
            statusCode: StatusCodes.Status404NotFound
        );

    private static IResult BadRequest(string detail) =>
        Results.Problem(
            title: "Bad Request",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest
        );

    private static IResult UnprocessableEntity(string detail) =>
        Results.Problem(
            title: "Unprocessable Entity",
            detail: detail,
            statusCode: StatusCodes.Status422UnprocessableEntity
        );

    private static IResult ToResult(ReservationResult result) =>
        result.IsReserved
            ? Results.Ok()
            : Results.Problem(
                title: "Conflict",
                detail: result.ConflictDetail,
                statusCode: StatusCodes.Status409Conflict
            );

    private static IResult ToResult(DirectoryTenantWriteResult result) =>
        result.Outcome switch
        {
            DirectoryTenantWriteOutcome.Done => Results.Ok(),
            DirectoryTenantWriteOutcome.Forbidden => Forbidden(
                result.Detail ?? TenantWriteForbiddenDetail
            ),
            DirectoryTenantWriteOutcome.NotFound => Results.Problem(
                title: "Not Found",
                detail: result.Detail,
                statusCode: StatusCodes.Status404NotFound
            ),
            _ => Results.Problem(
                title: "Conflict",
                detail: result.Detail,
                statusCode: StatusCodes.Status409Conflict
            ),
        };
}
