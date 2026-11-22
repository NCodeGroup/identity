// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NCode.Buffers;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.ControlPlane;

public sealed class ControlPlaneAuthorityEndpointProviderTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private Mock<IOpenIdContextFactory> MockContextFactory { get; }

    private Mock<IMediator> MockMediator { get; }

    private Mock<IControlPlaneAuthority> MockAuthority { get; }

    private Mock<IAmbientTenantAccessor> MockAccessor { get; }

    public ControlPlaneAuthorityEndpointProviderTests()
    {
        MockContextFactory = MockRepository.Create<IOpenIdContextFactory>();
        MockMediator = MockRepository.Create<IMediator>();
        MockAuthority = MockRepository.Create<IControlPlaneAuthority>();
        MockAccessor = MockRepository.Create<IAmbientTenantAccessor>();
    }

    private WebApplication CreateApp(bool isControlPlane)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.Configure<CellOptions>(options => options.IsControlPlane = isControlPlane);
        builder.Services.AddSingleton(MockContextFactory.Object);
        builder.Services.AddSingleton(MockMediator.Object);
        builder.Services.AddSingleton(MockAuthority.Object);
        builder.Services.AddSingleton(MockAccessor.Object);
        return builder.Build();
    }

    private static List<RouteEndpoint> MapEndpoints(WebApplication app)
    {
        new ControlPlaneAuthorityEndpointProvider().Map(app);

        return ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(x => x.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    #region Map Tests

    [Fact]
    public async Task Map_WhenTheCellIsTheControlPlane_MapsThePostRouteHiddenFromTheDescription()
    {
        await using var app = CreateApp(isControlPlane: true);

        var endpoints = MapEndpoints(app);

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("/control-plane/v1/authority", endpoint.RoutePattern.RawText);
        var methods = Assert.IsAssignableFrom<IHttpMethodMetadata>(
            endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()
        );
        Assert.Equal([HttpMethods.Post], methods.HttpMethods);
        var exclude = endpoint.Metadata.GetMetadata<IExcludeFromDescriptionMetadata>();
        Assert.True(exclude?.ExcludeFromDescription);
    }

    [Fact]
    public async Task Map_WhenTheCellIsTheControlPlane_TheRouteRunsTheAuthenticationGateBeforeTheAuthority()
    {
        await using var app = CreateApp(isControlPlane: true);
        var endpoint = Assert.Single(MapEndpoints(app));
        var refusal = TypedResults.NotFound();
        MockContextFactory
            .Setup(x =>
                x.CreateForControlPlaneAsync(
                    It.IsAny<HttpContext>(),
                    MockMediator.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(refusal.AsException("x"))
            .Verifiable();
        var httpContext = new DefaultHttpContext { RequestServices = app.Services };
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.ContentType = "application/json";
        var body = SecureEncoding.UTF8.GetBytes(
            """{"subject":"subject-1","clientId":"cell-eu1"}"""
        );
        httpContext.Request.Body = new MemoryStream(body);
        httpContext.Request.ContentLength = body.Length;
        httpContext.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetectionFeature());
        httpContext.Response.Body = new MemoryStream();
        httpContext.SetEndpoint(endpoint);

        var requestDelegate =
            endpoint.RequestDelegate
            ?? throw new InvalidOperationException("The endpoint has no request delegate.");

        await requestDelegate(httpContext);

        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task Map_WhenTheCellIsNotTheControlPlane_MapsNothing()
    {
        await using var app = CreateApp(isControlPlane: false);

        var endpoints = MapEndpoints(app);

        Assert.Empty(endpoints);
    }

    #endregion

    #region GetAuthorityAsync Tests

    [Fact]
    public async Task GetAuthorityAsync_WhenCalled_ReadsTheAuthorityInAnUnscopedScope()
    {
        var request = new ControlPlaneAuthorityRequest
        {
            Subject = "subject-1",
            ClientId = "cell-eu1",
        };
        var expected = new ControlPlaneAuthority { PrincipalId = "principal-1" };
        var isScopeOpen = false;
        bool? wasScopeOpenWhileReading = null;
        var mockScope = MockRepository.Create<IDisposable>();
        mockScope.Setup(x => x.Dispose()).Callback(() => isScopeOpen = false).Verifiable();
        MockAccessor
            .Setup(x => x.BeginUnscoped("control-plane authority"))
            .Callback(() => isScopeOpen = true)
            .Returns(mockScope.Object)
            .Verifiable();
        MockAuthority
            .Setup(x => x.GetAsync(request, CancellationToken.None))
            .Callback(() => wasScopeOpenWhileReading = isScopeOpen)
            .Returns(new ValueTask<ControlPlaneAuthority>(expected))
            .Verifiable();

        var result = await ControlPlaneAuthorityEndpointProvider.GetAuthorityAsync(
            request,
            MockAuthority.Object,
            MockAccessor.Object,
            CancellationToken.None
        );

        Assert.Same(expected, result.Value);
        Assert.True(wasScopeOpenWhileReading);
        Assert.False(isScopeOpen);
    }

    #endregion

    private sealed class BodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
