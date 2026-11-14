// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.ControlPlane;

public sealed class LocalControlPlaneAuthorityTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private const string RootTenantId = "control-plane";
    private const string ClientId = "cell-eu";
    private const string Subject = "principal-1";
    private const string TeamId = "Team-1";
    private const string ParentTeamId = "Parent-Team";

    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }
    private Mock<ITenantResolver> MockTenantResolver { get; }
    private Mock<IAuthorityChainResolver> MockAuthorityChainResolver { get; }
    private Mock<IStoreManager> MockStoreManager { get; }

    private LocalControlPlaneAuthority Authority { get; }

    public LocalControlPlaneAuthorityTests()
    {
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockAmbientTenantAccessor = MockRepository.Create<IAmbientTenantAccessor>();
        MockTenantResolver = MockRepository.Create<ITenantResolver>();
        MockAuthorityChainResolver = MockRepository.Create<IAuthorityChainResolver>();
        MockStoreManager = MockRepository.Create<IStoreManager>();

        Authority = new LocalControlPlaneAuthority(
            MockStoreManagerFactory.Object,
            MockAmbientTenantAccessor.Object,
            MockTenantResolver.Object,
            MockAuthorityChainResolver.Object
        );

        // Every answer is read in the control plane's tenant scope through one store manager.
        MockTenantResolver
            .Setup(x => x.GetControlPlaneTenantId())
            .Returns(RootTenantId)
            .Verifiable();

        var scope = MockRepository.Create<IDisposable>();
        scope.Setup(x => x.Dispose()).Verifiable();
        MockAmbientTenantAccessor
            .Setup(x => x.BeginScope(RootTenantId))
            .Returns(scope.Object)
            .Verifiable();

        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
    }

    private static ControlPlaneAuthorityRequest CreateRequest(
        string subject = Subject,
        string? teamId = null
    ) =>
        new()
        {
            Subject = subject,
            ClientId = ClientId,
            TeamId = teamId,
        };

    private static PersistedClient CreateClient(
        string principalId = "client-principal",
        bool isDisabled = false
    ) =>
        new PersistedClientBuilder
        {
            TenantId = RootTenantId,
            ClientId = ClientId,
            PrincipalId = principalId,
            IsDisabled = isDisabled,
        }.Build();

    private static PersistedRoleAssignment CreateAssignment(
        string roleName,
        string resourceType,
        string resourceId
    ) =>
        new()
        {
            TenantId = RootTenantId,
            AssignmentId = $"{roleName}-{resourceType}-{resourceId}",
            PrincipalId = Subject,
            RoleName = roleName,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ConcurrencyToken = string.Empty,
        };

    private void SetupClient(PersistedClient? client)
    {
        var clientStore = MockRepository.Create<IClientStore>();
        MockStoreManager
            .Setup(x => x.GetStore<IClientStore>())
            .Returns(clientStore.Object)
            .Verifiable();
        clientStore
            .Setup(x => x.GetOrDefaultAsync(ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(client)
            .Verifiable();
    }

    private void SetupFederatedPrincipal(string subject, string? principalId)
    {
        var principal = principalId is null
            ? null
            : new PersistedFederatedPrincipal
            {
                TenantId = RootTenantId,
                PrincipalId = principalId,
                ConcurrencyToken = string.Empty,
            };

        var principalStore = MockRepository.Create<IFederatedPrincipalStore>();
        MockStoreManager
            .Setup(x => x.GetStore<IFederatedPrincipalStore>())
            .Returns(principalStore.Object)
            .Verifiable();
        principalStore
            .Setup(x => x.GetOrDefaultAsync(subject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(principal)
            .Verifiable();
    }

    private void SetupAssignments(string principalId, params PersistedRoleAssignment[] assignments)
    {
        var assignmentStore = MockRepository.Create<IRoleAssignmentStore>();
        MockStoreManager
            .Setup(x => x.GetStore<IRoleAssignmentStore>())
            .Returns(assignmentStore.Object)
            .Verifiable();
        assignmentStore
            .Setup(x => x.GetByPrincipalAsync(principalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignments)
            .Verifiable();
    }

    private void SetupChain(string teamId, params string[] teamIds)
    {
        IReadOnlySet<(string ResourceType, string NormalizedResourceId)> chain = new HashSet<(
            string,
            string
        )>(
            teamIds
                .Select(id => (ResourceNodeTypes.Team, id.ToLowerInvariant()))
                .Append((ResourceNodeTypes.Platform, OpenIdConstants.PlatformResourceId))
        );

        MockAuthorityChainResolver
            .Setup(x =>
                x.GetAncestorNodesAsync(
                    It.Is<IResourceNode>(node =>
                        node.TenantId == RootTenantId
                        && node.ResourceType == ResourceNodeTypes.Team
                        && node.ResourceId == teamId
                    ),
                    MockStoreManager.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(new ValueTask<IReadOnlySet<(string, string)>>(chain))
            .Verifiable();
    }

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_ForAPlatformAdmin_ReturnsThePlatformRolesAndGlobalAdmin()
    {
        SetupClient(CreateClient());
        SetupFederatedPrincipal(Subject, Subject);
        SetupAssignments(
            Subject,
            CreateAssignment(BuiltInRoles.GlobalAdmin, ResourceNodeTypes.Platform, "platform"),
            CreateAssignment(BuiltInRoles.TeamAdmin, ResourceNodeTypes.Platform, "platform"),
            CreateAssignment(BuiltInRoles.GlobalAdmin, ResourceNodeTypes.Platform, "platform"),
            CreateAssignment(BuiltInRoles.Owner, ResourceNodeTypes.Team, TeamId)
        );

        var result = await Authority.GetAsync(CreateRequest(), CancellationToken.None);

        Assert.True(result.IsGlobalAdmin);
        Assert.Equal(Subject, result.PrincipalId);
        Assert.Equal<string>(
            [BuiltInRoles.GlobalAdmin, BuiltInRoles.TeamAdmin],
            result.PlatformRoles
        );
        Assert.Empty(result.TeamRoles);
    }

    [Fact]
    public async Task GetAsync_ForATeamOwnerThroughAnAncestor_ReturnsTheTeamRolesAndNotGlobalAdmin()
    {
        SetupClient(CreateClient());
        SetupFederatedPrincipal(Subject, Subject);
        SetupAssignments(
            Subject,
            CreateAssignment(BuiltInRoles.Owner, ResourceNodeTypes.Team, ParentTeamId),
            CreateAssignment(BuiltInRoles.TeamAdmin, ResourceNodeTypes.Team, "Other-Team"),
            CreateAssignment(BuiltInRoles.TenantAdmin, ResourceNodeTypes.Tenant, TeamId)
        );
        SetupChain(TeamId, TeamId, ParentTeamId);

        var result = await Authority.GetAsync(
            CreateRequest(teamId: TeamId),
            CancellationToken.None
        );

        Assert.False(result.IsGlobalAdmin);
        Assert.Equal(Subject, result.PrincipalId);
        Assert.Empty(result.PlatformRoles);
        Assert.Equal<string>([BuiltInRoles.Owner], result.TeamRoles);
    }

    [Fact]
    public async Task GetAsync_WhenTheSubjectIsTheActingClientsPrincipal_ReturnsItsRoles()
    {
        const string clientPrincipalId = "client-principal";
        SetupClient(CreateClient(clientPrincipalId));
        SetupFederatedPrincipal(clientPrincipalId, null);
        SetupAssignments(
            clientPrincipalId,
            CreateAssignment(BuiltInRoles.TeamAdmin, ResourceNodeTypes.Platform, "platform")
        );

        var result = await Authority.GetAsync(
            CreateRequest(clientPrincipalId),
            CancellationToken.None
        );

        Assert.False(result.IsGlobalAdmin);
        Assert.Equal(clientPrincipalId, result.PrincipalId);
        Assert.Equal<string>([BuiltInRoles.TeamAdmin], result.PlatformRoles);
    }

    [Fact]
    public async Task GetAsync_ForAnUnknownSubject_ReturnsNone()
    {
        SetupClient(CreateClient());
        SetupFederatedPrincipal("unknown", null);

        var result = await Authority.GetAsync(CreateRequest("unknown"), CancellationToken.None);

        Assert.Same(ControlPlaneAuthority.None, result);
    }

    [Fact]
    public async Task GetAsync_ForADisabledActingClient_ReturnsNone()
    {
        SetupClient(CreateClient(isDisabled: true));

        var result = await Authority.GetAsync(CreateRequest(), CancellationToken.None);

        Assert.Same(ControlPlaneAuthority.None, result);
    }

    [Fact]
    public async Task GetAsync_ForAMissingActingClient_ReturnsNone()
    {
        SetupClient(null);

        var result = await Authority.GetAsync(CreateRequest(), CancellationToken.None);

        Assert.Same(ControlPlaneAuthority.None, result);
    }

    #endregion
}
