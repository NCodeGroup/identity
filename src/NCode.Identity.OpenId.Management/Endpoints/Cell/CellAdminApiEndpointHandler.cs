// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Identity.Cryptography;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Management.Endpoints.Cell;

/*

GET api/cell/admins (workload cells)
POST api/cell/admins (workload cells)
DELETE api/cell/admins/{principalId} (workload cells)

*/

/// <summary>
/// Provides the API endpoints that manage the administrators of the cell: the principals of the cell's management
/// tenant that hold the <c>CellAdmin</c> role at the cell's node. They are mapped on every cell other than the
/// control-plane cell, and answer <c>404</c> in a cell without a management tenant. A grant or revoke is authorized
/// with the update operation at the cell's node.
/// </summary>
internal class CellAdminApiEndpointHandler(
    IStoreManagerFactory storeManagerFactory,
    IAuthorizationService authorizationService,
    ICryptoService cryptoService,
    IResourceOwnershipService resourceOwnershipService,
    IControlPlanePrincipalDirectory controlPlanePrincipalDirectory,
    IManagementAuditRecorder managementAuditRecorder,
    IPlatformTopology platformTopology,
    IOptions<CellOptions> cellOptions
) : BaseOwnableApiEndpointHandler, IEndpointProvider
{
    private IPlatformTopology PlatformTopology { get; } = platformTopology;
    private CellOptions CellOptions { get; } = cellOptions.Value;

    /// <inheritdoc />
    protected override IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;

    /// <inheritdoc />
    protected override IAuthorizationService AuthorizationService { get; } = authorizationService;

    /// <inheritdoc />
    protected override ICryptoService CryptoService { get; } = cryptoService;

    /// <inheritdoc />
    protected override IResourceOwnershipService ResourceOwnershipService { get; } =
        resourceOwnershipService;

    /// <inheritdoc />
    protected override IControlPlanePrincipalDirectory ControlPlanePrincipalDirectory { get; } =
        controlPlanePrincipalDirectory;

    /// <inheritdoc />
    protected override IManagementAuditRecorder ManagementAuditRecorder { get; } =
        managementAuditRecorder;

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        // The control-plane cell has no cell administrators, so only the other cells map these routes.
        if (CellOptions.IsControlPlane)
        {
            return;
        }

        endpoints
            .MapGet("/cell/admins", ListAdminsAsync)
            .WithTags(OpenIdConstants.EndpointTags.Tenants)
            .AddEndpointFilter<ControlPlaneScopeEndpointFilter>()
            .Produces<RoleListResource>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        endpoints
            .MapPost("/cell/admins", AddAdminAsync)
            .WithTags(OpenIdConstants.EndpointTags.Tenants)
            .AddEndpointFilter<ControlPlaneScopeEndpointFilter>()
            .Produces<RoleHolderResource>(StatusCodes.Status201Created)
            .Produces<RoleHolderResource>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        endpoints
            .MapDelete("/cell/admins/{principalId}", RemoveAdminAsync)
            .WithTags(OpenIdConstants.EndpointTags.Tenants)
            .AddEndpointFilter<ControlPlaneScopeEndpointFilter>()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);
    }

    /// <summary>
    /// Lists the administrators of the cell and whether the caller may grant and revoke the role.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="200">The cell's administrators and whether the caller may change them.</response>
    /// <response code="404">The cell has no management tenant.</response>
    [EndpointName("api/cell/admins/list")]
    internal virtual async ValueTask<IResult> ListAdminsAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var roleList = GetAdminList(httpContext);
        return await ProcessListRoleAssignmentsAsync(
            httpContext,
            roleList,
            (_, token) =>
                HoldsAuthorityAsync(httpContext, roleList.Node, roleList.Operation, token),
            cancellationToken
        );
    }

    /// <summary>
    /// Adds an administrator to the cell. The principal must be a principal of the cell's management tenant.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="request">The request that names the principal.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="201">The administrator was added.</response>
    /// <response code="200">The principal already administers the cell.</response>
    /// <response code="400">The request names no principal, or both an identifier and a username.</response>
    /// <response code="404">The cell has no management tenant.</response>
    /// <response code="409">The principal is not a principal of the cell's management tenant.</response>
    [EndpointName("api/cell/admins/add")]
    internal virtual async ValueTask<IResult> AddAdminAsync(
        HttpContext httpContext,
        [FromBody] AddRoleHolderRequest request,
        CancellationToken cancellationToken
    )
    {
        var roleList = GetAdminList(httpContext);

        // A caller without authority gets the shared forbidden answer before the cell says anything about its
        // principals.
        var holdsAuthority = await HoldsAuthorityAsync(
            httpContext,
            roleList.Node,
            roleList.Operation,
            cancellationToken
        );
        if (holdsAuthority && PlatformTopology.HasManagementTenant)
        {
            var isManagementPrincipal = await IsManagementPrincipalAsync(
                request,
                cancellationToken
            );
            if (isManagementPrincipal is false)
            {
                return TypedResults.Problem(
                    detail: "A cell administrator is a principal of the cell's management tenant.",
                    statusCode: StatusCodes.Status409Conflict
                );
            }
        }

        return await ProcessAddRoleAssignmentAsync(
            httpContext,
            roleList,
            request,
            cancellationToken
        );
    }

    /// <summary>
    /// Removes an administrator from the cell.
    /// </summary>
    /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
    /// <param name="principalId">The identifier of the principal to remove.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that may be used to cancel the asynchronous operation.</param>
    /// <response code="204">The administrator was removed.</response>
    /// <response code="404">The principal is not an administrator of the cell.</response>
    [EndpointName("api/cell/admins/remove")]
    internal virtual async ValueTask<IResult> RemoveAdminAsync(
        HttpContext httpContext,
        [FromRoute] string principalId,
        CancellationToken cancellationToken
    )
    {
        var roleList = GetAdminList(httpContext);
        return await ProcessRemoveRoleAssignmentAsync(
            httpContext,
            roleList,
            principalId,
            cancellationToken
        );
    }

    // Describes the administrators of the cell: the role is granted and revoked with 'Update' at the cell's node,
    // addressed from the tenant the request runs under.
    private RoleList GetAdminList(HttpContext httpContext)
    {
        var tenantId = httpContext.GetOpenIdContext().Tenant.TenantId;
        return new RoleList
        {
            Node = ResourceNode.For(tenantId, ResourceNodeTypes.Cell, CellOptions.Name),
            RoleName = BuiltInRoles.CellAdmin,
            Operation = Operations.Update,
            Path = "/cell/admins",
            ResourceExistsAsync = (_, _) =>
                ValueTask.FromResult(PlatformTopology.HasManagementTenant),
        };
    }

    // Returns null when the request names no single principal, which the shared grant answers with 400.
    private async ValueTask<bool?> IsManagementPrincipalAsync(
        AddRoleHolderRequest request,
        CancellationToken cancellationToken
    )
    {
        var principalId = request.PrincipalId;
        var userName = request.UserName;
        ControlPlanePrincipalQuery query;
        if (principalId is { Length: > 0 } && string.IsNullOrEmpty(userName))
        {
            query = new ControlPlanePrincipalQuery { PrincipalIds = [principalId] };
        }
        else if (userName is { Length: > 0 } && string.IsNullOrEmpty(principalId))
        {
            query = new ControlPlanePrincipalQuery { UserNames = [userName] };
        }
        else
        {
            return null;
        }

        var found = await ControlPlanePrincipalDirectory.FindAsync(query, cancellationToken);
        foreach (var summary in found)
        {
            var name = query.PrincipalIds.Count > 0 ? summary.PrincipalId : summary.UserName;
            var requested = query.PrincipalIds.Count > 0 ? principalId : userName;
            var matches = string.Equals(name, requested, StringComparison.OrdinalIgnoreCase);
            if (matches)
            {
                return true;
            }
        }

        return false;
    }
}
