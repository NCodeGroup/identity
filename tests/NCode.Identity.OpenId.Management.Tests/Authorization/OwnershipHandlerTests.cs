// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Persistence;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management;

public class OwnershipHandlerTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    internal const string TeamId = "team-1";
    private const string TenantId = "tenant-1";
    private const string Subject = "subject-1";
    private const string CallerClientId = "caller-client";

    private static ClaimsPrincipal CreateUser(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    private static ClaimsPrincipal CreateSubject(string subjectId = Subject) =>
        CreateUser(new Claim("sub", subjectId), new Claim("client_id", CallerClientId));

    private static ResourceNode ClientNode(string clientId = "client-1") =>
        ResourceNode.For(TenantId, ResourceNodeTypes.Client, clientId);

    private static ResourceNode TeamNode() =>
        ResourceNode.For(TenantId, ResourceNodeTypes.Team, TeamId);

    // A tenant-plane entity that is not a node, such as a persisted client.
    private sealed record TenantEntity(string TenantId) : ISupportTenantId;

    private static PersistedRoleAssignment Assignment(
        string roleName,
        string resourceType,
        string resourceId,
        string principalId = Subject,
        string tenantId = TenantId
    ) =>
        new()
        {
            TenantId = tenantId,
            AssignmentId = "assign-1",
            PrincipalId = principalId,
            RoleName = roleName,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ConcurrencyToken = string.Empty,
        };

    private static PersistedTenant CreateTenant(string teamId) =>
        new PersistedTenantBuilder
        {
            TenantId = TenantId,
            TeamId = teamId,
            Settings = default,
        }.Build();

    private static PersistedClient CreateClient(
        string tenantId,
        string clientId,
        string owningTeamId
    ) =>
        new PersistedClientBuilder
        {
            TenantId = tenantId,
            ClientId = clientId,
            OwningNodeType = ResourceNodeTypes.Team,
            OwningNodeId = owningTeamId,
        }.Build();

    private static PersistedTeam CreateTeam(string teamId, string[] ancestorTeamIds) =>
        new PersistedTeamBuilder
        {
            TeamId = teamId,
            AncestorTeamIds = ancestorTeamIds,
            Settings = default,
        }.Build();

    // A caller without a sub or client_id claim is judged before the handler asks anything, and a caller the control
    // plane does not know reads no assignments.
    private async ValueTask<bool> HandleAsync(
        IAuthorizationRequirement requirement,
        ISupportTenantId resource,
        ClaimsPrincipal user,
        IReadOnlyList<PersistedRoleAssignment> assignments,
        OwnershipHandlerOptions options,
        CancellationToken cancellationToken
    )
    {
        var factory = MockRepository.Create<IStoreManagerFactory>();
        var authority = MockRepository.Create<IControlPlaneAuthority>();
        var accessor = MockRepository.Create<IHttpContextAccessor>();
        var ambient = MockRepository.Create<IAmbientTenantAccessor>();
        SetupContext(accessor);
        if (options.Decides)
        {
            SetupDecision(factory, authority, ambient, resource, assignments, options);
        }

        var handler = new OwnershipHandler(
            factory.Object,
            authority.Object,
            accessor.Object,
            ambient.Object,
            new DefaultAuthorityChainResolver(ambient.Object)
        );
        var context = new AuthorizationHandlerContext([requirement], user, resource);

        await handler.HandleAsync(context);

        return context.HasSucceeded;
    }

    private ValueTask<bool> HandleAsync(
        IAuthorizationRequirement requirement,
        ISupportTenantId resource,
        ClaimsPrincipal user,
        IReadOnlyList<PersistedRoleAssignment> assignments,
        CancellationToken cancellationToken
    ) =>
        HandleAsync(
            requirement,
            resource,
            user,
            assignments,
            new OwnershipHandlerOptions(),
            cancellationToken
        );

    // The answer of the control plane for a caller that holds these assignments: the platform roles, the roles at the
    // requested team and the teams above it, and the principal the subject resolves to.
    private static ControlPlaneAuthority Answer(
        ControlPlaneAuthorityRequest request,
        IReadOnlyList<PersistedRoleAssignment> assignments,
        OwnershipHandlerOptions options
    )
    {
        if (!options.KnowsPrincipal)
        {
            return ControlPlaneAuthority.None;
        }

        string[] teamChain = request.TeamId is null
            ? []
            : [request.TeamId, .. options.TeamAncestors ?? []];

        var platformRoles = assignments
            .Where(assignment => assignment.ResourceType == ResourceNodeTypes.Platform)
            .Select(assignment => assignment.RoleName)
            .Distinct()
            .ToList();
        var teamRoles = assignments
            .Where(assignment =>
                assignment.ResourceType == ResourceNodeTypes.Team
                && teamChain.Contains(assignment.ResourceId, StringComparer.OrdinalIgnoreCase)
            )
            .Select(assignment => assignment.RoleName)
            .Distinct()
            .ToList();

        return new ControlPlaneAuthority
        {
            PrincipalId = request.Subject,
            PlatformRoles = platformRoles,
            TeamRoles = teamRoles,
            IsGlobalAdmin = platformRoles.Contains(BuiltInRoles.GlobalAdmin),
        };
    }

    // OwnershipHandler reads the ambient OpenIdContext from the request (the one blessed async-local seam).
    private void SetupContext(Mock<IHttpContextAccessor> accessor)
    {
        var openIdContext = MockRepository.Create<OpenIdContext>();
        openIdContext
            .Setup(x => x.Tenant)
            .Returns(Mock.Of<OpenIdTenant>(tenant => tenant.TenantId == TenantId));
        var feature = MockRepository.Create<IOpenIdContextFeature>();
        feature.Setup(x => x.OpenIdContext).Returns(openIdContext.Object).Verifiable();
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set(feature.Object);
        accessor.Setup(x => x.HttpContext).Returns(httpContext).Verifiable();
    }

    private void SetupDecision(
        Mock<IStoreManagerFactory> factory,
        Mock<IControlPlaneAuthority> authority,
        Mock<IAmbientTenantAccessor> ambient,
        ISupportTenantId resource,
        IReadOnlyList<PersistedRoleAssignment> assignments,
        OwnershipHandlerOptions options
    )
    {
        var manager = MockRepository.Create<IStoreManager>();

        // The handler reads the node, its teams and its chain in the request's tenant scope.
        var scope = MockRepository.Create<IDisposable>();
        scope.Setup(x => x.Dispose()).Verifiable();
        ambient.Setup(x => x.BeginScope(TenantId)).Returns(scope.Object).Verifiable();

        factory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(manager.Object)
            .Verifiable();
        manager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();

        // The control plane maps the caller to a control-plane principal id; mirror the token's subject claim.
        authority
            .Setup(x =>
                x.GetAsync(
                    It.Is<ControlPlaneAuthorityRequest>(request =>
                        request.Subject == Subject && request.ClientId == CallerClientId
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (ControlPlaneAuthorityRequest request, CancellationToken _) =>
                    options.Answer is { } answer
                        ? answer(request)
                        : Answer(request, assignments, options)
            )
            .Verifiable();

        var isTeamNode = resource is IResourceNode { ResourceType: ResourceNodeTypes.Team };

        if (options.ReadsTenant)
        {
            var tenantStore = MockRepository.Create<ITenantStore>();
            manager.Setup(x => x.GetStore<ITenantStore>()).Returns(tenantStore.Object).Verifiable();
            tenantStore
                .Setup(x => x.GetOrDefaultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                    options.TenantTeamId is null ? null : CreateTenant(options.TenantTeamId)
                )
                .Verifiable();
        }

        // A client node's owning node is read from the client row in the node's tenant scope.
        if (resource is IResourceNode { ResourceType: ResourceNodeTypes.Client } clientNode)
        {
            if (!string.Equals(clientNode.TenantId, TenantId, StringComparison.Ordinal))
            {
                var clientScope = MockRepository.Create<IDisposable>();
                clientScope.Setup(x => x.Dispose()).Verifiable();
                ambient
                    .Setup(x => x.BeginScope(clientNode.TenantId))
                    .Returns(clientScope.Object)
                    .Verifiable();
            }

            var clientStore = MockRepository.Create<IClientStore>();
            manager.Setup(x => x.GetStore<IClientStore>()).Returns(clientStore.Object).Verifiable();
            clientStore
                .Setup(x =>
                    x.GetOrDefaultAsync(clientNode.ResourceId, It.IsAny<CancellationToken>())
                )
                .ReturnsAsync(
                    options.ClientOwningTeamId is null
                        ? null
                        : CreateClient(
                            clientNode.TenantId,
                            clientNode.ResourceId,
                            options.ClientOwningTeamId
                        )
                )
                .Verifiable();
        }

        // The cell's chain and assignments are read only when the control plane did not already grant the role, so
        // these setups are not verified.
        if (options.KnowsPrincipal)
        {
            var teamId = isTeamNode ? ((IResourceNode)resource).ResourceId : options.TenantTeamId;
            if (teamId is { } storedTeamId && (isTeamNode || options.ReadsTenant))
            {
                var teamStore = MockRepository.Create<ITeamStore>();
                manager.Setup(x => x.GetStore<ITeamStore>()).Returns(teamStore.Object);
                teamStore
                    .Setup(x =>
                        x.GetOrDefaultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
                    )
                    .ReturnsAsync(CreateTeam(storedTeamId, options.TeamAncestors ?? []));
            }

            var store = MockRepository.Create<IRoleAssignmentStore>();
            manager.Setup(x => x.GetStore<IRoleAssignmentStore>()).Returns(store.Object);
            store
                .Setup(x =>
                    x.GetByPrincipalAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
                )
                .ReturnsAsync(assignments.ToList());
        }
    }

    #region HandleRequirementAsync Tests

    [Fact]
    public async Task HandleRequirementAsync_WhenOwnerAtExactNode_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Delete,
            ClientNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.Owner, ResourceNodeTypes.Client, "client-1")],
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenOwnerAtAncestorTenant_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ClientNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.Owner, ResourceNodeTypes.Tenant, TenantId)],
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenAssignmentAtThePlatformRoot_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Delete,
            ClientNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.GlobalAdmin, ResourceNodeTypes.Platform, "platform")],
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTeamAdminAtTheTenantsTeam_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ClientNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.TeamAdmin, ResourceNodeTypes.Team, TeamId)],
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTeamAdminAtTheParentOfTheTenantsTeam_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ClientNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.TeamAdmin, ResourceNodeTypes.Team, "parent-team")],
            new OwnershipHandlerOptions { TeamAncestors = ["parent-team"] },
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenCreateOnAChildTeamNodeAndTeamAdminAtItsParent_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Create,
            TeamNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.TeamAdmin, ResourceNodeTypes.Team, "parent-team")],
            new OwnershipHandlerOptions { ReadsTenant = false, TeamAncestors = ["parent-team"] },
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTeamAdminAtAChildOfTheTenantsTeam_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ClientNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.TeamAdmin, ResourceNodeTypes.Team, "child-team")],
            new OwnershipHandlerOptions { TeamAncestors = ["parent-team"] },
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTenantAdminAndTheResourceIsATeamNode_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            TeamNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.TenantAdmin, ResourceNodeTypes.Tenant, TenantId)],
            new OwnershipHandlerOptions { ReadsTenant = false, TeamAncestors = ["parent-team"] },
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTheTenantIsUnknown_SkipsTheTeamNode()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ClientNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.TeamAdmin, ResourceNodeTypes.Team, TeamId)],
            new OwnershipHandlerOptions { TenantTeamId = null },
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenCreateRequirementAndTeamAdminAtTheNode_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Create,
            ResourceNode.For(TenantId, ResourceNodeTypes.Tenant, TenantId),
            CreateSubject(),
            [Assignment(BuiltInRoles.TeamAdmin, ResourceNodeTypes.Tenant, TenantId)],
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenCreateRequirementAndOwnerAtTheNode_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Create,
            ClientNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.Owner, ResourceNodeTypes.Client, "client-1")],
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenCreateOnTheTeamNodeAndTeamAdminAtThatTeam_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Create,
            TeamNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.TeamAdmin, ResourceNodeTypes.Team, TeamId)],
            new OwnershipHandlerOptions { ReadsTenant = false },
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenCreateOnTheTeamNodeAndOwnerAtThatTeam_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Create,
            TeamNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.Owner, ResourceNodeTypes.Team, TeamId)],
            new OwnershipHandlerOptions { ReadsTenant = false },
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenCreateOnTheTeamNodeAndGlobalAdminAtThePlatform_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Create,
            TeamNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.GlobalAdmin, ResourceNodeTypes.Platform, "platform")],
            new OwnershipHandlerOptions { ReadsTenant = false },
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenCreateOnTheTeamNodeAndTeamAdminAtAnotherTeam_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Create,
            TeamNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.TeamAdmin, ResourceNodeTypes.Team, "team-2")],
            new OwnershipHandlerOptions { ReadsTenant = false },
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTeamAdminAtAnotherTeamOnATenantsClient_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ClientNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.TeamAdmin, ResourceNodeTypes.Team, "team-2")],
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenCreateOnThePlatformNodeAndTeamAdminAtATeam_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Create,
            ResourceNode.For(TenantId, ResourceNodeTypes.Platform, "platform"),
            CreateSubject(),
            [Assignment(BuiltInRoles.TeamAdmin, ResourceNodeTypes.Team, TeamId)],
            new OwnershipHandlerOptions { ReadsTenant = false },
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTenantAdminAtTheTenantOnTheTeamNode_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            TeamNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.TenantAdmin, ResourceNodeTypes.Tenant, TenantId)],
            new OwnershipHandlerOptions { ReadsTenant = false },
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenOwnerOfDifferentNode_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Delete,
            ClientNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.Owner, ResourceNodeTypes.Client, "client-2")],
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenNonManagementRole_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Delete,
            ClientNode(),
            CreateSubject(),
            [Assignment("SecretsManager", ResourceNodeTypes.Client, "client-1")],
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenNoSubjectClaim_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Delete,
            ClientNode(),
            CreateUser(),
            [Assignment(BuiltInRoles.Owner, ResourceNodeTypes.Client, "client-1")],
            new OwnershipHandlerOptions { Decides = false },
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenAnEntityAndCreateAndTeamAdminAtTheTenantsTeam_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Create,
            new TenantEntity(TenantId),
            CreateSubject(),
            [Assignment(BuiltInRoles.TeamAdmin, ResourceNodeTypes.Team, TeamId)],
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenAnEntityAndCreateAndTenantAdminAtTheTenant_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Create,
            new TenantEntity(TenantId),
            CreateSubject(),
            [Assignment(BuiltInRoles.TenantAdmin, ResourceNodeTypes.Tenant, TenantId)],
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenCreateOnTheTeamNodeAndTenantAdminAtATenant_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Create,
            TeamNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.TenantAdmin, ResourceNodeTypes.Tenant, TenantId)],
            new OwnershipHandlerOptions { ReadsTenant = false },
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTenantAdminAtTheTenantOnThePlatformNode_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ResourceNode.For(TenantId, ResourceNodeTypes.Platform, "platform"),
            CreateSubject(),
            [Assignment(BuiltInRoles.TenantAdmin, ResourceNodeTypes.Tenant, TenantId)],
            new OwnershipHandlerOptions { ReadsTenant = false },
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenAnEntityAndCreateAndOwnerAtTheTenant_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Create,
            new TenantEntity(TenantId),
            CreateSubject(),
            [Assignment(BuiltInRoles.Owner, ResourceNodeTypes.Tenant, TenantId)],
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenAnEntityAndUpdateAndOwnerAtTheTenant_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            new TenantEntity(TenantId),
            CreateSubject(),
            [Assignment(BuiltInRoles.Owner, ResourceNodeTypes.Tenant, TenantId)],
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenATenantScopeAndAssignmentAtThePlatform_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.List,
            TenantScopeResource.For(TenantId),
            CreateSubject(),
            [Assignment(BuiltInRoles.GlobalAdmin, ResourceNodeTypes.Platform, "platform")],
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenACustomNodeTypeAndOwnerAtTheTenant_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ResourceNode.For(TenantId, "webhook", "w1"),
            CreateSubject(),
            [Assignment(BuiltInRoles.Owner, ResourceNodeTypes.Tenant, TenantId)],
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenACustomNodeTypeAndTeamAdminAtTheTeam_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ResourceNode.For(TenantId, "webhook", "w1"),
            CreateSubject(),
            [Assignment(BuiltInRoles.TeamAdmin, ResourceNodeTypes.Team, TeamId)],
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenAClientAssignmentBelongsToAnotherTenant_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ResourceNode.For("tenant-b", ResourceNodeTypes.Client, "spa"),
            CreateSubject(),
            [Assignment(BuiltInRoles.Owner, ResourceNodeTypes.Client, "spa", tenantId: "tenant-a")],
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenAClientAssignmentBelongsToTheNodesTenant_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ResourceNode.For("tenant-b", ResourceNodeTypes.Client, "spa"),
            CreateSubject(),
            [Assignment(BuiltInRoles.Owner, ResourceNodeTypes.Client, "spa", tenantId: "tenant-b")],
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenACustomNodeAssignmentBelongsToAnotherTenant_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ResourceNode.For("tenant-b", "webhook", "w1"),
            CreateSubject(),
            [Assignment(BuiltInRoles.Owner, "webhook", "w1", tenantId: "tenant-a")],
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenACustomNodeAssignmentBelongsToTheNodesTenant_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ResourceNode.For("tenant-b", "webhook", "w1"),
            CreateSubject(),
            [Assignment(BuiltInRoles.Owner, "webhook", "w1", tenantId: "tenant-b")],
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenOwnerAtTheTenantNodeStoredUnderAnotherTenant_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ClientNode(),
            CreateSubject(),
            [
                Assignment(
                    BuiltInRoles.Owner,
                    ResourceNodeTypes.Tenant,
                    TenantId,
                    tenantId: "control-plane"
                ),
            ],
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTheControlPlaneDoesNotKnowTheCaller_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ClientNode(),
            CreateSubject(),
            [Assignment(BuiltInRoles.Owner, ResourceNodeTypes.Client, "client-1")],
            new OwnershipHandlerOptions
            {
                KnowsPrincipal = false,
                Answer = _ => ControlPlaneAuthority.None,
            },
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTheControlPlaneAnswersWithoutAPrincipalId_DoesNotSucceed()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ClientNode(),
            CreateSubject(),
            [],
            new OwnershipHandlerOptions
            {
                KnowsPrincipal = false,
                Answer = _ => new ControlPlaneAuthority
                {
                    PlatformRoles = [BuiltInRoles.GlobalAdmin],
                },
            },
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTheControlPlaneIsUnavailable_Throws()
    {
        await Assert.ThrowsAsync<DirectoryUnavailableException>(async () =>
            await HandleAsync(
                Operations.Update,
                ClientNode(),
                CreateSubject(),
                [],
                new OwnershipHandlerOptions
                {
                    KnowsPrincipal = false,
                    Answer = _ => throw new DirectoryUnavailableException("unavailable"),
                },
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenThePlatformRoleComesFromTheControlPlane_Succeeds()
    {
        var succeeded = await HandleAsync(
            Operations.Update,
            ClientNode(),
            CreateSubject(),
            [],
            new OwnershipHandlerOptions
            {
                Answer = request => new ControlPlaneAuthority
                {
                    PrincipalId = request.Subject,
                    PlatformRoles = [BuiltInRoles.GlobalAdmin],
                    IsGlobalAdmin = true,
                },
            },
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenTheNodeIsAPlatformNode_AsksTheControlPlaneOnceWithoutATeam()
    {
        var asked = new List<ControlPlaneAuthorityRequest>();

        await HandleAsync(
            Operations.Update,
            ResourceNode.For(TenantId, ResourceNodeTypes.Platform, "platform"),
            CreateSubject(),
            [],
            new OwnershipHandlerOptions
            {
                ReadsTenant = false,
                Answer = request =>
                {
                    asked.Add(request);
                    return ControlPlaneAuthority.None;
                },
            },
            CancellationToken.None
        );

        var request = Assert.Single(asked);
        Assert.Null(request.TeamId);
        Assert.Equal(Subject, request.Subject);
        Assert.Equal(CallerClientId, request.ClientId);
    }

    [Fact]
    public async Task HandleRequirementAsync_WhenAClientIsOwnedByATeam_AsksTheControlPlaneForTheTenantsTeamAndTheOwningTeam()
    {
        var asked = new List<ControlPlaneAuthorityRequest>();

        await HandleAsync(
            Operations.Update,
            ClientNode(),
            CreateSubject(),
            [],
            new OwnershipHandlerOptions
            {
                ClientOwningTeamId = "team-owner",
                Answer = request =>
                {
                    asked.Add(request);
                    return ControlPlaneAuthority.None;
                },
            },
            CancellationToken.None
        );

        Assert.Equal(
            [TeamId, "team-owner"],
            asked.Select(request => request.TeamId).OrderBy(teamId => teamId == TeamId ? 0 : 1)
        );
    }

    #endregion
}
