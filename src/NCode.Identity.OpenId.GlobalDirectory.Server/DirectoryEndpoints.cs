// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Buffers;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.GlobalDirectory.Server.Stores;
using NCode.Identity.OpenId.GlobalDirectory.Server.Tokens;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Secrets.Persistence;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

/// <summary>
/// Maps the global directory API under <c>/directory/v1/</c>: the token endpoint and the tenant, retired-issuer, team,
/// platform and cell routes that the remote adapters of <c>NCode.Identity.OpenId.GlobalDirectory</c> call. The token
/// endpoint authenticates a cell with its <c>private_key_jwt</c> client assertion and issues the directory's own access
/// token. Every other route requires a bearer access token that the directory issued, for the audience
/// <see cref="GlobalDirectoryProtocol.DirectoryAudience"/> with the scope
/// <see cref="GlobalDirectoryProtocol.DirectoryScope"/>, checked before the route runs. Every route waits for the
/// platform's trust anchor, its bodies are confidential so HTTP logging never writes them, and every failure, a lookup
/// that finds nothing included, is an RFC 9457 problem document: a missing resource is a <c>404 Not Found</c> problem,
/// never an empty body. Every write is bound to the calling cell: the platform and team writes need the control-plane
/// cell (<see cref="DirectoryControlPlaneOptions.Cell"/>), and a tenant write needs the cell that holds the tenant.
/// </summary>
// See ADR-1411. DirectoryAuthenticationGate checks the token before the route binds its body.
[PublicAPI]
public static class DirectoryEndpoints
{
    private const string RoutePrefix = "directory/v1";
    private const string TokenRoute = "token";

    private const int DefaultChangesLimit = 100;
    private const int MaxChangesLimit = 1000;

    private const string TenantWriteForbiddenDetail =
        "Only the cell that holds a tenant, or the control-plane cell, writes it.";

    /// <summary>
    /// The problem detail that a tenant route answers with when the tenant identifier is blank.
    /// </summary>
    private const string TenantRequiredDetail = "The tenant is required.";

    /// <summary>
    /// The problem detail that a team route answers with when the team identifier is blank.
    /// </summary>
    private const string TeamRequiredDetail = "The team is required.";

    /// <summary>
    /// The problem detail that a route answers with when its request body names a team whose identifier is blank.
    /// </summary>
    private const string BlankTeamDetail = "A team identifier is not blank.";

    /// <summary>
    /// The problem detail that a publish route answers with when the document's settings are not a JSON object.
    /// </summary>
    private const string SettingsNotAnObjectDetail = "The document's settings are a JSON object.";

    /// <summary>
    /// The problem detail that the cell registration route answers with when the management URL is not one that
    /// <see cref="AbsoluteUri.TryParseHttpsServiceUrl"/> accepts, or that
    /// <see cref="AbsoluteUri.TryParseAnyHttpServiceUrl"/> accepts in local development.
    /// </summary>
    private const string InvalidManagementUrlDetail =
        "The cell's management URL is an absolute https URL, or an http URL in local development, with no user "
        + "information, query or fragment.";

    /// <summary>
    /// The problem detail that a tenant route answers with when an issuer it writes is not a service URL that
    /// <see cref="AbsoluteUri.TryParseServiceUrl"/> accepts.
    /// </summary>
    private const string InvalidIssuerDetail =
        "A tenant's issuer is an absolute https URL, or an http URL on a loopback host, with no user information, "
        + "query or fragment.";

