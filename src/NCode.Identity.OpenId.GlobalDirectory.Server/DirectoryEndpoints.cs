// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NCode.Identity.AspNetCore;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Maps the global directory API under <c>/directory/v1/</c>: the tenant, team, platform and cell routes that the
/// remote adapters of <c>NCode.Identity.OpenId.GlobalDirectory</c> call. Every route runs behind
/// <see cref="DirectoryAuthenticationEndpointFilter"/>, its bodies are confidential so HTTP logging never writes
/// them, and its failures are RFC 9457 problem documents.
/// </summary>
// See ADR-1411.
[PublicAPI]
public static class DirectoryEndpoints
{
    private const string RoutePrefix = "directory/v1";
    private const string CellClientIdPrefix = "cell-";
    private const int DefaultChangesLimit = 100;
    private const int MaxChangesLimit = 1000;

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
        group.MapPost("reserve", ReserveTenantAsync).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("place", PlaceTenantAsync);
        group.MapPost("{tenantId}/release", ReleaseTenantAsync);
        group.MapPost("retire", RetireTenantAsync);
        group
            .MapPost("change-issuer", ChangeTenantIssuerAsync)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("{tenantId}", GetTenantPlacementAsync);
        group.MapGet("retired-ids/{tenantId}", GetRetiredTenantIdAsync);
        group.MapGet("retired-issuers", GetRetiredIssuerAsync);
    }

    private static void MapTeams(RouteGroupBuilder group)
    {
        group
            .MapPost("{teamId}/reserve", ReserveTeamAsync)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPut("{teamId}/document", PublishTeamAsync);
        group.MapPost("{teamId}/release", ReleaseTeamAsync);
        group.MapPost("{teamId}/retire", RetireTeamAsync);
        group.MapGet("{teamId}/retired", GetRetiredTeamIdAsync);
        group.MapGet("{teamId}/document", GetTeamDocumentAsync);
        group.MapGet("changes", ListChangesAsync);
    }

    private static void MapPlatform(RouteGroupBuilder group)
    {
        group.MapPut("document", PublishPlatformAsync);
        group.MapGet("document", GetPlatformDocumentAsync);
    }

    private static void MapCells(RouteGroupBuilder group)
    {
        group.MapPut("{name}", RegisterCellAsync).ProducesProblem(StatusCodes.Status403Forbidden);
        group.MapGet("", ListCellsAsync);
        group.MapGet("{name}", GetCellAsync);
    }

    #region Tenants

    private static async ValueTask<IResult> ReserveTenantAsync(
        TenantReservation reservation,
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

        var result = await directory.ReserveAsync(reservation, cancellationToken);
        return ToResult(result);
    }

    private static async ValueTask<IResult> PlaceTenantAsync(
        TenantPlacement placement,
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

        await directory.PlaceAsync(placement, cancellationToken);
        return Results.Ok();
    }

    private static async ValueTask<IResult> ReleaseTenantAsync(
        string tenantId,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        await directory.ReleaseAsync(tenantId, cancellationToken);
        return Results.Ok();
    }

    private static async ValueTask<IResult> RetireTenantAsync(
        TenantRetirement retirement,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(retirement.TenantId))
            return BadRequest("The tenant is required.");

        await directory.RetireAsync(retirement, cancellationToken);
        return Results.Ok();
    }

    private static async ValueTask<IResult> ChangeTenantIssuerAsync(
        TenantIssuerChange change,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(change.TenantId))
            return BadRequest("The tenant is required.");

        var result = await directory.ChangeIssuerAsync(change, cancellationToken);
        return ToResult(result);
    }

    private static async ValueTask<IResult> GetTenantPlacementAsync(
        string tenantId,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        var placement = await directory.GetPlacementOrDefaultAsync(tenantId, cancellationToken);
        return placement is null ? Results.NotFound() : Results.Ok(placement);
    }

    private static async ValueTask<IResult> GetRetiredTenantIdAsync(
        string tenantId,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        var isRetired = await directory.IsTenantIdRetiredAsync(tenantId, cancellationToken);
        return isRetired ? Results.Ok() : Results.NotFound();
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
        return holder is null ? Results.NotFound() : Results.Ok(holder);
    }

    #endregion

    #region Teams

    private static async ValueTask<IResult> ReserveTeamAsync(
        string teamId,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        var result = await directory.ReserveAsync(teamId, cancellationToken);
        return ToResult(result);
    }

    private static async ValueTask<IResult> PublishTeamAsync(
        string teamId,
        TeamDocument document,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(document.TeamId) || IsBlank(document.DisplayName))
            return BadRequest("The team and its display name are required.");

        if (!string.Equals(teamId, document.TeamId, StringComparison.OrdinalIgnoreCase))
            return BadRequest("The team of the document is not the team of the route.");

        await directory.PublishAsync(document, cancellationToken);
        return Results.Ok();
    }

    private static async ValueTask<IResult> ReleaseTeamAsync(
        string teamId,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        await directory.ReleaseAsync(teamId, cancellationToken);
        return Results.Ok();
    }

    private static async ValueTask<IResult> RetireTeamAsync(
        string teamId,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
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
        return isRetired ? Results.Ok() : Results.NotFound();
    }

    private static async ValueTask<IResult> GetTeamDocumentAsync(
        string teamId,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        var document = await directory.GetOrDefaultAsync(teamId, cancellationToken);
        return document is null ? Results.NotFound() : Results.Ok(document);
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
        IPlatformDirectory directory,
        CancellationToken cancellationToken
    )
    {
        await directory.PublishAsync(document, cancellationToken);
        return Results.Ok();
    }

    private static async ValueTask<IResult> GetPlatformDocumentAsync(
        IPlatformDirectory directory,
        CancellationToken cancellationToken
    )
    {
        var document = await directory.GetOrDefaultAsync(cancellationToken);
        return document is null ? Results.NotFound() : Results.Ok(document);
    }

    #endregion

    #region Cells

    private static async ValueTask<IResult> RegisterCellAsync(
        string name,
        CellDescriptor descriptor,
        HttpContext httpContext,
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
        var clientId = httpContext
            .User.FindFirst(DirectoryAuthenticationEndpointFilter.ClientIdClaimType)
            ?.Value;
        if (!string.Equals(clientId, CellClientIdPrefix + name, StringComparison.Ordinal))
        {
            return Results.Problem(
                title: "Forbidden",
                detail: "A cell registers only itself.",
                statusCode: StatusCodes.Status403Forbidden
            );
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
        return cell is null ? Results.NotFound() : Results.Ok(cell);
    }

    #endregion

    private static bool IsBlank([NotNullWhen(false)] string? value) =>
        string.IsNullOrWhiteSpace(value);

    private static IResult BadRequest(string detail) =>
        Results.Problem(
            title: "Bad Request",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest
        );

    private static IResult ToResult(ReservationResult result) =>
        result.IsReserved
            ? Results.Ok()
            : Results.Problem(
                title: "Conflict",
                detail: result.ConflictDetail,
                statusCode: StatusCodes.Status409Conflict
            );
}
