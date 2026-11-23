// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NCode.Identity.AspNetCore;
using NCode.Identity.Jose;
using NCode.Identity.OpenId.Authentication.Consent;
using NCode.Identity.OpenId.Authentication.Endpoints.Continue;
using NCode.Identity.OpenId.Authentication.Sessions;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Locales;
using NCode.Identity.OpenId.PrincipalResolution;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Settings;
using Xunit;

namespace NCode.Identity.OpenId.Authentication.Interactive.Tests;

public class InteractiveAuthenticationEndpointHandlerTests
{
    private const string Origin = "https://op.example";

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
        var mockRepository = new MockRepository(MockBehavior.Strict);
        var mockCatalog = mockRepository.Create<IUiLocaleCatalog>();
        mockCatalog.SetupGet(x => x.Locales).Returns([TestLocales.Xx, TestLocales.Yy]).Verifiable();
        IUiLocale? carried = TestLocales.Yy;
        mockCatalog.Setup(x => x.TryGet("yy", out carried)).Returns(true).Verifiable();

        var mockSettings = mockRepository.Create<IReadOnlySettingCollection>();
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.PasswordResetEnabled))
            .Returns(true)
            .Verifiable();
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

        var mockProvider = mockRepository.Create<IReadOnlySettingCollectionProvider>();
        mockProvider.SetupGet(x => x.Collection).Returns(mockSettings.Object).Verifiable();
        var mockTenant = mockRepository.Create<OpenIdTenant>();
        mockTenant.SetupGet(x => x.SettingsProvider).Returns(mockProvider.Object).Verifiable();
        var mockContext = mockRepository.Create<OpenIdContext>();
        mockContext.SetupGet(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        var mockFeature = mockRepository.Create<IOpenIdContextFeature>();
        mockFeature.SetupGet(x => x.OpenIdContext).Returns(mockContext.Object).Verifiable();

        var mockResetter = mockRepository.Create<IInteractivePasswordResetter>();
        mockResetter
            .Setup(x =>
                x.SendResetCodeAsync(
                    mockContext.Object,
                    "alice",
                    TestLocales.Yy,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync("flow-1")
            .Verifiable();

        var mockTenantResolver = mockRepository.Create<ITenantResolver>();
        mockTenantResolver
            .Setup(x => x.GetTenantRoute())
            .Returns(RoutePatternFactory.Parse("/{tenantId}"))
            .Verifiable();

        using var serviceProvider = new ServiceCollection().AddRouting().BuildServiceProvider();
        using var bundle = new SinglePageBundle<InteractiveBundleShell>(null, _ => null);
        var handler = new InteractiveAuthenticationEndpointHandler(
            mockRepository.Create<IInteractiveCredentialVerifier>().Object,
            mockRepository.Create<IPrincipalResolver>().Object,
            mockRepository.Create<IConsentService>().Object,
            mockRepository.Create<IContinueRefusalService>().Object,
            mockRepository.Create<IInteractivePasswordChanger>().Object,
            mockResetter.Object,
            mockRepository.Create<IInteractiveEmailVerifier>().Object,
            mockRepository.Create<IInteractiveSignInCodeChallenge>().Object,
            mockRepository.Create<ISessionService>().Object,
            mockRepository.Create<IPostCommitSecurityEffects>().Object,
            TimeProvider.System,
            mockRepository.Create<IAntiforgery>().Object,
            bundle,
            new InteractiveSurfacePaths(
                mockTenantResolver.Object,
                serviceProvider.GetRequiredService<TemplateBinderFactory>()
            ),
            mockRepository.Create<ITenantEnvironmentCatalog>().Object,
            new InteractiveLocaleSelector(mockCatalog.Object)
        );

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set(mockFeature.Object);
        httpContext.Request.RouteValues["tenantId"] = "acme";
        httpContext.Request.Headers.AcceptLanguage = "xx";

        var result = await handler.PostForgotPasswordAsync(
            httpContext,
            "alice",
            returnUrl: null,
            uiLocales: "yy"
        );

        var redirect = Assert.IsType<RedirectHttpResult>(result);
        Assert.Equal("/acme/account/password/reset?flow=flow-1&ui_locales=yy", redirect.Url);
        mockRepository.Verify();
    }

    #endregion

    #region PostSendEmailCodeAsync Tests

    [Fact]
    public async Task PostSendEmailCodeAsync_WhenTheFormCarriesALanguageTag_MailsInThatLocale()
    {
        var mockRepository = new MockRepository(MockBehavior.Strict);
        var mockCatalog = mockRepository.Create<IUiLocaleCatalog>();
        mockCatalog.SetupGet(x => x.Locales).Returns([TestLocales.Xx, TestLocales.Yy]).Verifiable();
        IUiLocale? carried = TestLocales.Yy;
        mockCatalog.Setup(x => x.TryGet("yy", out carried)).Returns(true).Verifiable();

        var mockSettings = mockRepository.Create<IReadOnlySettingCollection>();
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.EmailVerificationEnabled))
            .Returns(true)
            .Verifiable();
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

        var mockProvider = mockRepository.Create<IReadOnlySettingCollectionProvider>();
        mockProvider.SetupGet(x => x.Collection).Returns(mockSettings.Object).Verifiable();
        var mockTenant = mockRepository.Create<OpenIdTenant>();
        mockTenant.SetupGet(x => x.SettingsProvider).Returns(mockProvider.Object).Verifiable();
        var mockContext = mockRepository.Create<OpenIdContext>();
        mockContext.SetupGet(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        var mockFeature = mockRepository.Create<IOpenIdContextFeature>();
        mockFeature.SetupGet(x => x.OpenIdContext).Returns(mockContext.Object).Verifiable();

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(JoseClaimNames.Payload.Sid, "session-1")],
                authenticationType: "cookie"
            )
        );
        var mockAuthentication = mockRepository.Create<IAuthenticationService>();
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

        var mockSessionService = mockRepository.Create<ISessionService>();
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

        var mockVerifier = mockRepository.Create<IInteractiveEmailVerifier>();
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

        var mockTenantResolver = mockRepository.Create<ITenantResolver>();
        mockTenantResolver
            .Setup(x => x.GetTenantRoute())
            .Returns(RoutePatternFactory.Parse("/{tenantId}"))
            .Verifiable();

        using var serviceProvider = new ServiceCollection()
            .AddRouting()
            .AddSingleton(mockAuthentication.Object)
            .BuildServiceProvider();
        using var bundle = new SinglePageBundle<InteractiveBundleShell>(null, _ => null);
        var handler = new InteractiveAuthenticationEndpointHandler(
            mockRepository.Create<IInteractiveCredentialVerifier>().Object,
            mockRepository.Create<IPrincipalResolver>().Object,
            mockRepository.Create<IConsentService>().Object,
            mockRepository.Create<IContinueRefusalService>().Object,
            mockRepository.Create<IInteractivePasswordChanger>().Object,
            mockRepository.Create<IInteractivePasswordResetter>().Object,
            mockVerifier.Object,
            mockRepository.Create<IInteractiveSignInCodeChallenge>().Object,
            mockSessionService.Object,
            mockRepository.Create<IPostCommitSecurityEffects>().Object,
            TimeProvider.System,
            mockRepository.Create<IAntiforgery>().Object,
            bundle,
            new InteractiveSurfacePaths(
                mockTenantResolver.Object,
                serviceProvider.GetRequiredService<TemplateBinderFactory>()
            ),
            mockRepository.Create<ITenantEnvironmentCatalog>().Object,
            new InteractiveLocaleSelector(mockCatalog.Object)
        );

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        httpContext.Features.Set(mockFeature.Object);
        httpContext.Request.RouteValues["tenantId"] = "acme";
        httpContext.Request.Headers.AcceptLanguage = "xx";

        var result = await handler.PostSendEmailCodeAsync(httpContext, uiLocales: "yy");

        var redirect = Assert.IsType<RedirectHttpResult>(result);
        Assert.Equal("/acme/account/email?flow=flow-1&ui_locales=yy", redirect.Url);
        mockRepository.Verify();
    }

    #endregion

    #region PostVerifyEmailAsync Tests

    [Fact]
    public async Task PostVerifyEmailAsync_WhenTheFormCarriesALanguageTag_KeepsItInTheRedirect()
    {
        var mockRepository = new MockRepository(MockBehavior.Strict);
        var mockCatalog = mockRepository.Create<IUiLocaleCatalog>();
        mockCatalog.SetupGet(x => x.Locales).Returns([TestLocales.Xx, TestLocales.Yy]).Verifiable();
        IUiLocale? carried = TestLocales.Yy;
        mockCatalog.Setup(x => x.TryGet("yy", out carried)).Returns(true).Verifiable();

        var mockSettings = mockRepository.Create<IReadOnlySettingCollection>();
        mockSettings
            .Setup(x => x.GetValue(OpenIdSettingKeys.EmailVerificationEnabled))
            .Returns(true)
            .Verifiable();

        var mockProvider = mockRepository.Create<IReadOnlySettingCollectionProvider>();
        mockProvider.SetupGet(x => x.Collection).Returns(mockSettings.Object).Verifiable();
        var mockTenant = mockRepository.Create<OpenIdTenant>();
        mockTenant.SetupGet(x => x.SettingsProvider).Returns(mockProvider.Object).Verifiable();
        var mockContext = mockRepository.Create<OpenIdContext>();
        mockContext.SetupGet(x => x.Tenant).Returns(mockTenant.Object).Verifiable();
        var mockFeature = mockRepository.Create<IOpenIdContextFeature>();
        mockFeature.SetupGet(x => x.OpenIdContext).Returns(mockContext.Object).Verifiable();

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(JoseClaimNames.Payload.Sid, "session-1")],
                authenticationType: "cookie"
            )
        );
        var mockAuthentication = mockRepository.Create<IAuthenticationService>();
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

        var mockSessionService = mockRepository.Create<ISessionService>();
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

        var mockVerifier = mockRepository.Create<IInteractiveEmailVerifier>();
        mockVerifier
            .Setup(x =>
                x.VerifyEmailAsync(
                    mockContext.Object,
                    principal,
                    "flow-1",
                    "000000",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(InteractiveEmailCodeResult.Refused("invalid_code"))
            .Verifiable();

        var mockTenantResolver = mockRepository.Create<ITenantResolver>();
        mockTenantResolver
            .Setup(x => x.GetTenantRoute())
            .Returns(RoutePatternFactory.Parse("/{tenantId}"))
            .Verifiable();

        using var serviceProvider = new ServiceCollection()
            .AddRouting()
            .AddSingleton(mockAuthentication.Object)
            .BuildServiceProvider();
        using var bundle = new SinglePageBundle<InteractiveBundleShell>(null, _ => null);
        var handler = new InteractiveAuthenticationEndpointHandler(
            mockRepository.Create<IInteractiveCredentialVerifier>().Object,
            mockRepository.Create<IPrincipalResolver>().Object,
            mockRepository.Create<IConsentService>().Object,
            mockRepository.Create<IContinueRefusalService>().Object,
            mockRepository.Create<IInteractivePasswordChanger>().Object,
            mockRepository.Create<IInteractivePasswordResetter>().Object,
            mockVerifier.Object,
            mockRepository.Create<IInteractiveSignInCodeChallenge>().Object,
            mockSessionService.Object,
            mockRepository.Create<IPostCommitSecurityEffects>().Object,
            TimeProvider.System,
            mockRepository.Create<IAntiforgery>().Object,
            bundle,
            new InteractiveSurfacePaths(
                mockTenantResolver.Object,
                serviceProvider.GetRequiredService<TemplateBinderFactory>()
            ),
            mockRepository.Create<ITenantEnvironmentCatalog>().Object,
            new InteractiveLocaleSelector(mockCatalog.Object)
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
        mockRepository.Verify();
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

    #endregion
}
