// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NCode.Identity.AspNetCore;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Authentication.Tokens.Commands;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.ControlPlane;

public sealed class ControlPlaneAuthenticationGateTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private Mock<IOpenIdContextFactory> MockContextFactory { get; }

    private Mock<IMediator> MockMediator { get; }

    private Mock<OpenIdContext> MockContext { get; }

    private DefaultHttpContext HttpContext { get; }

    private ControlPlaneAuthenticationGate Gate { get; }

    public ControlPlaneAuthenticationGateTests()
    {
        MockContextFactory = MockRepository.Create<IOpenIdContextFactory>();
        MockMediator = MockRepository.Create<IMediator>();
        MockContext = MockRepository.Create<OpenIdContext>();

        HttpContext = new DefaultHttpContext();

        Gate = new ControlPlaneAuthenticationGate(MockContextFactory.Object, MockMediator.Object);
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
                            new[] { GlobalDirectoryProtocol.ControlPlaneAudience }
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

    private static int StatusCodeOf(IResult? result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode
        ?? throw new InvalidOperationException("The result has no status code.");

    #region InvokeAsync Tests

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

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        Assert.Same(refusal, result);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheContextIsMissing_Answers401()
    {
        SetupMaterialization(publishContext: false);
        HttpContext.Request.Headers.Authorization = "Bearer access-token";

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

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

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
        Assert.Equal("Bearer", HttpContext.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task InvokeAsync_WhenTheTokenDoesNotAuthenticate_Answers401WithAChallenge()
    {
        SetupMaterialization();
        SetupAuthentication(default);
        HttpContext.Request.Headers.Authorization = "Bearer access-token";

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, StatusCodeOf(result));
        Assert.Equal("Bearer", HttpContext.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task InvokeAsync_WhenTheTokenLacksTheScope_Answers403()
    {
        SetupMaterialization();
        SetupAuthentication(
            Authenticated(
                scope: "openid",
                clientId: GlobalDirectoryProtocol.CellClientIdPrefix + "eu1"
            )
        );
        HttpContext.Request.Headers.Authorization = "Bearer access-token";

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
    }

    [Theory]
    [InlineData("other")]
    [InlineData(GlobalDirectoryProtocol.CellClientIdPrefix)]
    [InlineData(null)]
    public async Task InvokeAsync_WhenTheClientIsNotACell_Answers403(string? clientId)
    {
        SetupMaterialization();
        SetupAuthentication(
            Authenticated(scope: GlobalDirectoryProtocol.ControlPlaneScope, clientId: clientId)
        );
        HttpContext.Request.Headers.Authorization = "Bearer access-token";

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusCodeOf(result));
    }

    [Theory]
    [InlineData("Bearer access-token", GlobalDirectoryProtocol.ControlPlaneScope)]
    [InlineData("DPoP access-token", "openid " + GlobalDirectoryProtocol.ControlPlaneScope)]
    public async Task InvokeAsync_WhenTheCellAuthenticatesWithTheScope_ReturnsNull(
        string authorization,
        string scope
    )
    {
        SetupMaterialization();
        SetupAuthentication(
            Authenticated(scope, clientId: GlobalDirectoryProtocol.CellClientIdPrefix + "eu1")
        );
        HttpContext.Request.Headers.Authorization = authorization;

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        Assert.Null(result);
    }

    #endregion
}
