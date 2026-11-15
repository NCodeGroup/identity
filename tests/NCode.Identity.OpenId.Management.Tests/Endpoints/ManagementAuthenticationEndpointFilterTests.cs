// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NCode.Identity.OpenId.GlobalDirectory;
using NCode.Identity.OpenId.Management.ControlPlane;

namespace NCode.Identity.OpenId.Management.Endpoints;

public sealed class ManagementAuthenticationEndpointFilterTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private Mock<IManagementTokenValidator> MockTokenValidator { get; }

    private Mock<IControlPlaneAuthority> MockControlPlaneAuthority { get; }

    private DefaultHttpContext HttpContext { get; } = new();

    private ManagementAuthenticationEndpointFilter Filter { get; }

    public ManagementAuthenticationEndpointFilterTests()
    {
        MockTokenValidator = MockRepository.Create<IManagementTokenValidator>();
        MockControlPlaneAuthority = MockRepository.Create<IControlPlaneAuthority>();

        Filter = new ManagementAuthenticationEndpointFilter(
            MockTokenValidator.Object,
            MockControlPlaneAuthority.Object
        );
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
        SetupValidation(ManagementTokenValidation.Authenticated(principal));

        var expected = TypedResults.NoContent();
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(expected);
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        var result = await Filter.InvokeAsync(context, next);

        Assert.Same(expected, result);
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

        bool? isActingClient = null;
        EndpointFilterDelegate next = _ =>
        {
            isActingClient = HttpContext.Features.Get<ManagementActorFeature>()?.IsActingClient;
            return ValueTask.FromResult<object?>(TypedResults.NoContent());
        };
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        await Filter.InvokeAsync(context, next);

        Assert.True(isActingClient);
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
        SetupValidation(ManagementTokenValidation.Authenticated(principal));
        MockControlPlaneAuthority
            .Setup(x =>
                x.GetAsync(It.IsAny<ControlPlaneAuthorityRequest>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new ControlPlaneAuthority { PrincipalId = "admin-1" })
            .Verifiable();

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(TypedResults.NoContent());
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        await Filter.InvokeAsync(context, next);

        Assert.False(HttpContext.Features.Get<ManagementActorFeature>()?.IsActingClient);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheAuthorityIsUnavailable_StoresFalseAndCallsTheRoute()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("sub", "admin-1"), new Claim("client_id", "client-1")],
                "Bearer"
            )
        );
        SetupValidation(ManagementTokenValidation.Authenticated(principal));
        MockControlPlaneAuthority
            .Setup(x =>
                x.GetAsync(It.IsAny<ControlPlaneAuthorityRequest>(), It.IsAny<CancellationToken>())
            )
            .ThrowsAsync(new DirectoryUnavailableException("The control plane is unavailable."))
            .Verifiable();

        var expected = TypedResults.NoContent();
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(expected);
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        var result = await Filter.InvokeAsync(context, next);

        Assert.Same(expected, result);
        Assert.False(HttpContext.Features.Get<ManagementActorFeature>()?.IsActingClient);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheCallerHasNoClientId_StoresFalseWithoutAskingTheAuthority()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("sub", "admin-1")], "Bearer")
        );
        SetupValidation(ManagementTokenValidation.Authenticated(principal));

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(TypedResults.NoContent());
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        await Filter.InvokeAsync(context, next);

        Assert.False(HttpContext.Features.Get<ManagementActorFeature>()?.IsActingClient);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheTokenAuthenticates_ReplacesThePrincipalAnotherSchemeSet()
    {
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity("Cookies"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity("Bearer"));
        SetupValidation(ManagementTokenValidation.Authenticated(principal));

        var expected = TypedResults.NoContent();
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(expected);
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        var result = await Filter.InvokeAsync(context, next);

        Assert.Same(expected, result);
        Assert.Same(principal, HttpContext.User);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheTokenDoesNotAuthenticate_CallsTheRouteAnonymously()
    {
        SetupValidation(ManagementTokenValidation.Unauthenticated);

        var expected = TypedResults.NoContent();
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(expected);
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        var result = await Filter.InvokeAsync(context, next);

        Assert.Same(expected, result);
        Assert.False(HttpContext.User.Identity?.IsAuthenticated);
        Assert.Null(HttpContext.Features.Get<IManagementCallerFeature>());
    }

    [Fact]
    public async Task InvokeAsync_WhenTheTokenDoesNotAuthenticate_DiscardsThePrincipalAnotherSchemeSet()
    {
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity("Cookies"));
        HttpContext.Features.Set<IManagementCallerFeature>(
            new DefaultManagementCallerFeature(HttpContext.User)
        );
        SetupValidation(ManagementTokenValidation.Unauthenticated);

        var expected = TypedResults.NoContent();
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(expected);
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        var result = await Filter.InvokeAsync(context, next);

        Assert.Same(expected, result);
        Assert.False(HttpContext.User.Identity?.IsAuthenticated);
        Assert.Null(HttpContext.Features.Get<IManagementCallerFeature>());
    }

    [Fact]
    public async Task InvokeAsync_WhenTheValidatorIsUnavailable_AnswersServiceUnavailableWithoutCallingTheRoute()
    {
        SetupValidation(ManagementTokenValidation.Unavailable(TimeSpan.FromSeconds(30)));

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(TypedResults.NoContent());
        };
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        var result = await Filter.InvokeAsync(context, next);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal("30", HttpContext.Response.Headers.RetryAfter.ToString());
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheRouteCannotReachTheControlPlane_AnswersServiceUnavailableWithRetryAfter()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity("Bearer"));
        SetupValidation(ManagementTokenValidation.Authenticated(principal));

        EndpointFilterDelegate next = _ =>
            throw new DirectoryUnavailableException("The control plane is unavailable.");
        var context = EndpointFilterInvocationContext.Create(HttpContext);

        var result = await Filter.InvokeAsync(context, next);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal("5", HttpContext.Response.Headers.RetryAfter.ToString());
    }

    #endregion
}