    /// <summary>
    /// Gets the options that serialize a document as the change feed carries it, which measure its size.
    /// </summary>
    private static JsonSerializerOptions WebOptions { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The problem detail that the cell registration route answers with when the key set is not a public JSON Web Key
    /// Set.
    /// </summary>
    private const string JwksRequiredDetail = "The cell's key set is a public JSON Web Key Set.";

    /// <summary>
    /// Gets the problem detail that the cell registration route answers with when the kind or the management URL is
    /// longer than the directory stores.
    /// </summary>
    private static string CellTooLongDetail =>
        $"A cell's kind is at most {DirectoryLimits.MaxCellKindLength} characters and its management URL at most "
        + $"{DirectoryLimits.MaxManagementUrlLength}.";

    private static string InvalidCellLabelDetail =>
        $"A cell and its region are each {CellLabelRules.Description}.";

    /// <summary>
    /// Gets the problem detail that a tenant route answers with when a tenant identifier is not one that
    /// <see cref="TenantIdentifierRules.IsValidTenantId"/> accepts.
    /// </summary>
    private static string InvalidTenantIdDetail =>
        $"The tenant identifier must be 1 to {TenantIdentifierRules.MaxTenantIdLength} lowercase letters, digits or "
        + "hyphens that start and end with a letter or digit.";

    /// <summary>
    /// Gets the problem detail that a route answers with when a team identifier, in the route or the request body, is
    /// not one that <see cref="TenantIdentifierRules.IsValidTenantId"/> accepts.
    /// </summary>
    private static string InvalidTeamIdDetail =>
        $"The team identifier must be 1 to {TenantIdentifierRules.MaxTenantIdLength} lowercase letters, digits or "
        + "hyphens that start and end with a letter or digit.";

    /// <summary>
    /// Gets the problem detail that a tenant route answers with when an issuer it writes is longer than the directory
    /// stores.
    /// </summary>
    private static string IssuerTooLongDetail =>
        $"An issuer is at most {DirectoryLimits.MaxIssuerLength} characters.";

    /// <summary>
    /// Maps the tenant routes: reserving, placing, releasing, moving, retiring and changing the issuer of a tenant, and
    /// reading a placement or a retired identifier. Each route declares the problem documents that it can answer.
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
            .ProducesProblem(StatusCodes.Status400BadRequest)
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
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group
            .MapGet("retired-ids/{tenantId}", GetRetiredTenantIdAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Maps the team routes: reserving, publishing, releasing and retiring a team, reading a team document or a retired
    /// identifier, and listing the change feed. Each route declares the problem documents that it can answer.
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
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
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
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group
            .MapGet("{teamId}/document", GetTeamDocumentAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group
            .MapPut("{teamId}/authority", PublishTeamAuthorityAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group
            .MapGet("{teamId}/authority", GetTeamAuthorityAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
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
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group
            .MapGet("document", GetPlatformDocumentAsync)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group
            .MapPut("authority", PublishPlatformAuthorityAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group
            .MapGet("authority", GetPlatformAuthorityAsync)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group
            .MapPut("management-keys", PublishManagementKeysAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group
            .MapGet("management-keys", GetManagementKeysAsync)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Maps the cell routes: registering a cell's client keys, retiring a cell, a cell's own descriptor report, listing
    /// the cells and reading one cell. Each route declares the problem documents that it can answer.
    /// </summary>
    /// <param name="group">The <see cref="RouteGroupBuilder"/> of <c>/directory/v1/cells</c>.</param>
    private static void MapCells(RouteGroupBuilder group)
    {
        group
            .MapPut("{name}", RegisterCellAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group
            .MapPut("{name}/registration", RegisterCellClientAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group
            .MapDelete("{name}", RetireCellAsync)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("", ListCellsAsync);
        group.MapGet("{name}", GetCellAsync).ProducesProblem(StatusCodes.Status404NotFound);
    }

    #region Token

    /// <summary>
    /// Authenticates a cell by its <c>private_key_jwt</c> client assertion (RFC 7523) and issues the directory's access
    /// token for it. The route is outside the authentication gate, because the token is what it issues.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the form request.</param>
    /// <param name="assertionValidator">The <see cref="DirectoryClientAssertionValidator"/> that checks the
    /// assertion.</param>
    /// <param name="tokenIssuer">The <see cref="DirectoryTokenIssuer"/> that issues the access token.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the directory's issuer and token
    /// lifetime.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> with the access token, its type and its lifetime in seconds; <c>400 Bad Request</c> with the RFC
    /// 6749 error <c>invalid_request</c> when the request is not a form or lacks or repeats a parameter, or
    /// <c>unsupported_grant_type</c> when the grant type is not <c>client_credentials</c>; or <c>401 Unauthorized</c>
    /// with the error <c>invalid_client</c> when the assertion is refused. Every answer carries
    /// <c>Cache-Control: no-store</c>.
    /// </returns>
    private static async ValueTask<IResult> TokenAsync(
        HttpContext httpContext,
        DirectoryClientAssertionValidator assertionValidator,
        DirectoryTokenIssuer tokenIssuer,
        IOptions<DirectoryServerOptions> options,
        CancellationToken cancellationToken
    )
    {
        var headers = httpContext.Response.Headers;
        headers.CacheControl = "no-store";
        headers.Pragma = "no-cache";

        if (!httpContext.Request.HasFormContentType)
            return TokenError(
                StatusCodes.Status400BadRequest,
                OpenIdProtocol.ErrorCodes.InvalidRequest,
                "The request is a form."
            );

        var form = await httpContext.Request.ReadFormAsync(cancellationToken);
        var grantType = form[OpenIdProtocol.Parameters.GrantType];
        if (grantType.Count != 1 || string.IsNullOrEmpty(grantType[0]))
            return TokenError(
                StatusCodes.Status400BadRequest,
                OpenIdProtocol.ErrorCodes.InvalidRequest,
                "The request has one grant_type."
            );

        if (
            !string.Equals(
                grantType[0],
                OpenIdProtocol.GrantTypes.ClientCredentials,
                StringComparison.Ordinal
            )
        )
            return TokenError(
                StatusCodes.Status400BadRequest,
                OpenIdProtocol.ErrorCodes.UnsupportedGrantType,
                "The grant type is client_credentials."
            );

        var assertionType = form[OpenIdProtocol.Parameters.ClientAssertionType];
        var assertion = form[OpenIdProtocol.Parameters.ClientAssertion];
        var isAssertion =
            assertionType.Count == 1
            && string.Equals(
                assertionType[0],
                OpenIdProtocol.ClientAssertionTypes.JwtBearer,
                StringComparison.Ordinal
            )
            && assertion.Count == 1
            && !string.IsNullOrEmpty(assertion[0]);
        if (!isAssertion)
            return TokenError(
                StatusCodes.Status400BadRequest,
                OpenIdProtocol.ErrorCodes.InvalidRequest,
                "The request has one JWT bearer client_assertion."
            );

        var issuer = options.Value.Issuer.TrimEnd('/');
        var tokenEndpointUrl = $"{issuer}/{RoutePrefix}/{TokenRoute}";
        var assertionValue = assertion[0] ?? string.Empty;
        var result = await assertionValidator.ValidateAsync(
            assertionValue,
            tokenEndpointUrl,
            cancellationToken
        );
        if (!result.IsValid)
            return TokenError(
                StatusCodes.Status401Unauthorized,
                OpenIdProtocol.ErrorCodes.InvalidClient,
                result.FailureReason
            );

        var accessToken = await tokenIssuer.IssueAsync(result.CellName, cancellationToken);
        var response = new Dictionary<string, object>
        {
            [OpenIdProtocol.Parameters.AccessToken] = accessToken,
            [OpenIdProtocol.Parameters.TokenType] = OpenIdProtocol.TokenTypes.Bearer,
            [OpenIdProtocol.Parameters.ExpiresIn] = (long)options.Value.TokenLifetime.TotalSeconds,
        };
        return TypedResults.Json(response);
    }

    /// <summary>
    /// Builds the RFC 6749 section 5.2 error document of the token endpoint.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="error">The error code.</param>
    /// <param name="description">The explanation for the caller.</param>
    /// <returns>The <see cref="IResult"/> that writes the error document.</returns>
    private static IResult TokenError(int statusCode, string error, string description)
    {
        var body = new Dictionary<string, string>
        {
            [OpenIdProtocol.Parameters.ErrorCode] = error,
            [OpenIdProtocol.Parameters.ErrorDescription] = description,
        };
        return TypedResults.Json(body, statusCode: statusCode);
    }

    #endregion

    #region Tenants

    /// <summary>
    /// Reserves a tenant identifier and, when the tenant has one, its issuer for the calling cell. A cell reserves a
    /// tenant only for itself.
    /// </summary>
    /// <param name="reservation">The tenant, its issuer, if any, the team that owns it, if any, and its cell.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="directory">The <see cref="ITenantDirectory"/> that holds the reservation.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the tenant is reserved; <c>400 Bad Request</c> when the tenant or its cell is blank, the cell
    /// is not a <see cref="CellLabelRules"/> label, the tenant or its team is not a <see cref="TenantIdentifierRules"/>
    /// identifier, the issuer it names is longer than the directory stores, or the issuer it names is not a service URL
    /// that <see cref="AbsoluteUri.TryParseServiceUrl"/> accepts; <c>403 Forbidden</c> when the cell is not the caller;
    /// <c>409 Conflict</c> when the identifier or the issuer is taken, the team has no record, or a <c>max_tenants</c>
    /// limit refuses the reservation or cannot be read.
    /// </returns>
    private static async ValueTask<IResult> ReserveTenantAsync(
        TenantReservation reservation,
        HttpContext httpContext,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(reservation.TenantId) || IsBlank(reservation.Cell))
            return BadRequest("The tenant and its cell are required.");

        if (!CellLabelRules.IsValidLabel(reservation.Cell))
            return BadRequest(InvalidCellLabelDetail);

        if (!TenantIdentifierRules.IsValidTenantId(reservation.TenantId))
            return BadRequest(InvalidTenantIdDetail);

        // A tenant without an issuer reserves its identifier alone; an issuer that is present, an empty one included,
        // is judged.
        if (IsOverLong(reservation.Issuer, DirectoryLimits.MaxIssuerLength))
            return BadRequest(IssuerTooLongDetail);

        if (IsMalformedIssuer(reservation.Issuer))
            return BadRequest(InvalidIssuerDetail);

        if (ValidateOptionalTeamId(reservation.TeamId) is { } invalidTeam)
            return invalidTeam;

        // A cell reserves a tenant only for itself.
        if (!IsCaller(httpContext, reservation.Cell))
            return Forbidden("A cell reserves a tenant only for itself.");

        var result = await directory.ReserveAsync(reservation, cancellationToken);
        return ToResult(result);
    }

    /// <summary>
    /// Places a tenant in the calling cell; its region is the region the cell registered. A cell places a tenant only
    /// in itself.
    /// </summary>
    /// <param name="placement">The tenant, its issuer, if any, and its cell.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="directory">The <see cref="ITenantDirectory"/> that holds the placement.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the tenant is placed; <c>400 Bad Request</c> when the tenant is blank, the tenant is not a
    /// <see cref="TenantIdentifierRules"/> identifier, the issuer it names is longer than the directory stores or is
    /// not a service URL that <see cref="AbsoluteUri.TryParseServiceUrl"/> accepts, or the cell is not a
    /// <see cref="CellLabelRules"/> label; <c>403 Forbidden</c> when the cell is not the caller; <c>409 Conflict</c>
    /// when the identifier or the issuer belongs to another tenant.
    /// </returns>
    private static async ValueTask<IResult> PlaceTenantAsync(
        TenantPlacementRequest placement,
        HttpContext httpContext,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(placement.TenantId))
            return BadRequest("The tenant is required.");

        if (!TenantIdentifierRules.IsValidTenantId(placement.TenantId))
            return BadRequest(InvalidTenantIdDetail);

        // A tenant without an issuer is placed by its identifier alone; an issuer that is present, an empty one
        // included, is judged.
        if (IsOverLong(placement.Issuer, DirectoryLimits.MaxIssuerLength))
            return BadRequest(IssuerTooLongDetail);

        if (IsMalformedIssuer(placement.Issuer))
            return BadRequest(InvalidIssuerDetail);

        if (!CellLabelRules.IsValidLabel(placement.Cell))
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
    /// <param name="writer">
    /// The <see cref="IDirectoryTenantWriter"/> that applies the write for the calling cell.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the reservation is released; <c>400 Bad Request</c> when the tenant is not a
    /// <see cref="TenantIdentifierRules"/> identifier; otherwise the problem document of the write outcome, and
    /// <c>403 Forbidden</c> when the caller is not a cell.
    /// </returns>
    private static async ValueTask<IResult> ReleaseTenantAsync(
        string tenantId,
        HttpContext httpContext,
        IDirectoryTenantWriter writer,
        CancellationToken cancellationToken
    )
    {
        if (!TenantIdentifierRules.IsValidTenantId(tenantId))
            return BadRequest(InvalidTenantIdDetail);

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
    /// <c>200 OK</c> when the tenant is moved; <c>400 Bad Request</c> when the tenant is not a
    /// <see cref="TenantIdentifierRules"/> identifier, or a cell is missing or is not a <see cref="CellLabelRules"/>
    /// label; <c>403 Forbidden</c> when the target cell is not the caller; <c>409 Conflict</c> when the directory
    /// refuses the move.
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
        if (!TenantIdentifierRules.IsValidTenantId(tenantId))
            return BadRequest(InvalidTenantIdDetail);

        if (!CellLabelRules.IsValidLabel(fromCell) || !CellLabelRules.IsValidLabel(toCell))
            return BadRequest(InvalidCellLabelDetail);

        // The target cell asks for the move: the machine client that calls is `cell-{toCell}`.
        if (!IsCaller(httpContext, toCell))
            return Forbidden("Only the target cell moves a tenant to itself.");

        var result = await directory.MoveAsync(tenantId, fromCell, toCell, cancellationToken);
        return result.IsMoved ? TypedResults.Ok() : Conflict(result.ConflictDetail);
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
    /// <c>204 No Content</c> when the offer is recorded; <c>400 Bad Request</c> when the tenant is not a
    /// <see cref="TenantIdentifierRules"/> identifier, or the target cell is missing or is not a
    /// <see cref="CellLabelRules"/> label; <c>403 Forbidden</c> when the tenant has no placement or the caller does not
    /// hold it; <c>409 Conflict</c> when the directory refuses the offer.
    /// </returns>
    private static async ValueTask<IResult> OfferTenantMoveAsync(
        string tenantId,
        TenantMoveOfferRequest request,
        HttpContext httpContext,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (!TenantIdentifierRules.IsValidTenantId(tenantId))
            return BadRequest(InvalidTenantIdDetail);

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
        return result.IsOffered ? TypedResults.NoContent() : Conflict(result.ConflictDetail);
    }

    /// <summary>
    /// Retires a tenant, so its identifier and issuer are never reused.
    /// </summary>
    /// <param name="retirement">The tenant to retire.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="writer">
    /// The <see cref="IDirectoryTenantWriter"/> that applies the write for the calling cell.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the tenant is retired; <c>400 Bad Request</c> when the tenant is blank or is not a
    /// <see cref="TenantIdentifierRules"/> identifier, the issuer is longer than the directory stores, or the issuer is
    /// not a service URL that <see cref="AbsoluteUri.TryParseServiceUrl"/> accepts; <c>403 Forbidden</c> when the
    /// caller is not a cell; otherwise the problem document of the write outcome.
    /// </returns>
    private static async ValueTask<IResult> RetireTenantAsync(
        TenantRetirement retirement,
        HttpContext httpContext,
        IDirectoryTenantWriter writer,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(retirement.TenantId))
            return BadRequest(TenantRequiredDetail);

        if (!TenantIdentifierRules.IsValidTenantId(retirement.TenantId))
            return BadRequest(InvalidTenantIdDetail);

        if (IsOverLong(retirement.Issuer, DirectoryLimits.MaxIssuerLength))
            return BadRequest(IssuerTooLongDetail);

        if (IsMalformedIssuer(retirement.Issuer))
            return BadRequest(InvalidIssuerDetail);

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
    /// <param name="writer">
    /// The <see cref="IDirectoryTenantWriter"/> that applies the write for the calling cell.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the issuer is changed; <c>400 Bad Request</c> when the tenant is blank or is not a
    /// <see cref="TenantIdentifierRules"/> identifier, the issuer or the previous issuer is longer than the directory
    /// stores, or the issuer is not a service URL that <see cref="AbsoluteUri.TryParseServiceUrl"/> accepts;
    /// <c>403 Forbidden</c> when the caller is not a cell; otherwise the problem document of the write outcome, such as
    /// <c>404 Not Found</c> for an unknown tenant.
    /// </returns>
    private static async ValueTask<IResult> ChangeTenantIssuerAsync(
        TenantIssuerChange change,
        HttpContext httpContext,
        IDirectoryTenantWriter writer,
        CancellationToken cancellationToken
    )
    {
        if (IsBlank(change.TenantId))
            return BadRequest(TenantRequiredDetail);

        if (!TenantIdentifierRules.IsValidTenantId(change.TenantId))
            return BadRequest(InvalidTenantIdDetail);

        if (
            IsOverLong(change.Issuer, DirectoryLimits.MaxIssuerLength)
            || IsOverLong(change.PreviousIssuer, DirectoryLimits.MaxIssuerLength)
        )
            return BadRequest(IssuerTooLongDetail);

        // The previous issuer is only compared with the stored one, so only the issuer the tenant takes is judged.
        if (IsMalformedIssuer(change.Issuer))
            return BadRequest(InvalidIssuerDetail);

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
    /// <returns>
    /// <c>200 OK</c> with the placement; <c>400 Bad Request</c> when the tenant is not a
    /// <see cref="TenantIdentifierRules"/> identifier; or a <c>404 Not Found</c> problem when the tenant has none.
    /// </returns>
    private static async ValueTask<IResult> GetTenantPlacementAsync(
        string tenantId,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (!TenantIdentifierRules.IsValidTenantId(tenantId))
            return BadRequest(InvalidTenantIdDetail);

        var placement = await directory.GetPlacementOrDefaultAsync(tenantId, cancellationToken);
        return placement is null
            ? NotFound($"The directory holds no placement for the tenant '{tenantId}'.")
            : TypedResults.Ok(placement);
    }

    /// <summary>
    /// Tells whether a tenant identifier is retired.
    /// </summary>
    /// <param name="tenantId">The tenant identifier from the route.</param>
    /// <param name="directory">The <see cref="ITenantDirectory"/> that holds the retired identifiers.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the identifier is retired; <c>400 Bad Request</c> when the tenant is not a
    /// <see cref="TenantIdentifierRules"/> identifier; or a <c>404 Not Found</c> problem when it is not retired.
    /// </returns>
    private static async ValueTask<IResult> GetRetiredTenantIdAsync(
        string tenantId,
        ITenantDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (!TenantIdentifierRules.IsValidTenantId(tenantId))
            return BadRequest(InvalidTenantIdDetail);

        var isRetired = await directory.IsTenantIdRetiredAsync(tenantId, cancellationToken);
        return isRetired
            ? TypedResults.Ok()
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
            : TypedResults.Ok(holder);
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
    /// <c>200 OK</c> when the team is reserved; <c>400 Bad Request</c> when the team is blank or is not a
    /// <see cref="TenantIdentifierRules"/> identifier; <c>403 Forbidden</c> when the caller is not the control-plane
    /// cell; <c>409 Conflict</c> when the identifier is taken.
    /// </returns>
    private static async ValueTask<IResult> ReserveTeamAsync(
        string teamId,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (ValidateTeamId(teamId) is { } invalid)
            return invalid;

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
    /// <param name="writer">The <see cref="IDirectoryTeamWriter"/> that publishes the document.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the document is published; <c>403 Forbidden</c> when the caller is not the control-plane
    /// cell; <c>400 Bad Request</c> when the document lacks its team, display name, settings or root keys, names a
    /// blank parent team, names a team or a parent team that is not a <see cref="TenantIdentifierRules"/> identifier,
    /// names a team other than the route's, holds settings that are not a JSON object, or would nest a team deeper than
    /// the directory stores; or <c>422 Unprocessable Entity</c> when the JSON of the document is longer than
    /// <see cref="DirectoryLimits.MaxDocumentBytes"/> bytes.
    /// </returns>
    private static async ValueTask<IResult> PublishTeamAsync(
        string teamId,
        TeamDocument document,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        IDirectoryTeamWriter writer,
        CancellationToken cancellationToken
    )
    {
        if (ForbidUnlessControlPlane(httpContext, options.Value) is { } forbidden)
            return forbidden;

        if (IsBlank(document.TeamId) || IsBlank(document.DisplayName))
            return BadRequest("The team and its display name are required.");

        if (!TenantIdentifierRules.IsValidTenantId(document.TeamId))
            return BadRequest(InvalidTeamIdDetail);

        if (ValidateOptionalTeamId(document.ParentTeamId) is { } invalidParent)
            return invalidParent;

        if (!string.Equals(teamId, document.TeamId, StringComparison.OrdinalIgnoreCase))
            return BadRequest("The team of the document is not the team of the route.");

        if (document.Settings.ValueKind != JsonValueKind.Object)
            return BadRequest(SettingsNotAnObjectDetail);

        if (RefuseOversizedDocument(document) is { } oversized)
            return oversized;

        var result = await writer.PublishAsync(document, cancellationToken);
        return result.IsDone ? TypedResults.Ok() : BadRequest(result.Detail);
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
    /// <c>200 OK</c> when the team is released; <c>400 Bad Request</c> when the team is blank or is not a
    /// <see cref="TenantIdentifierRules"/> identifier; or <c>403 Forbidden</c> when the caller is not the control-plane
    /// cell.
    /// </returns>
    private static async ValueTask<IResult> ReleaseTeamAsync(
        string teamId,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (ValidateTeamId(teamId) is { } invalid)
            return invalid;

        if (ForbidUnlessControlPlane(httpContext, options.Value) is { } forbidden)
            return forbidden;

        await directory.ReleaseAsync(teamId, cancellationToken);
        return TypedResults.Ok();
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
    /// <c>200 OK</c> when the team is retired; <c>400 Bad Request</c> when the team is blank or is not a
    /// <see cref="TenantIdentifierRules"/> identifier; or <c>403 Forbidden</c> when the caller is not the control-plane
    /// cell.
    /// </returns>
    private static async ValueTask<IResult> RetireTeamAsync(
        string teamId,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (ValidateTeamId(teamId) is { } invalid)
            return invalid;

        if (ForbidUnlessControlPlane(httpContext, options.Value) is { } forbidden)
            return forbidden;

        await directory.RetireAsync(teamId, cancellationToken);
        return TypedResults.Ok();
    }

    /// <summary>
    /// Tells whether a team identifier is retired.
    /// </summary>
    /// <param name="teamId">The team identifier from the route.</param>
    /// <param name="directory">The <see cref="ITeamDirectory"/> that holds the retired identifiers.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> when the identifier is retired; <c>400 Bad Request</c> when the team is blank or is not a
    /// <see cref="TenantIdentifierRules"/> identifier; or a <c>404 Not Found</c> problem when it is not retired.
    /// </returns>
    private static async ValueTask<IResult> GetRetiredTeamIdAsync(
        string teamId,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (ValidateTeamId(teamId) is { } invalid)
            return invalid;

        var isRetired = await directory.IsTeamIdRetiredAsync(teamId, cancellationToken);
        return isRetired
            ? TypedResults.Ok()
            : NotFound($"The directory holds no retired team '{teamId}'.");
    }

    /// <summary>
    /// Reads the published document of a team.
    /// </summary>
    /// <param name="teamId">The team identifier from the route.</param>
    /// <param name="directory">The <see cref="ITeamDirectory"/> that holds the document.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> with the document; <c>400 Bad Request</c> when the team is blank or is not a
    /// <see cref="TenantIdentifierRules"/> identifier; or a <c>404 Not Found</c> problem when the team has none.
    /// </returns>
    private static async ValueTask<IResult> GetTeamDocumentAsync(
        string teamId,
        ITeamDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (ValidateTeamId(teamId) is { } invalid)
            return invalid;

        var document = await directory.GetOrDefaultAsync(teamId, cancellationToken);
        return document is null
            ? NotFound($"The directory holds no document for the team '{teamId}'.")
            : TypedResults.Ok(document);
    }

    /// <summary>
    /// Lists the entries of the team change feed that follow a sequence number, in sequence order. The sequence number
    /// is raised to zero and the page size is clamped to a range of one to the maximum page size, so an out-of-range
    /// query value is bounded and never rejected. A page holds changes whose documents total at most
    /// <see cref="DirectoryLimits.MaxChangePageBytes"/>, and at least one change.
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
        return TypedResults.Ok(changes);
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
    /// cell; <c>400 Bad Request</c> when the document lacks its settings or holds settings that are not a JSON object;
    /// or <c>422 Unprocessable Entity</c> when the JSON of the document is longer than
    /// <see cref="DirectoryLimits.MaxDocumentBytes"/> bytes.
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

        if (RefuseOversizedDocument(document) is { } oversized)
            return oversized;

        await directory.PublishAsync(document, cancellationToken);
        return TypedResults.Ok();
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
            : TypedResults.Ok(document);
    }

    #endregion

    #region Replicated documents

    /// <summary>
    /// Publishes the platform's authority document. Only the control-plane cell publishes it.
    /// </summary>
    /// <param name="document">The <see cref="ReplicatedDocument"/> to publish.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <param name="directory">The <see cref="IReplicatedDocumentDirectory"/> that holds the document.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>See <see cref="PublishReplicatedAsync"/>.</returns>
    private static ValueTask<IResult> PublishPlatformAuthorityAsync(
        ReplicatedDocument document,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        IReplicatedDocumentDirectory directory,
        CancellationToken cancellationToken
    ) =>
        PublishReplicatedAsync(
            ReplicatedDocumentKinds.PlatformAuthority,
            string.Empty,
            document,
            httpContext,
            options.Value,
            directory,
            cancellationToken
        );

    /// <summary>
    /// Publishes the management keys document. Only the control-plane cell publishes it.
    /// </summary>
    /// <param name="document">The <see cref="ReplicatedDocument"/> to publish.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <param name="directory">The <see cref="IReplicatedDocumentDirectory"/> that holds the document.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>See <see cref="PublishReplicatedAsync"/>.</returns>
    private static ValueTask<IResult> PublishManagementKeysAsync(
        ReplicatedDocument document,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        IReplicatedDocumentDirectory directory,
        CancellationToken cancellationToken
    ) =>
        PublishReplicatedAsync(
            ReplicatedDocumentKinds.ManagementKeys,
            string.Empty,
            document,
            httpContext,
            options.Value,
            directory,
            cancellationToken
        );

    /// <summary>
    /// Publishes a team's authority document. Only the control-plane cell publishes it.
    /// </summary>
    /// <param name="teamId">The team identifier from the route.</param>
    /// <param name="document">The <see cref="ReplicatedDocument"/> to publish.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <param name="directory">The <see cref="IReplicatedDocumentDirectory"/> that holds the document.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>See <see cref="PublishReplicatedAsync"/>; <c>400 Bad Request</c> also when the team is not
    /// valid.</returns>
    private static async ValueTask<IResult> PublishTeamAuthorityAsync(
        string teamId,
        ReplicatedDocument document,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        IReplicatedDocumentDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (ForbidUnlessControlPlane(httpContext, options.Value) is { } forbidden)
            return forbidden;

        if (ValidateTeamId(teamId) is { } invalid)
            return invalid;

        return await PublishReplicatedAsync(
            ReplicatedDocumentKinds.TeamAuthority,
            teamId,
            document,
            httpContext,
            options.Value,
            directory,
            cancellationToken
        );
    }

    /// <summary>
    /// Publishes a replicated document of the kind and key that its route names.
    /// </summary>
    /// <param name="kind">The kind that the route names.</param>
    /// <param name="key">The key that the route names.</param>
    /// <param name="document">The <see cref="ReplicatedDocument"/> to publish.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <param name="directory">The <see cref="IReplicatedDocumentDirectory"/> that holds the document.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> with the stored revision, which is the document's when it was higher and the stored one otherwise;
    /// <c>403 Forbidden</c> when the caller is not the control-plane cell; <c>400 Bad Request</c> when the document's
    /// kind or key is not the route's; or <c>413 Payload Too Large</c> when its content is longer than
    /// <see cref="DirectoryLimits.MaxDocumentBytes"/> bytes.
    /// </returns>
    private static async ValueTask<IResult> PublishReplicatedAsync(
        string kind,
        string key,
        ReplicatedDocument document,
        HttpContext httpContext,
        DirectoryServerOptions options,
        IReplicatedDocumentDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (ForbidUnlessControlPlane(httpContext, options) is { } forbidden)
            return forbidden;

        var isRoute =
            string.Equals(document.Kind, kind, StringComparison.Ordinal)
            && string.Equals(document.Key, key, StringComparison.OrdinalIgnoreCase);
        if (!isRoute)
            return BadRequest("The kind and the key of the document are those of the route.");

        var contentJson = document.Content?.GetRawText();
        var size = contentJson is null ? 0 : SecureEncoding.UTF8.GetByteCount(contentJson);
        if (size > DirectoryLimits.MaxDocumentBytes)
        {
            return PayloadTooLarge(
                $"The document is {size} bytes, and a published document is at most {DirectoryLimits.MaxDocumentBytes} bytes."
            );
        }

        var revision = await directory.PublishAsync(document, cancellationToken);
        var response = new { revision };
        return TypedResults.Ok(response);
    }

    /// <summary>
    /// Reads the platform's authority document.
    /// </summary>
    /// <param name="directory">The <see cref="IReplicatedDocumentDirectory"/> that holds the document.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><c>200 OK</c> with the document, or a <c>404 Not Found</c> problem when none is published.</returns>
    private static ValueTask<IResult> GetPlatformAuthorityAsync(
        IReplicatedDocumentDirectory directory,
        CancellationToken cancellationToken
    ) =>
        GetReplicatedAsync(
            ReplicatedDocumentKinds.PlatformAuthority,
            string.Empty,
            directory,
            cancellationToken
        );

    /// <summary>
    /// Reads the management keys document.
    /// </summary>
    /// <param name="directory">The <see cref="IReplicatedDocumentDirectory"/> that holds the document.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><c>200 OK</c> with the document, or a <c>404 Not Found</c> problem when none is published.</returns>
    private static ValueTask<IResult> GetManagementKeysAsync(
        IReplicatedDocumentDirectory directory,
        CancellationToken cancellationToken
    ) =>
        GetReplicatedAsync(
            ReplicatedDocumentKinds.ManagementKeys,
            string.Empty,
            directory,
            cancellationToken
        );

    /// <summary>
    /// Reads a team's authority document.
    /// </summary>
    /// <param name="teamId">The team identifier from the route.</param>
    /// <param name="directory">The <see cref="IReplicatedDocumentDirectory"/> that holds the document.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> with the document; <c>400 Bad Request</c> when the team is not valid; or a <c>404 Not Found</c>
    /// problem when none is published.
    /// </returns>
    private static async ValueTask<IResult> GetTeamAuthorityAsync(
        string teamId,
        IReplicatedDocumentDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (ValidateTeamId(teamId) is { } invalid)
            return invalid;

        return await GetReplicatedAsync(
            ReplicatedDocumentKinds.TeamAuthority,
            teamId,
            directory,
            cancellationToken
        );
    }

    /// <summary>
    /// Reads a replicated document.
    /// </summary>
    /// <param name="kind">The kind of the document.</param>
    /// <param name="key">The key of the document.</param>
    /// <param name="directory">The <see cref="IReplicatedDocumentDirectory"/> that holds the document.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns><c>200 OK</c> with the document, or a <c>404 Not Found</c> problem when none is published.</returns>
    private static async ValueTask<IResult> GetReplicatedAsync(
        string kind,
        string key,
        IReplicatedDocumentDirectory directory,
        CancellationToken cancellationToken
    )
    {
        var document = await directory.GetOrDefaultAsync(kind, key, cancellationToken);
        return document is null
            ? NotFound($"The directory holds no '{kind}' document.")
            : TypedResults.Ok(document);
    }

    /// <summary>
    /// Lists the revision of every replicated document, which the control-plane cell reads when it starts.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <param name="directory">The <see cref="IReplicatedDocumentDirectory"/> that holds the documents.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>200 OK</c> with the documents, whose content is <c>null</c>; or <c>403 Forbidden</c> when the caller is not
    /// the control-plane cell.
    /// </returns>
    private static async ValueTask<IResult> ListReplicatedRevisionsAsync(
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        IReplicatedDocumentDirectory directory,
        CancellationToken cancellationToken
    )
    {
        if (ForbidUnlessControlPlane(httpContext, options.Value) is { } forbidden)
            return forbidden;

        var revisions = await directory.ListRevisionsAsync(cancellationToken);
        return TypedResults.Ok(revisions);
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
    /// <c>200 OK</c> when the cell is registered; <c>400 Bad Request</c> when the kind or the management URL is blank
    /// or longer than the directory stores, the management URL is not an absolute <c>https</c> URL (or an <c>http</c>
    /// URL in local development) without user information, a query or a fragment, the name or the region is not a
    /// <see cref="CellLabelRules"/> label, or the descriptor names a cell other than the route's; <c>403 Forbidden</c>
    /// when the cell is not the caller or claims to be the control plane without being it; <c>409 Conflict</c> when
    /// another cell reports the management URL.
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

        if (
            IsOverLong(descriptor.Kind, DirectoryLimits.MaxCellKindLength)
            || IsOverLong(descriptor.ManagementUrl, DirectoryLimits.MaxManagementUrlLength)
        )
            return BadRequest(CellTooLongDetail);

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
            && !string.Equals(name, options.Value.ControlPlane.Cell, StringComparison.Ordinal)
        )
        {
            return Forbidden("Only the control-plane cell registers itself as the control plane.");
        }

        try
        {
            await directory.RegisterAsync(descriptor, cancellationToken);
        }
        catch (CellDirectoryConflictException exception)
        {
            return Conflict(exception.Message);
        }

        return TypedResults.Ok();
    }

    /// <summary>
    /// Describes the body of a cell registration: the cell's region and kind and its client key set. The cell's name is
    /// the route's.
    /// </summary>
    internal sealed record CellRegistrationBody
    {
        public string? Region { get; init; }

        public string? Kind { get; init; }

        public JsonElement Jwks { get; init; }
    }

    /// <summary>
    /// Registers a cell, or replaces the client key set of a registered cell. Only the control-plane cell registers
    /// cells.
    /// </summary>
    /// <param name="name">The cell name from the route.</param>
    /// <param name="body">The cell's region, kind and client key set.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <param name="jwksReader">The <see cref="IPublicJwksReader"/> that checks the key set.</param>
    /// <param name="store">The <see cref="DefaultDirectoryStore"/> that holds the registration.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>204 No Content</c> when the cell is registered; <c>400 Bad Request</c> when the name or the region is not a
    /// <see cref="CellLabelRules"/> label, the kind is blank or longer than the directory stores, or the key set is not
    /// a public JSON Web Key Set; <c>403 Forbidden</c> when the caller is not the control-plane cell;
    /// <c>409 Conflict</c> when the directory refuses the registration.
    /// </returns>
    private static async ValueTask<IResult> RegisterCellClientAsync(
        string name,
        CellRegistrationBody body,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        IPublicJwksReader jwksReader,
        DefaultDirectoryStore store,
        CancellationToken cancellationToken
    )
    {
        if (ForbidUnlessControlPlane(httpContext, options.Value) is { } forbidden)
            return forbidden;

        if (!CellLabelRules.IsValidLabel(name) || !CellLabelRules.IsValidLabel(body.Region))
            return BadRequest(InvalidCellLabelDetail);

        if (IsBlank(body.Kind))
            return BadRequest("The cell's kind is required.");

        if (IsOverLong(body.Kind, DirectoryLimits.MaxCellKindLength))
            return BadRequest(CellTooLongDetail);

        if (body.Jwks.ValueKind != JsonValueKind.Object)
            return BadRequest(JwksRequiredDetail);

        if (!jwksReader.TryRead(body.Jwks, out _, out var error))
        {
            var detail = $"{JwksRequiredDetail} {error}";
            return BadRequest(detail);
        }

        var registration = new CellRegistration
        {
            Name = name,
            Region = body.Region,
            Kind = body.Kind,
            Jwks = body.Jwks,
        };
        try
        {
            await store.RegisterCellAsync(registration, cancellationToken);
        }
        catch (CellDirectoryConflictException exception)
        {
            return Conflict(exception.Message);
        }

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Retires a cell, so its name is never reissued. Only the control-plane cell retires cells.
    /// </summary>
    /// <param name="name">The cell name from the route.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated calling cell.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <param name="store">The <see cref="DefaultDirectoryStore"/> that holds the registration.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// <c>204 No Content</c> when the cell is retired; <c>400 Bad Request</c> when the name is not a
    /// <see cref="CellLabelRules"/> label; <c>403 Forbidden</c> when the caller is not the control-plane cell;
    /// <c>409 Conflict</c> when the cell is not registered, holds tenants or is the control-plane cell.
    /// </returns>
    private static async ValueTask<IResult> RetireCellAsync(
        string name,
        HttpContext httpContext,
        IOptions<DirectoryServerOptions> options,
        DefaultDirectoryStore store,
        CancellationToken cancellationToken
    )
    {
        if (ForbidUnlessControlPlane(httpContext, options.Value) is { } forbidden)
            return forbidden;

        if (!CellLabelRules.IsValidLabel(name))
            return BadRequest(InvalidCellLabelDetail);

        try
        {
            await store.RetireCellAsync(name, cancellationToken);
        }
        catch (CellDirectoryConflictException exception)
        {
            return Conflict(exception.Message);
        }

        return TypedResults.NoContent();
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
        return TypedResults.Ok(cells);
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
        return cell is null
            ? NotFound($"The directory holds no cell '{name}'.")
            : TypedResults.Ok(cell);
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
    /// Determines whether a request names an issuer that is not a service URL.
    /// </summary>
    /// <param name="issuer">The issuer the request names, or <c>null</c> when it names none.</param>
    /// <returns>
    /// <c>true</c> when the issuer is present, an empty one included, and <see cref="AbsoluteUri.TryParseServiceUrl"/>
    /// refuses it; otherwise <c>false</c>.
    /// </returns>
    private static bool IsMalformedIssuer(string? issuer) =>
        issuer is not null && !AbsoluteUri.TryParseServiceUrl(issuer, out _);

    /// <summary>
    /// Determines whether the authenticated caller is a given cell.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated caller.</param>
    /// <param name="cell">The name of the cell to compare with the caller's.</param>
    /// <returns>
    /// <c>true</c> when the caller's machine client names the cell, compared case-sensitively; otherwise <c>false</c>,
    /// including when the caller is not a cell.
    /// </returns>
    private static bool IsCaller(HttpContext httpContext, string cell)
    {
        var callerCell = DirectoryCaller.GetCell(httpContext.User);
        return string.Equals(callerCell, cell, StringComparison.Ordinal);
    }

    /// <summary>
    /// Refuses a write that only the control-plane cell makes, such as a platform or team write.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> that carries the authenticated caller.</param>
    /// <param name="options">The <see cref="DirectoryServerOptions"/> that name the control-plane cell.</param>
    /// <returns>
    /// <c>null</c> when the caller is the control-plane cell and the write proceeds; otherwise the <c>403 Forbidden</c>
    /// problem.
    /// </returns>
    private static ProblemHttpResult? ForbidUnlessControlPlane(
        HttpContext httpContext,
        DirectoryServerOptions options
    ) =>
        DirectoryCaller.IsControlPlane(httpContext.User, options)
            ? null
            : Forbidden("Only the control-plane cell writes the directory's global state.");

    /// <summary>
    /// Refuses a team identifier from a route that is blank or is not a <see cref="TenantIdentifierRules"/> identifier.
    /// </summary>
    /// <param name="teamId">The team identifier from the route.</param>
    /// <returns>
    /// <c>null</c> when the team identifier is acceptable; otherwise the <c>400 Bad Request</c> problem.
    /// </returns>
    private static ProblemHttpResult? ValidateTeamId(string teamId)
    {
        if (IsBlank(teamId))
            return BadRequest(TeamRequiredDetail);

        return TenantIdentifierRules.IsValidTenantId(teamId)
            ? null
            : BadRequest(InvalidTeamIdDetail);
    }

    /// <summary>
    /// Refuses a team identifier that a request body names, such as the team of a tenant reservation or the parent of a
    /// team document, when it is blank or is not a <see cref="TenantIdentifierRules"/> identifier. A body that names no
    /// team passes.
    /// </summary>
    /// <param name="teamId">The team identifier from the request body, or <c>null</c> when it names none.</param>
    /// <returns>
    /// <c>null</c> when the body names no team or an acceptable one; otherwise the <c>400 Bad Request</c> problem.
    /// </returns>
    private static ProblemHttpResult? ValidateOptionalTeamId(string? teamId)
    {
        if (teamId is null)
            return null;

        if (IsBlank(teamId))
            return BadRequest(BlankTeamDetail);

        return TenantIdentifierRules.IsValidTenantId(teamId)
            ? null
            : BadRequest(InvalidTeamIdDetail);
    }

    /// <summary>
    /// Refuses a team or platform document whose JSON, as the change feed carries it, is longer than
    /// <see cref="DirectoryLimits.MaxDocumentBytes"/> bytes.
    /// </summary>
    /// <typeparam name="TDocument">The type of the document to measure.</typeparam>
    /// <param name="document">The document that a publish route received.</param>
    /// <returns>
    /// <c>null</c> when the document is within the limit; otherwise the <c>422 Unprocessable Entity</c> problem.
    /// </returns>
    private static ProblemHttpResult? RefuseOversizedDocument<TDocument>(TDocument document)
    {
        var size = JsonSerializer.SerializeToUtf8Bytes(document, WebOptions).Length;
        return size > DirectoryLimits.MaxDocumentBytes
            ? UnprocessableEntity(
                $"The document is {size} bytes, and a published document is at most {DirectoryLimits.MaxDocumentBytes} bytes."
            )
            : null;
    }

    /// <summary>
    /// Builds a <c>403 Forbidden</c> problem document.
    /// </summary>
    /// <param name="detail">The explanation for the caller.</param>
    /// <returns>The <see cref="ProblemHttpResult"/> that writes the problem document.</returns>
    private static ProblemHttpResult Forbidden(string detail) =>
        TypedResults.Problem(
            title: "Forbidden",
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden
        );

    /// <summary>
    /// Builds a <c>404 Not Found</c> problem document for a lookup that finds nothing.
    /// </summary>
    /// <param name="detail">The explanation for the caller.</param>
    /// <returns>The <see cref="ProblemHttpResult"/> that writes the problem document.</returns>
    private static ProblemHttpResult NotFound(string? detail) =>
        TypedResults.Problem(
            title: "Not Found",
            detail: detail,
            statusCode: StatusCodes.Status404NotFound
        );

    /// <summary>
    /// Builds a <c>400 Bad Request</c> problem document.
    /// </summary>
    /// <param name="detail">The explanation for the caller.</param>
    /// <returns>The <see cref="ProblemHttpResult"/> that writes the problem document.</returns>
    private static ProblemHttpResult BadRequest(string detail) =>
        TypedResults.Problem(
            title: "Bad Request",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest
        );

    /// <summary>
    /// Builds a <c>409 Conflict</c> problem document.
    /// </summary>
    /// <param name="detail">The explanation for the caller.</param>
    /// <returns>The <see cref="ProblemHttpResult"/> that writes the problem document.</returns>
    private static ProblemHttpResult Conflict(string? detail) =>
        TypedResults.Problem(
            title: "Conflict",
            detail: detail,
            statusCode: StatusCodes.Status409Conflict
        );

    /// <summary>
    /// Builds a <c>413 Payload Too Large</c> problem document.
    /// </summary>
    /// <param name="detail">The explanation for the caller.</param>
    /// <returns>The <see cref="ProblemHttpResult"/> that writes the problem document.</returns>
    private static ProblemHttpResult PayloadTooLarge(string detail) =>
        TypedResults.Problem(
            title: "Payload Too Large",
            detail: detail,
            statusCode: StatusCodes.Status413PayloadTooLarge
        );

    /// <summary>
    /// Builds a <c>422 Unprocessable Entity</c> problem document.
    /// </summary>
    /// <param name="detail">The explanation for the caller.</param>
    /// <returns>The <see cref="ProblemHttpResult"/> that writes the problem document.</returns>
    private static ProblemHttpResult UnprocessableEntity(string detail) =>
        TypedResults.Problem(
            title: "Unprocessable Entity",
            detail: detail,
            statusCode: StatusCodes.Status422UnprocessableEntity
        );

    /// <summary>
    /// Converts the outcome of a reservation to its response.
    /// </summary>
    /// <param name="result">The <see cref="ReservationResult"/> of the directory.</param>
    /// <returns>
    /// <c>200 OK</c> when the reservation is made; otherwise the <c>409 Conflict</c> problem that carries the conflict
    /// detail.
    /// </returns>
    private static IResult ToResult(ReservationResult result) =>
        result.IsReserved ? TypedResults.Ok() : Conflict(result.ConflictDetail);

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
            DirectoryTenantWriteOutcome.Done => TypedResults.Ok(),
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

            var root = endpoints
                .MapGroup(RoutePrefix)
                .AddEndpointGate<RouteGroupBuilder, DirectoryAnchorGate>()
                .WithConfidentialBodies();

            // The token endpoint is outside the authentication gate: its client assertion is its authentication.
            root.MapPost(TokenRoute, TokenAsync)
                .Produces(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status401Unauthorized)
                .Produces(StatusCodes.Status503ServiceUnavailable);

            var group = root.MapGroup(string.Empty)
                .AddEndpointGate<RouteGroupBuilder, DirectoryAuthenticationGate>()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

            var tenantsGroup = group.MapGroup("tenants");
            MapTenants(tenantsGroup);
            group
                .MapGet("retired-issuers", GetRetiredIssuerAsync)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status404NotFound);
            group
                .MapGet("replicated-revisions", ListReplicatedRevisionsAsync)
                .ProducesProblem(StatusCodes.Status403Forbidden);
            var teamsGroup = group.MapGroup("teams");
            MapTeams(teamsGroup);
            var platformGroup = group.MapGroup("platform");
            MapPlatform(platformGroup);
            var cellsGroup = group.MapGroup("cells");
            MapCells(cellsGroup);

            return group;
        }
    }
}
