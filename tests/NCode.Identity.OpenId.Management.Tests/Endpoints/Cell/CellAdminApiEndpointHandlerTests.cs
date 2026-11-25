// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Identity.Cryptography;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.Endpoints.Cell;

public sealed class CellAdminApiEndpointHandlerTests : IDisposable
{
    private const string CellName = "cell-a";
    private const string ManagementTenantId = "mgmt";

    private MockRepository MockRepository { get; }
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<IAuthorizationService> MockAuthorizationService { get; }
    private Mock<ICryptoService> MockCryptoService { get; }
    private Mock<IResourceOwnershipService> MockResourceOwnershipService { get; }
    private Mock<IControlPlanePrincipalDirectory> MockPrincipalDirectory { get; }
    private Mock<IManagementAuditRecorder> MockAuditRecorder { get; }
    private Mock<IPlatformTopology> MockPlatformTopology { get; }
    private CellAdminApiEndpointHandler Handler { get; }

    public CellAdminApiEndpointHandlerTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockAuthorizationService = MockRepository.Create<IAuthorizationService>();
        MockCryptoService = MockRepository.Create<ICryptoService>();
        MockResourceOwnershipService = MockRepository.Create<IResourceOwnershipService>();
        MockPrincipalDirectory = MockRepository.Create<IControlPlanePrincipalDirectory>();
        MockAuditRecorder = MockRepository.Create<IManagementAuditRecorder>();
        MockPlatformTopology = MockRepository.Create<IPlatformTopology>();
        Handler = CreateHandler(isControlPlane: false);
    }

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private CellAdminApiEndpointHandler CreateHandler(bool isControlPlane) =>
        new(
            MockStoreManagerFactory.Object,
            MockAuthorizationService.Object,
            MockCryptoService.Object,
            MockResourceOwnershipService.Object,
            MockPrincipalDirectory.Object,
            MockAuditRecorder.Object,
            MockPlatformTopology.Object,
            Options.Create(
                new CellOptions
                {
                    Name = CellName,
                    Region = "eu-west",
                    Kind = CellKinds.Dedicated,
                    IsControlPlane = isControlPlane,
                }
            )
        );

    private static DefaultHttpContext CreateHttpContext()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"));
        var httpContext = new DefaultHttpContext { User = principal };
        httpContext.Features.Set<IManagementCallerFeature>(
            new DefaultManagementCallerFeature(principal)
        );
        httpContext.Features.Set<IOpenIdContextFeature>(
            Mock.Of<IOpenIdContextFeature>(feature =>
                feature.OpenIdContext
                == Mock.Of<OpenIdContext>(context =>
                    context.Tenant
                    == Mock.Of<OpenIdTenant>(tenant => tenant.TenantId == ManagementTenantId)
                )
            )
        );
        return httpContext;
    }

    private void SetupHasManagementTenant(bool hasManagementTenant) =>
        MockPlatformTopology
            .SetupGet(x => x.HasManagementTenant)
            .Returns(hasManagementTenant)
            .Verifiable();

    private void SetupAuthorization(
        bool succeeded,
        List<(ResourceNode Node, IAuthorizationRequirement[] Requirements)> seen
    )
    {
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                )
            )
            .Returns(
                (
                    ClaimsPrincipal _,
                    object? resource,
                    IEnumerable<IAuthorizationRequirement> required
                ) =>
                {
                    seen.Add((Assert.IsType<ResourceNode>(resource), [.. required]));
                    return Task.FromResult(
                        succeeded ? AuthorizationResult.Success() : AuthorizationResult.Failed()
                    );
                }
            );
    }

    private void SetupStore()
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
    }

    private void SetupDirectory(params string[] principalIds) =>
        MockPrincipalDirectory
            .Setup(x =>
                x.FindAsync(It.IsAny<ControlPlanePrincipalQuery>(), It.IsAny<CancellationToken>())
            )
            .Returns(
                (ControlPlanePrincipalQuery query, CancellationToken _) =>
                {
                    IReadOnlyList<PrincipalSummaryResource> found =
                    [
                        .. query
                            .PrincipalIds.Where(id => principalIds.Contains(id))
                            .Select(id => PrincipalDirectoryMocks.CreateSummary(id, "user", id)),
                    ];
                    return ValueTask.FromResult(found);
                }
            )
            .Verifiable();

    private static PersistedRoleAssignment CreateAssignment(string principalId) =>
        new()
        {
            TenantId = ManagementTenantId,
            AssignmentId = $"assignment-{principalId}",
            PrincipalId = principalId,
            RoleName = BuiltInRoles.CellAdmin,
            ResourceType = ResourceNodeTypes.Cell,
            ResourceId = CellName,
            ConcurrencyToken = "assignment-ct",
        };

    private void SetupAuditRecorded() =>
        MockAuditRecorder
            .Setup(x =>
                x.RecordResourceChangeAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<RoleAssignmentChangedAuditEvent>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private static string[] MapRoutes(CellAdminApiEndpointHandler handler)
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        handler.Map(app);

        return
        [
            .. ((IEndpointRouteBuilder)app)
                .DataSources.SelectMany(dataSource => dataSource.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(endpoint =>
                    $"{string.Join(',', endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}"
                )
                .Order(StringComparer.Ordinal),
        ];
    }

    #endregion

    #region Map Tests

    [Fact]
    public void Map_WhenTheCellIsNotTheControlPlaneCell_MapsTheAdminRoutes()
    {
        var routes = MapRoutes(Handler);

        Assert.Equal(
            ["DELETE /cell/admins/{principalId}", "GET /cell/admins", "POST /cell/admins"],
            routes
        );
    }

    [Fact]
    public void Map_WhenTheCellIsTheControlPlaneCell_MapsNothing()
    {
        Assert.Empty(MapRoutes(CreateHandler(isControlPlane: true)));
    }

    #endregion

    #region ListAdminsAsync Tests

    [Fact]
    public async Task ListAdminsAsync_WhenAuthorized_ListsTheCellAdminsAtTheCellNode()
    {
        var seen = new List<(ResourceNode Node, IAuthorizationRequirement[] Requirements)>();
        SetupAuthorization(succeeded: true, seen);
        SetupHasManagementTenant(true);
        SetupStore();
        SetupDirectory("principal-1");
        MockResourceOwnershipService
            .Setup(x =>
                x.GetAssignmentsAsync(
                    MockStoreManager.Object,
                    ResourceNodeTypes.Cell,
                    CellName,
                    BuiltInRoles.CellAdmin,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([CreateAssignment("principal-1")])
            .Verifiable();

        var result = await Handler.ListAdminsAsync(CreateHttpContext(), CancellationToken.None);

        var json = Assert.IsType<JsonHttpResult<RoleListResource>>(result);
        var value = Assert.IsType<RoleListResource>(json.Value);
        var admin = Assert.Single(value.Items);
        Assert.Equal("principal-1", admin.PrincipalId);
        Assert.True(value.CanChange);
        Assert.All(
            seen,
            entry =>
            {
                Assert.Equal(ResourceNodeTypes.Cell, entry.Node.ResourceType);
                Assert.Equal(CellName, entry.Node.ResourceId);
            }
        );
    }

    [Fact]
    public async Task ListAdminsAsync_WhenTheCellHasNoManagementTenant_ReturnsNotFound()
    {
        SetupAuthorization(succeeded: true, []);
        SetupHasManagementTenant(false);
        SetupStore();

        var result = await Handler.ListAdminsAsync(CreateHttpContext(), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task ListAdminsAsync_WhenForbidden_ReturnsForbidden()
    {
        SetupAuthorization(succeeded: false, []);

        var result = await Handler.ListAdminsAsync(CreateHttpContext(), CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    #endregion

    #region AddAdminAsync Tests

    [Fact]
    public async Task AddAdminAsync_WhenThePrincipalBelongsToTheManagementTenant_GrantsCellAdminWithUpdate()
    {
        var seen = new List<(ResourceNode Node, IAuthorizationRequirement[] Requirements)>();
        SetupAuthorization(succeeded: true, seen);
        SetupHasManagementTenant(true);
        SetupStore();
        SetupDirectory("principal-1");
        SetupAuditRecorded();
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockResourceOwnershipService
            .Setup(x =>
                x.AddAssignmentAsync(
                    MockStoreManager.Object,
                    ManagementTenantId,
                    ResourceNodeTypes.Cell,
                    CellName,
                    "principal-1",
                    BuiltInRoles.CellAdmin,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new RoleAssignmentGrant
                {
                    Assignment = CreateAssignment("principal-1"),
                    IsAdded = true,
                }
            )
            .Verifiable();

        var result = await Handler.AddAdminAsync(
            CreateHttpContext(),
            new AddRoleHolderRequest { PrincipalId = "principal-1" },
            CancellationToken.None
        );

        var created = Assert.IsType<Created<RoleHolderResource>>(result);
        Assert.Equal("/cell/admins/principal-1", created.Location);
        Assert.Contains(seen, entry => entry.Requirements.Contains(Operations.Update));
    }

    [Fact]
    public async Task AddAdminAsync_WhenThePrincipalIsNotOfTheManagementTenant_ReturnsConflict()
    {
        SetupAuthorization(succeeded: true, []);
        SetupHasManagementTenant(true);
        SetupDirectory();

        var result = await Handler.AddAdminAsync(
            CreateHttpContext(),
            new AddRoleHolderRequest { PrincipalId = "control-plane-user" },
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task AddAdminAsync_WhenForbidden_ReturnsForbiddenWithoutReadingPrincipals()
    {
        SetupAuthorization(succeeded: false, []);

        var result = await Handler.AddAdminAsync(
            CreateHttpContext(),
            new AddRoleHolderRequest { PrincipalId = "principal-1" },
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task AddAdminAsync_WhenTheCellHasNoManagementTenant_ReturnsNotFound()
    {
        SetupAuthorization(succeeded: true, []);
        MockPlatformTopology.SetupGet(x => x.HasManagementTenant).Returns(false).Verifiable();
        SetupStore();

        var result = await Handler.AddAdminAsync(
            CreateHttpContext(),
            new AddRoleHolderRequest { PrincipalId = "principal-1" },
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public async Task AddAdminAsync_WhenTheRequestNamesNoPrincipal_ReturnsBadRequest()
    {
        SetupAuthorization(succeeded: true, []);
        SetupHasManagementTenant(true);

        var result = await Handler.AddAdminAsync(
            CreateHttpContext(),
            new AddRoleHolderRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    #endregion

    #region RemoveAdminAsync Tests

    [Fact]
    public async Task RemoveAdminAsync_WhenRemoved_ReturnsNoContent()
    {
        SetupAuthorization(succeeded: true, []);
        SetupStore();
        SetupAuditRecorded();
        MockStoreManager
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        MockResourceOwnershipService
            .Setup(x =>
                x.RemoveAssignmentAsync(
                    MockStoreManager.Object,
                    ResourceNodeTypes.Cell,
                    CellName,
                    "principal-1",
                    BuiltInRoles.CellAdmin,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([CreateAssignment("principal-1")])
            .Verifiable();

        var result = await Handler.RemoveAdminAsync(
            CreateHttpContext(),
            "principal-1",
            CancellationToken.None
        );

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task RemoveAdminAsync_WhenThePrincipalIsNotAnAdmin_ReturnsNotFound()
    {
        SetupAuthorization(succeeded: true, []);
        SetupStore();
        MockResourceOwnershipService
            .Setup(x =>
                x.RemoveAssignmentAsync(
                    MockStoreManager.Object,
                    ResourceNodeTypes.Cell,
                    CellName,
                    "principal-1",
                    BuiltInRoles.CellAdmin,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync([])
            .Verifiable();

        var result = await Handler.RemoveAdminAsync(
            CreateHttpContext(),
            "principal-1",
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    #endregion
}
