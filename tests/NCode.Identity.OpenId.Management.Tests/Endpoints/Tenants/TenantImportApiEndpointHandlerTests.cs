// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NCode.Identity.Cryptography;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Management.Authorization;
using NCode.Identity.OpenId.Management.Contracts.Tenants;
using NCode.Identity.OpenId.Management.Transfers;
using NCode.Identity.OpenId.Tenants;

namespace NCode.Identity.OpenId.Management.Endpoints.Tenants;

// See ADR-1514.
public sealed class TenantImportApiEndpointHandlerTests : IDisposable
{
    private const string TenantId = "tenant-1";

    private MockRepository MockRepository { get; }
    private Mock<IAuthorizationService> MockAuthorizationService { get; }
    private Mock<ICryptoService> MockCryptoService { get; }
    private Mock<ITenantTransferKeyService> MockTenantTransferKeyService { get; }
    private TenantImportApiEndpointHandler Handler { get; }

    public TenantImportApiEndpointHandlerTests()
    {
        MockRepository = new MockRepository(MockBehavior.Strict);
        MockAuthorizationService = MockRepository.Create<IAuthorizationService>();
        MockCryptoService = MockRepository.Create<ICryptoService>();
        MockTenantTransferKeyService = MockRepository.Create<ITenantTransferKeyService>();
        Handler = new TenantImportApiEndpointHandler(
            MockAuthorizationService.Object,
            MockCryptoService.Object,
            MockTenantTransferKeyService.Object
        );
    }

    public void Dispose()
    {
        MockRepository.Verify();
    }

    #region Helpers

    private static HttpContext CreateHttpContext(bool authenticated)
    {
        var identity = authenticated
            ? new ClaimsIdentity(authenticationType: "test")
            : new ClaimsIdentity();
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        // The shared environment filter publishes the context in the pipeline; emulate it for direct handler calls.
        httpContext.Features.Set<IOpenIdContextFeature>(
            Mock.Of<IOpenIdContextFeature>(feature =>
                feature.OpenIdContext
                == Mock.Of<OpenIdContext>(context =>
                    context.Tenant == Mock.Of<OpenIdTenant>(tenant => tenant.TenantId == TenantId)
                )
            )
        );

        return httpContext;
    }

    private sealed class CapturedAuthorization
    {
        public object? Resource { get; set; }
        public IAuthorizationRequirement[] Requirements { get; set; } = [];
    }

    private CapturedAuthorization SetupAuthorization(AuthorizationResult result)
    {
        var captured = new CapturedAuthorization();
        MockAuthorizationService
            .Setup(x =>
                x.AuthorizeAsync(
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<object?>(),
                    It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                )
            )
            .Callback(
                (
                    ClaimsPrincipal _,
                    object? resource,
                    IEnumerable<IAuthorizationRequirement> requirements
                ) =>
                {
                    captured.Resource = resource;
                    captured.Requirements = requirements.ToArray();
                }
            )
            .ReturnsAsync(result)
            .Verifiable();
        return captured;
    }

    #endregion

    #region CreateTransferKeyAsync Tests

    [Fact]
    public async Task CreateTransferKeyAsync_WhenAuthorized_ReturnsTheKeyIdAndThePublicKey()
    {
        SetupAuthorization(AuthorizationResult.Success());
        var jwk = JsonSerializer.SerializeToElement(new { kty = "EC", crv = "P-256" });
        MockTenantTransferKeyService
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantTransferKey { KeyId = "kid-1", Jwk = jwk })
            .Verifiable();

        var result = await Handler.CreateTransferKeyAsync(
            CreateHttpContext(authenticated: true),
            CancellationToken.None
        );

        var json = Assert.IsType<JsonHttpResult<TenantTransferKeyResource>>(result);
        var resource = Assert.IsType<TenantTransferKeyResource>(json.Value);
        Assert.Equal("kid-1", resource.Kid);
        Assert.Equal("EC", resource.Jwk.GetProperty("kty").GetString());
        Assert.Equal("P-256", resource.Jwk.GetProperty("crv").GetString());
    }

    [Fact]
    public async Task CreateTransferKeyAsync_Always_RequestsUpdateOnThePlatformNode()
    {
        var captured = SetupAuthorization(AuthorizationResult.Success());
        MockTenantTransferKeyService
            .Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new TenantTransferKey
                {
                    KeyId = "kid-1",
                    Jwk = JsonSerializer.SerializeToElement(1),
                }
            )
            .Verifiable();

        await Handler.CreateTransferKeyAsync(
            CreateHttpContext(authenticated: true),
            CancellationToken.None
        );

        Assert.Same(Operations.Update, Assert.Single(captured.Requirements));
        var node = Assert.IsType<ResourceNode>(captured.Resource);
        Assert.Equal(
            (TenantId, ResourceNodeTypes.Platform, OpenIdConstants.PlatformResourceId),
            (node.TenantId, node.ResourceType, node.ResourceId)
        );
    }

    [Fact]
    public async Task CreateTransferKeyAsync_WhenNotAuthorized_ReturnsForbiddenWithoutIssuingAKey()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var result = await Handler.CreateTransferKeyAsync(
            CreateHttpContext(authenticated: true),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
    }

    [Fact]
    public async Task CreateTransferKeyAsync_WhenTheCallerIsAnonymous_ReturnsUnauthorizedWithoutIssuingAKey()
    {
        SetupAuthorization(AuthorizationResult.Failed());

        var result = await Handler.CreateTransferKeyAsync(
            CreateHttpContext(authenticated: false),
            CancellationToken.None
        );

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
    }

    #endregion

    #region Map Tests

    [Theory]
    [InlineData("POST", "/tenant-imports/transfer-keys", new[] { 401, 403 })]
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
    public void Map_Always_MapsTheTransferKeysRouteOnAnyCell()
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        Handler.Map(app);

        var routes = ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText);

        Assert.Equal(["/tenant-imports/transfer-keys"], routes);
    }

    #endregion
}
