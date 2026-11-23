// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NCode.Buffers;
using NCode.Identity.AspNetCore;
using NCode.Identity.Jose;
using NCode.Identity.Json;
using NCode.Identity.OpenId.Authentication.Consent;
using NCode.Identity.OpenId.Authentication.Endpoints.Continue;
using NCode.Identity.OpenId.Authentication.Endpoints.EndSession.Commands;
using NCode.Identity.OpenId.Authentication.Endpoints.EndSession.Messages;
using NCode.Identity.OpenId.Authentication.Endpoints.EndSession.Models;
using NCode.Identity.OpenId.Authentication.Sessions;
using NCode.Identity.OpenId.Authentication.Subject;
using NCode.Identity.OpenId.Contexts;
using NCode.Identity.OpenId.Errors;
using NCode.Identity.OpenId.Messages;
using NCode.Identity.OpenId.PrincipalResolution;
using NCode.Identity.OpenId.Settings;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Registration.AspNetCore;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// The <see cref="IEndpointProvider"/> for the interactive login/consent/account surface. It serves the single-page
/// bundle for the login, consent, and account ("me") pages, verifies credentials through the registered
/// <see cref="IInteractiveCredentialVerifier"/>, issues the cookie session on success, exposes the current session as
/// JSON, and clears it on logout. The login, consent, and logout forms submit as native form POSTs carrying the
/// antiforgery token, so the server-driven cookie challenge/redirect flow is preserved. The self-service surface
/// serves the cookie only while the session it names is active.
/// </summary>
internal sealed class InteractiveAuthenticationEndpointHandler(
    IInteractiveCredentialVerifier credentialVerifier,
    IPrincipalResolver principalResolver,
    IConsentService consentService,
    IContinueRefusalService continueRefusalService,
    IInteractivePasswordChanger passwordChanger,
    IInteractivePasswordResetter passwordResetter,
    IInteractiveEmailVerifier emailVerifier,
    IInteractiveSignInCodeChallenge signInCodeChallenge,
    ISessionService sessionService,
    IPostCommitSecurityEffects postCommitSecurityEffects,
    TimeProvider timeProvider,
    IAntiforgery antiforgery,
    SinglePageBundle<InteractiveBundleShell> bundle,
    InteractiveSurfacePaths surfacePaths,
    ITenantEnvironmentCatalog tenantEnvironmentCatalog,
    InteractiveLocaleSelector localeSelector
) : IEndpointProvider
{
    // The `scope` form value that selects "sign out everywhere else" (retain the current session); any other value is
    // a full "sign out everywhere".
    private const string SignOutOthersScope = "others";

    // The `action` form value that confirms a logout; any other value declines it.
    private const string LogoutAction = "logout";

    // The `error` and `status` query values the change-password page reads back to show its message.
    private const string ErrorQueryName = "error";
    private const string StatusQueryName = "status";
    private const string PasswordChangedStatus = "changed";

    // The `status` query value the login page reads back to tell the end user that the password is reset, and the query
    // names the forgot-password and reset-password pages carry.
    private const string PasswordResetStatus = "password_reset";
    private const string FlowQueryName = InteractiveAuthenticationPageRenderer.FlowFieldName;
    private const string ReturnUrlName = "ReturnUrl";

    // The `status` query value the sign-in code page reads back to tell the end user that a new code is sent, and the
    // `action` form value that asks the page for it; any other value verifies the code.
    private const string CodeSentStatusValue = "sent";
    private const string ResendAction = InteractiveAuthenticationPageRenderer.ResendActionValue;

    // The `status` query value the email page reads back to tell the end user that the address is verified.
    private const string EmailVerifiedStatus = "verified";

    // The items the pending sign-in cookie carries beside the principal: the handle of the code's flow, the local URL
    // the sign-in returns to and the language tag of the page the end user signed in on.
    private const string EmailCodeFlowItem = "email_code_flow";
    private const string ReturnUrlItem = "return_url";
    private const string UiLocalesItem = InteractiveAuthenticationPageRenderer.UiLocalesFieldName;

    private IInteractiveCredentialVerifier CredentialVerifier { get; } = credentialVerifier;
    private IPrincipalResolver PrincipalResolver { get; } = principalResolver;
    private IConsentService ConsentService { get; } = consentService;
    private IContinueRefusalService ContinueRefusalService { get; } = continueRefusalService;
    private IInteractivePasswordChanger PasswordChanger { get; } = passwordChanger;
    private IInteractivePasswordResetter PasswordResetter { get; } = passwordResetter;
    private IInteractiveEmailVerifier EmailVerifier { get; } = emailVerifier;
    private IInteractiveSignInCodeChallenge SignInCodeChallenge { get; } = signInCodeChallenge;
    private ISessionService SessionService { get; } = sessionService;
    private IPostCommitSecurityEffects PostCommitSecurityEffects { get; } =
        postCommitSecurityEffects;
    private TimeProvider TimeProvider { get; } = timeProvider;
    private IAntiforgery Antiforgery { get; } = antiforgery;
    private SinglePageBundle<InteractiveBundleShell> Bundle { get; } = bundle;
    private InteractiveSurfacePaths SurfacePaths { get; } = surfacePaths;
    private ITenantEnvironmentCatalog TenantEnvironmentCatalog { get; } = tenantEnvironmentCatalog;
    private InteractiveLocaleSelector LocaleSelector { get; } = localeSelector;

    /// <inheritdoc />
    public void Map(IEndpointRouteBuilder endpoints)
    {
        // The user agent navigates to the surface, and its own pages call it from the same origin; no CORS policy
        // applies to it (FAPI 2.0 Security Profile §5.3.2.1).
        var surface = endpoints.MapGroup(string.Empty).WithFrontChannel();

        surface.MapGet(InteractiveAuthenticationDefaults.AccountPath, GetAccountPageAsync);
        surface.MapGet(InteractiveAuthenticationDefaults.MePath, GetMeAsync);
        surface.MapGet(InteractiveAuthenticationDefaults.LoginPath, GetLoginPageAsync);
        surface.MapPost(InteractiveAuthenticationDefaults.LoginPath, PostLoginAsync);
        surface.MapGet(InteractiveAuthenticationDefaults.ConsentPath, GetConsentPageAsync);
        surface.MapGet(
            InteractiveAuthenticationDefaults.ConsentDetailsPath,
            GetConsentDetailsAsync
        );
        surface.MapPost(InteractiveAuthenticationDefaults.ConsentPath, PostConsentAsync);
        surface.MapPost(InteractiveAuthenticationDefaults.LogoutPath, PostLogoutAsync);
        surface.MapGet(
            InteractiveAuthenticationDefaults.LogoutConfirmPath,
            GetLogoutConfirmationPageAsync
        );
        surface.MapPost(
            InteractiveAuthenticationDefaults.LogoutConfirmPath,
            PostLogoutConfirmationAsync
        );
        surface.MapGet(
            InteractiveAuthenticationDefaults.ApplicationsPath,
            GetApplicationsPageAsync
        );
        surface.MapGet(
            InteractiveAuthenticationDefaults.ApplicationsListPath,
            GetApplicationsAsync
        );
        surface.MapPost(
            InteractiveAuthenticationDefaults.ApplicationsRevokePath,
            PostRevokeApplicationAsync
        );
        surface.MapGet(InteractiveAuthenticationDefaults.SessionsPath, GetSessionsPageAsync);
        surface.MapGet(InteractiveAuthenticationDefaults.SessionsListPath, GetSessionsAsync);
        surface.MapPost(
            InteractiveAuthenticationDefaults.SessionsRevokePath,
            PostRevokeSessionAsync
        );
        surface.MapPost(
            InteractiveAuthenticationDefaults.SessionsRevokeAllPath,
            PostRevokeAllSessionsAsync
        );
        surface.MapGet(InteractiveAuthenticationDefaults.PasswordPath, GetPasswordPageAsync);
        surface.MapPost(InteractiveAuthenticationDefaults.PasswordPath, PostPasswordAsync);
        surface.MapGet(
            InteractiveAuthenticationDefaults.PasswordForgotPath,
            GetForgotPasswordPageAsync
        );
        surface.MapPost(
            InteractiveAuthenticationDefaults.PasswordForgotPath,
            PostForgotPasswordAsync
        );
        surface.MapGet(
            InteractiveAuthenticationDefaults.PasswordResetPath,
            GetResetPasswordPageAsync
        );
        surface.MapPost(
            InteractiveAuthenticationDefaults.PasswordResetPath,
            PostResetPasswordAsync
        );
        surface.MapGet(InteractiveAuthenticationDefaults.LoginCodePath, GetSignInCodePageAsync);
        surface.MapPost(InteractiveAuthenticationDefaults.LoginCodePath, PostSignInCodeAsync);
        surface.MapGet(InteractiveAuthenticationDefaults.EmailPath, GetEmailPageAsync);
        surface.MapGet(InteractiveAuthenticationDefaults.EmailStatusPath, GetEmailStatusAsync);
        surface.MapPost(InteractiveAuthenticationDefaults.EmailSendPath, PostSendEmailCodeAsync);
        surface.MapPost(InteractiveAuthenticationDefaults.EmailVerifyPath, PostVerifyEmailAsync);
    }

    /// <summary>
    /// Serves the login page. The server renders a working no-JavaScript login form (progressive-enhancement baseline)
    /// carrying the antiforgery token and the return URL; when the single-page bundle is present the form is injected
    /// into its <c>#root</c> so the SPA replaces it on mount.
    /// </summary>
    public async ValueTask<IResult> GetLoginPageAsync(HttpContext httpContext)
    {
        var tokens = IssueAntiforgeryTokens(httpContext);
        var query = httpContext.Request.Query;
        var branding = ReadBranding(httpContext);
        var security = InteractivePageSecurity.Create(branding);
        var frame = await CreatePageFrameAsync(
            httpContext,
            branding,
            security,
            httpContext.RequestAborted
        );
        var html = InteractiveHtmlResult.CreateBuffer();
        var returnUrl = query[ReturnUrlName].ToString();
        var errorQueryValue = query[ErrorQueryName].ToString();
        var loginError = DescribeLoginError(errorQueryValue, frame.Locale.Messages);
        var statusQueryValue = query[StatusQueryName].ToString();
        var statusMessage = string.Equals(
            statusQueryValue,
            PasswordResetStatus,
            StringComparison.Ordinal
        )
            ? frame.Locale.Messages.PasswordResetStatus
            : null;

        // The link to the forgot-password page appears only while the tenant lets its end users reset a password.
        // See ADR-1533.
        string? forgotPasswordHref = null;
        if (IsPasswordResetEnabled(httpContext))
        {
            var carriedUiLocales = query[InteractiveAuthenticationPageRenderer.UiLocalesFieldName];
            var carriedLanguageTag = LocaleSelector.GetCarriedLanguageTag(carriedUiLocales);
            forgotPasswordHref = BuildForgotPasswordHref(returnUrl, carriedLanguageTag);
        }
        InteractiveAuthenticationPageRenderer.RenderLoginPage(
            html,
            frame,
            returnUrl,
            tokens.FormFieldName,
            tokens.RequestToken ?? string.Empty,
            loginError,
            statusMessage,
            forgotPasswordHref
        );
        return new InteractiveHtmlResult(html, security.Policy);
    }

    /// <summary>
    /// Serves the consent page. The server resolves the pending authorization request and renders a working
    /// no-JavaScript consent form (a per-scope checkbox plus allow/deny) carrying the antiforgery token; when the
    /// single-page bundle is present the form is injected into its <c>#root</c> so the SPA replaces it on mount.
    /// </summary>
    public async ValueTask<IResult> GetConsentPageAsync(HttpContext httpContext)
    {
        var tokens = IssueAntiforgeryTokens(httpContext);
        var rawReturnUrl = httpContext.Request.Query[ReturnUrlName].ToString();

        var appName = "An application";
        IReadOnlyList<InteractiveAuthenticationPageRenderer.ScopeView> scopes = [];

        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (authenticateResult.Succeeded && !string.IsNullOrEmpty(rawReturnUrl))
        {
            var openIdContext = httpContext.GetOpenIdContext();
            var subjectId = openIdContext.GetSubjectId(authenticateResult.Principal);
            if (!string.IsNullOrEmpty(subjectId))
            {
                var pending = await ConsentService.ResolvePendingConsentAsync(
                    openIdContext,
                    subjectId,
                    rawReturnUrl,
                    httpContext.RequestAborted
                );
                if (pending is { } value)
                {
                    appName = string.IsNullOrEmpty(value.ClientName)
                        ? value.ClientId
                        : value.ClientName;
                    scopes = value
                        .Scopes.Select(scope => new InteractiveAuthenticationPageRenderer.ScopeView(
                            scope.Value,
                            scope.Description,
                            // A consent screen pre-selects every requested scope so Allow authorizes the request;
                            // the subject unchecks a scope to down-scope. Defaulting to the prior-granted state would
                            // silently drop a freshly-requested scope (e.g. openid) when the subject clicks Allow.
                            Checked: true
                        ))
                        .ToList();
                }
            }
        }

        var branding = ReadBranding(httpContext);
        var security = InteractivePageSecurity.Create(branding);
        var html = InteractiveHtmlResult.CreateBuffer();
        var frame = await CreatePageFrameAsync(
            httpContext,
            branding,
            security,
            httpContext.RequestAborted
        );
        InteractiveAuthenticationPageRenderer.RenderConsentPage(
            html,
            frame,
            rawReturnUrl,
            tokens.FormFieldName,
            tokens.RequestToken ?? string.Empty,
            appName,
            scopes
        );
        return new InteractiveHtmlResult(html, security.Policy);
    }

    /// <summary>
    /// Serves the account ("me") page (the single-page bundle), establishing the antiforgery token the sign-out form
    /// echoes back. An anonymous request is redirected to the login page with the account page as its return URL.
    /// </summary>
    public async ValueTask<IResult> GetAccountPageAsync(HttpContext httpContext)
    {
        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return RedirectToLogin(httpContext, InteractiveAuthenticationDefaults.AccountPath);
        }

        return await ServeBundleAsync(httpContext, httpContext.RequestAborted);
    }

    /// <summary>
    /// Returns the current end-user's session as JSON for the account page, or <c>401</c> when the request is not
    /// authenticated.
    /// </summary>
    public async ValueTask<IResult> GetMeAsync(HttpContext httpContext)
    {
        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return TypedResults.Unauthorized();
        }

        var openIdContext = httpContext.GetOpenIdContext();
        var principal = authenticateResult.Principal;
        var subjectId = openIdContext.GetSubjectId(principal);
        if (string.IsNullOrEmpty(subjectId))
        {
            return TypedResults.Unauthorized();
        }

        var emailVerification = IsEmailVerificationEnabled(httpContext);
        var meResponse = BuildMeResponse(principal, subjectId, emailVerification);
        return TypedResults.Json(meResponse);
    }

    /// <summary>
    /// Projects the authenticated cookie principal into the account page's view model.
    /// </summary>
    /// <param name="principal">The authenticated cookie principal.</param>
    /// <param name="subjectId">The subject identifier of the principal, which names the account when it carries no
    /// name.</param>
    /// <param name="emailVerification">Whether the tenant lets its end users verify their email address.</param>
    /// <returns>The view model of the account page.</returns>
    internal static AccountMeResponse BuildMeResponse(
        ClaimsPrincipal principal,
        string subjectId,
        bool emailVerification
    )
    {
        var name =
            principal.FindFirst(JoseClaimNames.Payload.Name)?.Value
            ?? principal.FindFirst(JoseClaimNames.Payload.PreferredUsername)?.Value
            ?? subjectId;

        var amr = principal
            .FindAll(JoseClaimNames.Payload.Amr)
            .Select(claim => claim.Value)
            .ToList();

        var authTimeValue = principal.FindFirst(JoseClaimNames.Payload.AuthTime)?.Value;
        long? authTime = DateCoercion.TryParseNumericDate(authTimeValue, out var parsedAuthTime)
            ? parsedAuthTime.ToUnixTimeSeconds()
            : null;

        var idp = principal.FindFirst(JoseClaimNames.Payload.Idp)?.Value;
        var sid = principal.FindFirst(JoseClaimNames.Payload.Sid)?.Value;
        return new AccountMeResponse(subjectId, name, authTime, amr, idp, sid, emailVerification);
    }

    /// <summary>
    /// Verifies the posted credentials through the registered verifier and, on success, signs in the cookie session
    /// and redirects to the validated local return URL; on failure it redirects back to the login page with an error.
    /// An end user whose standing is refused, such as a blocked principal or a disabled account, signs in nowhere: no
    /// session is created, and the parked authorization request the return URL names is answered with the refusal,
    /// otherwise the login page shows it.
    /// </summary>
    public async ValueTask<IResult> PostLoginAsync(
        HttpContext httpContext,
        [FromForm] string? username,
        [FromForm] string? password,
        [FromForm(Name = ReturnUrlName)] string? returnUrl,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.UiLocalesFieldName)]
            string? uiLocales
    )
    {
        var safeReturnUrl = ResolveSafeReturnUrl(httpContext, returnUrl);
        var languageTag = LocaleSelector.GetCarriedLanguageTag(uiLocales);

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            var invalidRequestRedirect = BuildLoginRedirect(
                httpContext,
                safeReturnUrl,
                "invalid_request",
                languageTag
            );
            return TypedResults.Redirect(invalidRequestRedirect);
        }

        var openIdContext = httpContext.GetOpenIdContext();
        var passwordChars = password.AsSpan();
        using var passwordSecret = Secret.Create(passwordChars);
        var principal = await CredentialVerifier.VerifyCredentialAsync(
            openIdContext,
            username,
            passwordSecret,
            httpContext.RequestAborted
        );
        if (principal is null)
        {
            var invalidCredentialsRedirect = BuildLoginRedirect(
                httpContext,
                safeReturnUrl,
                "invalid_credentials",
                languageTag
            );
            return TypedResults.Redirect(invalidCredentialsRedirect);
        }

        // Resolve the verified credential to the stable, server-owned principal id and normalize the cookie subject to
        // it, so the session, every subsequent authorize request, and the grants they issue all key on the single
        // principal regardless of how the user authenticates (ADR-1035). Without this the interactive path would carry
        // the raw local-account id while ROPC/bearer carry the resolved principal, keying the same user's grants
        // inconsistently. The same tenant-aware normalization seam enforces the Subject.sub == PrincipalId invariant at
        // every ticket-construction site (ADR-1035).
        principal = await NormalizeSubjectToPrincipalAsync(
            openIdContext,
            principal,
            httpContext.RequestAborted
        );

        // The end user's standing is validated before a session is created, so a blocked principal or a disabled
        // account signs in nowhere.
        // See ADR-1229.
        var standing = await ValidateStandingAsync(
            openIdContext,
            principal,
            httpContext.RequestAborted
        );
        if (standing.HasError)
        {
            return await RefuseSignInAsync(
                httpContext,
                openIdContext,
                safeReturnUrl,
                languageTag,
                standing.Error,
                httpContext.RequestAborted
            );
        }

        // A tenant that requires the email second factor keeps the end user, whose password is verified, in the pending
        // sign-in cookie: no session is created until the mailed code is right.
        // See ADR-1533.
        var settings = openIdContext.Tenant.SettingsProvider.Collection;
        if (settings.GetValue(OpenIdSettingKeys.EmailMfaRequired))
        {
            return await BeginSignInCodeAsync(
                httpContext,
                openIdContext,
                principal,
                safeReturnUrl,
                languageTag,
                httpContext.RequestAborted
            );
        }

        // Establish a server-side session and mint its opaque `sid` onto the cookie principal so every subsequent
        // authorize request resolves the same session and the `sid` claim can be emitted into issued tokens.
        return await CompleteSignInAsync(
            httpContext,
            openIdContext,
            principal,
            safeReturnUrl,
            httpContext.RequestAborted
        );
    }

    /// <summary>
    /// Creates the end user's session, signs the principal in with the session cookie and redirects to the return URL.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="openIdContext">The context of the tenant and environment the request is for.</param>
    /// <param name="principal">The principal whose sign-in is complete.</param>
    /// <param name="safeReturnUrl">The return URL, which is local to the server.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The redirect to the return URL.</returns>
    private async ValueTask<IResult> CompleteSignInAsync(
        HttpContext httpContext,
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        string safeReturnUrl,
        CancellationToken cancellationToken
    )
    {
        await EstablishSessionAsync(openIdContext, principal, cancellationToken);

        await httpContext.SignInAsync(
            InteractiveAuthenticationDefaults.CookieSchemeName,
            principal
        );

        return TypedResults.Redirect(safeReturnUrl);
    }

    private async ValueTask<IResult> RefuseSignInAsync(
        HttpContext httpContext,
        OpenIdContext openIdContext,
        string safeReturnUrl,
        string? languageTag,
        IOpenIdError error,
        CancellationToken cancellationToken
    )
    {
        var refusal = await ContinueRefusalService.RefuseAsync(
            openIdContext,
            safeReturnUrl,
            error,
            cancellationToken
        );
        if (refusal.HasHttpResult)
        {
            return refusal.HttpResult;
        }

        var loginRedirect = BuildLoginRedirect(
            httpContext,
            safeReturnUrl,
            OpenIdProtocol.ErrorCodes.AccessDenied,
            languageTag
        );
        return TypedResults.Redirect(loginRedirect);
    }

    private static async ValueTask<OperationDisposition<IOpenIdError>> ValidateStandingAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken
    )
    {
        var mediator = openIdContext.Mediator;
        var operationDisposition = new OperationDisposition<IOpenIdError>();

        var authenticationProperties = new AuthenticationProperties();
        var subjectId = openIdContext.GetSubjectId(principal) ?? string.Empty;
        var subjectAuthentication = new SubjectAuthentication(
            InteractiveAuthenticationDefaults.CookieSchemeName,
            authenticationProperties,
            principal,
            subjectId
        );
        var command = new ValidateSubjectStandingCommand(
            openIdContext,
            subjectAuthentication,
            operationDisposition
        );
        await mediator.SendAsync(command, cancellationToken);

        return operationDisposition;
    }

    private async ValueTask EstablishSessionAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken
    )
    {
        // A re-authentication onto an already-session-bearing principal keeps its existing session.
        if (principal.HasClaim(claim => claim.Type == JoseClaimNames.Payload.Sid))
            return;

        var subjectId = openIdContext.GetSubjectId(principal);
        if (string.IsNullOrEmpty(subjectId))
            return;

        var identity = principal.Identities.FirstOrDefault(candidate => candidate.IsAuthenticated);
        if (identity is null)
            return;

        var amr = principal
            .FindAll(JoseClaimNames.Payload.Amr)
            .Select(claim => claim.Value)
            .ToList();

        // Session lifecycle policy is tenant-scoped data (a session spans every client the subject signs into), read
        // from the tenant's effective settings. Both session bounds always apply; a zero max_concurrent_sessions
        // disables the cap.
        var settings = openIdContext.Tenant.SettingsProvider.Collection;
        var absoluteLifetime = settings.GetValue(OpenIdSettingKeys.SessionAbsoluteLifetime);
        var idleTimeout = settings.GetValue(OpenIdSettingKeys.SessionIdleTimeout);
        var maxConcurrentSessions = settings.GetValue(OpenIdSettingKeys.MaxConcurrentSessions);

        // Capture the device/origin at sign-in so the self-service active-sessions surface can describe each session.
        var httpRequest = openIdContext.Http.Request;
        var userAgent = httpRequest.Headers.UserAgent.ToString();
        var remoteIpAddress = openIdContext.Http.Connection.RemoteIpAddress?.ToString();

        var request = new CreateSessionRequest
        {
            SubjectId = subjectId,
            AuthTime = ReadAuthTime(principal),
            AuthenticationMethods = amr.Count > 0 ? amr : null,
            AuthenticationContextClassReference = principal
                .FindFirst(JoseClaimNames.Payload.Acr)
                ?.Value,
            IdentityProvider = principal.FindFirst(JoseClaimNames.Payload.Idp)?.Value,
            AbsoluteLifetime = absoluteLifetime,
            IdleTimeout = idleTimeout,
            MaxConcurrentSessions = maxConcurrentSessions > 0 ? maxConcurrentSessions : null,
            UserAgent = string.IsNullOrEmpty(userAgent) ? null : userAgent,
            RemoteIpAddress = remoteIpAddress,
        };

        var sessionId = await SessionService.CreateAsync(openIdContext, request, cancellationToken);
        var sidClaim = new Claim(JoseClaimNames.Payload.Sid, sessionId);
        identity.AddClaim(sidClaim);
    }

    /// <summary>
    /// Resolves the verified credential to the stable, server-owned principal id and returns a principal whose subject
    /// claim is that principal id, so the cookie, session, and every grant key on one subject.
    /// </summary>
    // See ADR-1035.
    private async ValueTask<ClaimsPrincipal> NormalizeSubjectToPrincipalAsync(
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken
    )
    {
        var principalId = await PrincipalResolver.ResolvePrincipalIdAsync(
            openIdContext,
            principal,
            cancellationToken
        );
        if (string.IsNullOrEmpty(principalId))
        {
            return principal;
        }

        return openIdContext.NormalizeSubjectId(principal, principalId);
    }

    private DateTimeOffset ReadAuthTime(ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(JoseClaimNames.Payload.AuthTime)?.Value;
        return DateCoercion.TryParseNumericDate(value, out var authTime)
            ? authTime
            : TimeProvider.GetUtcNow();
    }

    /// <summary>
    /// Returns the pending authorization request (the client — with its friendly display name — and the requested
    /// scopes with friendly descriptions and already-granted state) the consent page must present, resolved — without
    /// consuming — from the parked continuation carried by the return URL. Returns <c>401</c> when the request is not
    /// authenticated and <c>404</c> when no pending request resolves.
    /// </summary>
    public async ValueTask<IResult> GetConsentDetailsAsync(
        HttpContext httpContext,
        [FromQuery(Name = ReturnUrlName)] string? returnUrl
    )
    {
        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return TypedResults.Unauthorized();
        }

        if (string.IsNullOrEmpty(returnUrl))
        {
            return TypedResults.NotFound();
        }

        var openIdContext = httpContext.GetOpenIdContext();
        var subjectId = openIdContext.GetSubjectId(authenticateResult.Principal);
        if (string.IsNullOrEmpty(subjectId))
        {
            return TypedResults.Unauthorized();
        }

        var pending = await ConsentService.ResolvePendingConsentAsync(
            openIdContext,
            subjectId,
            returnUrl,
            httpContext.RequestAborted
        );
        if (pending is not { } value)
        {
            return TypedResults.NotFound();
        }

        var scopes = value
            .Scopes.Select(scope => new ConsentScopeResponse(
                scope.Value,
                scope.Description,
                scope.IsGranted
            ))
            .ToList();
        var consentDetails = new ConsentDetailsResponse(value.ClientId, value.ClientName, scopes);
        return TypedResults.Json(consentDetails);
    }

    /// <summary>
    /// Records the granular consent outcome: an allow persists the subject's decision — the requested scopes (resolved
    /// authoritatively from the continuation, not the posted form) and the subset the subject selected (the posted
    /// <c>scope</c> fields, intersected with the requested scopes) — and returns to the original authorize URL to
    /// resume; a deny answers the parked authorization request with an <c>access_denied</c> error at the client's
    /// redirect URI, or returns to the home page when the return URL names no parked request.
    /// </summary>
    public async ValueTask<IResult> PostConsentAsync(
        HttpContext httpContext,
        [FromForm] string? action,
        [FromForm(Name = ReturnUrlName)] string? returnUrl,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.UiLocalesFieldName)]
            string? uiLocales
    )
    {
        var safeReturnUrl = ResolveSafeReturnUrl(httpContext, returnUrl);
        var languageTag = LocaleSelector.GetCarriedLanguageTag(uiLocales);
        var openIdContext = httpContext.GetOpenIdContext();

        if (string.Equals(action, "deny", StringComparison.OrdinalIgnoreCase))
        {
            // A denial answers the parked authorization request with `access_denied` (OpenID Connect Core 1.0
            // §3.1.2.6).
            // See ADR-1229.
            var accessDenied = openIdContext.ErrorFactory.AccessDenied(
                "The end user denied the authorization request."
            );
            var refusal = await ContinueRefusalService.RefuseAsync(
                openIdContext,
                safeReturnUrl,
                accessDenied,
                httpContext.RequestAborted
            );
            return refusal.HasHttpResult ? refusal.HttpResult : TypedResults.Redirect("/");
        }

        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            var invalidRequestRedirect = BuildLoginRedirect(
                httpContext,
                safeReturnUrl,
                "invalid_request",
                languageTag
            );
            return TypedResults.Redirect(invalidRequestRedirect);
        }

        var subjectId = openIdContext.GetSubjectId(authenticateResult.Principal);
        if (string.IsNullOrEmpty(subjectId) || string.IsNullOrEmpty(returnUrl))
        {
            var invalidRequestRedirect = BuildLoginRedirect(
                httpContext,
                safeReturnUrl,
                "invalid_request",
                languageTag
            );
            return TypedResults.Redirect(invalidRequestRedirect);
        }

        var pending = await ConsentService.ResolvePendingConsentAsync(
            openIdContext,
            subjectId,
            returnUrl,
            httpContext.RequestAborted
        );
        if (pending is { } value)
        {
            var requestedScopes = value.Scopes.Select(scope => scope.Value).ToList();
            var grantedScopes = httpContext.Request.Form["scope"].OfType<string>().ToList();

            await ConsentService.GrantConsentAsync(
                openIdContext,
                value.ClientId,
                subjectId,
                requestedScopes,
                grantedScopes,
                httpContext.RequestAborted
            );
        }

        return TypedResults.Redirect(safeReturnUrl);
    }

    /// <summary>
    /// Ends the current end-user's session from the first-party account page. It runs the same server-side logout
    /// pipeline as the OpenID Connect <c>end_session_endpoint</c> — resolving the session from the login cookie,
    /// clearing the cookie, revoking the session and the subject's grants, and notifying participating relying parties
    /// via back-channel logout — then redirects to the validated local return URL (the login page by default).
    /// </summary>
    public async ValueTask<IResult> PostLogoutAsync(
        HttpContext httpContext,
        [FromForm(Name = ReturnUrlName)] string? returnUrl
    )
    {
        var openIdContext = httpContext.GetOpenIdContext();
        var mediator = openIdContext.Mediator;

        // No id_token_hint is supplied: this is a first-party sign-out, so the pipeline resolves the session from the
        // OP login cookie. The end_session_endpoint remains the entry point for RP-initiated logout.
        var loadCommand = new LoadEndSessionRequestCommand(openIdContext);
        var request = await mediator.SendAsync<LoadEndSessionRequestCommand, IEndSessionRequest>(
            loadCommand,
            httpContext.RequestAborted
        );

        var resolveCommand = new ResolveEndSessionSubjectCommand(openIdContext, request);
        var subject = await mediator.SendAsync<ResolveEndSessionSubjectCommand, EndSessionSubject>(
            resolveCommand,
            httpContext.RequestAborted
        );

        var endSessionCommand = new EndSessionCommand(openIdContext, request, subject);
        await mediator.SendAsync<EndSessionCommand, EndSessionDisposition>(
            endSessionCommand,
            httpContext.RequestAborted
        );

        var requestedReturnUrl = returnUrl;
        if (string.IsNullOrEmpty(returnUrl))
        {
            requestedReturnUrl = SurfacePaths.GetSurfacePath(
                httpContext,
                InteractiveAuthenticationDefaults.LoginPath
            );
        }

        var safeReturnUrl = ResolveSafeReturnUrl(httpContext, requestedReturnUrl);
        return TypedResults.Redirect(safeReturnUrl);
    }

    /// <summary>
    /// Serves the logout confirmation page that the end-session endpoint redirects to when a logout needs the end-user's
    /// confirmation. The server renders a working no-JavaScript form (confirm and cancel buttons) carrying the
    /// antiforgery token and the return URL, which is the parked logout's one-time continue URL; when the single-page
    /// bundle is present the form is injected into its <c>#root</c> so the SPA replaces it on mount.
    /// </summary>
    public async ValueTask<IResult> GetLogoutConfirmationPageAsync(HttpContext httpContext)
    {
        var tokens = IssueAntiforgeryTokens(httpContext);
        var returnUrl = httpContext.Request.Query[ReturnUrlName].ToString();
        var branding = ReadBranding(httpContext);
        var security = InteractivePageSecurity.Create(branding);
        var html = InteractiveHtmlResult.CreateBuffer();
        var frame = await CreatePageFrameAsync(
            httpContext,
            branding,
            security,
            httpContext.RequestAborted
        );
        InteractiveAuthenticationPageRenderer.RenderLogoutConfirmationPage(
            html,
            frame,
            returnUrl,
            tokens.FormFieldName,
            tokens.RequestToken ?? string.Empty
        );
        return new InteractiveHtmlResult(html, security.Policy);
    }

    /// <summary>
    /// Records the end-user's answer to a logout confirmation: a confirmation redirects to the parked logout's one-time
    /// continue URL, which resumes it; any other answer refuses the continuation, so the logout is spent and the
    /// session is left, and returns to the account page in the locale the form carried.
    /// </summary>
    public async ValueTask<IResult> PostLogoutConfirmationAsync(
        HttpContext httpContext,
        [FromForm] string? action,
        [FromForm(Name = ReturnUrlName)] string? returnUrl,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.UiLocalesFieldName)]
            string? uiLocales
    )
    {
        var safeReturnUrl = ResolveSafeReturnUrl(httpContext, returnUrl);
        var languageTag = LocaleSelector.GetCarriedLanguageTag(uiLocales);

        // See ADR-1380.
        if (string.Equals(action, LogoutAction, StringComparison.OrdinalIgnoreCase))
        {
            // The continue URL resumes the parked logout.
            return TypedResults.Redirect(safeReturnUrl);
        }

        var openIdContext = httpContext.GetOpenIdContext();
        var declined = openIdContext.ErrorFactory.AccessDenied(
            "The end user declined to sign out."
        );
        var refusal = await ContinueRefusalService.RefuseAsync(
            openIdContext,
            safeReturnUrl,
            declined,
            httpContext.RequestAborted
        );
        if (refusal.HasHttpResult)
        {
            return refusal.HttpResult;
        }

        var accountPath = SurfacePaths.GetSurfacePath(
            httpContext,
            InteractiveAuthenticationDefaults.AccountPath
        );
        var location = InteractiveLocaleSelector.AppendUiLocalesQuery(accountPath, languageTag);
        return TypedResults.Redirect(location);
    }

    /// <summary>
    /// Serves the authorized-applications page (the single-page bundle), establishing the antiforgery token the revoke
    /// form echoes back. An anonymous request is redirected to the login page with the applications page as its return
    /// URL.
    /// </summary>
    public async ValueTask<IResult> GetApplicationsPageAsync(HttpContext httpContext)
    {
        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return RedirectToLogin(httpContext, InteractiveAuthenticationDefaults.ApplicationsPath);
        }

        return await ServeBundleAsync(httpContext, httpContext.RequestAborted);
    }

    /// <summary>
    /// Returns the current end-user's authorized applications (each with its granted scopes) as JSON for the
    /// authorized-applications page, or <c>401</c> when the request is not authenticated.
    /// </summary>
    public async ValueTask<IResult> GetApplicationsAsync(HttpContext httpContext)
    {
        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return TypedResults.Unauthorized();
        }

        var openIdContext = httpContext.GetOpenIdContext();
        var subjectId = openIdContext.GetSubjectId(authenticateResult.Principal);
        if (string.IsNullOrEmpty(subjectId))
        {
            return TypedResults.Unauthorized();
        }

        var applications = await ConsentService.GetAuthorizedApplicationsAsync(
            openIdContext,
            subjectId,
            httpContext.RequestAborted
        );

        var applicationResponses = applications
            .Select(app =>
            {
                var scopes = app
                    .Scopes.Select(scope => new ConsentScopeResponse(
                        scope.Value,
                        scope.Description,
                        scope.IsGranted
                    ))
                    .ToList();
                return new AuthorizedApplicationResponse(app.ClientId, app.ClientName, scopes);
            })
            .ToList();
        var applicationsResponse = new AuthorizedApplicationsResponse(applicationResponses);
        return TypedResults.Json(applicationsResponse);
    }

    /// <summary>
    /// Revokes the current end-user's standing consent for a single application, then redirects back to the
    /// authorized-applications page.
    /// </summary>
    public async ValueTask<IResult> PostRevokeApplicationAsync(
        HttpContext httpContext,
        [FromForm(Name = "client_id")] string? clientId
    )
    {
        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return RedirectToLogin(httpContext, InteractiveAuthenticationDefaults.ApplicationsPath);
        }

        var openIdContext = httpContext.GetOpenIdContext();
        var subjectId = openIdContext.GetSubjectId(authenticateResult.Principal);
        if (!string.IsNullOrEmpty(subjectId) && !string.IsNullOrEmpty(clientId))
        {
            await ConsentService.RevokeConsentAsync(
                openIdContext,
                clientId,
                subjectId,
                httpContext.RequestAborted
            );
        }

        var applicationsPath = SurfacePaths.GetSurfacePath(
            httpContext,
            InteractiveAuthenticationDefaults.ApplicationsPath
        );
        return TypedResults.Redirect(applicationsPath);
    }

    /// <summary>
    /// Serves the active-sessions page (the single-page bundle), establishing the antiforgery token the revoke forms
    /// echo back. An anonymous request is redirected to the login page with the sessions page as its return URL.
    /// </summary>
    public async ValueTask<IResult> GetSessionsPageAsync(HttpContext httpContext)
    {
        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return RedirectToLogin(httpContext, InteractiveAuthenticationDefaults.SessionsPath);
        }

        return await ServeBundleAsync(httpContext, httpContext.RequestAborted);
    }

    /// <summary>
    /// Returns the current end-user's active sessions as JSON for the active-sessions page, marking the request's own
    /// session, or <c>401</c> when the request is not authenticated.
    /// </summary>
    public async ValueTask<IResult> GetSessionsAsync(HttpContext httpContext)
    {
        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return TypedResults.Unauthorized();
        }

        var openIdContext = httpContext.GetOpenIdContext();
        var principal = authenticateResult.Principal;
        var subjectId = openIdContext.GetSubjectId(principal);
        if (string.IsNullOrEmpty(subjectId))
        {
            return TypedResults.Unauthorized();
        }

        var currentSessionId = principal.FindFirst(JoseClaimNames.Payload.Sid)?.Value;

        var sessions = await SessionService.GetSessionsAsync(
            openIdContext,
            subjectId,
            httpContext.RequestAborted
        );

        var sessionSummaries = sessions
            .Select(session => ToSummary(session, currentSessionId))
            .ToList();
        var sessionsResponse = new SessionsResponse(sessionSummaries);
        return TypedResults.Json(sessionsResponse);
    }

    // Projects a session into the active-sessions page's view model, marking the request's own session.
    private static SessionSummaryResponse ToSummary(SessionInfo session, string? currentSessionId)
    {
        var grant = session.Grant;
        var isCurrent = string.Equals(
            session.SessionId,
            currentSessionId,
            StringComparison.Ordinal
        );
        var createdAt = session.CreatedWhen.ToUnixTimeSeconds();
        var lastActivityAt = grant.LastActivityWhen.ToUnixTimeSeconds();
        var expiresAt = session.ExpiresWhen.ToUnixTimeSeconds();
        return new SessionSummaryResponse(
            session.SessionId,
            isCurrent,
            createdAt,
            lastActivityAt,
            expiresAt,
            grant.AuthenticationMethods ?? [],
            grant.IdentityProvider,
            grant.UserAgent,
            grant.RemoteIpAddress
        );
    }

    /// <summary>
    /// Revokes a single one of the current end-user's sessions by its <c>sid</c> (self-service), then redirects back to
    /// the active-sessions page. The session is revoked only when it belongs to the authenticated subject, so a user
    /// cannot terminate another subject's session.
    /// </summary>
    public async ValueTask<IResult> PostRevokeSessionAsync(
        HttpContext httpContext,
        [FromForm(Name = "sid")] string? sessionId
    )
    {
        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return RedirectToLogin(httpContext, InteractiveAuthenticationDefaults.SessionsPath);
        }

        var openIdContext = httpContext.GetOpenIdContext();
        var subjectId = openIdContext.GetSubjectId(authenticateResult.Principal);
        if (!string.IsNullOrEmpty(subjectId) && !string.IsNullOrEmpty(sessionId))
        {
            // Resolve the session and confirm it belongs to the authenticated subject before revoking, so a crafted
            // sid cannot terminate a session the caller does not own.
            var session = await SessionService.GetOrDefaultAsync(
                openIdContext,
                sessionId,
                httpContext.RequestAborted
            );
            if (
                session is { } found
                && string.Equals(found.SubjectId, subjectId, StringComparison.Ordinal)
            )
            {
                await SessionService.RevokeAsync(
                    openIdContext,
                    sessionId,
                    SessionTerminationReasons.UserInitiated,
                    httpContext.RequestAborted
                );

                // Revoking the request's own session invalidates the login cookie; clear it so the next request
                // re-authenticates instead of presenting a cookie whose session no longer exists.
                var currentSessionId = authenticateResult
                    .Principal.FindFirst(JoseClaimNames.Payload.Sid)
                    ?.Value;
                if (string.Equals(sessionId, currentSessionId, StringComparison.Ordinal))
                {
                    await httpContext.SignOutAsync(
                        InteractiveAuthenticationDefaults.CookieSchemeName
                    );
                }
            }
        }

        var sessionsPath = SurfacePaths.GetSurfacePath(
            httpContext,
            InteractiveAuthenticationDefaults.SessionsPath
        );
        return TypedResults.Redirect(sessionsPath);
    }

    /// <summary>
    /// Revokes the current end-user's sessions in bulk (self-service), then redirects back to the active-sessions page.
    /// When <c>scope</c> is <c>others</c> ("sign out everywhere else") the request's own session is retained; otherwise
    /// every session is revoked and the login cookie is cleared ("sign out everywhere").
    /// Signing out everywhere else also revokes every grant the end user holds under no session.
    /// </summary>
    public async ValueTask<IResult> PostRevokeAllSessionsAsync(
        HttpContext httpContext,
        [FromForm(Name = "scope")] string? scope
    )
    {
        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return RedirectToLogin(httpContext, InteractiveAuthenticationDefaults.SessionsPath);
        }

        var openIdContext = httpContext.GetOpenIdContext();
        var principal = authenticateResult.Principal;
        var subjectId = openIdContext.GetSubjectId(principal);
        if (string.IsNullOrEmpty(subjectId))
        {
            var sessionsPath = SurfacePaths.GetSurfacePath(
                httpContext,
                InteractiveAuthenticationDefaults.SessionsPath
            );
            return TypedResults.Redirect(sessionsPath);
        }

        var currentSessionId = principal.FindFirst(JoseClaimNames.Payload.Sid)?.Value;

        // "Sign out everywhere else" retains the request's own session; it needs a known current sid to exclude.
        if (
            string.Equals(scope, SignOutOthersScope, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(currentSessionId)
        )
        {
            await SessionService.RevokeAllExceptAsync(
                openIdContext,
                subjectId,
                currentSessionId,
                SessionTerminationReasons.UserInitiated,
                httpContext.RequestAborted
            );

            var sessionsPath = SurfacePaths.GetSurfacePath(
                httpContext,
                InteractiveAuthenticationDefaults.SessionsPath
            );
            return TypedResults.Redirect(sessionsPath);
        }

        await SessionService.RevokeAllAsync(
            openIdContext,
            subjectId,
            SessionTerminationReasons.GlobalSignOut,
            httpContext.RequestAborted
        );

        // A full sign-out revokes the request's own session too, so clear the login cookie and return to the login page.
        await httpContext.SignOutAsync(InteractiveAuthenticationDefaults.CookieSchemeName);
        var loginPath = SurfacePaths.GetSurfacePath(
            httpContext,
            InteractiveAuthenticationDefaults.LoginPath
        );
        return TypedResults.Redirect(loginPath);
    }

    /// <summary>
    /// Serves the change-password page (the single-page bundle), establishing the antiforgery token its form echoes
    /// back. A request without an active session is redirected to the login page with the change-password page as its
    /// return URL.
    /// </summary>
    public async ValueTask<IResult> GetPasswordPageAsync(HttpContext httpContext)
    {
        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return RedirectToLogin(httpContext, InteractiveAuthenticationDefaults.PasswordPath);
        }

        return await ServeBundleAsync(httpContext, httpContext.RequestAborted);
    }

    /// <summary>
    /// Changes the current end-user's password from the change-password form, then redirects back to the page with the
    /// outcome: <c>?status=changed</c>, or <c>?error=</c> with <c>invalid_request</c> for a missing field,
    /// <c>password_mismatch</c> when the confirmation differs from the new password, or the error code the credential
    /// backend reports: <c>invalid_current_password</c> when the current password is wrong, <c>not_supported</c> when
    /// it has no password to change, or a password policy code such as <c>password_too_short</c>. A change keeps the
    /// current session and ends every other session. A request without an active session is redirected to the login
    /// page.
    /// </summary>
    public async ValueTask<IResult> PostPasswordAsync(
        HttpContext httpContext,
        [FromForm(Name = "current_password")] string? currentPassword,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.NewPasswordFieldName)]
            string? newPassword,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.ConfirmPasswordFieldName)]
            string? confirmPassword
    )
    {
        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return RedirectToLogin(httpContext, InteractiveAuthenticationDefaults.PasswordPath);
        }

        var openIdContext = httpContext.GetOpenIdContext();
        var principal = authenticateResult.Principal;
        var subjectId = openIdContext.GetSubjectId(principal);
        var currentSessionId = principal.FindFirst(JoseClaimNames.Payload.Sid)?.Value;
        if (string.IsNullOrEmpty(subjectId) || string.IsNullOrEmpty(currentSessionId))
        {
            return RedirectToLogin(httpContext, InteractiveAuthenticationDefaults.PasswordPath);
        }

        if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
        {
            return RedirectToSurfacePage(
                httpContext,
                InteractiveAuthenticationDefaults.PasswordPath,
                languageTag: null,
                (ErrorQueryName, InteractivePasswordChangeErrors.InvalidRequest)
            );
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            return RedirectToSurfacePage(
                httpContext,
                InteractiveAuthenticationDefaults.PasswordPath,
                languageTag: null,
                (ErrorQueryName, InteractivePasswordChangeErrors.PasswordMismatch)
            );
        }

        var currentPasswordChars = currentPassword.AsSpan();
        var newPasswordChars = newPassword.AsSpan();
        using var currentPasswordSecret = Secret.Create(currentPasswordChars);
        using var newPasswordSecret = Secret.Create(newPasswordChars);
        var result = await PasswordChanger.ChangePasswordAsync(
            openIdContext,
            principal,
            currentPasswordSecret,
            newPasswordSecret,
            httpContext.RequestAborted
        );
        if (!result.IsChanged)
        {
            return RedirectToSurfacePage(
                httpContext,
                InteractiveAuthenticationDefaults.PasswordPath,
                languageTag: null,
                (ErrorQueryName, result.Error)
            );
        }

        // A change keeps the current session, ends every other one and revokes the grants issued under no session.
        // The password is saved, so the termination runs on its own bounded token and a failure is logged.
        // See ADR-1230 and ADR-1351.
        await PostCommitSecurityEffects.TerminateOtherSessionsAsync(
            openIdContext,
            subjectId,
            currentSessionId,
            SessionTerminationReasons.PasswordChanged
        );
        return RedirectToSurfacePage(
            httpContext,
            InteractiveAuthenticationDefaults.PasswordPath,
            languageTag: null,
            (StatusQueryName, PasswordChangedStatus)
        );
    }

    /// <summary>
    /// Serves the forgot-password page, where an end user who is not signed in asks for a reset code. The server
    /// renders a working no-JavaScript form carrying the antiforgery token and the return URL. The page is not found
    /// while the tenant does not let its end users reset a password.
    /// </summary>
    // See ADR-1533.
    public async ValueTask<IResult> GetForgotPasswordPageAsync(HttpContext httpContext)
    {
        if (!IsPasswordResetEnabled(httpContext))
        {
            return TypedResults.NotFound();
        }

        var tokens = IssueAntiforgeryTokens(httpContext);
        var query = httpContext.Request.Query;
        var branding = ReadBranding(httpContext);
        var security = InteractivePageSecurity.Create(branding);
        var frame = await CreatePageFrameAsync(
            httpContext,
            branding,
            security,
            httpContext.RequestAborted
        );
        var html = InteractiveHtmlResult.CreateBuffer();
        var returnUrl = query[ReturnUrlName].ToString();
        var errorQueryValue = query[ErrorQueryName].ToString();
        var errorMessage = DescribePasswordResetError(errorQueryValue, frame.Locale.Messages);
        InteractiveAuthenticationPageRenderer.RenderForgotPasswordPage(
            html,
            frame,
            returnUrl,
            tokens.FormFieldName,
            tokens.RequestToken ?? string.Empty,
            errorMessage
        );
        return new InteractiveHtmlResult(html, security.Policy);
    }

    /// <summary>
    /// Serves the reset-password page, where an end user who is not signed in presents the reset code and chooses a new
    /// password. The server renders a working no-JavaScript form carrying the antiforgery token, the return URL and the
    /// handle of the reset flow. The page is not found while the tenant does not let its end users reset a password.
    /// </summary>
    // See ADR-1533.
    public async ValueTask<IResult> GetResetPasswordPageAsync(HttpContext httpContext)
    {
        if (!IsPasswordResetEnabled(httpContext))
        {
            return TypedResults.NotFound();
        }

        var tokens = IssueAntiforgeryTokens(httpContext);
        var query = httpContext.Request.Query;
        var branding = ReadBranding(httpContext);
        var security = InteractivePageSecurity.Create(branding);
        var frame = await CreatePageFrameAsync(
            httpContext,
            branding,
            security,
            httpContext.RequestAborted
        );
        var html = InteractiveHtmlResult.CreateBuffer();
        var returnUrl = query[ReturnUrlName].ToString();
        var flowId = query[FlowQueryName].ToString();
        var errorQueryValue = query[ErrorQueryName].ToString();
        var errorMessage = DescribePasswordResetError(errorQueryValue, frame.Locale.Messages);
        InteractiveAuthenticationPageRenderer.RenderResetPasswordPage(
            html,
            frame,
            returnUrl,
            flowId,
            tokens.FormFieldName,
            tokens.RequestToken ?? string.Empty,
            errorMessage
        );
        return new InteractiveHtmlResult(html, security.Policy);
    }

    /// <summary>
    /// Starts a password reset for the user name the forgot-password form posts, then redirects to the reset-password
    /// page with the handle of the reset flow, whether or not the account exists, so the answer never tells. A missing
    /// user name redirects back to the forgot-password page with <c>?error=invalid_request</c>. The page is not found
    /// while the tenant does not let its end users reset a password.
    /// </summary>
    // See ADR-1533.
    public async ValueTask<IResult> PostForgotPasswordAsync(
        HttpContext httpContext,
        [FromForm] string? username,
        [FromForm(Name = ReturnUrlName)] string? returnUrl,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.UiLocalesFieldName)]
            string? uiLocales
    )
    {
        if (!IsPasswordResetEnabled(httpContext))
        {
            return TypedResults.NotFound();
        }

        var languageTag = LocaleSelector.GetCarriedLanguageTag(uiLocales);
        var carriedReturnUrl = string.IsNullOrEmpty(returnUrl)
            ? null
            : ResolveSafeReturnUrl(httpContext, returnUrl);
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToSurfacePage(
                httpContext,
                InteractiveAuthenticationDefaults.PasswordForgotPath,
                languageTag,
                (ReturnUrlName, carriedReturnUrl),
                (ErrorQueryName, InteractivePasswordChangeErrors.InvalidRequest)
            );
        }

        var openIdContext = httpContext.GetOpenIdContext();
        var locale = SelectLocale(httpContext, languageTag);
        var flowId = await PasswordResetter.SendResetCodeAsync(
            openIdContext,
            username,
            locale,
            httpContext.RequestAborted
        );
        return RedirectToSurfacePage(
            httpContext,
            InteractiveAuthenticationDefaults.PasswordResetPath,
            languageTag,
            (FlowQueryName, flowId),
            (ReturnUrlName, carriedReturnUrl)
        );
    }

    /// <summary>
    /// Resets the password from the reset-password form, then redirects to the login page with
    /// <c>?status=password_reset</c>, or back to the reset-password page, keeping the flow, with <c>?error=</c> and
    /// <c>invalid_request</c> for a missing field, <c>password_mismatch</c> when the confirmation differs from the new
    /// password, or the error code the credential backend reports, such as <c>invalid_code</c> or a password policy
    /// code. The page is not found while the tenant does not let its end users reset a password.
    /// </summary>
    // See ADR-1533.
    public async ValueTask<IResult> PostResetPasswordAsync(
        HttpContext httpContext,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.FlowFieldName)] string? flow,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.CodeFieldName)] string? code,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.NewPasswordFieldName)]
            string? newPassword,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.ConfirmPasswordFieldName)]
            string? confirmPassword,
        [FromForm(Name = ReturnUrlName)] string? returnUrl,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.UiLocalesFieldName)]
            string? uiLocales
    )
    {
        if (!IsPasswordResetEnabled(httpContext))
        {
            return TypedResults.NotFound();
        }

        var languageTag = LocaleSelector.GetCarriedLanguageTag(uiLocales);
        var carriedReturnUrl = string.IsNullOrEmpty(returnUrl)
            ? null
            : ResolveSafeReturnUrl(httpContext, returnUrl);

        string? error = null;
        if (
            string.IsNullOrEmpty(flow)
            || string.IsNullOrEmpty(code)
            || string.IsNullOrEmpty(newPassword)
        )
        {
            error = InteractivePasswordChangeErrors.InvalidRequest;
        }
        else if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            error = InteractivePasswordChangeErrors.PasswordMismatch;
        }
        else
        {
            var openIdContext = httpContext.GetOpenIdContext();
            var resetCodeChars = code.AsSpan();
            var newPasswordChars = newPassword.AsSpan();
            using var codeSecret = Secret.Create(resetCodeChars);
            using var newPasswordSecret = Secret.Create(newPasswordChars);
            var result = await PasswordResetter.ResetPasswordAsync(
                openIdContext,
                flow,
                codeSecret,
                newPasswordSecret,
                httpContext.RequestAborted
            );
            if (result.IsChanged)
            {
                return RedirectToSurfacePage(
                    httpContext,
                    InteractiveAuthenticationDefaults.LoginPath,
                    languageTag,
                    (ReturnUrlName, carriedReturnUrl),
                    (StatusQueryName, PasswordResetStatus)
                );
            }

            error = result.Error;
        }

        return RedirectToSurfacePage(
            httpContext,
            InteractiveAuthenticationDefaults.PasswordResetPath,
            languageTag,
            (FlowQueryName, flow),
            (ReturnUrlName, carriedReturnUrl),
            (ErrorQueryName, error)
        );
    }

    /// <summary>
    /// Serves the email page (the single-page bundle), establishing the antiforgery token its forms echo back. A
    /// request without an active session is redirected to the login page with the email page as its return URL. The
    /// page is not found while the tenant does not let its end users verify their address.
    /// </summary>
    // See ADR-1533.
    public async ValueTask<IResult> GetEmailPageAsync(HttpContext httpContext)
    {
        if (!IsEmailVerificationEnabled(httpContext))
        {
            return TypedResults.NotFound();
        }

        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return RedirectToLogin(httpContext, InteractiveAuthenticationDefaults.EmailPath);
        }

        return await ServeBundleAsync(httpContext, httpContext.RequestAborted);
    }

    /// <summary>
    /// Returns the address of the current end user and whether it is verified as JSON for the email page, <c>401</c>
    /// when the request is not authenticated, or <c>404</c> when the end user has no address to verify or the tenant
    /// does not let its end users verify their address.
    /// </summary>
    // See ADR-1533.
    public async ValueTask<IResult> GetEmailStatusAsync(HttpContext httpContext)
    {
        if (!IsEmailVerificationEnabled(httpContext))
        {
            return TypedResults.NotFound();
        }

        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return TypedResults.Unauthorized();
        }

        var openIdContext = httpContext.GetOpenIdContext();
        var status = await EmailVerifier.GetStatusAsync(
            openIdContext,
            authenticateResult.Principal,
            httpContext.RequestAborted
        );
        if (status is null)
        {
            return TypedResults.NotFound();
        }

        var response = new AccountEmailResponse(status.Email, status.IsVerified);
        return TypedResults.Json(response);
    }

    /// <summary>
    /// Mails the current end user's address a code that verifies it, then redirects to the email page with
    /// <c>?flow=</c> and the handle of the verification flow, or with <c>?error=</c> and the reason the code is
    /// refused, such as <c>already_verified</c>, <c>no_email</c> or <c>too_many_codes</c>. A request without an active
    /// session is redirected to the login page. The page is not found while the tenant does not let its end users
    /// verify their address.
    /// </summary>
    // See ADR-1533.
    public async ValueTask<IResult> PostSendEmailCodeAsync(
        HttpContext httpContext,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.UiLocalesFieldName)]
            string? uiLocales
    )
    {
        if (!IsEmailVerificationEnabled(httpContext))
        {
            return TypedResults.NotFound();
        }

        var languageTag = LocaleSelector.GetCarriedLanguageTag(uiLocales);

        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return RedirectToLogin(httpContext, InteractiveAuthenticationDefaults.EmailPath);
        }

        var openIdContext = httpContext.GetOpenIdContext();
        var locale = SelectLocale(httpContext, languageTag);
        var sent = await EmailVerifier.SendVerificationCodeAsync(
            openIdContext,
            authenticateResult.Principal,
            locale,
            httpContext.RequestAborted
        );
        return sent.IsAccepted
            ? RedirectToSurfacePage(
                httpContext,
                InteractiveAuthenticationDefaults.EmailPath,
                languageTag,
                (FlowQueryName, sent.FlowId)
            )
            : RedirectToSurfacePage(
                httpContext,
                InteractiveAuthenticationDefaults.EmailPath,
                languageTag,
                (ErrorQueryName, sent.Error)
            );
    }

    /// <summary>
    /// Verifies the current end user's address from the code the email page posts, then redirects to the email page
    /// with <c>?status=verified</c>, or back to it, keeping the flow, with <c>?error=</c> and <c>invalid_request</c>
    /// for a missing field or the error code the backend reports, such as <c>invalid_code</c>. Every redirect keeps the
    /// language the form carries. A request without an active session is redirected to the login page. The page is not
    /// found while the tenant does not let its end users verify their address.
    /// </summary>
    // See ADR-1533.
    public async ValueTask<IResult> PostVerifyEmailAsync(
        HttpContext httpContext,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.FlowFieldName)] string? flow,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.CodeFieldName)] string? code,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.UiLocalesFieldName)]
            string? uiLocales
    )
    {
        if (!IsEmailVerificationEnabled(httpContext))
        {
            return TypedResults.NotFound();
        }

        var languageTag = LocaleSelector.GetCarriedLanguageTag(uiLocales);

        var authenticateResult = await AuthenticateSessionAsync(
            httpContext,
            httpContext.RequestAborted
        );
        if (!authenticateResult.Succeeded)
        {
            return RedirectToLogin(httpContext, InteractiveAuthenticationDefaults.EmailPath);
        }

        string? error;
        if (string.IsNullOrEmpty(flow) || string.IsNullOrEmpty(code))
        {
            error = InteractivePasswordChangeErrors.InvalidRequest;
        }
        else
        {
            var openIdContext = httpContext.GetOpenIdContext();
            var emailCodeChars = code.AsSpan();
            using var codeSecret = Secret.Create(emailCodeChars);
            var verified = await EmailVerifier.VerifyEmailAsync(
                openIdContext,
                authenticateResult.Principal,
                flow,
                codeSecret,
                httpContext.RequestAborted
            );
            if (verified.IsAccepted)
            {
                return RedirectToSurfacePage(
                    httpContext,
                    InteractiveAuthenticationDefaults.EmailPath,
                    languageTag,
                    (StatusQueryName, EmailVerifiedStatus)
                );
            }

            error = verified.Error;
        }

        return RedirectToSurfacePage(
            httpContext,
            InteractiveAuthenticationDefaults.EmailPath,
            languageTag,
            (FlowQueryName, flow),
            (ErrorQueryName, error)
        );
    }

    /// <summary>
    /// Serves the sign-in code page, where an end user whose password is verified presents the code mailed to the
    /// account's address. The server renders a working no-JavaScript form carrying the antiforgery token. A request
    /// without a pending sign-in is redirected to the login page.
    /// </summary>
    // See ADR-1533.
    public async ValueTask<IResult> GetSignInCodePageAsync(HttpContext httpContext)
    {
        var pending = await httpContext.AuthenticateAsync(
            InteractiveAuthenticationDefaults.PendingSignInSchemeName
        );
        if (!pending.Succeeded)
        {
            return RedirectToSurfacePage(
                httpContext,
                InteractiveAuthenticationDefaults.LoginPath,
                languageTag: null
            );
        }

        var tokens = IssueAntiforgeryTokens(httpContext);
        var query = httpContext.Request.Query;
        var branding = ReadBranding(httpContext);
        var security = InteractivePageSecurity.Create(branding);
        var frame = await CreatePageFrameAsync(
            httpContext,
            branding,
            security,
            httpContext.RequestAborted
        );
        var html = InteractiveHtmlResult.CreateBuffer();
        var errorQueryValue = query[ErrorQueryName].ToString();
        var errorMessage = DescribeSignInCodeError(errorQueryValue, frame.Locale.Messages);
        var statusQueryValue = query[StatusQueryName].ToString();
        var statusMessage = string.Equals(
            statusQueryValue,
            CodeSentStatusValue,
            StringComparison.Ordinal
        )
            ? frame.Locale.Messages.CodeSentStatus
            : null;
        InteractiveAuthenticationPageRenderer.RenderSignInCodePage(
            html,
            frame,
            tokens.FormFieldName,
            tokens.RequestToken ?? string.Empty,
            errorMessage,
            statusMessage
        );
        return new InteractiveHtmlResult(html, security.Policy);
    }

    /// <summary>
    /// Completes or repeats the second factor of a sign-in from the sign-in code form. The <c>resend</c> action mails a
    /// new code and redirects back to the page with <c>?status=sent</c>, or with <c>?error=</c> and the reason the code
    /// is refused. Otherwise the code is verified: a refusal redirects back to the page with <c>?error=</c>, and a
    /// right code adds the <c>otp</c> and <c>mfa</c> authentication methods to the end user, creates the session and
    /// redirects to the return URL the sign-in began with. A request without a pending sign-in is redirected to the
    /// login page.
    /// </summary>
    // See ADR-1533.
    public async ValueTask<IResult> PostSignInCodeAsync(
        HttpContext httpContext,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.CodeFieldName)] string? code,
        [FromForm(Name = InteractiveAuthenticationPageRenderer.ActionFieldName)] string? action
    )
    {
        var pending = await httpContext.AuthenticateAsync(
            InteractiveAuthenticationDefaults.PendingSignInSchemeName
        );
        if (!pending.Succeeded || pending.Properties is null)
        {
            return RedirectToSurfacePage(
                httpContext,
                InteractiveAuthenticationDefaults.LoginPath,
                languageTag: null
            );
        }

        var principal = pending.Principal;
        var properties = pending.Properties;
        var flowId = ReadPendingItem(properties, EmailCodeFlowItem);
        var storedReturnUrl = ReadPendingItem(properties, ReturnUrlItem);
        var returnUrl = ResolveSafeReturnUrl(httpContext, storedReturnUrl);
        var storedUiLocales = ReadPendingItem(properties, UiLocalesItem);
        var languageTag = LocaleSelector.GetCarriedLanguageTag(storedUiLocales);
        var identity = principal.Identities.FirstOrDefault(candidate => candidate.IsAuthenticated);
        if (string.IsNullOrEmpty(flowId) || identity is null)
        {
            await httpContext.SignOutAsync(
                InteractiveAuthenticationDefaults.PendingSignInSchemeName
            );
            return RedirectToSurfacePage(
                httpContext,
                InteractiveAuthenticationDefaults.LoginPath,
                languageTag
            );
        }

        var openIdContext = httpContext.GetOpenIdContext();
        if (string.Equals(action, ResendAction, StringComparison.Ordinal))
        {
            var locale = SelectLocale(httpContext, languageTag);
            var sent = await SignInCodeChallenge.SendSignInCodeAsync(
                openIdContext,
                principal,
                locale,
                httpContext.RequestAborted
            );
            if (!sent.IsAccepted || sent.FlowId is null)
            {
                return RedirectToSurfacePage(
                    httpContext,
                    InteractiveAuthenticationDefaults.LoginCodePath,
                    languageTag,
                    (ErrorQueryName, sent.Error ?? InteractiveEmailCodeErrors.TooManyCodes)
                );
            }

            var renewedProperties = CreatePendingSignInProperties(
                sent.FlowId,
                returnUrl,
                languageTag
            );
            await httpContext.SignInAsync(
                InteractiveAuthenticationDefaults.PendingSignInSchemeName,
                principal,
                renewedProperties
            );
            return RedirectToSurfacePage(
                httpContext,
                InteractiveAuthenticationDefaults.LoginCodePath,
                languageTag,
                (StatusQueryName, CodeSentStatusValue)
            );
        }

        if (string.IsNullOrEmpty(code))
        {
            return RedirectToSurfacePage(
                httpContext,
                InteractiveAuthenticationDefaults.LoginCodePath,
                languageTag,
                (ErrorQueryName, InteractivePasswordChangeErrors.InvalidRequest)
            );
        }

        var signInCodeChars = code.AsSpan();
        using var codeSecret = Secret.Create(signInCodeChars);
        var verified = await SignInCodeChallenge.VerifySignInCodeAsync(
            openIdContext,
            principal,
            flowId,
            codeSecret,
            httpContext.RequestAborted
        );
        if (!verified.IsAccepted)
        {
            return RedirectToSurfacePage(
                httpContext,
                InteractiveAuthenticationDefaults.LoginCodePath,
                languageTag,
                (ErrorQueryName, verified.Error)
            );
        }

        // The mailed code is a one-time password, and with the password it is a second factor.
        AddAuthenticationMethod(identity, OpenIdProtocol.AuthenticationMethods.OneTimePassword);
        AddAuthenticationMethod(
            identity,
            OpenIdProtocol.AuthenticationMethods.MultiFactorAuthentication
        );

        // The end user's standing is validated again, since it may have changed while the code was on its way.
        var standing = await ValidateStandingAsync(
            openIdContext,
            principal,
            httpContext.RequestAborted
        );
        if (standing.HasError)
        {
            await httpContext.SignOutAsync(
                InteractiveAuthenticationDefaults.PendingSignInSchemeName
            );
            return await RefuseSignInAsync(
                httpContext,
                openIdContext,
                returnUrl,
                languageTag,
                standing.Error,
                httpContext.RequestAborted
            );
        }

        var completed = await CompleteSignInAsync(
            httpContext,
            openIdContext,
            principal,
            returnUrl,
            httpContext.RequestAborted
        );
        await httpContext.SignOutAsync(InteractiveAuthenticationDefaults.PendingSignInSchemeName);
        return completed;
    }

    /// <summary>
    /// Mails the end user whose password is verified the code that completes the sign-in and holds the principal in the
    /// pending sign-in cookie. A tenant that allows no more codes sends the end user back to the login page with that
    /// error; an account that cannot receive a code is refused as an account whose standing is refused.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="openIdContext">The context of the tenant and environment the request is for.</param>
    /// <param name="principal">The principal whose password is verified.</param>
    /// <param name="safeReturnUrl">The return URL the end user continues to once the sign-in completes.</param>
    /// <param name="languageTag">The language tag the form carried, which words the mail, or
    /// <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The redirect to the sign-in code page, to the login page or to the refusal of the sign-in.</returns>
    // See ADR-1533.
    private async ValueTask<IResult> BeginSignInCodeAsync(
        HttpContext httpContext,
        OpenIdContext openIdContext,
        ClaimsPrincipal principal,
        string safeReturnUrl,
        string? languageTag,
        CancellationToken cancellationToken
    )
    {
        var locale = SelectLocale(httpContext, languageTag);
        var sent = await SignInCodeChallenge.SendSignInCodeAsync(
            openIdContext,
            principal,
            locale,
            cancellationToken
        );
        if (!sent.IsAccepted || sent.FlowId is null)
        {
            if (
                string.Equals(
                    sent.Error,
                    InteractiveEmailCodeErrors.TooManyCodes,
                    StringComparison.Ordinal
                )
            )
            {
                var tooManyCodesRedirect = BuildLoginRedirect(
                    httpContext,
                    safeReturnUrl,
                    InteractiveEmailCodeErrors.TooManyCodes,
                    languageTag
                );
                return TypedResults.Redirect(tooManyCodesRedirect);
            }

            var accessDenied = openIdContext.ErrorFactory.AccessDenied(
                "The tenant requires a second factor this account cannot receive."
            );
            return await RefuseSignInAsync(
                httpContext,
                openIdContext,
                safeReturnUrl,
                languageTag,
                accessDenied,
                cancellationToken
            );
        }

        var pendingProperties = CreatePendingSignInProperties(
            sent.FlowId,
            safeReturnUrl,
            languageTag
        );
        await httpContext.SignInAsync(
            InteractiveAuthenticationDefaults.PendingSignInSchemeName,
            principal,
            pendingProperties
        );
        return RedirectToSurfacePage(
            httpContext,
            InteractiveAuthenticationDefaults.LoginCodePath,
            languageTag
        );
    }

    /// <summary>
    /// Creates the properties of the pending sign-in cookie, which carry the email code flow, the return URL and the
    /// language tag the sign-in code page renders in.
    /// </summary>
    /// <param name="flowId">The identifier of the email code flow the mailed code belongs to.</param>
    /// <param name="returnUrl">The return URL the end user continues to once the sign-in completes.</param>
    /// <param name="languageTag">The language tag the form carried, which is kept only when it is set.</param>
    /// <returns>The properties of the pending sign-in cookie.</returns>
    private static AuthenticationProperties CreatePendingSignInProperties(
        string flowId,
        string returnUrl,
        string? languageTag
    )
    {
        var properties = new AuthenticationProperties();
        properties.Items[EmailCodeFlowItem] = flowId;
        properties.Items[ReturnUrlItem] = returnUrl;
        if (!string.IsNullOrEmpty(languageTag))
        {
            properties.Items[UiLocalesItem] = languageTag;
        }

        return properties;
    }

    /// <summary>
    /// Reads an item of the pending sign-in cookie's properties.
    /// </summary>
    /// <param name="properties">The properties of the pending sign-in cookie.</param>
    /// <param name="name">The name of the item.</param>
    /// <returns>The value of the item, or <see langword="null"/> when the properties hold no such item.</returns>
    private static string? ReadPendingItem(AuthenticationProperties properties, string name) =>
        properties.Items.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// Adds an authentication method reference to the identity unless the identity already carries it.
    /// </summary>
    /// <param name="identity">The identity of the signed-in principal.</param>
    /// <param name="method">The authentication method reference to add.</param>
    private static void AddAuthenticationMethod(ClaimsIdentity identity, string method)
    {
        if (!identity.HasClaim(JoseClaimNames.Payload.Amr, method))
        {
            var claim = new Claim(JoseClaimNames.Payload.Amr, method);
            identity.AddClaim(claim);
        }
    }

    /// <summary>
    /// Determines whether the tenant lets its end users verify their address, because the email page and its endpoints
    /// exist only then.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns><see langword="true"/> when the tenant lets its end users verify their address.</returns>
    // See ADR-1533.
    private static bool IsEmailVerificationEnabled(HttpContext httpContext)
    {
        var settings = httpContext.GetOpenIdContext().Tenant.SettingsProvider.Collection;
        return settings.GetValue(OpenIdSettingKeys.EmailVerificationEnabled);
    }

    /// <summary>
    /// Determines whether the tenant lets its end users reset a password, because the forgot-password and
    /// reset-password pages exist only then.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns><see langword="true"/> when the tenant lets its end users reset a password.</returns>
    // See ADR-1533.
    private static bool IsPasswordResetEnabled(HttpContext httpContext)
    {
        var settings = httpContext.GetOpenIdContext().Tenant.SettingsProvider.Collection;
        return settings.GetValue(OpenIdSettingKeys.PasswordResetEnabled);
    }

    /// <summary>
    /// Builds the address of the forgot-password page, relative to the surface the page's base names, carrying the
    /// return URL and the locale the login page carried.
    /// </summary>
    /// <param name="returnUrl">The return URL the login page carried, which is left out when it is empty.</param>
    /// <param name="languageTag">The language tag the login page carried, or <see langword="null"/>.</param>
    /// <returns>The address of the forgot-password page.</returns>
    private static string BuildForgotPasswordHref(string returnUrl, string? languageTag)
    {
        var href = InteractiveAuthenticationPageRenderer.ForgotPasswordFormAction;
        if (!string.IsNullOrEmpty(returnUrl))
        {
            href += $"?{ReturnUrlName}={Uri.EscapeDataString(returnUrl)}";
        }

        return InteractiveLocaleSelector.AppendUiLocalesQuery(href, languageTag);
    }

    /// <summary>
    /// Maps the error code the forgot-password and reset-password pages receive to the page's message for it.
    /// </summary>
    internal static string? DescribePasswordResetError(
        string? error,
        InteractiveMessages messages
    ) =>
        error switch
        {
            null or "" => null,
            InteractiveEmailCodeErrors.InvalidCode => messages.InvalidCodeError,
            InteractiveEmailCodeErrors.TooManyCodes => messages.TooManyCodesError,
            InteractivePasswordChangeErrors.PasswordMismatch => messages.PasswordMismatchError,
            InteractivePasswordChangeErrors.InvalidRequest => messages.MissingFieldsError,
            // The password policy's codes are an open vocabulary, so any other code the reset post answers is a
            // refused password.
            _ => messages.PasswordRefusedError,
        };

    /// <summary>
    /// Maps the error code the sign-in code page receives to the page's message for it.
    /// </summary>
    internal static string? DescribeSignInCodeError(string? error, InteractiveMessages messages) =>
        error switch
        {
            null or "" => null,
            InteractiveEmailCodeErrors.InvalidCode => messages.InvalidCodeError,
            InteractiveEmailCodeErrors.TooManyCodes => messages.TooManyCodesError,
            InteractivePasswordChangeErrors.InvalidRequest => messages.MissingFieldsError,
            InteractiveEmailCodeErrors.NoEmail => messages.NoEmailError,
            _ => messages.EmailCodeFailedError,
        };

    /// <summary>
    /// Redirects to a page of the surface with the query values that are set, in a page that renders in the locale the
    /// form carried.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="surfacePath">The path of the page, relative to the surface.</param>
    /// <param name="languageTag">The language tag the form carried, or <see langword="null"/>.</param>
    /// <param name="query">The query values to carry, of which those without a value are left out.</param>
    /// <returns>The redirect to the page.</returns>
    private RedirectHttpResult RedirectToSurfacePage(
        HttpContext httpContext,
        string surfacePath,
        string? languageTag,
        params (string Name, string? Value)[] query
    )
    {
        var url = SurfacePaths.GetSurfacePath(httpContext, surfacePath);
        var separator = '?';
        foreach (var (name, value) in query)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            url += $"{separator}{name}={Uri.EscapeDataString(value)}";
            separator = '&';
        }

        var location = InteractiveLocaleSelector.AppendUiLocalesQuery(url, languageTag);
        return TypedResults.Redirect(location);
    }

    /// <summary>
    /// Authenticates the login cookie and requires the session it names to be active, so a cookie whose session has
    /// ended or been revoked serves nothing. A cookie without an active session is cleared and reported as no result.
    /// </summary>
    // See ADR-1229.
    private async ValueTask<AuthenticateResult> AuthenticateSessionAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var authenticateResult = await httpContext.AuthenticateAsync(
            InteractiveAuthenticationDefaults.CookieSchemeName
        );
        if (!authenticateResult.Succeeded)
        {
            return authenticateResult;
        }

        // The cookie serves the surface only while the session it names is active, so the cookie of an ended, revoked,
        // or blocked end user's session serves nothing.
        var sessionId = authenticateResult.Principal.FindFirst(JoseClaimNames.Payload.Sid)?.Value;
        var isSessionActive = false;
        if (!string.IsNullOrEmpty(sessionId))
        {
            var sessionOpenIdContext = httpContext.GetOpenIdContext();
            isSessionActive = await SessionService.ValidateAndTouchAsync(
                sessionOpenIdContext,
                sessionId,
                cancellationToken
            );
        }

        if (!isSessionActive)
        {
            await httpContext.SignOutAsync(InteractiveAuthenticationDefaults.CookieSchemeName);
            return AuthenticateResult.NoResult();
        }

        return authenticateResult;
    }

    private async ValueTask<IResult> ServeBundleAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        IssueAntiforgeryTokens(httpContext);

        var shell = await Bundle.TryReadIndexAsync(cancellationToken);
        if (shell is null)
        {
            return BundleMissingResult();
        }

        var branding = ReadBranding(httpContext);
        var security = InteractivePageSecurity.Create(branding);
        var html = InteractiveHtmlResult.CreateBuffer();
        var frame = CreatePageFrame(httpContext, shell, branding, security);
        InteractiveAuthenticationPageRenderer.RenderBundlePage(html, frame);
        return new InteractiveHtmlResult(html, security.Policy);
    }

    // The document a page of the request's tenant is written into: the bundle when the host has one, the surface
    // beneath the tenant as its base, the tenant's branding, the response's nonce and the locale the request selects.
    private async ValueTask<InteractivePageFrame> CreatePageFrameAsync(
        HttpContext httpContext,
        InteractiveBranding branding,
        InteractivePageSecurity security,
        CancellationToken cancellationToken
    )
    {
        var shell = await Bundle.TryReadIndexAsync(cancellationToken);
        return CreatePageFrame(httpContext, shell, branding, security);
    }

    private InteractivePageFrame CreatePageFrame(
        HttpContext httpContext,
        InteractiveBundleShell? shell,
        InteractiveBranding branding,
        InteractivePageSecurity security
    )
    {
        var surfaceBase = SurfacePaths.GetSurfaceBase(httpContext);
        var locale = SelectLocale(httpContext);
        return new InteractivePageFrame(shell, surfaceBase, branding, security.Nonce, locale);
    }

    // The tenant the request resolved is the one whose page the bundle themes itself for (ADR-1253).
    private InteractiveBranding ReadBranding(HttpContext httpContext)
    {
        var settings = httpContext.GetOpenIdContext().Tenant.SettingsProvider.Collection;
        return InteractiveBranding.FromSettings(settings, TenantEnvironmentCatalog);
    }

    /// <summary>
    /// Selects the locale the page renders in, which is the locale the request prefers among those the tenant supports.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns>The locale the page renders in.</returns>
    // See ADR-1402.
    private InteractiveLocale SelectLocale(HttpContext httpContext) =>
        SelectLocale(httpContext, languageTag: null);

    /// <summary>
    /// Selects the locale a mail is worded in, which is the locale of the page the end user saw rendered, as the form
    /// carried it in its language tag.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="languageTag">The language tag the form carried, or <see langword="null"/>.</param>
    /// <returns>The locale the mail is worded in.</returns>
    private InteractiveLocale SelectLocale(HttpContext httpContext, string? languageTag)
    {
        var settings = httpContext.GetOpenIdContext().Tenant.SettingsProvider.Collection;
        return LocaleSelector.Select(httpContext, settings, languageTag);
    }

    /// <summary>
    /// Maps the error code the login page receives to the page's message for it, so the login form without scripts
    /// shows the same messages as the single-page form.
    /// </summary>
    internal static string? DescribeLoginError(string? error, InteractiveMessages messages) =>
        error switch
        {
            null or "" => null,
            "invalid_credentials" => messages.InvalidCredentialsError,
            "invalid_request" => messages.MissingCredentialsError,
            OpenIdProtocol.ErrorCodes.AccessDenied => messages.AccessDeniedError,
            InteractiveEmailCodeErrors.TooManyCodes => messages.TooManyCodesError,
            _ => messages.SignInFailedError,
        };

    private AntiforgeryTokenSet IssueAntiforgeryTokens(HttpContext httpContext)
    {
        var tokens = Antiforgery.GetAndStoreTokens(httpContext);
        if (tokens.RequestToken is { Length: > 0 } requestToken)
        {
            // A JavaScript-readable cookie (not the HttpOnly antiforgery cookie) so the single-page form can echo the
            // request token back as a hidden field, which the antiforgery middleware then validates on POST.
            var cookieOptions = new CookieOptions
            {
                HttpOnly = false,
                Path = "/",
                SameSite = SameSiteMode.Strict,
                Secure = true,
            };
            httpContext.Response.Cookies.Append(
                InteractiveAuthenticationDefaults.AntiforgeryTokenCookieName,
                requestToken,
                cookieOptions
            );
        }

        return tokens;
    }

    private static ContentHttpResult BundleMissingResult() =>
        TypedResults.Content(
            "The interactive login bundle is not available. Build the 'apps/auth' surface and configure "
                + "'InteractiveAuthentication:BundleRootPath' to its 'dist' directory.",
            "text/plain",
            contentEncoding: SecureEncoding.UTF8,
            statusCode: StatusCodes.Status503ServiceUnavailable
        );

    private string BuildLoginUrl(HttpContext httpContext, string returnUrl) =>
        $"{SurfacePaths.GetSurfacePath(httpContext, InteractiveAuthenticationDefaults.LoginPath)}?ReturnUrl={Uri.EscapeDataString(returnUrl)}";

    // An anonymous request for an account page is sent to the login page, which returns to the page it asked for;
    // both carry the request's ui_locales, so the login page and the page after it render in the same locale.
    private RedirectHttpResult RedirectToLogin(HttpContext httpContext, string returnSurfacePath)
    {
        var languageTag = LocaleSelector.GetCarriedLanguageTag(
            httpContext.Request.Query[InteractiveAuthenticationPageRenderer.UiLocalesFieldName]
        );
        var returnPath = SurfacePaths.GetSurfacePath(httpContext, returnSurfacePath);
        var returnUrl = InteractiveLocaleSelector.AppendUiLocalesQuery(returnPath, languageTag);
        var loginUrl = BuildLoginUrl(httpContext, returnUrl);
        var location = InteractiveLocaleSelector.AppendUiLocalesQuery(loginUrl, languageTag);
        return TypedResults.Redirect(location);
    }

    /// <summary>
    /// Builds the address of the login page with an error, which renders in the locale the form carried.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="returnUrl">The return URL the login page carries.</param>
    /// <param name="error">The error code the login page shows.</param>
    /// <param name="languageTag">The language tag the form carried, or <see langword="null"/>.</param>
    /// <returns>The address of the login page.</returns>
    private string BuildLoginRedirect(
        HttpContext httpContext,
        string returnUrl,
        string error,
        string? languageTag
    )
    {
        var loginUrl = BuildLoginUrl(httpContext, returnUrl);
        var escapedError = Uri.EscapeDataString(error);
        var urlWithError = loginUrl + $"&{ErrorQueryName}={escapedError}";
        return InteractiveLocaleSelector.AppendUiLocalesQuery(urlWithError, languageTag);
    }

    // Guards against an open redirect: a local URL as LocalUrl defines it, or an absolute URL that targets this
    // same origin (the OP's own continue endpoint), is honored; anything else falls back to the application root.
    internal static string ResolveSafeReturnUrl(HttpContext httpContext, string? returnUrl)
    {
        var request = httpContext.Request;
        var requestOrigin = $"{request.Scheme}://{request.Host.Value}";
        return ResolveSafeReturnUrl(returnUrl, requestOrigin);
    }

    internal static string ResolveSafeReturnUrl(string? returnUrl, string requestOrigin)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            return "/";
        }

        if (LocalUrl.IsLocal(returnUrl))
        {
            return returnUrl;
        }

        if (
            AbsoluteUri.TryParse(returnUrl, out var target)
            && AbsoluteUri.TryParse(requestOrigin, out var origin)
            && string.Equals(target.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.Authority, origin.Authority, StringComparison.OrdinalIgnoreCase)
        )
        {
            return returnUrl;
        }

        return "/";
    }
}
