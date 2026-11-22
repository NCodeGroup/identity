// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NCode.Identity.AspNetCore;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.ControlPlane;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.Endpoints;

public sealed class ManagementCallerGateTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private Mock<IOpenIdContextFactory> MockContextFactory { get; }

    private Mock<IMediator> MockMediator { get; }

    private Mock<IManagementTokenValidator> MockTokenValidator { get; }

    private Mock<IControlPlaneAuthority> MockControlPlaneAuthority { get; }

    private DefaultHttpContext HttpContext { get; } = new();

    private ManagementCallerGate Gate { get; }

    public ManagementCallerGateTests()
    {
        MockContextFactory = MockRepository.Create<IOpenIdContextFactory>();
        MockMediator = MockRepository.Create<IMediator>();
        MockTokenValidator = MockRepository.Create<IManagementTokenValidator>();
        MockControlPlaneAuthority = MockRepository.Create<IControlPlaneAuthority>();

        Gate = new ManagementCallerGate(
            MockContextFactory.Object,
            MockMediator.Object,
            MockTokenValidator.Object,
            MockControlPlaneAuthority.Object
        );
    }

    private void SetupMaterialization()
    {
        MockContextFactory
            .Setup(x =>
                x.CreateForControlPlaneAsync(
                    HttpContext,
                    MockMediator.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(Mock.Of<OpenIdContext>())
            .Verifiable();
    }

    private void SetupValidation(ManagementTokenValidation validation)
    {
        MockTokenValidator
            .Setup(x => x.ValidateAsync(HttpContext, It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ManagementTokenValidation>(validation))
            .Verifiable();
    }

    #region InvokeAsync Tests

    [Fact]
    public async Task InvokeAsync_WhenTheTokenAuthenticates_SetsThePrincipalAndTheCallerFeature()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity("Bearer"));
        SetupMaterialization();
        SetupValidation(ManagementTokenValidation.Authenticated(principal));

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        Assert.Null(result);
        Assert.Same(principal, HttpContext.User);
        Assert.Same(principal, HttpContext.Features.Get<IManagementCallerFeature>()?.Caller);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheCallerIsTheActingClient_StoresTheAnswerOnTheActorFeature()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("sub", "principal-1"), new Claim("client_id", "client-1")],
                "Bearer"
            )
        );
        SetupMaterialization();
        SetupValidation(ManagementTokenValidation.Authenticated(principal));
        MockControlPlaneAuthority
            .Setup(x =>
                x.GetAsync(
                    It.Is<ControlPlaneAuthorityRequest>(request =>
                        request.Subject == "principal-1"
                        && request.ClientId == "client-1"
                        && request.TeamId == null
                    ),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                new ControlPlaneAuthority { PrincipalId = "principal-1", IsActingClient = true }
            )
            .Verifiable();

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        Assert.Null(result);
        Assert.True(HttpContext.Features.Get<ManagementActorFeature>()?.IsActingClient);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheCallerIsNotTheActingClient_StoresFalseOnTheActorFeature()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("sub", "admin-1"), new Claim("client_id", "client-1")],
                "Bearer"
            )
        );
        SetupMaterialization();
        SetupValidation(ManagementTokenValidation.Authenticated(principal));
        MockControlPlaneAuthority
            .Setup(x =>
                x.GetAsync(It.IsAny<ControlPlaneAuthorityRequest>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new ControlPlaneAuthority { PrincipalId = "admin-1" })
            .Verifiable();

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        Assert.Null(result);
        Assert.False(HttpContext.Features.Get<ManagementActorFeature>()?.IsActingClient);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheAuthorityIsUnavailable_StoresFalseAndLetsTheRequestThrough()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("sub", "admin-1"), new Claim("client_id", "client-1")],
                "Bearer"
            )
        );
        SetupMaterialization();
        SetupValidation(ManagementTokenValidation.Authenticated(principal));
        MockControlPlaneAuthority
            .Setup(x =>
                x.GetAsync(It.IsAny<ControlPlaneAuthorityRequest>(), It.IsAny<CancellationToken>())
            )
            .ThrowsAsync(new DirectoryUnavailableException("The control plane is unavailable."))
            .Verifiable();

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        Assert.Null(result);
        Assert.False(HttpContext.Features.Get<ManagementActorFeature>()?.IsActingClient);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheCallerHasNoClientId_StoresFalseWithoutAskingTheAuthority()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("sub", "admin-1")], "Bearer")
        );
        SetupMaterialization();
        SetupValidation(ManagementTokenValidation.Authenticated(principal));

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        Assert.Null(result);
        Assert.False(HttpContext.Features.Get<ManagementActorFeature>()?.IsActingClient);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheTokenAuthenticates_ReplacesThePrincipalAnotherSchemeSet()
    {
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity("Cookies"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity("Bearer"));
        SetupMaterialization();
        SetupValidation(ManagementTokenValidation.Authenticated(principal));

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        Assert.Null(result);
        Assert.Same(principal, HttpContext.User);
    }

    [Fact]
    public async Task InvokeAsync_WithoutAValidToken_AnswersAuthenticationRequired()
    {
        SetupMaterialization();
        SetupValidation(ManagementTokenValidation.Unauthenticated);

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, problem.StatusCode);
        Assert.Null(HttpContext.Features.Get<IManagementCallerFeature>());
    }

    [Fact]
    public async Task InvokeAsync_WithoutAValidToken_DoesNotPublishThePrincipalAnotherSchemeSet()
    {
        var other = new ClaimsPrincipal(new ClaimsIdentity("Cookies"));
        HttpContext.User = other;
        SetupMaterialization();
        SetupValidation(ManagementTokenValidation.Unauthenticated);

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        Assert.IsType<ProblemHttpResult>(result);
        Assert.Null(HttpContext.Features.Get<IManagementCallerFeature>());
        Assert.Null(HttpContext.Features.Get<ManagementActorFeature>());
    }

    [Fact]
    public async Task InvokeAsync_WhenTheValidatorIsUnavailable_AnswersServiceUnavailableWithRetryAfter()
    {
        SetupMaterialization();
        SetupValidation(ManagementTokenValidation.Unavailable(TimeSpan.FromSeconds(30)));

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal("30", HttpContext.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task InvokeAsync_WhenTheContextCannotBeMaterialized_ReturnsItsResultWithoutValidatingTheToken()
    {
        var refusal = TypedResults.NotFound();
        MockContextFactory
            .Setup(x =>
                x.CreateForControlPlaneAsync(
                    HttpContext,
                    MockMediator.Object,
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(refusal.AsException("x"))
            .Verifiable();

        var result = await Gate.InvokeAsync(HttpContext, CancellationToken.None);

        Assert.Same(refusal, result);
    }

    #endregion
}
