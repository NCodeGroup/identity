// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
/// HTTP logging never writes them, and every failure, a lookup that finds nothing included, is an RFC 9457 problem
/// document: a missing resource is a <c>404 Not Found</c> problem, never an empty body. Every write is bound to the
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

    /// <summary>
    /// The problem detail that a team route answers with when the team identifier is blank.
    /// </summary>
    private const string TeamRequiredDetail = "The team is required.";

    /// <summary>
    /// The problem detail that a publish route answers with when the document's settings are not a JSON object.
    /// </summary>
    private const string SettingsNotAnObjectDetail = "The document's settings are a JSON object.";

    /// <summary>
    /// The problem detail that the cell registration route answers with when the management URL is not one that
    /// <see cref="AbsoluteUri.TryParseHttpsServiceUrl"/> accepts, or that <see cref="AbsoluteUri.TryParseAnyHttpServiceUrl"/>
    /// accepts in local development.
    /// </summary>
    private const string InvalidManagementUrlDetail =
        "The cell's management URL is an absolute https URL, or an http URL in local development, with no user "
        + "information, query or fragment.";

    private static string InvalidCellLabelDetail =>
        $"A cell and its region are each 1 to {CellLabelRules.MaxLabelLength} lowercase letters, digits or hyphens that "
        + "start with a letter and end with a letter or digit.";

    private static string TooLongDetail =>
        $"A tenant identifier is at most {DirectoryLimits.MaxIdentifierLength} characters, and an issuer at most "
        + $"{DirectoryLimits.MaxIssuerLength}.";

    /// <summary>
    /// The problem detail that a team route answers with when a team identifier is longer than the directory stores.
    /// </summary>
    private static string TeamTooLongDetail =>
        $"A team identifier is at most {DirectoryLimits.MaxIdentifierLength} characters.";

    /// <summary>
    /// Maps the tenant routes: reserving, placing, releasing, moving, retiring and changing the issuer of a tenant,
    /// and reading a placement, a retired identifier or a retired issuer. Each route declares the problem documents
    /// that it can answer.
    /// </summary>
    /// <param name="group">The <see cref="RouteGroupBuilder"/> of <c>/directory/v1/tenants</c>.</param>
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
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group
            .MapPost("{tenantId}/move", MoveTenantAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group
            .MapPost("{tenantId}/move-offers", OfferTenantMoveAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group
            .MapPost("retire", RetireTenantAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
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

    /// <summary>
    /// Maps the team routes: reserving, publishing, releasing and retiring a team, reading a team document or a
    /// retired identifier, and listing the change feed. Each route declares the problem documents that it can answer.
    /// </summary>
    /// <param name="group">The <see cref="RouteGroupBuilder"/> of <c>/directory/v1/teams</c>.</param>
    private static void MapTeams(RouteGroupBuilder group)
    {
        group
            .MapPost("{teamId}/reserve", ReserveTeamAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group
            .MapPut("{teamId}/document", PublishTeamAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group
            .MapPost("{teamId}/release", ReleaseTeamAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        group
            .MapPost("{teamId}/retire", RetireTeamAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        group
            .MapGet("{teamId}/retired", GetRetiredTeamIdAsync)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group
            .MapGet("{teamId}/document", GetTeamDocumentAsync)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("changes", ListChangesAsync).ProducesProblem(StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Maps the platform routes: publishing the platform document and reading it. Each route declares the problem
    /// documents that it can answer.
    /// </summary>
    /// <param name="group">The <see cref="RouteGroupBuilder"/> of <c>/directory/v1/platform</c>.</param>
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

    /// <summary>
    /// Maps the cell routes: registering a cell, listing the cells and reading one cell. Each route declares the
    /// problem documents that it can answer.
    /// </summary>
    /// <param name="group">The <see cref="RouteGroupBuilder"/> of <c>/directory/v1/cells</c>.</param>
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

    /// <summary>
    /// Reserves a tenant identifier and its issuer for the calling cell. A cell reserves a tenant only for itself.
    /// </summary>
    /// <param name="reservation">The tenant, its issuer and its cell.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="directory">The <see cref="ITenantDirectory"/> that holds the reservation.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the tenant is reserved; <c>400 Bad Request</c> when a field is blank, the cell is not a
    /// <see cref="CellLabelRules"/> label, or the tenant or its issuer is longer than the directory stores;
    /// <c>403 Forbidden</c> when the cell is not the caller; <c>409 Conflict</c> when the identifier or the issuer is
    /// taken.
    /// </returns>
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

        if (!CellLabelRules.IsValidLabel(reservation.Cell))
            return BadRequest(InvalidCellLabelDetail);

        if (
            IsOverLong(reservation.TenantId, DirectoryLimits.MaxIdentifierLength)
            || IsOverLong(reservation.Issuer, DirectoryLimits.MaxIssuerLength)
        )
            return BadRequest(TooLongDetail);

        // A cell reserves a tenant only for itself.
        if (!IsCaller(httpContext, reservation.Cell))
            return Forbidden("A cell reserves a tenant only for itself.");

        var result = await directory.ReserveAsync(reservation, cancellationToken);
        return ToResult(result);
    }

    /// <summary>
    /// Places a tenant in the calling cell and records the cell's region. A cell places a tenant only in itself.
    /// </summary>
    /// <param name="placement">The tenant, its issuer, its cell and its region.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="directory">The <see cref="ITenantDirectory"/> that holds the placement.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the tenant is placed; <c>400 Bad Request</c> when the tenant or its issuer is blank or longer
    /// than the directory stores, or the cell or its region is not a <see cref="CellLabelRules"/> label;
    /// <c>403 Forbidden</c> when the cell is not the caller; <c>409 Conflict</c> when the identifier or the issuer
    /// belongs to another tenant.
    /// </returns>
    private static async ValueTask<IResult> PlaceTenantAsync(
        TenantPlacement placement,
        HttpContext httpContext,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(placement.TenantId) || IsBlank(placement.Issuer))
            return BadRequest("The tenant and its issuer are required.");

        if (
            IsOverLong(placement.TenantId, DirectoryLimits.MaxIdentifierLength)
            || IsOverLong(placement.Issuer, DirectoryLimits.MaxIssuerLength)
        )
            return BadRequest(TooLongDetail);

        if (
            !CellLabelRules.IsValidLabel(placement.Cell)
            || !CellLabelRules.IsValidLabel(placement.Region)
        )
            return BadRequest(InvalidCellLabelDetail);

        // A cell places a tenant only in itself.
        if (!IsCaller(httpContext, placement.Cell))
            return Forbidden("A cell places a tenant only in itself.");

        var result = await directory.PlaceAsync(placement, cancellationToken);
        return ToResult(result);
    }

    /// <summary>
    /// Releases the reservation of a tenant that never became active, so its identifier and issuer are free again.
    /// </summary>
    /// <param name="tenantId">The tenant identifier from the route.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="writer">The <see cref="IDirectoryTenantWriter"/> that applies the write for the calling cell.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the reservation is released; otherwise the problem document of the write outcome, and
    /// <c>403 Forbidden</c> when the caller is not a cell.
    /// </returns>
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

    /// <summary>
    /// Moves a tenant from its source cell to the calling cell. Only the target cell, which is the caller, moves a
    /// tenant to itself.
    /// </summary>
    /// <param name="tenantId">The tenant identifier from the route.</param>
    /// <param name="fromCell">The cell that holds the tenant.</param>
    /// <param name="toCell">The cell that receives the tenant.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="directory">The <see cref="ITenantDirectory"/> that holds the placement.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the tenant is moved; <c>400 Bad Request</c> when a cell is missing or is not a
    /// <see cref="CellLabelRules"/> label; <c>403 Forbidden</c> when the target cell is not the caller;
    /// <c>409 Conflict</c> when the directory refuses the move.
    /// </returns>
    private static async ValueTask<IResult> MoveTenantAsync(
        string tenantId,
        string? fromCell,
        string? toCell,
        HttpContext httpContext,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (!CellLabelRules.IsValidLabel(fromCell) || !CellLabelRules.IsValidLabel(toCell))
            return BadRequest(InvalidCellLabelDetail);

        // The target cell asks for the move: the machine client that calls is `cell-{toCell}`.
        if (!IsCaller(httpContext, toCell))
            return Forbidden("Only the target cell moves a tenant to itself.");

        var result = await directory.MoveAsync(tenantId, fromCell, toCell, cancellationToken);
        return result.IsMoved ? Results.Ok() : Conflict(result.ConflictDetail);
    }

    /// <summary>
    /// Offers a tenant to another cell. Only the cell that holds the tenant offers it.
    /// </summary>
    /// <param name="tenantId">The tenant identifier from the route.</param>
    /// <param name="request">The cell that receives the offer.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="directory">The <see cref="ITenantDirectory"/> that holds the placement and the offer.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>204 No Content</c> when the offer is recorded; <c>400 Bad Request</c> when the target cell is missing or is
    /// not a <see cref="CellLabelRules"/> label;
    /// <c>403 Forbidden</c> when the tenant has no placement or the caller does not hold it; <c>409 Conflict</c> when
    /// the directory refuses the offer.
    /// </returns>
    private static async ValueTask<IResult> OfferTenantMoveAsync(
        string tenantId,
        TenantMoveOfferRequest request,
        HttpContext httpContext,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (!CellLabelRules.IsValidLabel(request.ToCell))
            return BadRequest(InvalidCellLabelDetail);

        // The cell that holds the tenant offers it: the machine client that calls is `cell-{cell}`.
        var placement = await directory.GetPlacementOrDefaultAsync(tenantId, cancellationToken);
        if (placement is null || !IsCaller(httpContext, placement.Cell))
            return Forbidden("Only the cell that holds a tenant offers it to another cell.");

        var result = await directory.OfferMoveAsync(
            tenantId,
            placement.Cell,
            request.ToCell,
            cancellationToken
        );
        return result.IsOffered ? Results.NoContent() : Conflict(result.ConflictDetail);
    }

    /// <summary>
    /// Retires a tenant, so its identifier and issuer are never reused.
    /// </summary>
    /// <param name="retirement">The tenant to retire.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="writer">The <see cref="IDirectoryTenantWriter"/> that applies the write for the calling cell.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the tenant is retired; <c>400 Bad Request</c> when the tenant is blank, or the tenant or its
    /// issuer is longer than the directory stores; <c>403 Forbidden</c> when the caller is not a cell; otherwise the
    /// problem document of the write outcome.
    /// </returns>
    private static async ValueTask<IResult> RetireTenantAsync(
        TenantRetirement retirement,
        HttpContext httpContext,
        IDirectoryTenantWriter writer,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(retirement.TenantId))
            return BadRequest("The tenant is required.");

        if (
            IsOverLong(retirement.TenantId, DirectoryLimits.MaxIdentifierLength)
            || IsOverLong(retirement.Issuer, DirectoryLimits.MaxIssuerLength)
        )
            return BadRequest(TooLongDetail);

        if (DirectoryCaller.GetCell(httpContext.User) is not { } callerCell)
            return Forbidden(TenantWriteForbiddenDetail);

        var result = await writer.RetireAsync(retirement, callerCell, cancellationToken);
        return ToResult(result);
    }

    /// <summary>
    /// Changes the issuer of a tenant and retires the previous issuer.
    /// </summary>
    /// <param name="change">The tenant and its new issuer.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="writer">The <see cref="IDirectoryTenantWriter"/> that applies the write for the calling cell.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the issuer is changed; <c>400 Bad Request</c> when the tenant is blank, or the tenant, the
    /// issuer or the previous issuer is longer than the directory stores; <c>403 Forbidden</c> when the caller is not a
    /// cell; otherwise the problem document of the write outcome, such as <c>404 Not Found</c>
    /// for an unknown tenant.
    /// </returns>
    private static async ValueTask<IResult> ChangeTenantIssuerAsync(
        TenantIssuerChange change,
        HttpContext httpContext,
        IDirectoryTenantWriter writer,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(change.TenantId))
            return BadRequest("The tenant is required.");

        if (
            IsOverLong(change.TenantId, DirectoryLimits.MaxIdentifierLength)
            || IsOverLong(change.Issuer, DirectoryLimits.MaxIssuerLength)
            || IsOverLong(change.PreviousIssuer, DirectoryLimits.MaxIssuerLength)
        )
            return BadRequest(TooLongDetail);

        if (DirectoryCaller.GetCell(httpContext.User) is not { } callerCell)
            return Forbidden(TenantWriteForbiddenDetail);

        var result = await writer.ChangeIssuerAsync(change, callerCell, cancellationToken);
        return ToResult(result);
    }

    /// <summary>
    /// Reads the placement of a tenant: its cell, its region and its issuer.
    /// </summary>
    /// <param name="tenantId">The tenant identifier from the route.</param>
    /// <param name="directory">The <see cref="ITenantDirectory"/> that holds the placement.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><c>200 OK</c> with the placement, or a <c>404 Not Found</c> problem when the tenant has none.</returns>
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

    /// <summary>
    /// Tells whether a tenant identifier is retired.
    /// </summary>
    /// <param name="tenantId">The tenant identifier from the route.</param>
    /// <param name="directory">The <see cref="ITenantDirectory"/> that holds the retired identifiers.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><c>200 OK</c> when the identifier is retired, or a <c>404 Not Found</c> problem when it is not.</returns>
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

    /// <summary>
    /// Reads the tenant that retired an issuer.
    /// </summary>
    /// <param name="issuer">The issuer from the query string.</param>
    /// <param name="directory">The <see cref="ITenantDirectory"/> that holds the retired issuers.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> with the holder of the retired issuer; <c>400 Bad Request</c> when the issuer is blank; or a
    /// <c>404 Not Found</c> problem when the issuer is not retired.
    /// </returns>
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

    /// <summary>
    /// Reserves a team identifier. Only the control-plane cell reserves a team.
    /// </summary>
    /// <param name="teamId">The team identifier from the route.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <param name="directory">The <see cref="ITeamDirectory"/> that holds the reservation.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the team is reserved; <c>400 Bad Request</c> when the team is blank or longer than the
    /// directory stores; <c>403 Forbidden</c> when the caller is not the control-plane cell; <c>409 Conflict</c> when
    /// the identifier is taken.
    /// </returns>
    private static async ValueTask<IResult> ReserveTeamAsync(
        string teamId,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(teamId))
            return BadRequest(TeamRequiredDetail);

        if (IsOverLong(teamId, DirectoryLimits.MaxIdentifierLength))
            return BadRequest(TeamTooLongDetail);

        if (ForbidUnlessControlPlane(httpContext, options.Value) is { } forbidden)
            return forbidden;

        var result = await directory.ReserveAsync(teamId, cancellationToken);
        return ToResult(result);
    }

    /// <summary>
    /// Publishes the document of a team. Only the control-plane cell publishes a team.
    /// </summary>
    /// <param name="teamId">The team identifier from the route.</param>
    /// <param name="document">The team document to publish.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <param name="directory">The <see cref="ITeamDirectory"/> that holds the document.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the document is published; <c>403 Forbidden</c> when the caller is not the control-plane
    /// cell; <c>400 Bad Request</c> when the document lacks its team or display name, names a team or a parent team
    /// longer than the directory stores, names a team other than the route's, or holds settings that are not a JSON
    /// object.
    /// </returns>
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

        if (
            IsOverLong(document.TeamId, DirectoryLimits.MaxIdentifierLength)
            || IsOverLong(document.ParentTeamId, DirectoryLimits.MaxIdentifierLength)
        )
            return BadRequest(TeamTooLongDetail);

        if (!string.Equals(teamId, document.TeamId, StringComparison.OrdinalIgnoreCase))
            return BadRequest("The team of the document is not the team of the route.");

        if (document.Settings.ValueKind != JsonValueKind.Object)
            return BadRequest(SettingsNotAnObjectDetail);

        await directory.PublishAsync(document, cancellationToken);
        return Results.Ok();
    }

    /// <summary>
    /// Releases the reservation of a team, so its identifier is free again. Only the control-plane cell releases a
    /// team.
    /// </summary>
    /// <param name="teamId">The team identifier from the route.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <param name="directory">The <see cref="ITeamDirectory"/> that holds the reservation.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the team is released; <c>400 Bad Request</c> when the team is blank or longer than the
    /// directory stores; or <c>403 Forbidden</c> when the caller is not the control-plane cell.
    /// </returns>
    private static async ValueTask<IResult> ReleaseTeamAsync(
        string teamId,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(teamId))
            return BadRequest(TeamRequiredDetail);

        if (IsOverLong(teamId, DirectoryLimits.MaxIdentifierLength))
            return BadRequest(TeamTooLongDetail);

        if (ForbidUnlessControlPlane(httpContext, options.Value) is { } forbidden)
            return forbidden;

        await directory.ReleaseAsync(teamId, cancellationToken);
        return Results.Ok();
    }

    /// <summary>
    /// Retires a team, so its identifier is never reused. Only the control-plane cell retires a team.
    /// </summary>
    /// <param name="teamId">The team identifier from the route.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <param name="directory">The <see cref="ITeamDirectory"/> that holds the retired identifiers.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the team is retired; <c>400 Bad Request</c> when the team is blank or longer than the
    /// directory stores; or <c>403 Forbidden</c> when the caller is not the control-plane cell.
    /// </returns>
    private static async ValueTask<IResult> RetireTeamAsync(
        string teamId,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(teamId))
            return BadRequest(TeamRequiredDetail);

        if (IsOverLong(teamId, DirectoryLimits.MaxIdentifierLength))
            return BadRequest(TeamTooLongDetail);

        if (ForbidUnlessControlPlane(httpContext, options.Value) is { } forbidden)
            return forbidden;

        await directory.RetireAsync(teamId, cancellationToken);
        return Results.Ok();
    }

    /// <summary>
    /// Tells whether a team identifier is retired.
    /// </summary>
    /// <param name="teamId">The team identifier from the route.</param>
    /// <param name="directory">The <see cref="ITeamDirectory"/> that holds the retired identifiers.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><c>200 OK</c> when the identifier is retired, or a <c>404 Not Found</c> problem when it is not.</returns>
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

    /// <summary>
    /// Reads the published document of a team.
    /// </summary>
    /// <param name="teamId">The team identifier from the route.</param>
    /// <param name="directory">The <see cref="ITeamDirectory"/> that holds the document.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><c>200 OK</c> with the document, or a <c>404 Not Found</c> problem when the team has none.</returns>
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

    /// <summary>
    /// Lists the entries of the team change feed that follow a sequence number, in sequence order. The sequence number
    /// is raised to zero and the page size is clamped to a range of one to the maximum page size, so an out-of-range
    /// query value is bounded and never rejected.
    /// </summary>
    /// <param name="directory">The <see cref="ITeamDirectory"/> that holds the change feed.</param>
    /// <param name="after">The sequence number after which the listing starts.</param>
    /// <param name="limit">The requested page size.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><c>200 OK</c> with the changes, which are empty when none follows the sequence number.</returns>
    private static async ValueTask<IResult> ListChangesAsync(
        ITeamDirectory directory,
        [FromQuery] long? after,
        [FromQuery] int? limit,
        CancellationToken cancellationToken
    )
    {
        var boundedAfter = Math.Max(after ?? 0, 0);
        var boundedLimit = Math.Clamp(limit ?? DefaultChangesLimit, 1, MaxChangesLimit);

        var changes = await directory.ListChangesAsync(
            boundedAfter,
            boundedLimit,
            cancellationToken
        );
        return Results.Ok(changes);
    }

    #endregion

    #region Platform

    /// <summary>
    /// Publishes the platform document. Only the control-plane cell publishes the platform.
    /// </summary>
    /// <param name="document">The platform document to publish.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <param name="directory">The <see cref="IPlatformDirectory"/> that holds the document.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the document is published; <c>403 Forbidden</c> when the caller is not the control-plane
    /// cell; or <c>400 Bad Request</c> when the document holds settings that are not a JSON object.
    /// </returns>
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

        if (document.Settings.ValueKind != JsonValueKind.Object)
            return BadRequest(SettingsNotAnObjectDetail);

        await directory.PublishAsync(document, cancellationToken);
        return Results.Ok();
    }

    /// <summary>
    /// Reads the published platform document.
    /// </summary>
    /// <param name="directory">The <see cref="IPlatformDirectory"/> that holds the document.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><c>200 OK</c> with the document, or a <c>404 Not Found</c> problem when none is published.</returns>
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

    /// <summary>
    /// Registers a cell with the directory. A cell registers only itself, and only the control-plane cell registers
    /// itself as the control plane.
    /// </summary>
    /// <param name="name">The cell name from the route.</param>
    /// <param name="descriptor">The cell's name, region, kind and management URL.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <param name="localDevelopmentMode">
    /// The <see cref="LocalDevelopmentMode"/> that, when enabled, lets a management URL use <c>http</c> on any host.
    /// </param>
    /// <param name="directory">The <see cref="ICellDirectory"/> that holds the registration.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the cell is registered; <c>400 Bad Request</c> when the kind or the management URL is blank,
    /// the management URL is not an absolute <c>https</c> URL (or an <c>http</c> URL in local development)
    /// without user information, a query or a fragment, the name or the region is not a
    /// <see cref="CellLabelRules"/> label, or the descriptor names a cell other than the route's;
    /// <c>403 Forbidden</c> when the cell is not the caller or claims to be the control plane without being it.
    /// </returns>
    private static async ValueTask<IResult> RegisterCellAsync(
        string name,
        CellDescriptor descriptor,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        [FromServices] LocalDevelopmentMode localDevelopmentMode,
        ICellDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(descriptor.Kind) || IsBlank(descriptor.ManagementUrl))
            return BadRequest("The cell's kind and its management URL are required.");

        var isManagementUrl = localDevelopmentMode.Enabled
            ? AbsoluteUri.TryParseAnyHttpServiceUrl(descriptor.ManagementUrl, out _)
            : AbsoluteUri.TryParseHttpsServiceUrl(descriptor.ManagementUrl, out _);
        if (!isManagementUrl)
            return BadRequest(InvalidManagementUrlDetail);

        if (
            !CellLabelRules.IsValidLabel(descriptor.Name)
            || !CellLabelRules.IsValidLabel(descriptor.Region)
        )
        {
            return BadRequest(InvalidCellLabelDetail);
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

    /// <summary>
    /// Lists every registered cell.
    /// </summary>
    /// <param name="directory">The <see cref="ICellDirectory"/> that holds the registrations.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><c>200 OK</c> with the cells, which are empty when none is registered.</returns>
    private static async ValueTask<IResult> ListCellsAsync(
        ICellDirectory directory,
        CancellationToken cancellationToken
    )
    {
        var cells = await directory.ListAsync(cancellationToken);
        return Results.Ok(cells);
    }

    /// <summary>
    /// Reads one registered cell.
    /// </summary>
    /// <param name="name">The cell name from the route.</param>
    /// <param name="directory">The <see cref="ICellDirectory"/> that holds the registrations.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><c>200 OK</c> with the cell, or a <c>404 Not Found</c> problem when it is not registered.</returns>
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

    /// <summary>
    /// Determines whether a request value is missing.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><c>true</c> when the value is <c>null</c>, empty or white space; otherwise <c>false</c>.</returns>
    private static bool IsBlank([NotNullWhen(false)] string? value) =>
        string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Determines whether a request value is longer than the directory stores.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <param name="maxLength">The most characters the directory stores for the value.</param>
    /// <returns>
    /// <c>true</c> when the value is present and longer than <paramref name="maxLength"/>; otherwise <c>false</c>.
    /// </returns>
    private static bool IsOverLong(string? value, int maxLength) =>
        value is not null && value.Length > maxLength;

    /// <summary>
    /// Determines whether the authenticated caller is a given cell.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated caller.</param>
    /// <param name="cell">The name of the cell to compare with the caller's.</param>
    /// <returns>
    /// <c>true</c> when the caller's machine client names the cell, compared case-sensitively; otherwise <c>false</c>,
    /// including when the caller is not a cell.
    /// </returns>
    private static bool IsCaller(HttpContext httpContext, string cell) =>
        string.Equals(DirectoryCaller.GetCell(httpContext.User), cell, StringComparison.Ordinal);

    /// <summary>
    /// Refuses a write that only the control-plane cell makes, such as a platform or team write.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated caller.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <returns>
    /// <c>null</c> when the caller is the control-plane cell and the write proceeds; otherwise the
    /// <c>403 Forbidden</c> problem.
    /// </returns>
    private static IResult? ForbidUnlessControlPlane(
        HttpContext httpContext,
        DirectoryServerOptions options
    ) =>
        DirectoryCaller.IsControlPlane(httpContext.User, options)
            ? null
            : Forbidden("Only the control-plane cell writes the platform and the teams.");

    /// <summary>
    /// Builds a <c>403 Forbidden</c> problem document.
    /// </summary>
    /// <param name="detail">The explanation for the caller.</param>
    /// <returns>The <see cref="IResult"/> that writes the problem document.</returns>
    private static IResult Forbidden(string detail) =>
        Results.Problem(
            title: "Forbidden",
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden
        );

    /// <summary>
    /// Builds a <c>404 Not Found</c> problem document for a lookup that finds nothing.
    /// </summary>
    /// <param name="detail">The explanation for the caller.</param>
    /// <returns>The <see cref="IResult"/> that writes the problem document.</returns>
    private static IResult NotFound(string? detail) =>
        Results.Problem(
            title: "Not Found",
            detail: detail,
            statusCode: StatusCodes.Status404NotFound
        );

    /// <summary>
    /// Builds a <c>400 Bad Request</c> problem document.
    /// </summary>
    /// <param name="detail">The explanation for the caller.</param>
    /// <returns>The <see cref="IResult"/> that writes the problem document.</returns>
    private static IResult BadRequest(string detail) =>
        Results.Problem(
            title: "Bad Request",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest
        );

    /// <summary>
    /// Builds a <c>409 Conflict</c> problem document.
    /// </summary>
    /// <param name="detail">The explanation for the caller.</param>
    /// <returns>The <see cref="IResult"/> that writes the problem document.</returns>
    private static IResult Conflict(string? detail) =>
        Results.Problem(
            title: "Conflict",
            detail: detail,
            statusCode: StatusCodes.Status409Conflict
        );

    /// <summary>
    /// Converts the outcome of a reservation to its response.
    /// </summary>
    /// <param name="result">The <see cref="ReservationResult"/> of the directory.</param>
    /// <returns>
    /// <c>200 OK</c> when the reservation is made; otherwise the <c>409 Conflict</c> problem that carries the
    /// conflict detail.
    /// </returns>
    private static IResult ToResult(ReservationResult result) =>
        result.IsReserved ? Results.Ok() : Conflict(result.ConflictDetail);

    /// <summary>
    /// Converts the outcome of a tenant write to its response.
    /// </summary>
    /// <param name="result">The <see cref="DirectoryTenantWriteResult"/> of the tenant writer.</param>
    /// <returns>
    /// <c>200 OK</c> when the write is done; the <c>403 Forbidden</c> problem when the calling cell does not hold the
    /// tenant; the <c>404 Not Found</c> problem when the tenant is unknown; otherwise the <c>409 Conflict</c> problem.
    /// </returns>
    private static IResult ToResult(DirectoryTenantWriteResult result) =>
        result.Outcome switch
        {
            DirectoryTenantWriteOutcome.Done => Results.Ok(),
            DirectoryTenantWriteOutcome.Forbidden => Forbidden(
                result.Detail ?? TenantWriteForbiddenDetail
            ),
            DirectoryTenantWriteOutcome.NotFound => NotFound(result.Detail),
            _ => Conflict(result.Detail),
        };

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
}
