// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.PrincipalResolution;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.Authorization;

/// <summary>
/// An <see cref="AuthorizationHandler{TRequirement,TResource}"/> that lets a <c>CellAdmin</c> of a workload cell create
/// a tenant of a team that grants the cell. The creation succeeds when the caller's management token is issued by the
/// cell's own management tenant, the caller holds <see cref="BuiltInRoles.CellAdmin"/> at the cell's node in this cell,
/// and the team, as the cell mirrors it from the directory's feed, is enabled, is not the platform's own team and lists
/// the cell in its effective <c>tenant_creation_cells</c> setting. Every input is the cell's own data, so a cell's
/// administrators create its customers' tenants with the control-plane cell and the platform console down; reserving
/// the tenant still needs the directory. The handler judges only a <see cref="TenantCreationNode"/>, so the grant
/// reaches no other operation at the team.
/// </summary>
// See ADR-1536 (decision 10).
internal sealed class CellTenantCreationHandler(
    IStoreManagerFactory storeManagerFactory,
    IPrincipalResolver principalResolver,
    IEffectiveSettingsResolver effectiveSettingsResolver,
    IPlatformTopology platformTopology,
    IOptions<CellOptions> cellOptions,
    IOptions<TenantResolutionOptions> tenantResolutionOptions,
    IHttpContextAccessor httpContextAccessor,
    IAmbientTenantAccessor ambientTenantAccessor
) : AuthorizationHandler<OperationAuthorizationRequirement, TenantCreationNode>
{
    /// <summary>
    /// The reason of the unscoped scope in which the handler reads the caller's role assignments.
    /// </summary>
    internal const string UnscopedReason = "cell tenant creation authorization";

    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;

    private IPrincipalResolver PrincipalResolver { get; } = principalResolver;

    private IEffectiveSettingsResolver EffectiveSettingsResolver { get; } =
        effectiveSettingsResolver;

    private IPlatformTopology PlatformTopology { get; } = platformTopology;

    private IOptions<CellOptions> CellOptions { get; } = cellOptions;

    private IOptions<TenantResolutionOptions> TenantResolutionOptions { get; } =
        tenantResolutionOptions;

    // ASP.NET Core authorization handlers are invoked by the framework without the request's OpenIdContext in hand, so
    // the context is read from the request being authorized, as OwnershipHandler reads it.
    private IHttpContextAccessor HttpContextAccessor { get; } = httpContextAccessor;

    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OperationAuthorizationRequirement requirement,
        TenantCreationNode resource
    )
    {
        // Only the creation of the tenant is granted, and only to a caller of the cell's own management tenant, which
        // a cell has only when it is a workload cell of a platform with a directory.
        if (
            !string.Equals(requirement.Name, Operations.Create.Name, StringComparison.Ordinal)
            || !context.User.IsCellIssued
            || !PlatformTopology.HasManagementTenant
        )
        {
            return;
        }

        // The platform's own team holds the control-plane tenant and every cell's management tenant, never a
        // customer's tenant.
        var platformTeamId = TenantResolutionOptions.Value.Root.TeamId;
        if (string.Equals(resource.ResourceId, platformTeamId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var httpContext = HttpContextAccessor.HttpContext;
        var openIdContext = httpContext?.GetOpenIdContextOrDefault();
        if (httpContext is null || openIdContext is null)
        {
            return;
        }

        var cancellationToken = httpContext.RequestAborted;
        var cellName = CellOptions.Value.Name;

        using var managementScope = AmbientTenantAccessor.BeginScope(openIdContext.Tenant.TenantId);
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var team = await storeManager
            .GetStore<ITeamStore>()
            .GetOrDefaultAsync(resource.ResourceId, cancellationToken);
        if (team is null)
        {
            return;
        }

        var isGranted = await CellTenantCreationRules.GrantsAsync(
            EffectiveSettingsResolver,
            team,
            cellName,
            platformTeamId,
            cancellationToken
        );
        if (!isGranted)
        {
            return;
        }

        var principalId = await PrincipalResolver.ResolvePrincipalIdOrDefaultAsync(
            openIdContext,
            context.User,
            storeManager,
            cancellationToken
        );
        if (string.IsNullOrEmpty(principalId))
        {
            return;
        }

        IReadOnlyList<PersistedRoleAssignment> assignments;
        // A principal's assignments are stored under the tenant of each node that holds them, so they are read across
        // tenants. See ADR-1018.
        using (AmbientTenantAccessor.BeginUnscoped(UnscopedReason))
        {
            assignments = await storeManager
                .GetStore<IRoleAssignmentStore>()
                .GetByPrincipalAsync(principalId, cancellationToken);
        }

        var isCellAdmin = assignments.Any(assignment =>
            string.Equals(
                assignment.RoleName,
                BuiltInRoles.CellAdmin,
                StringComparison.OrdinalIgnoreCase
            )
            && string.Equals(
                assignment.ResourceType,
                ResourceNodeTypes.Cell,
                StringComparison.Ordinal
            )
            && string.Equals(assignment.ResourceId, cellName, StringComparison.OrdinalIgnoreCase)
        );
        if (isCellAdmin)
        {
            context.Succeed(requirement);
        }
    }
}
