// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.Contracts.Cells;

namespace NCode.Identity.OpenId.Management.Endpoints.Cells;

public sealed class CellApiEndpointHandlerTests : IDisposable
{
    private MockRepository MockRepository { get; }
    private Mock<ICellDirectory> MockCellDirectory { get; }
    private CellApiEndpointHandler Handler { get; }

    public CellApiEndpointHandlerTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockCellDirectory = MockRepository.Create<ICellDirectory>();
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
        return httpContext;
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
        Assert.Equal("5", httpContext.Response.Headers.RetryAfter.ToString());
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

        Assert.Equal(["/cells"], routes);
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
