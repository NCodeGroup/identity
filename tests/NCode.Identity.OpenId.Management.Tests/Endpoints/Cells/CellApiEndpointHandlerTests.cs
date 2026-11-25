// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Identity.Cryptography;
using NCode.Identity.Events.Audit;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Auditing;
using NCode.Identity.OpenId.Management.Contracts.Cells;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Secrets.Persistence;

namespace NCode.Identity.OpenId.Management.Endpoints.Cells;

public sealed class CellApiEndpointHandlerTests : IDisposable
{
    private MockRepository MockRepository { get; }
    private Mock<ICellDirectory> MockCellDirectory { get; }
    private Mock<IAuthorizationService> MockAuthorizationService { get; }
    private Mock<ICryptoService> MockCryptoService { get; }
    private Mock<IPublicJwksReader> MockPublicJwksReader { get; }
    private Mock<IManagementAuditRecorder> MockManagementAuditRecorder { get; }
    private CellApiEndpointHandler Handler { get; }

    public CellApiEndpointHandlerTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockCellDirectory = MockRepository.Create<ICellDirectory>();
        MockAuthorizationService = MockRepository.Create<IAuthorizationService>();
        MockCryptoService = MockRepository.Create<ICryptoService>();
        MockPublicJwksReader = MockRepository.Create<IPublicJwksReader>();
        MockManagementAuditRecorder = MockRepository.Create<IManagementAuditRecorder>();
        Handler = CreateHandler(isControlPlane: true);
    }

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private CellApiEndpointHandler CreateHandler(bool isControlPlane) =>
        new(
            MockCellDirectory.Object,
            MockAuthorizationService.Object,
            MockCryptoService.Object,
            MockPublicJwksReader.Object,
            MockManagementAuditRecorder.Object,
            Options.Create(new CellOptions { IsControlPlane = isControlPlane })
        );

    private static DefaultHttpContext CreateHttpContext(bool authenticated)
    {
        if (!authenticated)
            return new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test"));
        var httpContext = new DefaultHttpContext { User = principal };
        httpContext.Features.Set<IManagementCallerFeature>(
            new DefaultManagementCallerFeature(principal)
        );
        httpContext.Features.Set<IOpenIdContextFeature>(
            Mock.Of<IOpenIdContextFeature>(feature =>
                feature.OpenIdContext
                == Mock.Of<OpenIdContext>(context =>
                    context.Tenant == Mock.Of<OpenIdTenant>(tenant => tenant.TenantId == "root")
                )
            )
        );
        return httpContext;
    }

    private void SetupAuthorization(bool succeeds)
    {
        var result = succeeds ? AuthorizationResult.Success() : AuthorizationResult.Failed();
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                )
            )
            .ReturnsAsync(result)
            .Verifiable();
    }

    private void SetupAudit(string changeType) =>
        MockManagementAuditRecorder
            .Setup(x =>
                x.RecordPlatformChangedAsync(
                    It.IsAny<HttpContext>(),
                    changeType,
                    It.IsAny<JsonElement?>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

    private static RegisterCellRequest CreateRequest() =>
        new()
        {
            Region = "eu-west",
            Kind = CellKinds.Shared,
            Jwks = JsonSerializer.SerializeToElement(new { keys = Array.Empty<object>() }),
        };

    private void SetupJwks(bool valid)
    {
        IReadOnlyList<NCode.Identity.Secrets.Keys.SecretKey>? keys = null;
        string? error = valid ? null : "The key set is not valid.";
        MockPublicJwksReader
            .Setup(x => x.TryRead(It.IsAny<JsonElement>(), out keys, out error))
            .Returns(valid)
            .Verifiable();
    }

    private static CellDescriptor CreateDescriptor(
        string name,
        string region,
        string kind,
        bool isControlPlane = false
    ) =>
        new()
        {
            Name = name,
            Region = region,
            Kind = kind,
            ManagementUrl = $"https://{name}.example.com/api",
            IsControlPlane = isControlPlane,
        };

    #endregion

    #region ListCellsAsync Tests

    [Fact]
    public async Task ListCellsAsync_WhenTheCallerIsAnonymous_ReturnsAuthenticationRequiredWithoutReadingTheDirectory()
    {
        var httpContext = CreateHttpContext(authenticated: false);

        var result = await Handler.ListCellsAsync(httpContext, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
    }

    [Fact]
    public async Task ListCellsAsync_WhenTheCallerIsAuthenticated_ReturnsACellResourcePerCell()
    {
        MockCellDirectory
            .Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                CreateDescriptor("cell-a", "us-west", CellKinds.Shared, isControlPlane: true),
                CreateDescriptor("cell-b", "eu-west", CellKinds.Dedicated),
            ])
            .Verifiable();
        var httpContext = CreateHttpContext(authenticated: true);

        var result = await Handler.ListCellsAsync(httpContext, CancellationToken.None);

        var json = Assert.IsType<JsonHttpResult<List<CellResource>>>(result);
        var cells = Assert.IsType<List<CellResource>>(json.Value);
        Assert.Collection(
            cells,
            first =>
            {
                Assert.Equal("cell-a", first.Name);
                Assert.Equal("us-west", first.Region);
                Assert.Equal(CellKinds.Shared, first.Kind);
                Assert.True(first.IsControlPlane);
            },
            second =>
            {
                Assert.Equal("cell-b", second.Name);
                Assert.Equal("eu-west", second.Region);
                Assert.Equal(CellKinds.Dedicated, second.Kind);
                Assert.False(second.IsControlPlane);
            }
        );
    }

    [Fact]
    public async Task ListCellsAsync_WhenTheDirectoryHasNoCells_ReturnsAnEmptyList()
    {
        MockCellDirectory
            .Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .Verifiable();
        var httpContext = CreateHttpContext(authenticated: true);

        var result = await Handler.ListCellsAsync(httpContext, CancellationToken.None);

        var json = Assert.IsType<JsonHttpResult<List<CellResource>>>(result);
        Assert.Empty(Assert.IsType<List<CellResource>>(json.Value));
    }

    [Fact]
    public async Task ListCellsAsync_WhenTheDirectoryIsUnavailable_Returns503WithRetryAfter()
    {
        MockCellDirectory
            .Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DirectoryUnavailableException("The directory is down."))
            .Verifiable();
        var httpContext = CreateHttpContext(authenticated: true);

        var result = await Handler.ListCellsAsync(httpContext, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal(
            $"{GlobalDirectoryProtocol.UnavailableRetryAfterSeconds}",
            httpContext.Response.Headers.RetryAfter.ToString()
        );
    }

    #endregion

    #region GetCellAsync Tests

    [Fact]
    public async Task GetCellAsync_WhenTheCallerIsNotAuthorized_Returns403WithoutReadingTheDirectory()
    {
        SetupAuthorization(succeeds: false);
        var httpContext = CreateHttpContext(authenticated: true);

        var result = await Handler.GetCellAsync(httpContext, "cell-b", CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task GetCellAsync_WhenTheCellIsRetired_ReturnsItWithoutAManagementUrl()
    {
        SetupAuthorization(succeeds: true);
        var descriptor = CreateDescriptor("cell-b", "eu-west", CellKinds.Shared) with
        {
            IsRegistered = true,
            IsRetired = true,
        };
        MockCellDirectory
            .Setup(x => x.GetOrDefaultAsync("cell-b", It.IsAny<CancellationToken>()))
            .ReturnsAsync(descriptor)
            .Verifiable();
        var httpContext = CreateHttpContext(authenticated: true);

        var result = await Handler.GetCellAsync(httpContext, "cell-b", CancellationToken.None);

        var json = Assert.IsType<JsonHttpResult<CellResource>>(result);
        Assert.True(json.Value!.IsRetired);
        Assert.True(json.Value.IsRegistered);
    }

    [Fact]
    public async Task GetCellAsync_WhenNoCellHasTheName_Returns404()
    {
        SetupAuthorization(succeeds: true);
        MockCellDirectory
            .Setup(x => x.GetOrDefaultAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CellDescriptor?)null)
            .Verifiable();
        var httpContext = CreateHttpContext(authenticated: true);

        var result = await Handler.GetCellAsync(httpContext, "missing", CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    #endregion

    #region RegisterCellAsync Tests

    [Fact]
    public async Task RegisterCellAsync_WhenTheCallerIsNotAuthorized_Returns403()
    {
        SetupAuthorization(succeeds: false);
        var httpContext = CreateHttpContext(authenticated: true);

        var result = await Handler.RegisterCellAsync(
            httpContext,
            "cell-b",
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task RegisterCellAsync_WhenTheKeySetIsInvalid_Returns400WithoutRegistering()
    {
        SetupAuthorization(succeeds: true);
        SetupJwks(valid: false);
        var httpContext = CreateHttpContext(authenticated: true);

        var result = await Handler.RegisterCellAsync(
            httpContext,
            "cell-b",
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Contains("jwks", problem.ProblemDetails.Errors.Keys);
    }

    [Fact]
    public async Task RegisterCellAsync_WhenTheRegistrationIsValid_RegistersAuditsAndReturnsTheCell()
    {
        SetupAuthorization(succeeds: true);
        SetupJwks(valid: true);
        SetupAudit(ResourceChangeTypes.Updated);
        MockCellDirectory
            .Setup(x =>
                x.RegisterAsync(
                    It.Is<CellRegistration>(registration =>
                        registration.Name == "cell-b"
                        && registration.Region == "eu-west"
                        && registration.Kind == CellKinds.Shared
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var httpContext = CreateHttpContext(authenticated: true);

        var result = await Handler.RegisterCellAsync(
            httpContext,
            "cell-b",
            CreateRequest(),
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<CellResource>>(result);
        Assert.Equal("cell-b", json.Value!.Name);
        Assert.True(json.Value.IsRegistered);
    }

    [Fact]
    public async Task RegisterCellAsync_WhenTheDirectoryRefuses_Returns409WithItsDetail()
    {
        SetupAuthorization(succeeds: true);
        SetupJwks(valid: true);
        MockCellDirectory
            .Setup(x =>
                x.RegisterAsync(It.IsAny<CellRegistration>(), It.IsAny<CancellationToken>())
            )
            .ThrowsAsync(new CellDirectoryConflictException("The region differs."))
            .Verifiable();
        var httpContext = CreateHttpContext(authenticated: true);

        var result = await Handler.RegisterCellAsync(
            httpContext,
            "cell-b",
            CreateRequest(),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("The region differs.", problem.ProblemDetails.Detail);
    }

    #endregion

    #region RetireCellAsync Tests

    [Fact]
    public async Task RetireCellAsync_WhenTheCallerIsNotAuthorized_Returns403()
    {
        SetupAuthorization(succeeds: false);
        var httpContext = CreateHttpContext(authenticated: true);

        var result = await Handler.RetireCellAsync(httpContext, "cell-b", CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task RetireCellAsync_WhenTheCellHoldsNoTenants_RetiresAndAudits()
    {
        SetupAuthorization(succeeds: true);
        SetupAudit(ResourceChangeTypes.Deleted);
        MockCellDirectory
            .Setup(x => x.RetireAsync("cell-b", It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var httpContext = CreateHttpContext(authenticated: true);

        var result = await Handler.RetireCellAsync(httpContext, "cell-b", CancellationToken.None);

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task RetireCellAsync_WhenTheCellHoldsTenants_Returns409()
    {
        SetupAuthorization(succeeds: true);
        MockCellDirectory
            .Setup(x => x.RetireAsync("cell-b", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CellDirectoryConflictException("The cell holds tenants."))
            .Verifiable();
        var httpContext = CreateHttpContext(authenticated: true);

        var result = await Handler.RetireCellAsync(httpContext, "cell-b", CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    #endregion

    #region ToCellResource Tests

    [Fact]
    public void ToCellResource_Always_CopiesTheDescriptorWithoutItsManagementUrl()
    {
        var descriptor = CreateDescriptor("cell-a", "us-west", CellKinds.Shared, true);

        var resource = Handler.ToCellResource(descriptor);

        Assert.Equal("cell-a", resource.Name);
        Assert.Equal("us-west", resource.Region);
        Assert.Equal(CellKinds.Shared, resource.Kind);
        Assert.True(resource.IsControlPlane);
    }

    #endregion

    #region Map Tests

    [Theory]
    [InlineData("GET", "/cells", new[] { 401 })]
    [InlineData("GET", "/cells/{name}", new[] { 401, 403, 404, 503 })]
    [InlineData("PUT", "/cells/{name}", new[] { 400, 401, 403, 409, 415, 503 })]
    [InlineData("DELETE", "/cells/{name}", new[] { 401, 403, 409, 503 })]
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
    public void Map_OnTheControlPlane_MapsTheCellsRoute()
    {
        var routes = MapRoutes(CreateHandler(isControlPlane: true));

        Assert.Equal(["/cells", "/cells/{name}", "/cells/{name}", "/cells/{name}"], routes);
    }

    [Fact]
    public void Map_OnAnotherCell_MapsNoRoute()
    {
        var routes = MapRoutes(CreateHandler(isControlPlane: false));

        Assert.Empty(routes);
    }

    private static string[] MapRoutes(CellApiEndpointHandler handler)
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
}
