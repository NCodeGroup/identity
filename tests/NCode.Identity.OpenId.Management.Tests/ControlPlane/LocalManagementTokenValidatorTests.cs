// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NCode.Identity.OpenId.Authentication.Tokens.Commands;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.PrincipalResolution;
using NCode.Mediator;

namespace NCode.Identity.OpenId.Management.ControlPlane;

public sealed class LocalManagementTokenValidatorTests : IDisposable
{
    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    private Mock<IClientPrincipalResolver> MockClientPrincipalResolver { get; }

    private Mock<IMediator> MockMediator { get; }

    private Mock<OpenIdContext> MockContext { get; }

    private DefaultHttpContext HttpContext { get; } = new();

    private LocalManagementTokenValidator Validator { get; }

    public LocalManagementTokenValidatorTests()
    {
        MockClientPrincipalResolver = MockRepository.Create<IClientPrincipalResolver>();
        MockMediator = MockRepository.Create<IMediator>();
        MockContext = MockRepository.Create<OpenIdContext>();

        Validator = new LocalManagementTokenValidator(MockClientPrincipalResolver.Object);
    }

    private void SetupOpenIdContext()
    {
        var mockFeature = MockRepository.Create<IOpenIdContextFeature>();
        mockFeature.Setup(x => x.OpenIdContext).Returns(MockContext.Object).Verifiable();
        HttpContext.Features.Set(mockFeature.Object);
    }

    private void SetupAuthentication(AuthenticateAccessTokenDisposition disposition)
    {
        MockContext.Setup(x => x.Mediator).Returns(MockMediator.Object).Verifiable();

        var result = new ValueTask<AuthenticateAccessTokenDisposition>(disposition);
        MockMediator
            .Setup(x =>
                x.SendAsync<AuthenticateAccessTokenCommand, AuthenticateAccessTokenDisposition>(
                    It.IsAny<AuthenticateAccessTokenCommand>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(result)
            .Verifiable();
    }

    #region ValidateAsync Tests

    [Theory]
    [InlineData("Bearer access-token")]
    [InlineData("DPoP access-token")]
    public async Task ValidateAsync_WhenTheTokenAuthenticates_ResolvesTheActingClient(
        string authorization
    )
    {
        SetupOpenIdContext();
        HttpContext.Request.Headers.Authorization = authorization;
        var principal = new ClaimsPrincipal(new ClaimsIdentity("Bearer"));
        var disposition = new AuthenticateAccessTokenDisposition(principal);
        SetupAuthentication(disposition);

        var noClient = new ValueTask<PersistedClient?>((PersistedClient?)null);
        MockClientPrincipalResolver
            .Setup(x =>
                x.GetActingClientOrDefaultAsync(
                    It.IsAny<OpenIdContext>(),
                    principal,
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(noClient)
            .Verifiable();

        var validation = await Validator.ValidateAsync(HttpContext, CancellationToken.None);

        Assert.True(validation.IsAuthenticated);
        Assert.False(validation.IsUnavailable);
        Assert.Same(principal, validation.Principal);
    }

    [Fact]
    public async Task ValidateAsync_WhenTheTokenDoesNotAuthenticate_IsUnauthenticatedAndResolvesNoActingClient()
    {
        SetupOpenIdContext();
        HttpContext.Request.Headers.Authorization = "Bearer access-token";
        var disposition = default(AuthenticateAccessTokenDisposition);
        SetupAuthentication(disposition);

        var validation = await Validator.ValidateAsync(HttpContext, CancellationToken.None);

        Assert.Same(ManagementTokenValidation.Unauthenticated, validation);
        Assert.False(validation.IsAuthenticated);
        Assert.False(validation.IsUnavailable);
        Assert.Null(validation.Principal);
    }

    [Fact]
    public async Task ValidateAsync_WithoutABearerToken_IsUnauthenticatedWithoutAuthenticating()
    {
        SetupOpenIdContext();

        var validation = await Validator.ValidateAsync(HttpContext, CancellationToken.None);

        Assert.Same(ManagementTokenValidation.Unauthenticated, validation);
    }

    [Fact]
    public async Task ValidateAsync_WithAnEmptyBearerToken_IsUnauthenticatedWithoutAuthenticating()
    {
        SetupOpenIdContext();
        HttpContext.Request.Headers.Authorization = "Bearer  ";

        var validation = await Validator.ValidateAsync(HttpContext, CancellationToken.None);

        Assert.Same(ManagementTokenValidation.Unauthenticated, validation);
    }

    [Fact]
    public async Task ValidateAsync_WithoutAnOpenIdContext_IsUnauthenticated()
    {
        HttpContext.Request.Headers.Authorization = "Bearer access-token";

        var validation = await Validator.ValidateAsync(HttpContext, CancellationToken.None);

        Assert.Same(ManagementTokenValidation.Unauthenticated, validation);
    }

    #endregion
}
