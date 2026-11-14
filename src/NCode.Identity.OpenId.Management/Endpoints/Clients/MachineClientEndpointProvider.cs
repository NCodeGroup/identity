// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Contracts.Clients;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Management.Endpoints.Clients;

/// <summary>
/// Maps the routes that create and list the machine clients of the platform and of a team, which the root group serves
/// because they name no tenant. The routes are mapped only on the control-plane cell.
/// </summary>
// See ADR-1466.
internal sealed class MachineClientEndpointProvider(
    ClientApiEndpointHandler clientApiEndpointHandler,
    IOptions<CellOptions> cellOptions
) : IEndpointProvider
{
    private ClientApiEndpointHandler ClientApiEndpointHandler { get; } = clientApiEndpointHandler;
    private CellOptions CellOptions { get; } = cellOptions.Value;

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        // Platform and team machine clients are owned by the control-plane cell, so no other cell maps their routes.
        if (!CellOptions.IsControlPlane)
        {
            return;
        }

        var platformClients = endpoints
            .MapGroup("/platform/clients")
            .WithTags(OpenIdConstants.EndpointTags.Clients)
            .AddEndpointFilter<RouteGroupBuilder, ControlPlaneScopeEndpointFilter>();

        platformClients
            .MapPost("", ClientApiEndpointHandler.CreatePlatformClientAsync)
            .Produces<ClientResource>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        platformClients
            .MapGet("", ClientApiEndpointHandler.ListPlatformClientsAsync)
            .Produces<CollectionResource<ClientResource>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        var teamClients = endpoints
            .MapGroup("/teams/{teamId}/clients")
            .WithTags(OpenIdConstants.EndpointTags.Clients)
            .AddEndpointFilter<RouteGroupBuilder, ControlPlaneScopeEndpointFilter>();

        teamClients
            .MapPost("", ClientApiEndpointHandler.CreateTeamClientAsync)
            .Produces<ClientResource>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        teamClients
            .MapGet("", ClientApiEndpointHandler.ListTeamClientsAsync)
            .Produces<CollectionResource<ClientResource>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }
}
