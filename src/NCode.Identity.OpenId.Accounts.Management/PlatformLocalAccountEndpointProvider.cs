// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Accounts.Management.Contracts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// Maps the routes that create and list the local accounts of the platform, which the root group serves because they
/// name no tenant. The accounts belong to the local connection that the control-plane tenant's
/// <c>default_connection</c> setting names, so the routes are mapped only on the control-plane cell.
/// </summary>
internal sealed class PlatformLocalAccountEndpointProvider(
    LocalAccountApiEndpointHandler handler,
    IOptions<CellOptions> cellOptions
) : IEndpointProvider
{
    private LocalAccountApiEndpointHandler Handler { get; } = handler;
    private CellOptions CellOptions { get; } = cellOptions.Value;

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        if (!CellOptions.IsControlPlane)
        {
            return;
        }

        var accounts = endpoints
            .MapGroup("/platform/local-accounts")
            .WithTags(OpenIdConstants.EndpointTags.LocalAccounts);

        accounts
            .MapPost("", Handler.CreatePlatformLocalAccountAsync)
            .Produces<LocalAccountResource>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        accounts
            .MapGet("", Handler.ListPlatformLocalAccountsAsync)
            .Produces<CollectionResource<LocalAccountResource>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
