// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NCode.Identity.AspNetCore;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Authentication.Tokens.Commands;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.Tenants;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.ControlPlane;

public sealed class ControlPlaneAuthenticationEndpointFilterTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private Mock<IOpenIdContextFactory> MockContextFactory { get; }

    private Mock<IMediator> MockMediator { get; }

    private Mock<OpenIdContext> MockContext { get; }

    private StubAmbientTenantAccessor Accessor { get; } = new();

    private DefaultHttpContext HttpContext { get; }

    private ControlPlaneAuthenticationEndpointFilter Filter { get; }

    public ControlPlaneAuthenticationEndpointFilterTests()
    {
        MockContextFactory = MockRepository.Create<IOpenIdContextFactory>();
        MockMediator = MockRepository.Create<IMediator>();
        MockContext = MockRepository.Create<OpenIdContext>();

        HttpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(MockMediator.Object)
                .BuildServiceProvider(),
        };

        Filter = new ControlPlaneAuthenticationEndpointFilter(MockContextFactory.Object, Accessor);
    }

    private void SetupMaterialization(bool publishContext = true) =>
        MockContextFactory
            .Setup(x =>
                x.CreateForControlPlaneAsync(
                    It.IsAny<HttpContext>(),
                    MockMediator.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                (HttpContext httpContext, IMediator _, CancellationToken _) =>
                {
                    if (publishContext)
                    {
                        var mockFeature = MockRepository.Create<IOpenIdContextFeature>();
                        mockFeature
                            .Setup(x => x.OpenIdContext)
                            .Returns(MockContext.Object)
                            .Verifiable();
                        httpContext.Features.Set(mockFeature.Object);
                    }

                    return new ValueTask<OpenIdContext>(MockContext.Object);
                }
            )
            .Verifiable();

    private void SetupAuthentication(AuthenticateAccessTokenDisposition disposition)
    {
        MockContext.Setup(x => x.Mediator).Returns(MockMediator.Object).Verifiable();

        MockMediator
            .Setup(x =>
                x.SendAsync<AuthenticateAccessTokenCommand, AuthenticateAccessTokenDisposition>(
                    It.Is<AuthenticateAccessTokenCommand>(command =>
                        command.AccessToken == "access-token"
                        && command.RequiredAudiences.SequenceEqual(
                            new[] { OpenIdConstants.SystemResourceServerIdentifiers.ControlPlane }
                        )
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(new ValueTask<AuthenticateAccessTokenDisposition>(disposition))
            .Verifiable();
    }

    private static AuthenticateAccessTokenDisposition Authenticated(string? scope, string? clientId)
    {
        var claims = new List<Claim>();
        if (scope is not null)
            claims.Add(new Claim(JoseClaimNames.Payload.Scope, scope));
        if (clientId is not null)
            claims.Add(new Claim(JoseClaimNames.Payload.ClientId, clientId));

        return new AuthenticateAccessTokenDisposition(
            new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
        );
    }

    private static EndpointFilterDelegate Unreachable =>
        _ => throw new InvalidOperationException("The route must not run.");

    private static int StatusCodeOf(object? result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode
        ?? throw new InvalidOperationException("The result has no status code.");

    #region InvokeAsync Tests

    [Fact]
    public async Task InvokeAsync_WhenBindingFailed_ReturnsEmptyWithoutMaterializingTheContext()
    {
        HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        var result = await Filter.InvokeAsync(context, Unreachable);

        Assert.Same(TypedResults.Empty, result);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheContextCannotBeMaterialized_ReturnsItsResult()
    {
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
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        var result = await Filter.InvokeAsync(context, Unreachable);

        Assert.Same(refusal, result);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheContextIsMissing_Answers401()
    {
        SetupMaterialization(publishContext: false);
        HttpContext.Request.Headers.Authorization = "Bearer access-token";
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        var result = await Filter.InvokeAsync(context, Unreachable);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
        Assert.Equal("Bearer", HttpContext.Response.Headers.WWWAuthenticate.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic access-token")]
    [InlineData("Bearer  ")]
    public async Task InvokeAsync_WithoutABearerToken_Answers401WithAChallenge(
        string? authorization
    )
    {
        SetupMaterialization();
        if (authorization is not null)
            HttpContext.Request.Headers.Authorization = authorization;
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        var result = await Filter.InvokeAsync(context, Unreachable);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
        Assert.Equal("Bearer", HttpContext.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task InvokeAsync_WhenTheTokenDoesNotAuthenticate_Answers401WithAChallenge()
    {
        SetupMaterialization();
        SetupAuthentication(default);
        HttpContext.Request.Headers.Authorization = "Bearer access-token";
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        var result = await Filter.InvokeAsync(context, Unreachable);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
        Assert.Equal("Bearer", HttpContext.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task InvokeAsync_WhenTheTokenLacksTheScope_Answers403()
    {
        SetupMaterialization();
        SetupAuthentication(Authenticated(scope: "openid", clientId: "cell-eu1"));
        HttpContext.Request.Headers.Authorization = "Bearer access-token";
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        var result = await Filter.InvokeAsync(context, Unreachable);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
    }

    [Theory]
    [InlineData("other")]
    [InlineData("cell-")]
    [InlineData(null)]
    public async Task InvokeAsync_WhenTheClientIsNotACell_Answers403(string? clientId)
    {
        SetupMaterialization();
        SetupAuthentication(Authenticated(scope: "control-plane", clientId: clientId));
        HttpContext.Request.Headers.Authorization = "Bearer access-token";
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        var result = await Filter.InvokeAsync(context, Unreachable);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
    }

    [Theory]
    [InlineData("Bearer access-token", "control-plane")]
    [InlineData("DPoP access-token", "openid control-plane")]
    public async Task InvokeAsync_WhenTheCellAuthenticatesWithTheScope_RunsTheRouteInAnUnscopedScope(
        string authorization,
        string scope
    )
    {
        SetupMaterialization();
        SetupAuthentication(Authenticated(scope, clientId: "cell-eu1"));
        HttpContext.Request.Headers.Authorization = authorization;
        var context = EndpointFilterInvocationContext.Create(HttpContext);
        var expected = TypedResults.NoContent();
        bool? isUnscoped = null;
        string? reason = null;
        EndpointFilterDelegate next = _ =>
        {
            isUnscoped = Accessor.IsUnscoped;
            reason = Accessor.UnscopedReason;
            return ValueTask.FromResult<object?>(expected);
        };

        var result = await Filter.InvokeAsync(context, next);

        Assert.Same(expected, result);
        Assert.True(isUnscoped);
        Assert.Equal("control-plane authority", reason);
        Assert.False(Accessor.IsUnscoped);
    }

    #endregion

    private sealed class StubAmbientTenantAccessor : IAmbientTenantAccessor
    {
        private Stack<(string? TenantId, string? Reason)> States { get; } = new();

        public bool IsScoped => TenantId is not null;

        public string? TenantId => States.TryPeek(out var state) ? state.TenantId : null;

        public bool IsUnscoped => States.TryPeek(out var state) && state.Reason is not null;

        public string? UnscopedReason => States.TryPeek(out var state) ? state.Reason : null;

        public IDisposable BeginScope(string tenantId)
        {
            States.Push((tenantId, null));
            return new Scope(States);
        }

        public IDisposable BeginUnscoped(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);

            States.Push((null, reason));
            return new Scope(States);
        }

        private sealed class Scope(Stack<(string? TenantId, string? Reason)> states) : IDisposable
        {
            public void Dispose() => states.Pop();
        }
    }
}
