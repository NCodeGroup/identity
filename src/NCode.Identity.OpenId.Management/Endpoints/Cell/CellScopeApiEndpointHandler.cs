// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Identity.Cryptography;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Contracts.Cells;
using NCode.Identity.OpenId.Management.Contracts.Teams;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Management.Endpoints.Teams;
using NCode.Identity.OpenId.Management.Endpoints.Tenants;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Management.Endpoints.Cell;

/*

GET api/cell (workload cells)
GET api/cell/tenants (workload cells)
GET api/cell/teams (workload cells)

*/

/// <summary>
/// Provides the API endpoints that describe the cell serving the request and list the tenants it holds. They are mapped
/// on every cell other than the control-plane cell. Listing the tenants is authorized at the cell's node, where a
/// <c>CellAdmin</c> or a <c>GlobalAdmin</c> holds the read operation, so a tenant administrator never lists tenants
/// beyond its own. Listing the teams that grant the cell the creation of their tenants is authorized there too: a
/// <c>CellAdmin</c> creates a tenant of each, through <c>POST /api/teams/{teamId}/tenants</c>.
/// </summary>
// See ADR-1536 (decision 10).
internal class CellScopeApiEndpointHandler(
    IStoreManagerFactory storeManagerFactory,
    IAuthorizationService authorizationService,
    ICryptoService cryptoService,
    IPlatformTopology platformTopology,
    IEffectiveSettingsResolver effectiveSettingsResolver,
    IOptions<CellOptions> cellOptions,
    IOptions<TenantResolutionOptions> tenantResolutionOptions
) : BaseApiEndpointHandler, IEndpointProvider
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IPlatformTopology PlatformTopology { get; } = platformTopology;
    private IEffectiveSettingsResolver EffectiveSettingsResolver { get; } =
        effectiveSettingsResolver;

    private CellOptions CellOptions { get; } = cellOptions.Value;

    private TenantResolutionOptions TenantResolutionOptions { get; } =
        tenantResolutionOptions.Value;

    /// <inheritdoc />
    protected override IAuthorizationService AuthorizationService { get; } = authorizationService;

    /// <inheritdoc />
    protected override ICryptoService CryptoService { get; } = cryptoService;

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        // The control-plane cell serves the platform's tenant listing, so only the other cells map these routes.
        if (CellOptions.IsControlPlane)
        {
            return;
        }

        endpoints
            .MapGet("/cell", GetCellAsync)
            .WithTags(OpenIdConstants.EndpointTags.Tenants)
            .AddEndpointFilter<ControlPlaneScopeEndpointFilter>()
            .Produces<CellScopeResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        endpoints
            .MapGet("/cell/tenants", ListCellTenantsAsync)
            .WithTags(OpenIdConstants.EndpointTags.Tenants)
            .AddEndpointFilter<ControlPlaneScopeEndpointFilter>()
            .Produces<CollectionResource<TenantResource>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        endpoints
            .MapGet("/cell/teams", ListCellTeamsAsync)
            .WithTags(OpenIdConstants.EndpointTags.Tenants)
            .AddEndpointFilter<ControlPlaneScopeEndpointFilter>()
            .Produces<CollectionResource<TeamResource>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Describes the cell that serves the request.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <response code="200">The cell's name, region and kind, and whether it has a management tenant.</response>
    [EndpointName("api/cell/get")]
    internal virtual IResult GetCellAsync(HttpContext httpContext)
    {
        if (httpContext.Features.Get<IManagementCallerFeature>() is null)
            return ManagementProblems.AuthenticationRequired(httpContext);

        var resource = new CellScopeResource
        {
            Name = CellOptions.Name,
            Region = CellOptions.Region,
            Kind = CellOptions.Kind,
            HasManagementTenant = PlatformTopology.HasManagementTenant,
        };
        return TypedResults.Json(resource);
    }

    /// <summary>
    /// Lists the tenants that this cell holds.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="cursor">The opaque continuation token from a previous page, or <c>null</c> for the first
    /// page.</param>
    /// <param name="limit">The maximum number of tenants to return on the page.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">A page of the tenants the cell holds.</response>
    [EndpointName("api/cell/tenants/list")]
    internal virtual async ValueTask<IResult> ListCellTenantsAsync(
        HttpContext httpContext,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken
    )
    {
        var effectiveLimit = NormalizeLimit(limit);

        var cellNode = GetCellNode(httpContext, CellOptions.Name);
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            cellNode,
            Operations.Read,
            cancellationToken
        );
        if (authorizationFailure is not null)
            return authorizationFailure;

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<ITenantStore>();
        var pageRequest = new PageRequest { Cursor = cursor, Limit = effectiveLimit };
        var page = await store.GetPageAsync(pageRequest, cancellationToken);
        var items = page.Items.Select(TenantApiEndpointHandler.ToTenantResource).ToList();
        var collection = new CollectionResource<TenantResource>
        {
            Items = items,
            ContinuationToken = page.NextCursor,
        };
        return TypedResults.Json(collection);
    }

    /// <summary>
    /// Lists the teams that grant this cell the creation of their tenants: each enabled team, as the cell mirrors it
    /// from the directory's feed, other than the platform's own team, whose effective <c>tenant_creation_cells</c>
    /// setting names the cell. A <c>CellAdmin</c> creates a tenant of any of them in this cell.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The teams that grant this cell the creation of their tenants.</response>
    [EndpointName("api/cell/teams/list")]
    internal virtual async ValueTask<IResult> ListCellTeamsAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var cellNode = GetCellNode(httpContext, CellOptions.Name);
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            cellNode,
            Operations.Read,
            cancellationToken
        );
        if (authorizationFailure is not null)
            return authorizationFailure;

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);
        var store = storeManager.GetStore<ITeamStore>();
        var platformTeamId = TenantResolutionOptions.Root.TeamId;

        // Every team's own settings document is read in pages, and only the few teams that name this cell, or sit
        // beneath one that does, have their effective settings resolved.
        var teams = new List<PersistedTeam>();
        string? cursor = null;
        do
        {
            var pageRequest = new PageRequest { Cursor = cursor, Limit = MaxPageSize };
            var page = await store.GetPageAsync(pageRequest, cancellationToken);
            teams.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);

        var teamsById = teams.ToDictionary(team => team.TeamId, StringComparer.OrdinalIgnoreCase);
        var items = new List<TeamResource>();
        foreach (var team in teams)
        {
            if (!CellTenantCreationRules.MayGrant(team, teamsById, CellOptions.Name))
            {
                continue;
            }

            var grants = await CellTenantCreationRules.GrantsAsync(
                EffectiveSettingsResolver,
                team,
                CellOptions.Name,
                platformTeamId,
                cancellationToken
            );
            if (grants)
            {
                var resource = TeamApiEndpointHandler.ToTeamResource(team);
                items.Add(resource);
            }
        }

        var collection = new CollectionResource<TeamResource> { Items = items };
        return TypedResults.Json(collection);
    }
}
