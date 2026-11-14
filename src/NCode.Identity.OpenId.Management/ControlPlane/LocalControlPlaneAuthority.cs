// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.ControlPlane;

/// <summary>
/// Provides the adapter of <see cref="IControlPlaneAuthority"/> that reads the platform and team role assignments from
/// the cell's own store, in the control plane's tenant. It resolves the token's subject to a principal directly, as
/// <c>DefaultPrincipalResolver</c> does for a server-issued token: a federated principal of that identifier, or the
/// principal of the enabled client that acts for itself.
/// </summary>
// See ADR-1411 and ADR-1466.
internal sealed class LocalControlPlaneAuthority(
    IStoreManagerFactory storeManagerFactory,
    IAmbientTenantAccessor ambientTenantAccessor,
    ITenantResolver tenantResolver,
    IAuthorityChainResolver authorityChainResolver
) : IControlPlaneAuthority
{
    private IStoreManagerFactory StoreManagerFactory { get; } = storeManagerFactory;
    private IAmbientTenantAccessor AmbientTenantAccessor { get; } = ambientTenantAccessor;
    private ITenantResolver TenantResolver { get; } = tenantResolver;
    private IAuthorityChainResolver AuthorityChainResolver { get; } = authorityChainResolver;

    /// <inheritdoc />
    public async ValueTask<ControlPlaneAuthority> GetAsync(
        ControlPlaneAuthorityRequest request,
        CancellationToken cancellationToken
    )
    {
        var rootTenantId = TenantResolver.GetControlPlaneTenantId();

        using var scope = AmbientTenantAccessor.BeginScope(rootTenantId);
        await using var storeManager = await StoreManagerFactory.CreateAsync(cancellationToken);

        var client = await storeManager
            .GetStore<IClientStore>()
            .GetOrDefaultAsync(request.ClientId, cancellationToken);
        if (client is null || client.IsDisabled)
        {
            return ControlPlaneAuthority.None;
        }

        var (principalId, isActingClient) = await FindPrincipalIdAsync(
            request.Subject,
            client,
            storeManager,
            cancellationToken
        );
        if (principalId is null)
        {
            return ControlPlaneAuthority.None;
        }

        var assignments = await storeManager
            .GetStore<IRoleAssignmentStore>()
            .GetByPrincipalAsync(principalId, cancellationToken);

        var platformAssignments = assignments
            .Where(assignment =>
                string.Equals(
                    assignment.ResourceType,
                    ResourceNodeTypes.Platform,
                    StringComparison.Ordinal
                )
            )
            .ToList();

        var teamAssignments = new List<PersistedRoleAssignment>();
        if (request.TeamId is { } teamId)
        {
            var chain = await AuthorityChainResolver.GetAncestorNodesAsync(
                ResourceNode.For(rootTenantId, ResourceNodeTypes.Team, teamId),
                storeManager,
                cancellationToken
            );

            teamAssignments.AddRange(
                assignments.Where(assignment =>
                    string.Equals(
                        assignment.ResourceType,
                        ResourceNodeTypes.Team,
                        StringComparison.Ordinal
                    )
                    && chain.Contains(
                        (ResourceNodeTypes.Team, assignment.ResourceId.ToLowerInvariant())
                    )
                )
            );
        }

        return new ControlPlaneAuthority
        {
            PrincipalId = principalId,
            IsActingClient = isActingClient,
            PlatformRoles = GetRoleNames(platformAssignments),
            TeamRoles = GetRoleNames(teamAssignments),
            IsGlobalAdmin = platformAssignments.Any(assignment =>
                string.Equals(
                    assignment.RoleName,
                    BuiltInRoles.GlobalAdmin,
                    StringComparison.OrdinalIgnoreCase
                )
            ),
        };
    }

    // Returns the principal identifier the subject resolves to, or null when it resolves to none, together with whether
    // it resolved to the principal of the requesting client acting for itself.
    private static async ValueTask<(string? PrincipalId, bool IsActingClient)> FindPrincipalIdAsync(
        string subject,
        PersistedClient actingClient,
        IStoreManager storeManager,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrEmpty(subject))
        {
            return (null, false);
        }

        // The subject may be a server-owned principal identifier, the steady state where the server issued the token.
        var principal = await storeManager
            .GetStore<IFederatedPrincipalStore>()
            .GetOrDefaultAsync(subject, cancellationToken);
        if (principal is not null)
        {
            return (principal.PrincipalId, false);
        }

        // Otherwise the subject may be the principal of the client that acts for itself.
        return string.Equals(subject, actingClient.PrincipalId, StringComparison.Ordinal)
            ? (actingClient.PrincipalId, true)
            : (null, false);
    }

    private static string[] GetRoleNames(IEnumerable<PersistedRoleAssignment> assignments) =>
        [
            .. assignments
                .Select(assignment => assignment.RoleName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal),
        ];
}
