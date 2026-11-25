// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Identity.OpenId.PrincipalResolution;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;
using NCode.Identity.Settings;

namespace NCode.Identity.OpenId.Management;

public sealed class CellTenantCreationHandlerTests : IDisposable
{
    private const string CellName = "cell-a";
    private const string ManagementTenantId = "cell-a-root";
    private const string TeamId = "acme";
    private const string PrincipalId = "principal-1";

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITeamStore> MockTeamStore { get; }
    private Mock<IRoleAssignmentStore> MockRoleAssignmentStore { get; }
    private Mock<IPrincipalResolver> MockPrincipalResolver { get; }
    private Mock<IEffectiveSettingsResolver> MockEffectiveSettingsResolver { get; }
    private Mock<IPlatformTopology> MockPlatformTopology { get; }
    private Mock<IHttpContextAccessor> MockHttpContextAccessor { get; }
    private Mock<IAmbientTenantAccessor> MockAmbientTenantAccessor { get; }

    public CellTenantCreationHandlerTests()
    {
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockTeamStore = MockRepository.Create<ITeamStore>();
        MockRoleAssignmentStore = MockRepository.Create<IRoleAssignmentStore>();
        MockPrincipalResolver = MockRepository.Create<IPrincipalResolver>();
        MockEffectiveSettingsResolver = MockRepository.Create<IEffectiveSettingsResolver>();
        MockPlatformTopology = MockRepository.Create<IPlatformTopology>();
        MockHttpContextAccessor = MockRepository.Create<IHttpContextAccessor>();
        MockAmbientTenantAccessor = MockRepository.Create<IAmbientTenantAccessor>();
    }

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private CellTenantCreationHandler CreateHandler() =>
        new(
            MockStoreManagerFactory.Object,
            MockPrincipalResolver.Object,
            MockEffectiveSettingsResolver.Object,
            MockPlatformTopology.Object,
            Options.Create(new CellOptions { Name = CellName }),
            Options.Create(new TenantResolutionOptions()),
            MockHttpContextAccessor.Object,
            MockAmbientTenantAccessor.Object
        );

