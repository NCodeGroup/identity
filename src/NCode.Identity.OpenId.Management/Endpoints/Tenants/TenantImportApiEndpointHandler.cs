// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NCode.Identity.Cryptography;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Management.Transfers;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Management.Endpoints.Tenants;

/*

POST api/tenant-imports/transfer-keys

*/

/// <summary>
/// Provides the API endpoint that a cell publishes to receive a tenant: it issues a one-time transfer key whose public
/// half seals the tenant's export. Every cell maps the route, and the caller needs the update operation on the
/// platform node.
/// </summary>
// See ADR-1514.
internal class TenantImportApiEndpointHandler(
    IAuthorizationService authorizationService,
    ICryptoService cryptoService,
    ITenantTransferKeyService tenantTransferKeyService
) : BaseApiEndpointHandler, IEndpointProvider
{
    private ITenantTransferKeyService TenantTransferKeyService { get; } = tenantTransferKeyService;

    /// <inheritdoc />
    protected override IAuthorizationService AuthorizationService { get; } = authorizationService;

    /// <inheritdoc />
    protected override ICryptoService CryptoService { get; } = cryptoService;

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var imports = endpoints
            .MapGroup("/tenant-imports")
            .WithTags(OpenIdConstants.EndpointTags.Tenants)
            .AddEndpointFilter<RouteGroupBuilder, ControlPlaneScopeEndpointFilter>();

        imports
            .MapPost("/transfer-keys", CreateTransferKeyAsync)
            .Produces<TenantTransferKeyResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Issues a one-time transfer key that this cell takes once, when it imports the tenant that the key seals.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The <c>kid</c> and the public <c>JWK</c> of the transfer key, which expires after one hour.</response>
    [EndpointName("api/tenant-imports/transfer-keys/create")]
    internal virtual async ValueTask<IResult> CreateTransferKeyAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var authorizationFailure = await GetAuthorizationFailureAsync(
            httpContext,
            GetPlatformNode(httpContext),
            Operations.Update,
            cancellationToken
        );

        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var transferKey = await TenantTransferKeyService.CreateAsync(cancellationToken);
        return TypedResults.Json(
            new TenantTransferKeyResource { Kid = transferKey.KeyId, Jwk = transferKey.Jwk }
        );
    }
}
