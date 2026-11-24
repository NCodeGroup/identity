// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Buffers;
using NCode.Identity.AspNetCore;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Authentication.Connections;
using NCode.Identity.OpenId.Authentication.Consent;
using NCode.Identity.OpenId.Authentication.Endpoints.Continue;
using NCode.Identity.OpenId.Authentication.Sessions;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Connections;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Locales;
using NCode.Identity.OpenId.Persistence.DataContracts;
using NCode.Identity.OpenId.PrincipalResolution;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Authentication.Interactive.Tests;

public sealed class InteractiveAuthenticationEndpointHandlerTests : IDisposable
{
    private const string Origin = "https://op.example";

    private MockRepository MockRepository { get; } = new(MockBehavior.Strict);

    public void Dispose() => MockRepository.Verify();

    #region ResolveSafeReturnUrl Tests

    [Fact]
    public void ResolveSafeReturnUrl_WhenLocalPath_ReturnsSamePath()
    {
        var result = InteractiveAuthenticationEndpointHandler.ResolveSafeReturnUrl(
            "/connect/authorize?client_id=mgmt-spa",
            Origin
        );

        Assert.Equal("/connect/authorize?client_id=mgmt-spa", result);
    }

    [Fact]
    public void ResolveSafeReturnUrl_WhenSameOriginAbsoluteUrl_ReturnsSameUrl()
    {
        // The OP's challenge sets an absolute, same-origin continue URL as the return target.
        var returnUrl = $"{Origin}/oauth2/continue?state=abc";

        var result = InteractiveAuthenticationEndpointHandler.ResolveSafeReturnUrl(
            returnUrl,
            Origin
        );

        Assert.Equal(returnUrl, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("//evil.example")] // protocol-relative authority
    [InlineData("/\\evil.example")] // backslash-escaped authority
    [InlineData("/\t/evil.example")] // tab stripped by browsers, leaving a protocol-relative authority
    [InlineData("/\t\\evil.example")] // tab stripped by browsers, leaving a backslash-escaped authority
    [InlineData("/\r\n/evil.example")] // newline stripped by browsers, leaving a protocol-relative authority
    [InlineData("https://evil.example/path")] // cross-origin absolute url
    [InlineData("evil.example")] // no leading slash
    [InlineData("javascript:alert(1)")] // scheme without slash
    public void ResolveSafeReturnUrl_WhenNonLocalOrCrossOrigin_ReturnsRoot(string? returnUrl)
    {
        var result = InteractiveAuthenticationEndpointHandler.ResolveSafeReturnUrl(
            returnUrl,
            Origin
        );

        Assert.Equal("/", result);
    }

    #endregion

    #region BuildMeResponse Tests

    [Fact]
    public void BuildMeResponse_WhenNameClaimPresent_UsesNameAndProjectsSessionClaims()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(JoseClaimNames.Payload.Name, "Ada Lovelace"),
                    new Claim(JoseClaimNames.Payload.PreferredUsername, "ada"),
                    new Claim(JoseClaimNames.Payload.AuthTime, "1700000000"),
                    new Claim(JoseClaimNames.Payload.Amr, "pwd"),
                    new Claim(JoseClaimNames.Payload.Amr, "mfa"),
                    new Claim(JoseClaimNames.Payload.Idp, "local"),
                    new Claim(JoseClaimNames.Payload.Sid, "session-123"),
                ],
                authenticationType: "cookie"
            )
        );

        var result = InteractiveAuthenticationEndpointHandler.BuildMeResponse(
            principal,
            "subject-1",
            emailVerification: true
        );

        Assert.Equal("subject-1", result.Subject);
        Assert.Equal("Ada Lovelace", result.Name);
        Assert.Equal(1700000000L, result.AuthTime);
        Assert.Equal(["pwd", "mfa"], result.Amr);
        Assert.Equal("local", result.Idp);
        Assert.Equal("session-123", result.SessionId);
        Assert.True(result.EmailVerification);
    }

    [Fact]
    public void BuildMeResponse_WhenNoNameClaim_FallsBackToPreferredUsername()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(JoseClaimNames.Payload.PreferredUsername, "ada")],
                authenticationType: "cookie"
            )
        );

        var result = InteractiveAuthenticationEndpointHandler.BuildMeResponse(
            principal,
            "subject-1",
            emailVerification: false
        );

        Assert.Equal("ada", result.Name);
    }

    [Fact]
    public void BuildMeResponse_WhenNoNameOrUsername_FallsBackToSubjectAndOmitsOptionalClaims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], authenticationType: "cookie"));

        var result = InteractiveAuthenticationEndpointHandler.BuildMeResponse(
            principal,
            "subject-1",
            emailVerification: false
        );

        Assert.Equal("subject-1", result.Name);
        Assert.Null(result.AuthTime);
        Assert.Empty(result.Amr);
        Assert.Null(result.Idp);
        Assert.Null(result.SessionId);
        Assert.False(result.EmailVerification);
    }

    [Fact]
    public void BuildMeResponse_WhenAuthTimeIsNotANumber_ReturnsNullAuthTime()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(JoseClaimNames.Payload.AuthTime, "not-a-number")],
                authenticationType: "cookie"
            )
        );

        var result = InteractiveAuthenticationEndpointHandler.BuildMeResponse(
            principal,
            "subject-1",
            emailVerification: false
        );

        Assert.Null(result.AuthTime);
    }

    [Fact]
    public void BuildMeResponse_WhenAuthTimeOutOfRange_ReturnsNullAuthTime()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(JoseClaimNames.Payload.AuthTime, "99999999999999")],
                authenticationType: "cookie"
            )
        );

        var result = InteractiveAuthenticationEndpointHandler.BuildMeResponse(
            principal,
            "subject-1",
            emailVerification: false
        );

        Assert.Null(result.AuthTime);
    }

    [Fact]
    public void BuildMeResponse_WhenAuthTimeIsFractional_ReturnsWholeSeconds()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(JoseClaimNames.Payload.AuthTime, "1700000000.75")],
                authenticationType: "cookie"
            )
        );

        var result = InteractiveAuthenticationEndpointHandler.BuildMeResponse(
            principal,
            "subject-1",
            emailVerification: false
        );

        Assert.Equal(1700000000L, result.AuthTime);
    }

    #endregion

    #region PostForgotPasswordAsync Tests

    [Fact]
    public async Task PostForgotPasswordAsync_WhenTheFormCarriesALanguageTag_MailsInThatLocale()
    {
        var mockCatalog = MockRepository.Create<IUiLocaleCatalog>();
        mockCatalog.SetupGet(x => x.Locales).Returns([TestLocales.Xx, TestLocales.Yy]).Verifiable();
        IUiLocale? carried = TestLocales.Yy;
        mockCatalog.Setup(x => x.TryGet("yy", out carried)).Returns(true).Verifiable();

        var mockSettings = MockRepository.Create<IReadOnlySettingCollection>();
        // The page the end user saw was yy, so the mail is worded in yy, although the browser prefers xx.
        mockCatalog
            .Setup(x =>
                x.Select(
                    mockSettings.Object,
                    It.Is<IEnumerable<string>>(preferences =>
                        preferences.SequenceEqual(new[] { "yy", "xx" }, StringComparer.Ordinal)
                    )
                )
            )
            .Returns(TestLocales.Yy)
            .Verifiable();

        var mockProvider = MockRepository.Create<IReadOnlySettingCollectionProvider>();
        mockProvider.SetupGet(x => x.Collection).Returns(mockSettings.Object).Verifiable();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        mockTenant.SetupGet(x => x.SettingsProvider).Returns(mockProvider.Object).Verifiable();
        var mockContext = MockRepository.Create<OpenIdContext>();
        mockContext.SetupGet(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        var mockFeature = MockRepository.Create<IOpenIdContextFeature>();
        mockFeature.SetupGet(x => x.OpenIdContext).Returns(mockContext.Object).Verifiable();

        var connection = CreateConnection();
        var mockConnections = MockRepository.Create<ISignInConnectionService>();
        mockConnections
            .Setup(x =>
                x.SelectAsync(mockContext.Object, null, "local", It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new ConnectionSelection { Connection = connection })
            .Verifiable();

        var mockResetter = MockRepository.Create<IInteractivePasswordResetter>();
        mockResetter
            .Setup(x =>
                x.SendResetCodeAsync(
                    mockContext.Object,
                    connection,
                    "alice",
                    TestLocales.Yy,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync("flow-1")
            .Verifiable();

        var mockTenantResolver = MockRepository.Create<ITenantResolver>();
        mockTenantResolver
            .Setup(x => x.GetTenantRoute())
            .Returns(RoutePatternFactory.Parse("/{tenantId}"))
            .Verifiable();

        using var serviceProvider = new ServiceCollection().AddRouting().BuildServiceProvider();
        using var bundle = new SinglePageBundle<InteractiveBundleShell>(null, _ => null);
        var handler = CreateHandler(
            mockCatalog.Object,
            mockTenantResolver.Object,
            serviceProvider,
            bundle,
            passwordResetter: mockResetter.Object,
            signInConnectionService: mockConnections.Object
        );

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set(mockFeature.Object);
        httpContext.Request.RouteValues["tenantId"] = "acme";
        httpContext.Request.Headers.AcceptLanguage = "xx";

        var result = await handler.PostForgotPasswordAsync(
            httpContext,
            "alice",
            connection: "local",
            returnUrl: null,
            uiLocales: "yy"
        );

        var redirect = Assert.IsType<RedirectHttpResult>(result);
        Assert.Equal("/acme/account/password/reset?flow=flow-1&ui_locales=yy", redirect.Url);
    }

    [Fact]
    public async Task PostForgotPasswordAsync_WhenTheConnectionIsRefused_RedirectsBackWithInvalidRequest()
    {
        var mockCatalog = MockRepository.Create<IUiLocaleCatalog>();
        mockCatalog.SetupGet(x => x.Locales).Returns([TestLocales.Xx]).Verifiable();
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockFeature = MockRepository.Create<IOpenIdContextFeature>();
        mockFeature.SetupGet(x => x.OpenIdContext).Returns(mockContext.Object).Verifiable();

        var mockConnections = MockRepository.Create<ISignInConnectionService>();
        mockConnections
            .Setup(x =>
                x.SelectAsync(mockContext.Object, null, "missing", It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(
                new ConnectionSelection { ErrorDescription = "The connection is not offered." }
            )
            .Verifiable();

        var mockTenantResolver = MockRepository.Create<ITenantResolver>();
        mockTenantResolver
            .Setup(x => x.GetTenantRoute())
            .Returns(RoutePatternFactory.Parse("/{tenantId}"))
            .Verifiable();

        using var serviceProvider = new ServiceCollection().AddRouting().BuildServiceProvider();
        using var bundle = new SinglePageBundle<InteractiveBundleShell>(null, _ => null);
        var handler = CreateHandler(
            mockCatalog.Object,
            mockTenantResolver.Object,
            serviceProvider,
            bundle,
            signInConnectionService: mockConnections.Object
        );

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set(mockFeature.Object);
        httpContext.Request.RouteValues["tenantId"] = "acme";

        var result = await handler.PostForgotPasswordAsync(
            httpContext,
            "alice",
            connection: "missing",
            returnUrl: null,
            uiLocales: null
        );

        var redirect = Assert.IsType<RedirectHttpResult>(result);
        Assert.Equal("/acme/account/password/forgot?error=invalid_request", redirect.Url);
    }

    [Fact]
    public async Task PostForgotPasswordAsync_WhenTheSelectedConnectionDisablesReset_IsNotFound()
    {
        var mockCatalog = MockRepository.Create<IUiLocaleCatalog>();
        mockCatalog.SetupGet(x => x.Locales).Returns([TestLocales.Xx]).Verifiable();
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockFeature = MockRepository.Create<IOpenIdContextFeature>();
        mockFeature.SetupGet(x => x.OpenIdContext).Returns(mockContext.Object).Verifiable();

        var connection = CreateConnection(passwordResetEnabled: false);
        var mockConnections = MockRepository.Create<ISignInConnectionService>();
        mockConnections
            .Setup(x =>
                x.SelectAsync(mockContext.Object, null, "local", It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(new ConnectionSelection { Connection = connection })
            .Verifiable();

        var mockTenantResolver = MockRepository.Create<ITenantResolver>();
        using var serviceProvider = new ServiceCollection().AddRouting().BuildServiceProvider();
        using var bundle = new SinglePageBundle<InteractiveBundleShell>(null, _ => null);
        var handler = CreateHandler(
            mockCatalog.Object,
            mockTenantResolver.Object,
            serviceProvider,
            bundle,
            signInConnectionService: mockConnections.Object
        );

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set(mockFeature.Object);

        var result = await handler.PostForgotPasswordAsync(
            httpContext,
            "alice",
            connection: "local",
            returnUrl: null,
            uiLocales: null
        );

        Assert.IsType<NotFound>(result);
    }

    #endregion

    #region GetForgotPasswordPageAsync Tests

    [Fact]
    public async Task GetForgotPasswordPageAsync_WhenNoOfferedConnectionEnablesReset_IsNotFound()
    {
        var mockCatalog = MockRepository.Create<IUiLocaleCatalog>();
        mockCatalog.SetupGet(x => x.Locales).Returns([TestLocales.Xx]).Verifiable();
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockFeature = MockRepository.Create<IOpenIdContextFeature>();
        mockFeature.SetupGet(x => x.OpenIdContext).Returns(mockContext.Object).Verifiable();

        var connection = CreateConnection(passwordResetEnabled: false);
        var mockConnections = MockRepository.Create<ISignInConnectionService>();
        mockConnections
            .Setup(x => x.GetOfferedAsync(mockContext.Object, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new SignInConnections { Offered = [connection], Preselected = connection }
            )
            .Verifiable();

        var mockTenantResolver = MockRepository.Create<ITenantResolver>();
        using var serviceProvider = new ServiceCollection().AddRouting().BuildServiceProvider();
        using var bundle = new SinglePageBundle<InteractiveBundleShell>(null, _ => null);
        var handler = CreateHandler(
            mockCatalog.Object,
            mockTenantResolver.Object,
            serviceProvider,
            bundle,
            signInConnectionService: mockConnections.Object
        );

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set(mockFeature.Object);

        var result = await handler.GetForgotPasswordPageAsync(httpContext);

        Assert.IsType<NotFound>(result);
    }

    #endregion

    #region GetForgotPasswordHref Tests

    [Fact]
    public void GetForgotPasswordHref_WhenOnlyANonPreselectedConnectionEnablesReset_ShowsTheLinkWithoutAName()
    {
        var off = CreateConnection(passwordResetEnabled: false);
        var on = CreateConnection(passwordResetEnabled: true);

        var href = InteractiveAuthenticationEndpointHandler.GetForgotPasswordHref(
            new SignInConnections { Offered = [off, on], Preselected = off },
            "",
            null
        );

        Assert.NotNull(href);
        Assert.DoesNotContain("connection=", href);
    }

    [Fact]
    public void GetForgotPasswordHref_WhenEveryOfferedConnectionDisablesReset_HidesTheLink()
    {
        var first = CreateConnection(passwordResetEnabled: false);
        var second = CreateConnection(passwordResetEnabled: false);

        var href = InteractiveAuthenticationEndpointHandler.GetForgotPasswordHref(
            new SignInConnections { Offered = [first, second], Preselected = null },
            "",
            null
        );

        Assert.Null(href);
    }

    [Fact]
    public void GetForgotPasswordHref_WhenThePreselectedConnectionEnablesReset_CarriesItsName()
    {
        var on = CreateConnection(passwordResetEnabled: true);

        var href = InteractiveAuthenticationEndpointHandler.GetForgotPasswordHref(
            new SignInConnections { Offered = [on], Preselected = on },
            "",
            null
        );

        Assert.NotNull(href);
        Assert.Contains("connection=local", href);
    }

    #endregion

    #region GetEmailPageAsync Tests

    [Fact]
    public async Task GetEmailPageAsync_WhenThePrincipalsConnectionDisablesVerification_IsNotFound()
    {
        var mockCatalog = MockRepository.Create<IUiLocaleCatalog>();
        mockCatalog.SetupGet(x => x.Locales).Returns([TestLocales.Xx]).Verifiable();
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockFeature = MockRepository.Create<IOpenIdContextFeature>();
        mockFeature.SetupGet(x => x.OpenIdContext).Returns(mockContext.Object).Verifiable();

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(JoseClaimNames.Payload.Sid, "session-1"),
                    new Claim(ConnectionClaimTypes.Connection, "connection-1"),
                ],
                authenticationType: "cookie"
            )
        );
        var mockAuthentication = MockRepository.Create<IAuthenticationService>();
        mockAuthentication
            .Setup(x =>
                x.AuthenticateAsync(
                    It.IsAny<HttpContext>(),
                    InteractiveAuthenticationDefaults.CookieSchemeName
                )
            )
            .ReturnsAsync(
                AuthenticateResult.Success(
                    new AuthenticationTicket(
                        principal,
                        InteractiveAuthenticationDefaults.CookieSchemeName
                    )
                )
            )
            .Verifiable();

        var mockSessionService = MockRepository.Create<ISessionService>();
        mockSessionService
            .Setup(x =>
                x.ValidateAndTouchAsync(
                    mockContext.Object,
                    "session-1",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true)
            .Verifiable();

        var mockConnectionResolver = MockRepository.Create<IConnectionResolver>();
        mockConnectionResolver
            .Setup(x =>
                x.GetByIdOrDefaultAsync(
                    mockContext.Object,
                    "connection-1",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(CreateConnection(emailVerificationEnabled: false))
            .Verifiable();

        var mockTenantResolver = MockRepository.Create<ITenantResolver>();
        using var serviceProvider = new ServiceCollection()
            .AddRouting()
            .AddSingleton(mockAuthentication.Object)
            .BuildServiceProvider();
        using var bundle = new SinglePageBundle<InteractiveBundleShell>(null, _ => null);
        var handler = CreateHandler(
            mockCatalog.Object,
            mockTenantResolver.Object,
            serviceProvider,
            bundle,
            connectionResolver: mockConnectionResolver.Object,
            sessionService: mockSessionService.Object
        );

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        httpContext.Features.Set(mockFeature.Object);

        var result = await handler.GetEmailPageAsync(httpContext);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task GetEmailPageAsync_WhenThePrincipalsConnectionDoesNotResolve_IsNotFound()
    {
        var mockCatalog = MockRepository.Create<IUiLocaleCatalog>();
        mockCatalog.SetupGet(x => x.Locales).Returns([TestLocales.Xx]).Verifiable();
        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockFeature = MockRepository.Create<IOpenIdContextFeature>();
        mockFeature.SetupGet(x => x.OpenIdContext).Returns(mockContext.Object).Verifiable();

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(JoseClaimNames.Payload.Sid, "session-1"),
                    new Claim(ConnectionClaimTypes.Connection, "connection-1"),
                ],
                authenticationType: "cookie"
            )
        );
        var mockAuthentication = MockRepository.Create<IAuthenticationService>();
        mockAuthentication
            .Setup(x =>
                x.AuthenticateAsync(
                    It.IsAny<HttpContext>(),
                    InteractiveAuthenticationDefaults.CookieSchemeName
                )
            )
            .ReturnsAsync(
                AuthenticateResult.Success(
                    new AuthenticationTicket(
                        principal,
                        InteractiveAuthenticationDefaults.CookieSchemeName
                    )
                )
            )
            .Verifiable();

        var mockSessionService = MockRepository.Create<ISessionService>();
        mockSessionService
            .Setup(x =>
                x.ValidateAndTouchAsync(
                    mockContext.Object,
                    "session-1",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true)
            .Verifiable();

        var mockConnectionResolver = MockRepository.Create<IConnectionResolver>();
        mockConnectionResolver
            .Setup(x =>
                x.GetByIdOrDefaultAsync(
                    mockContext.Object,
                    "connection-1",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync((ResolvedConnection?)null)
            .Verifiable();

        var mockTenantResolver = MockRepository.Create<ITenantResolver>();
        using var serviceProvider = new ServiceCollection()
            .AddRouting()
            .AddSingleton(mockAuthentication.Object)
            .BuildServiceProvider();
        using var bundle = new SinglePageBundle<InteractiveBundleShell>(null, _ => null);
        var handler = CreateHandler(
            mockCatalog.Object,
            mockTenantResolver.Object,
            serviceProvider,
            bundle,
            connectionResolver: mockConnectionResolver.Object,
            sessionService: mockSessionService.Object
        );

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        httpContext.Features.Set(mockFeature.Object);

        var result = await handler.GetEmailPageAsync(httpContext);

        Assert.IsType<NotFound>(result);
    }

    #endregion

    #region PostSendEmailCodeAsync Tests

    [Fact]
    public async Task PostSendEmailCodeAsync_WhenTheFormCarriesALanguageTag_MailsInThatLocale()
    {
        var mockCatalog = MockRepository.Create<IUiLocaleCatalog>();
        mockCatalog.SetupGet(x => x.Locales).Returns([TestLocales.Xx, TestLocales.Yy]).Verifiable();
        IUiLocale? carried = TestLocales.Yy;
        mockCatalog.Setup(x => x.TryGet("yy", out carried)).Returns(true).Verifiable();

        var mockSettings = MockRepository.Create<IReadOnlySettingCollection>();
        // The page the end user saw was yy, so the mail is worded in yy, although the browser prefers xx.
        mockCatalog
            .Setup(x =>
                x.Select(
                    mockSettings.Object,
                    It.Is<IEnumerable<string>>(preferences =>
                        preferences.SequenceEqual(new[] { "yy", "xx" }, StringComparer.Ordinal)
                    )
                )
            )
            .Returns(TestLocales.Yy)
            .Verifiable();

        var mockProvider = MockRepository.Create<IReadOnlySettingCollectionProvider>();
        mockProvider.SetupGet(x => x.Collection).Returns(mockSettings.Object).Verifiable();
        var mockTenant = MockRepository.Create<OpenIdTenant>();
        mockTenant.SetupGet(x => x.SettingsProvider).Returns(mockProvider.Object).Verifiable();
        var mockContext = MockRepository.Create<OpenIdContext>();
        mockContext.SetupGet(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        var mockFeature = MockRepository.Create<IOpenIdContextFeature>();
        mockFeature.SetupGet(x => x.OpenIdContext).Returns(mockContext.Object).Verifiable();

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(JoseClaimNames.Payload.Sid, "session-1"),
                    new Claim(ConnectionClaimTypes.Connection, "connection-1"),
                ],
                authenticationType: "cookie"
            )
        );
        var mockAuthentication = MockRepository.Create<IAuthenticationService>();
        mockAuthentication
            .Setup(x =>
                x.AuthenticateAsync(
                    It.IsAny<HttpContext>(),
                    InteractiveAuthenticationDefaults.CookieSchemeName
                )
            )
            .ReturnsAsync(
                AuthenticateResult.Success(
                    new AuthenticationTicket(
                        principal,
                        InteractiveAuthenticationDefaults.CookieSchemeName
                    )
                )
            )
            .Verifiable();

        var mockSessionService = MockRepository.Create<ISessionService>();
        mockSessionService
            .Setup(x =>
                x.ValidateAndTouchAsync(
                    mockContext.Object,
                    "session-1",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true)
            .Verifiable();

        var mockVerifier = MockRepository.Create<IInteractiveEmailVerifier>();
        mockVerifier
            .Setup(x =>
                x.SendVerificationCodeAsync(
                    mockContext.Object,
                    principal,
                    TestLocales.Yy,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(InteractiveEmailCodeResult.Sent("flow-1"))
            .Verifiable();

        var mockConnectionResolver = MockRepository.Create<IConnectionResolver>();
        mockConnectionResolver
            .Setup(x =>
                x.GetByIdOrDefaultAsync(
                    mockContext.Object,
                    "connection-1",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(CreateConnection())
            .Verifiable();

        var mockTenantResolver = MockRepository.Create<ITenantResolver>();
        mockTenantResolver
            .Setup(x => x.GetTenantRoute())
            .Returns(RoutePatternFactory.Parse("/{tenantId}"))
            .Verifiable();

        using var serviceProvider = new ServiceCollection()
            .AddRouting()
            .AddSingleton(mockAuthentication.Object)
            .BuildServiceProvider();
        using var bundle = new SinglePageBundle<InteractiveBundleShell>(null, _ => null);
        var handler = CreateHandler(
            mockCatalog.Object,
            mockTenantResolver.Object,
            serviceProvider,
            bundle,
            connectionResolver: mockConnectionResolver.Object,
            emailVerifier: mockVerifier.Object,
            sessionService: mockSessionService.Object
        );

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        httpContext.Features.Set(mockFeature.Object);
        httpContext.Request.RouteValues["tenantId"] = "acme";
        httpContext.Request.Headers.AcceptLanguage = "xx";

        var result = await handler.PostSendEmailCodeAsync(httpContext, uiLocales: "yy");

        var redirect = Assert.IsType<RedirectHttpResult>(result);
        Assert.Equal("/acme/account/email?flow=flow-1&ui_locales=yy", redirect.Url);
    }

    #endregion

    #region PostVerifyEmailAsync Tests

    [Fact]
    public async Task PostVerifyEmailAsync_WhenTheFormCarriesALanguageTag_KeepsItInTheRedirect()
    {
        var mockCatalog = MockRepository.Create<IUiLocaleCatalog>();
        mockCatalog.SetupGet(x => x.Locales).Returns([TestLocales.Xx, TestLocales.Yy]).Verifiable();
        IUiLocale? carried = TestLocales.Yy;
        mockCatalog.Setup(x => x.TryGet("yy", out carried)).Returns(true).Verifiable();

        var mockContext = MockRepository.Create<OpenIdContext>();
        var mockFeature = MockRepository.Create<IOpenIdContextFeature>();
        mockFeature.SetupGet(x => x.OpenIdContext).Returns(mockContext.Object).Verifiable();

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(JoseClaimNames.Payload.Sid, "session-1"),
                    new Claim(ConnectionClaimTypes.Connection, "connection-1"),
                ],
                authenticationType: "cookie"
            )
        );
        var mockAuthentication = MockRepository.Create<IAuthenticationService>();
        mockAuthentication
            .Setup(x =>
                x.AuthenticateAsync(
                    It.IsAny<HttpContext>(),
                    InteractiveAuthenticationDefaults.CookieSchemeName
                )
            )
            .ReturnsAsync(
                AuthenticateResult.Success(
                    new AuthenticationTicket(
                        principal,
                        InteractiveAuthenticationDefaults.CookieSchemeName
                    )
                )
            )
            .Verifiable();

        var mockSessionService = MockRepository.Create<ISessionService>();
        mockSessionService
            .Setup(x =>
                x.ValidateAndTouchAsync(
                    mockContext.Object,
                    "session-1",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true)
            .Verifiable();

        var mockVerifier = MockRepository.Create<IInteractiveEmailVerifier>();
        mockVerifier
            .Setup(x =>
                x.VerifyEmailAsync(
                    mockContext.Object,
                    principal,
                    "flow-1",
                    It.Is<Secret<char>>(s => SecretArguments.Reveals(s, "000000")),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(InteractiveEmailCodeResult.Refused("invalid_code"))
            .Verifiable();

        var mockConnectionResolver = MockRepository.Create<IConnectionResolver>();
        mockConnectionResolver
            .Setup(x =>
                x.GetByIdOrDefaultAsync(
                    mockContext.Object,
                    "connection-1",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(CreateConnection())
            .Verifiable();

        var mockTenantResolver = MockRepository.Create<ITenantResolver>();
        mockTenantResolver
            .Setup(x => x.GetTenantRoute())
            .Returns(RoutePatternFactory.Parse("/{tenantId}"))
            .Verifiable();

        using var serviceProvider = new ServiceCollection()
            .AddRouting()
            .AddSingleton(mockAuthentication.Object)
            .BuildServiceProvider();
        using var bundle = new SinglePageBundle<InteractiveBundleShell>(null, _ => null);
        var handler = CreateHandler(
            mockCatalog.Object,
            mockTenantResolver.Object,
            serviceProvider,
            bundle,
            connectionResolver: mockConnectionResolver.Object,
            emailVerifier: mockVerifier.Object,
            sessionService: mockSessionService.Object
        );

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        httpContext.Features.Set(mockFeature.Object);
        httpContext.Request.RouteValues["tenantId"] = "acme";

        var result = await handler.PostVerifyEmailAsync(
            httpContext,
            flow: "flow-1",
            code: "000000",
            uiLocales: "yy"
        );

        var redirect = Assert.IsType<RedirectHttpResult>(result);
        Assert.Equal(
            "/acme/account/email?flow=flow-1&error=invalid_code&ui_locales=yy",
            redirect.Url
        );
    }

    #endregion

    #region DescribeLoginError Tests

    [Fact]
    public void DescribeLoginError_WhenTooManyCodes_ReturnsTheTooManyCodesMessage()
    {
        var messages = TestLocales.Xx.Messages;

        var result = InteractiveAuthenticationEndpointHandler.DescribeLoginError(
            InteractiveEmailCodeErrors.TooManyCodes,
            messages
        );

        Assert.Equal(messages.TooManyCodesError, result);
    }

    #endregion

    #region DescribeSignInCodeError Tests

    [Fact]
    public void DescribeSignInCodeError_WhenTheAccountHasNoEmail_ReturnsTheNoEmailMessage()
    {
        var messages = TestLocales.Xx.Messages;

        var result = InteractiveAuthenticationEndpointHandler.DescribeSignInCodeError(
            InteractiveEmailCodeErrors.NoEmail,
            messages
        );

        Assert.Equal(messages.NoEmailError, result);
    }

    [Fact]
    public void DescribeSignInCodeError_ForAnUnknownCode_ReturnsTheGenericMessage()
    {
        var messages = TestLocales.Xx.Messages;

        var result = InteractiveAuthenticationEndpointHandler.DescribeSignInCodeError(
            "boom",
            messages
        );

        Assert.Equal(messages.EmailCodeFailedError, result);
    }

    [Fact]
    public void DescribeSignInCodeError_ForAnEmptyField_ReturnsTheMissingFieldsMessage()
    {
        var messages = TestLocales.Xx.Messages;

        var result = InteractiveAuthenticationEndpointHandler.DescribeSignInCodeError(
            InteractivePasswordChangeErrors.InvalidRequest,
            messages
        );

        Assert.Equal(messages.MissingFieldsError, result);
    }

    #endregion

    #region DescribePasswordResetError Tests

    [Fact]
    public void DescribePasswordResetError_ForAnUnknownCode_ReturnsTheRefusedPasswordMessage()
    {
        var messages = TestLocales.Xx.Messages;

        var result = InteractiveAuthenticationEndpointHandler.DescribePasswordResetError(
            "password_too_short",
            messages
        );

        Assert.Equal(messages.PasswordRefusedError, result);
    }

    [Fact]
    public void DescribePasswordResetError_ForAnEmptyField_ReturnsTheMissingFieldsMessage()
    {
        var messages = TestLocales.Xx.Messages;

        var result = InteractiveAuthenticationEndpointHandler.DescribePasswordResetError(
            InteractivePasswordChangeErrors.InvalidRequest,
            messages
        );

        Assert.Equal(messages.MissingFieldsError, result);
    }

    [Fact]
    public void DescribePasswordResetError_ForAResetTheBackendDoesNotSupport_ReturnsTheGenericMessage()
    {
        var messages = TestLocales.Xx.Messages;

        var result = InteractiveAuthenticationEndpointHandler.DescribePasswordResetError(
            InteractivePasswordChangeErrors.NotSupported,
            messages
        );

        Assert.Equal(messages.EmailCodeFailedError, result);
    }

    #endregion

    #region CreatePendingSignInProperties Tests

    [Fact]
    public void CreatePendingSignInProperties_WhenRenewingAPendingSignIn_KeepsItsIssueAndExpiryTimes()
    {
        var issued = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
        var expires = issued.AddMinutes(15);
        var pending = new AuthenticationProperties { IssuedUtc = issued, ExpiresUtc = expires };

        var renewed = InteractiveAuthenticationEndpointHandler.CreatePendingSignInProperties(
            "flow-2",
            "/foo",
            "yy",
            pending
        );

        Assert.Equal(issued, renewed.IssuedUtc);
        Assert.Equal(expires, renewed.ExpiresUtc);
        Assert.Equal("flow-2", renewed.Items["email_code_flow"]);
    }

    [Fact]
    public void CreatePendingSignInProperties_ForANewPendingSignIn_LeavesTheTimesToTheCookie()
    {
        var properties = InteractiveAuthenticationEndpointHandler.CreatePendingSignInProperties(
            "flow-1",
            "/foo",
            "yy",
            pending: null
        );

        Assert.Null(properties.IssuedUtc);
        Assert.Null(properties.ExpiresUtc);
    }

    #endregion

    #region Helpers

    private ResolvedConnection CreateConnection(
        bool passwordResetEnabled = true,
        bool emailVerificationEnabled = true
    )
    {
        var mockConnectionSettings = MockRepository.Create<IReadOnlySettingCollection>();
        mockConnectionSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.PasswordResetEnabled))
            .Returns(passwordResetEnabled);
        mockConnectionSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.EmailVerificationEnabled))
            .Returns(emailVerificationEnabled);
        return new ResolvedConnection
        {
            Connection = new PersistedConnection
            {
                TenantId = "acme",
                ConnectionId = "connection-1",
                Name = "local",
                Strategy = "local",
                DisplayName = "Username and password",
                IsSystem = false,
                IsEnabled = true,
                Options = JsonSerializer.SerializeToElement(new { store = "default" }),
                Settings = new PersistedConnectionSettings
                {
                    TenantId = "acme",
                    ConnectionId = "connection-1",
                    Value = JsonSerializer.SerializeToElement(new { }),
                },
            },
            Strategy = MockRepository.Create<IConnectionStrategy>().Object,
            Settings = mockConnectionSettings.Object,
        };
    }

    private InteractiveAuthenticationEndpointHandler CreateHandler(
        IUiLocaleCatalog localeCatalog,
        ITenantResolver tenantResolver,
        IServiceProvider serviceProvider,
        SinglePageBundle<InteractiveBundleShell> bundle,
        IInteractivePasswordResetter? passwordResetter = null,
        ISignInConnectionService? signInConnectionService = null,
        IConnectionResolver? connectionResolver = null,
        IInteractiveEmailVerifier? emailVerifier = null,
        ISessionService? sessionService = null
    ) =>
        new(
            MockRepository.Create<IInteractiveCredentialVerifier>().Object,
            MockRepository.Create<IPrincipalResolver>().Object,
            MockRepository.Create<IConsentService>().Object,
            signInConnectionService ?? MockRepository.Create<ISignInConnectionService>().Object,
            connectionResolver ?? MockRepository.Create<IConnectionResolver>().Object,
            MockRepository.Create<IContinueRefusalService>().Object,
            MockRepository.Create<IInteractivePasswordChanger>().Object,
            passwordResetter ?? MockRepository.Create<IInteractivePasswordResetter>().Object,
            emailVerifier ?? MockRepository.Create<IInteractiveEmailVerifier>().Object,
            MockRepository.Create<IInteractiveSignInCodeChallenge>().Object,
            sessionService ?? MockRepository.Create<ISessionService>().Object,
            MockRepository.Create<IPostCommitSecurityEffects>().Object,
            TimeProvider.System,
            MockRepository.Create<IAntiforgery>().Object,
            bundle,
            new InteractiveSurfacePaths(
                tenantResolver,
                serviceProvider.GetRequiredService<TemplateBinderFactory>()
            ),
            MockRepository.Create<ITenantEnvironmentCatalog>().Object,
            new InteractiveLocaleSelector(localeCatalog)
        );

    #endregion
}