    private static ClaimsPrincipal CreateUser(bool isCellIssued)
    {
        List<Claim> claims = [new("sub", "subject-1"), new("client_id", "cell-console")];
        if (isCellIssued)
            claims.Add(new Claim(ManagementIssuerKinds.ClaimType, ManagementIssuerKinds.Cell));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static PersistedRoleAssignment Assignment(
        string roleName,
        string resourceType,
        string resourceId
    ) =>
        new()
        {
            TenantId = ManagementTenantId,
            AssignmentId = "assign-1",
            PrincipalId = PrincipalId,
            RoleName = roleName,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ConcurrencyToken = string.Empty,
        };

    private async ValueTask<bool> HandleAsync(
        ClaimsPrincipal user,
        OperationAuthorizationRequirement requirement,
        string teamId,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = TenantCreationNode.For(ManagementTenantId, teamId);
        var context = new AuthorizationHandlerContext([requirement], user, resource);

        await CreateHandler().HandleAsync(context);

        return context.HasSucceeded;
    }

    private void SetupDecision(
        PersistedTeam? team,
        IReadOnlyCollection<string>? cells,
        IReadOnlyList<PersistedRoleAssignment>? assignments
    )
    {
        var openIdContext = MockRepository.Create<OpenIdContext>();
        openIdContext
            .Setup(x => x.Tenant)
            .Returns(Mock.Of<OpenIdTenant>(tenant => tenant.TenantId == ManagementTenantId));
        var feature = MockRepository.Create<IOpenIdContextFeature>();
        feature.Setup(x => x.OpenIdContext).Returns(openIdContext.Object).Verifiable();
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set(feature.Object);
        MockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext).Verifiable();

        var scope = MockRepository.Create<IDisposable>();
        scope.Setup(x => x.Dispose()).Verifiable();
        MockAmbientTenantAccessor
            .Setup(x => x.BeginScope(ManagementTenantId))
            .Returns(scope.Object)
            .Verifiable();
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();
        MockStoreManager.Setup(x => x.GetStore<ITeamStore>()).Returns(MockTeamStore.Object);
        MockTeamStore
            .Setup(x => x.GetOrDefaultAsync(TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(team)
            .Verifiable();

        if (cells is not null)
        {
            var settings = MockRepository.Create<IReadOnlySettingCollection>();
            IReadOnlyCollection<string>? granted = cells;
            settings
                .Setup(x => x.TryGetValue(OpenIdSettingKeys.TenantCreationCells, out granted))
                .Returns(true)
                .Verifiable();
            MockEffectiveSettingsResolver
                .Setup(x => x.ResolveTeamAsync(TeamId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(settings.Object)
                .Verifiable();
        }

        if (assignments is not null)
        {
            MockPrincipalResolver
                .Setup(x =>
                    x.ResolvePrincipalIdOrDefaultAsync(
                        openIdContext.Object,
                        It.IsAny<ClaimsPrincipal>(),
                        MockStoreManager.Object,
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync(PrincipalId)
                .Verifiable();
            var unscoped = MockRepository.Create<IDisposable>();
            unscoped.Setup(x => x.Dispose()).Verifiable();
            MockAmbientTenantAccessor
                .Setup(x => x.BeginUnscoped(CellTenantCreationHandler.UnscopedReason))
                .Returns(unscoped.Object)
                .Verifiable();
            MockStoreManager
                .Setup(x => x.GetStore<IRoleAssignmentStore>())
                .Returns(MockRoleAssignmentStore.Object);
            MockRoleAssignmentStore
                .Setup(x => x.GetByPrincipalAsync(PrincipalId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(assignments)
                .Verifiable();
        }
    }

    #endregion

    #region HandleAsync Tests

    [Fact]
    public async Task HandleAsync_WhenACellAdministratorCreatesATenantOfATeamThatGrantsTheCell_Succeeds()
    {
        MockPlatformTopology.Setup(x => x.HasManagementTenant).Returns(true).Verifiable();
        SetupDecision(
            new PersistedTeamBuilder { TeamId = TeamId }.Build(),
            ["cell-b", CellName],
            [Assignment(BuiltInRoles.CellAdmin, ResourceNodeTypes.Cell, CellName)]
        );

        var succeeded = await HandleAsync(
            CreateUser(isCellIssued: true),
            Operations.Create,
            TeamId,
            CancellationToken.None
        );

        Assert.True(succeeded);
    }

    [Fact]
    public async Task HandleAsync_WhenTheTeamGrantsAnotherCell_DoesNotSucceed()
    {
        MockPlatformTopology.Setup(x => x.HasManagementTenant).Returns(true).Verifiable();
        SetupDecision(new PersistedTeamBuilder { TeamId = TeamId }.Build(), ["cell-b"], null);

        var succeeded = await HandleAsync(
            CreateUser(isCellIssued: true),
            Operations.Create,
            TeamId,
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleAsync_WhenTheTeamIsDisabled_DoesNotSucceed()
    {
        MockPlatformTopology.Setup(x => x.HasManagementTenant).Returns(true).Verifiable();
        SetupDecision(
            new PersistedTeamBuilder { TeamId = TeamId, IsDisabled = true }.Build(),
            null,
            null
        );

        var succeeded = await HandleAsync(
            CreateUser(isCellIssued: true),
            Operations.Create,
            TeamId,
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleAsync_WhenTheCallerAdministersOnlyATenant_DoesNotSucceed()
    {
        MockPlatformTopology.Setup(x => x.HasManagementTenant).Returns(true).Verifiable();
        SetupDecision(
            new PersistedTeamBuilder { TeamId = TeamId }.Build(),
            [CellName],
            [Assignment(BuiltInRoles.TenantAdmin, ResourceNodeTypes.Tenant, ManagementTenantId)]
        );

        var succeeded = await HandleAsync(
            CreateUser(isCellIssued: true),
            Operations.Create,
            TeamId,
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleAsync_WhenTheTeamIsThePlatformsOwn_DoesNotSucceedWithoutReadingIt()
    {
        MockPlatformTopology.Setup(x => x.HasManagementTenant).Returns(true).Verifiable();
        var platformTeamId = new TenantResolutionOptions().Root.TeamId;

        var succeeded = await HandleAsync(
            CreateUser(isCellIssued: true),
            Operations.Create,
            platformTeamId,
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleAsync_WhenTheCallerIsAControlPlaneCaller_DoesNotSucceedWithoutReadingAnything()
    {
        var succeeded = await HandleAsync(
            CreateUser(isCellIssued: false),
            Operations.Create,
            TeamId,
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleAsync_WhenTheOperationIsNotACreation_DoesNotSucceedWithoutReadingAnything()
    {
        var succeeded = await HandleAsync(
            CreateUser(isCellIssued: true),
            Operations.Update,
            TeamId,
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    [Fact]
    public async Task HandleAsync_WhenTheCellHasNoManagementTenant_DoesNotSucceedWithoutReadingAnything()
    {
        MockPlatformTopology.Setup(x => x.HasManagementTenant).Returns(false).Verifiable();

        var succeeded = await HandleAsync(
            CreateUser(isCellIssued: true),
            Operations.Create,
            TeamId,
            CancellationToken.None
        );

        Assert.False(succeeded);
    }

    #endregion
}
