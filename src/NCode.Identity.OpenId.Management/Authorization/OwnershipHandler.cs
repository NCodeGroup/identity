// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Persistence;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.PrincipalResolution;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.Authorization;

/// <summary>
/// An <see cref="AuthorizationHandler{TRequirement,TResource}"/> that grants a principal authority over a tenant-plane
/// resource. Any <see cref="ISupportTenantId"/> resource is judged at its <see cref="IResourceNode"/>; a persisted
/// entity or a <see cref="TenantScopeResource"/> is judged at its tenant's node, and any other node type inherits the
/// tenant's and the team's assignments. A principal holds authority when it has a role assignment at that node or an
/// ancestor: the chain is the node, its tenant, its team, every team above that team, then the platform root. The
/// platform, team and tenant nodes have globally unique identifiers and match on type and identifier; any other node's
/// identifier is unique only within its tenant, so an assignment at it counts only when it belongs to the node's
/// tenant. This realizes ownership: an <c>Owner</c> assignment at an instance lets the principal manage that instance
/// (and everything beneath it) without any tenant-wide role. Ownership grants every operation except
/// <see cref="Operations.Create"/>; creating a new resource is authority at the parent node, held by a
/// <c>TeamAdmin</c>, <c>TenantAdmin</c> or <c>GlobalAdmin</c> assignment at the node or an ancestor. The platform and
/// team roles come from <see cref="IControlPlaneAuthority"/>, asked about the caller's <c>sub</c> and <c>client_id</c>
/// claims and the teams above the node, which also answers the control-plane principal identifier that the cell's
/// tenant-level and resource-level assignments are matched against. A caller whose management token the cell's own
/// management tenant issued never reaches <see cref="IControlPlaneAuthority"/>: its principal is resolved in that
/// tenant and it is authorized only by the assignments the cell stores, never by a platform or team role.
/// </summary>
// See ADR-1411 and ADR-1466.
internal sealed class OwnershipHandler(
    IStoreManagerFactory storeManagerFactory,
    IControlPlaneAuthority controlPlaneAuthority,
    IPrincipalResolver principalResolver,
    IHttpContextAccessor httpContextAccessor,
    IAmbientTenantAccessor ambientTenantAccessor,
    IAuthorityChainResolver authorityChainResolver
) : AuthorizationHandler<IAuthorizationRequirement, ISupportTenantId>
{
    /// <summary>
    /// The reason of the unscoped scope in which the handler reads the caller's role assignments.
    /// </summary>
    internal const string UnscopedReason = "ownership authorization";

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IControlPlaneAuthority ControlPlaneAuthority { get; } = controlPlaneAuthority;
    private IPrincipalResolver PrincipalResolver { get; } = principalResolver;
    private IAuthorityChainResolver AuthorityChainResolver { get; } = authorityChainResolver;

    // ASP.NET Core authorization handlers are invoked by the framework without the request's OpenIdContext in hand, so
    // this is the one blessed place that reads the ambient context from the request rather than receiving it threaded.
    private IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        IAuthorizationRequirement requirement,
        ISupportTenantId resource
    )
    {
        // A node is judged where it is; any other tenant-plane resource is judged at its tenant's node.
        var node =
            resource as IResourceNode
            ?? ResourceNode.For(resource.TenantId, ResourceNodeTypes.Tenant, resource.TenantId);

        var httpContext = HttpContextAccessor.HttpContext;
        var openIdContext = httpContext?.GetOpenIdContextOrDefault();
        if (httpContext is null || openIdContext is null)
        {
            return;
        }

        var cancellationToken = httpContext.RequestAborted;

        // Creating a resource is authority at the parent node, which is the node passed here (ADR-1417, ADR-1411).
        var isCreate =
            requirement is OperationAuthorizationRequirement operation
            && string.Equals(operation.Name, Operations.Create.Name, StringComparison.Ordinal);

        // Resolve the caller to the control-plane principal id and the platform and team roles (ADR-1035, ADR-1411); a
        // caller the control plane does not know has no authority, so there is nothing to look up.
        var subject = context.User.FindFirstValue(JoseClaimNames.Payload.Sub);
        var clientId = context.User.FindFirstValue(JoseClaimNames.Payload.ClientId);
        if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(clientId))
        {
            return;
        }

        // The platform, team and tenant nodes are read in the request's tenant scope.
        // See ADR-1411
        using var controlPlaneScope = AmbientTenantAccessor.BeginScope(
            openIdContext.Tenant.TenantId
        );

        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        // A caller of the cell's own management tenant is known to that tenant alone, and the cell stores everything
        // that authorizes it.
        var isCellCaller = context.User.IsCellIssued;

        string? principalId = null;
        if (isCellCaller)
        {
            principalId = await PrincipalResolver.ResolvePrincipalIdOrDefaultAsync(
                openIdContext,
                context.User,
                storeManager,
                cancellationToken
            );
        }
        else
        {
            var teamIds = await GetTeamIdsAsync(node, storeManager, cancellationToken);
            foreach (var teamId in teamIds)
            {
                var authorityRequest = new ControlPlaneAuthorityRequest
                {
                    Subject = subject,
                    ClientId = clientId,
                    TeamId = teamId,
                };
                var authority = await ControlPlaneAuthority.GetAsync(
                    authorityRequest,
                    cancellationToken
                );

                if (string.IsNullOrEmpty(authority.PrincipalId))
                {
                    continue;
                }

                principalId ??= authority.PrincipalId;

                if (
                    authority
                        .PlatformRoles.Concat(authority.TeamRoles)
                        .Any(roleName =>
                            isCreate ? IsCreationRole(roleName) : IsManagementRole(roleName)
                        )
                )
                {
                    context.Succeed(requirement);
                    return;
                }
            }
        }

        if (string.IsNullOrEmpty(principalId))
        {
            return;
        }

        var ancestorNodes = await AuthorityChainResolver.GetAncestorNodesAsync(
            node,
            storeManager,
            cancellationToken
        );

        var store = storeManager.GetStore<IRoleAssignmentStore>();
        IReadOnlyList<PersistedRoleAssignment> assignments;

        // A principal's assignments are stored under the tenant of each node that holds them, so they are read across
        // tenants. See ADR-1018.
        using (AmbientTenantAccessor.BeginUnscoped(UnscopedReason))
        {
            assignments = await store.GetByPrincipalAsync(principalId, cancellationToken);
        }

        foreach (var assignment in assignments)
        {
            // The platform and team assignments are the control plane's to judge, above.
            if (
                string.Equals(
                    assignment.ResourceType,
                    ResourceNodeTypes.Platform,
                    StringComparison.Ordinal
                )
                || string.Equals(
                    assignment.ResourceType,
                    ResourceNodeTypes.Team,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            var isAuthorizingRole = isCreate
                ? IsCreationRole(assignment.RoleName)
                : IsManagementRole(assignment.RoleName);
            if (!isAuthorizingRole)
            {
                continue;
            }

            // An ancestor (tenant) matches on type and identifier. Any other node's identifier is unique only within
            // its tenant, so an assignment at it counts only when it belongs to the node's tenant.
            // See ADR-1411
            var normalizedResourceId = Normalize(assignment.ResourceId);
            var isAncestor = ancestorNodes.Contains(
                (assignment.ResourceType, normalizedResourceId)
            );
            var isTheNode =
                string.Equals(assignment.ResourceType, node.ResourceType, StringComparison.Ordinal)
                && normalizedResourceId == Normalize(node.ResourceId)
                && Normalize(assignment.TenantId) == Normalize(node.TenantId);
            if (isAncestor || isTheNode)
            {
                context.Succeed(requirement);
                return;
            }
        }
    }

    // The teams the control plane is asked about: a team node's own team, otherwise the tenant's team and, for a
    // client node, the team that owns the client. A platform or cell node has no team, so the control plane is asked
    // once for the platform's roles alone.
    private async ValueTask<IReadOnlyList<string?>> GetTeamIdsAsync(
        IResourceNode node,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        if (string.Equals(node.ResourceType, ResourceNodeTypes.Team, StringComparison.Ordinal))
        {
            return [node.ResourceId];
        }

        var teamIds = new List<string>();
        if (
            !string.Equals(node.ResourceType, ResourceNodeTypes.Platform, StringComparison.Ordinal)
            && !string.Equals(node.ResourceType, ResourceNodeTypes.Cell, StringComparison.Ordinal)
        )
        {
            var tenant = await storeManager
                .GetStore<ITenantStore>()
                .GetOrDefaultAsync(node.TenantId, cancellationToken);
            if (tenant?.TeamId is { } tenantTeamId)
            {
                teamIds.Add(tenantTeamId);
            }

            if (
                string.Equals(node.ResourceType, ResourceNodeTypes.Client, StringComparison.Ordinal)
            )
            {
                // A machine client sits beneath the node that owns it. See ADR-1466.
                using var clientScope = AmbientTenantAccessor.BeginScope(node.TenantId);
                var client = await storeManager
                    .GetStore<IClientStore>()
                    .GetOrDefaultAsync(node.ResourceId, cancellationToken);
                if (
                    client is { OwningNodeId: { } owningTeamId }
                    && string.Equals(
                        client.OwningNodeType,
                        ResourceNodeTypes.Team,
                        StringComparison.Ordinal
                    )
                    && !teamIds.Contains(owningTeamId, StringComparer.OrdinalIgnoreCase)
                )
                {
                    teamIds.Add(owningTeamId);
                }
            }
        }

        return teamIds.Count == 0 ? [null] : [.. teamIds];
    }

    private static bool IsCreationRole(string roleName) =>
        string.Equals(roleName, BuiltInRoles.TeamAdmin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(roleName, BuiltInRoles.TenantAdmin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(roleName, BuiltInRoles.GlobalAdmin, StringComparison.OrdinalIgnoreCase);

    private static bool IsManagementRole(string roleName) =>
        string.Equals(roleName, BuiltInRoles.Owner, StringComparison.OrdinalIgnoreCase)
        || string.Equals(roleName, BuiltInRoles.CellAdmin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(roleName, BuiltInRoles.TeamAdmin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(roleName, BuiltInRoles.TenantAdmin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(roleName, BuiltInRoles.GlobalAdmin, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) => value.ToLowerInvariant();
}
