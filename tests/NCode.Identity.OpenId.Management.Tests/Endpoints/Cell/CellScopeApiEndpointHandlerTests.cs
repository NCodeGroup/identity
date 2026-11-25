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
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts;
using NCode.Identity.OpenId.Management.Contracts.Cells;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.Persistence.Stores;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Persistence.Stores;

namespace NCode.Identity.OpenId.Management.Endpoints.Cell;

public sealed class CellScopeApiEndpointHandlerTests : IDisposable
{
    private const string CellName = "cell-a";

    private MockRepository MockRepository { get; }
    private Mock<IStoreManagerFactory> MockStoreManagerFactory { get; }
    private Mock<IStoreManager> MockStoreManager { get; }
    private Mock<ITenantStore> MockTenantStore { get; }
    private Mock<IAuthorizationService> MockAuthorizationService { get; }
    private Mock<ICryptoService> MockCryptoService { get; }
    private Mock<IPlatformTopology> MockPlatformTopology { get; }
    private CellScopeApiEndpointHandler Handler { get; }

    public CellScopeApiEndpointHandlerTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockStoreManagerFactory = MockRepository.Create<IStoreManagerFactory>();
        MockStoreManager = MockRepository.Create<IStoreManager>();
        MockTenantStore = MockRepository.Create<ITenantStore>();
        MockAuthorizationService = MockRepository.Create<IAuthorizationService>();
        MockCryptoService = MockRepository.Create<ICryptoService>();
        MockPlatformTopology = MockRepository.Create<IPlatformTopology>();
        Handler = CreateHandler(isControlPlane: false);
    }

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private CellScopeApiEndpointHandler CreateHandler(bool isControlPlane) =>
        new(
            MockStoreManagerFactory.Object,
            MockAuthorizationService.Object,
            MockCryptoService.Object,
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

    private static DefaultHttpContext CreateHttpContext(bool authenticated)
    {
        var principal = authenticated
            ? new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"))
            : new ClaimsPrincipal(new ClaimsIdentity());
        var httpContext = new DefaultHttpContext { User = principal };
        if (authenticated)
        {
            httpContext.Features.Set<IManagementCallerFeature>(
                new DefaultManagementCallerFeature(principal)
            );
        }

        httpContext.Features.Set<IOpenIdContextFeature>(
            Mock.Of<IOpenIdContextFeature>(feature =>
                feature.OpenIdContext
                == Mock.Of<OpenIdContext>(context =>
                    context.Tenant == Mock.Of<OpenIdTenant>(tenant => tenant.TenantId == "mgmt")
                )
            )
        );
        return httpContext;
    }

    // Authorizes only a caller who holds the operation at the node of this cell, as a CellAdmin or a GlobalAdmin does;
    // a tenant administrator holds authority at its own tenant node alone.
    private void SetupCellAuthority(bool holdsAuthorityAtTheCell, List<ResourceNode> authorized)
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
                (ClaimsPrincipal _, object? resource, IEnumerable<IAuthorizationRequirement> _) =>
                {
                    var node = Assert.IsType<ResourceNode>(resource);
                    authorized.Add(node);
                    var atTheCell =
                        node.ResourceType == ResourceNodeTypes.Cell && node.ResourceId == CellName;
                    return Task.FromResult(
                        holdsAuthorityAtTheCell && atTheCell
                            ? AuthorizationResult.Success()
                            : AuthorizationResult.Failed()
                    );
                }
            )
            .Verifiable();
    }

    private void SetupTenantPage(PagedResult<PersistedTenant> page)
    {
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.GetStore<ITenantStore>()).Returns(MockTenantStore.Object);
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        MockTenantStore
            .Setup(x => x.GetPageAsync(It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page)
            .Verifiable();
    }

    private static PersistedTenant CreateTenant(string tenantId) =>
        new PersistedTenantBuilder
        {
            TenantId = tenantId,
            ConcurrencyToken = "ct",
            DisplayName = tenantId,
            SettingsConcurrencyToken = "settings-ct",
            SecretsConcurrencyToken = "secrets-ct",
        }.Build();

    private static string[] MapRoutes(CellScopeApiEndpointHandler handler)
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        handler.Map(app);

        return
        [
            .. ((IEndpointRouteBuilder)app)
                .DataSources.SelectMany(dataSource => dataSource.Endpoints)
                .OfType<RouteEndpoint>()
                .Select(endpoint => endpoint.RoutePattern.RawText?.TrimEnd('/') ?? string.Empty)
                .Order(StringComparer.Ordinal),
        ];
    }

    #endregion

    #region GetCellAsync Tests

    [Fact]
    public void GetCellAsync_WhenTheCallerIsAnonymous_ReturnsAuthenticationRequired()
    {
        var result = Handler.GetCellAsync(CreateHttpContext(authenticated: false));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetCellAsync_WhenTheCallerIsAuthenticated_DescribesTheCell(bool hasManagementTenant)
    {
        MockPlatformTopology
            .SetupGet(x => x.HasManagementTenant)
            .Returns(hasManagementTenant)
            .Verifiable();

        var result = Handler.GetCellAsync(CreateHttpContext(authenticated: true));

        var json = Assert.IsType<JsonHttpResult<CellScopeResource>>(result);
        var resource = Assert.IsType<CellScopeResource>(json.Value);
        Assert.Equal(CellName, resource.Name);
        Assert.Equal("eu-west", resource.Region);
        Assert.Equal(CellKinds.Dedicated, resource.Kind);
        Assert.Equal(hasManagementTenant, resource.HasManagementTenant);
    }

    #endregion

    #region ListCellTenantsAsync Tests

    [Fact]
    public async Task ListCellTenantsAsync_WhenTheCallerHoldsAuthorityAtTheCell_ListsOnlyTheTenantsTheStoreHolds()
    {
        var authorized = new List<ResourceNode>();
        SetupCellAuthority(holdsAuthorityAtTheCell: true, authorized);
        SetupTenantPage(
            new PagedResult<PersistedTenant>
            {
                Items = [CreateTenant("tenant-one"), CreateTenant("tenant-two")],
                NextCursor = "next",
            }
        );

        var result = await Handler.ListCellTenantsAsync(
            CreateHttpContext(authenticated: true),
            cursor: null,
            limit: null,
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<CollectionResource<TenantResource>>>(result);
        var collection = Assert.IsType<CollectionResource<TenantResource>>(json.Value);
        Assert.Equal(["tenant-one", "tenant-two"], collection.Items.Select(x => x.TenantId));
        Assert.Equal("next", collection.ContinuationToken);
        var node = Assert.Single(authorized);
        Assert.Equal(ResourceNodeTypes.Cell, node.ResourceType);
        Assert.Equal(CellName, node.ResourceId);
    }

    [Fact]
    public async Task ListCellTenantsAsync_WhenTheCallerAdministersOneTenant_ReturnsForbiddenWithoutReadingTheStore()
    {
        SetupCellAuthority(holdsAuthorityAtTheCell: false, []);

        var result = await Handler.ListCellTenantsAsync(
            CreateHttpContext(authenticated: true),
            cursor: null,
            limit: null,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task ListCellTenantsAsync_WhenTheCallerIsAnonymous_ReturnsAuthenticationRequired()
    {
        SetupCellAuthority(holdsAuthorityAtTheCell: false, []);

        var result = await Handler.ListCellTenantsAsync(
            CreateHttpContext(authenticated: false),
            cursor: null,
            limit: null,
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
    }

    [Fact]
    public async Task ListCellTenantsAsync_WhenALimitAndCursorAreGiven_PassesThemToTheStore()
    {
        SetupCellAuthority(holdsAuthorityAtTheCell: true, []);
        MockStoreManagerFactory
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockStoreManager.Object)
            .Verifiable();
        MockStoreManager.Setup(x => x.GetStore<ITenantStore>()).Returns(MockTenantStore.Object);
        MockStoreManager.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);
        MockTenantStore
            .Setup(x =>
                x.GetPageAsync(
                    It.Is<PageRequest>(page => page.Cursor == "c1" && page.Limit == 7),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(new PagedResult<PersistedTenant> { Items = [], NextCursor = null })
            .Verifiable();

        var result = await Handler.ListCellTenantsAsync(
            CreateHttpContext(authenticated: true),
            cursor: "c1",
            limit: 7,
            CancellationToken.None
        );

        Assert.IsType<JsonHttpResult<CollectionResource<TenantResource>>>(result);
    }

    #endregion

    #region Map Tests

    [Theory]
    [InlineData("GET", "/cell", new[] { 401 })]
    [InlineData("GET", "/cell/tenants", new[] { 400, 401, 403 })]
    public async Task Map_Always_DeclaresAndDescribesEveryProblemARouteAnswers(
        string httpMethod,
        string routePattern,
        int[] statusCodes
    )
    {
        Assert.Equal(
            statusCodes,
            await EndpointProblemMetadata.ReadStatusCodesAsync(
                Handler,
                httpMethod,
                routePattern,
                CancellationToken.None
            )
        );
        Assert.Empty(
            await EndpointProblemMetadata.ReadUndescribedStatusCodesAsync(
                Handler,
                httpMethod,
                routePattern,
                CancellationToken.None
            )
        );
    }

    [Fact]
    public void Map_OnAWorkloadCell_MapsTheCellRoutes()
    {
        var routes = MapRoutes(Handler);

        Assert.Equal(["/cell", "/cell/tenants"], routes);
    }

    [Fact]
    public void Map_OnTheControlPlaneCell_MapsNoRoute()
    {
        var routes = MapRoutes(CreateHandler(isControlPlane: true));

        Assert.Empty(routes);
    }

    #endregion
}
