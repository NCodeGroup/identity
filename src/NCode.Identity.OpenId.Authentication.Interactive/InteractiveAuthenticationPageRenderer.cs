// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using NCode.Identity.AspNetCore;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// Renders the <strong>no-JavaScript</strong> login and consent forms as server-side HTML. The forms carry the same
/// stable element ids and field names the React <c>apps/auth</c> surface uses, so the server-rendered markup is a
/// working baseline on its own: when the single-page bundle is present the form is injected into its <c>#root</c>
/// element and the SPA replaces it on mount (progressive enhancement); when the bundle is absent the form is served
/// standalone. This is what makes the interactive surface driveable by clients that do not execute JavaScript —
/// assistive technology, hardened browsers, and the OpenID conformance suite's headless browser. Every page is
/// written into an <see cref="InteractivePageFrame"/> and rendered in its <see cref="InteractiveLocale"/>: its text
/// comes from the locale's messages, its <c>html</c> element carries the locale's language tag, and its form carries
/// the tag as <c>ui_locales</c> so a page the surface redirects back to renders in the same locale.
/// </summary>
// Each page is written segment by segment into an IBufferWriter<char> — the bundle's head, the page's markup, and the
// bundle's tail — so neither the form nor the combined document is ever composed as a string. See ADR-1402 for the
// locale.
internal static class InteractiveAuthenticationPageRenderer
{
    /// <summary>
    /// The name of the form field and the query parameter that carry the return URL the end user continues to.
    /// </summary>
    public const string ReturnUrlFieldName = "ReturnUrl";

    /// <summary>
    /// The name of the form field and the query parameter that carry the language tag of the locale a page rendered
    /// in, the same name as the authorization request's parameter.
    /// </summary>
    public const string UiLocalesFieldName = OpenIdProtocol.Parameters.UiLocales;

    /// <summary>
    /// The name of the form field and the query parameter that carry the handle of an email code flow.
    /// </summary>
    public const string FlowFieldName = "flow";

    /// <summary>
    /// The name of the form field that carries the one-time code the end user typed.
    /// </summary>
    public const string CodeFieldName = "code";

    /// <summary>
    /// The name of the form field that carries the new password.
    /// </summary>
    public const string NewPasswordFieldName = "new_password";

    /// <summary>
    /// The name of the form field that carries the new password a second time.
    /// </summary>
    public const string ConfirmPasswordFieldName = "confirm_password";

    /// <summary>
    /// The name of the submit button field that carries the action the end user chose.
    /// </summary>
    public const string ActionFieldName = "action";

    /// <summary>
    /// The value of the action field that asks the sign-in code page for a new code.
    /// </summary>
    public const string ResendActionValue = "resend";

    /// <summary>
    /// The name of the form field that carries the connection a sign-in goes through.
    /// </summary>
    public const string ConnectionFieldName = "connection";

    /// <summary>
    /// The path of the forgot-password page, relative to the surface the page's base names.
    /// </summary>
    public const string ForgotPasswordFormAction = "password/forgot";

    /// <summary>
    /// The path of the reset-password page, relative to the surface the page's base names.
    /// </summary>
    public const string ResetPasswordFormAction = "password/reset";

    /// <summary>
    /// The path of the sign-in code page, relative to the surface the page's base names.
    /// </summary>
    public const string SignInCodeFormAction = "login/code";

    private const string ConsentPromptPlaceholder = "{0}";

    private const string StandaloneHeadStart = "<!doctype html><html lang=\"";
    private const string StandaloneHeadAfterLanguage = "\"><head><meta charset=\"utf-8\" />";

    private const string StandaloneTitleStart =
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" /><title>";

    private const string StandaloneHeadEnd = "</title></head><body><div id=\"root\">";
    private const string StandaloneTail = "</div></body></html>";

    private const string BrandingStart = "<script type=\"application/json\" id=\"branding\">";
    private const string BrandingEnd = "</script>";

    /// <summary>A single requested scope the consent form presents as a checkbox. <paramref name="Checked"/> is the
    /// default selection state — a consent screen pre-selects the requested scopes so <c>Allow</c> authorizes the
    /// request, and the subject unchecks a scope to down-scope the grant.</summary>
    internal readonly record struct ScopeView(string Value, string? Description, bool Checked);

    /// <summary>
    /// Renders the login page: a native form POST to the login endpoint carrying <c>username</c>, <c>password</c>, the
    /// return URL, and the antiforgery token, with the stable <c>username</c> / <c>password</c> / <c>login</c> ids.
    /// </summary>
    /// <param name="writer">The buffer the page is written to.</param>
    /// <param name="frame">The document the page is written into.</param>
    /// <param name="form">The form the page posts.</param>
    public static void RenderLoginPage(
        IBufferWriter<char> writer,
        InteractivePageFrame frame,
        InteractivePageForm form
    )
    {
        var locale = frame.Locale;
        var messages = locale.Messages;
        WriteHead(writer, frame, messages.SignInTitle);
        // The action is relative to the page, so the form posts to the login endpoint of the tenant that served it.
        Write(writer, "<main class=\"auth-card\"><form method=\"post\" action=\"login\"><h1>");
        WriteEncoded(writer, messages.SignInTitle);
        Write(writer, "</h1>");
        WriteError(writer, form.ErrorMessage);
        WriteStatus(writer, form.StatusMessage);
        WriteHiddenFields(writer, form, locale);
        WriteConnectionField(writer, messages, form);
        Write(writer, "<p><label for=\"username\">");
        WriteEncoded(writer, messages.UsernameLabel);
        Write(
            writer,
            "</label><br />"
                + "<input id=\"username\" name=\"username\" type=\"text\" autocomplete=\"username\" autofocus required /></p>"
                + "<p><label for=\"password\">"
        );
        WriteEncoded(writer, messages.PasswordLabel);
        Write(
            writer,
            "</label><br />"
                + "<input id=\"password\" name=\"password\" type=\"password\" autocomplete=\"current-password\" required /></p>"
                + "<button id=\"login\" name=\"login\" value=\"login\" type=\"submit\">"
        );
        WriteEncoded(writer, messages.SignInButton);
        Write(writer, "</button>");
        if (!string.IsNullOrEmpty(form.ForgotPasswordHref))
        {
            Write(writer, "<p><a id=\"forgot-password-link\" href=\"");
            WriteEncoded(writer, form.ForgotPasswordHref);
            Write(writer, "\">");
            WriteEncoded(writer, messages.ForgotPasswordLink);
            Write(writer, "</a></p>");
        }
        Write(writer, "</form></main>");
        WriteTail(writer, frame);
    }

    /// <summary>
    /// Renders the forgot-password page: a native form POST to the forgot-password endpoint carrying <c>username</c>,
    /// the return URL, and the antiforgery token, with the stable <c>username</c> / <c>send-code</c> ids.
    /// </summary>
    /// <param name="writer">The buffer the page is written to.</param>
    /// <param name="frame">The document the page is written into.</param>
    /// <param name="form">The form the page posts.</param>
    public static void RenderForgotPasswordPage(
        IBufferWriter<char> writer,
        InteractivePageFrame frame,
        InteractivePageForm form
    )
    {
        var locale = frame.Locale;
        var messages = locale.Messages;
        WriteHead(writer, frame, messages.ForgotPasswordTitle);
        Write(
            writer,
            "<main class=\"auth-card\"><form method=\"post\" action=\""
                + ForgotPasswordFormAction
                + "\"><h1>"
        );
        WriteEncoded(writer, messages.ForgotPasswordTitle);
        Write(writer, "</h1>");
        WriteError(writer, form.ErrorMessage);
        WriteHiddenFields(writer, form, locale);
        WriteConnectionField(writer, messages, form);
        Write(writer, "<p>");
        WriteEncoded(writer, messages.ForgotPasswordPrompt);
        Write(writer, "</p><p><label for=\"username\">");
        WriteEncoded(writer, messages.UsernameLabel);
        Write(
            writer,
            "</label><br />"
                + "<input id=\"username\" name=\"username\" type=\"text\" autocomplete=\"username\" autofocus required /></p>"
                + "<button id=\"send-code\" type=\"submit\">"
        );
        WriteEncoded(writer, messages.SendCodeButton);
        Write(writer, "</button></form></main>");
        WriteTail(writer, frame);
    }

    /// <summary>
    /// Renders the reset-password page: a native form POST to the reset-password endpoint carrying the flow handle,
    /// <c>code</c>, <c>new_password</c>, <c>confirm_password</c>, the return URL, and the antiforgery token, with the
    /// stable <c>code</c> / <c>new_password</c> / <c>confirm_password</c> / <c>reset-password</c> ids.
    /// </summary>
    /// <param name="writer">The buffer the page is written to.</param>
    /// <param name="frame">The document the page is written into.</param>
    /// <param name="form">The form the page posts.</param>
    /// <param name="flowId">The handle of the reset flow the form carries.</param>
    public static void RenderResetPasswordPage(
        IBufferWriter<char> writer,
        InteractivePageFrame frame,
        InteractivePageForm form,
        string flowId
    )
    {
        var locale = frame.Locale;
        var messages = locale.Messages;
        WriteHead(writer, frame, messages.ResetPasswordTitle);
        Write(
            writer,
            "<main class=\"auth-card\"><form method=\"post\" action=\""
                + ResetPasswordFormAction
                + "\"><h1>"
        );
        WriteEncoded(writer, messages.ResetPasswordTitle);
        Write(writer, "</h1>");
        WriteError(writer, form.ErrorMessage);
        WriteHiddenFields(writer, form, locale);
        Write(writer, "<input type=\"hidden\" name=\"" + FlowFieldName + "\" value=\"");
        WriteEncoded(writer, flowId);
        Write(writer, "\" /><p>");
        WriteEncoded(writer, messages.ResetPasswordPrompt);
        Write(writer, "</p><p><label for=\"code\">");
        WriteEncoded(writer, messages.CodeLabel);
        Write(
            writer,
            "</label><br />"
                + "<input id=\"code\" name=\""
                + CodeFieldName
                + "\" type=\"text\" inputmode=\"numeric\" autocomplete=\"one-time-code\" pattern=\"[0-9]{6}\" maxlength=\"6\" autofocus required /></p>"
                + "<p><label for=\"new_password\">"
        );
        WriteEncoded(writer, messages.NewPasswordLabel);
        Write(
            writer,
            "</label><br />"
                + "<input id=\"new_password\" name=\""
                + NewPasswordFieldName
                + "\" type=\"password\" autocomplete=\"new-password\" required /></p>"
                + "<p><label for=\"confirm_password\">"
        );
        WriteEncoded(writer, messages.ConfirmPasswordLabel);
        Write(
            writer,
            "</label><br />"
                + "<input id=\"confirm_password\" name=\""
                + ConfirmPasswordFieldName
                + "\" type=\"password\" autocomplete=\"new-password\" required /></p>"
                + "<button id=\"reset-password\" type=\"submit\">"
        );
        WriteEncoded(writer, messages.ResetPasswordButton);
        Write(writer, "</button></form></main>");
        WriteTail(writer, frame);
    }

    /// <summary>
    /// Renders the sign-in code page: a native form POST to the sign-in code endpoint carrying <c>code</c> and the
    /// antiforgery token, with the stable <c>code</c> / <c>verify</c> / <c>resend</c> ids.
    /// </summary>
    /// <param name="writer">The buffer the page is written to.</param>
    /// <param name="frame">The document the page is written into.</param>
    /// <param name="form">The form the page posts.</param>
    public static void RenderSignInCodePage(
        IBufferWriter<char> writer,
        InteractivePageFrame frame,
        InteractivePageForm form
    )
    {
        var locale = frame.Locale;
        var messages = locale.Messages;
        WriteHead(writer, frame, messages.SignInCodeTitle);
        Write(
            writer,
            "<main class=\"auth-card\"><form method=\"post\" action=\""
                + SignInCodeFormAction
                + "\"><h1>"
        );
        WriteEncoded(writer, messages.SignInCodeTitle);
        Write(writer, "</h1>");
        WriteError(writer, form.ErrorMessage);
        WriteStatus(writer, form.StatusMessage);
        WriteAntiforgeryField(writer, form);
        Write(writer, "<input type=\"hidden\" name=\"" + UiLocalesFieldName + "\" value=\"");
        WriteEncoded(writer, locale.LanguageTag);
        Write(writer, "\" /><p>");
        WriteEncoded(writer, messages.SignInCodePrompt);
        Write(writer, "</p><p><label for=\"code\">");
        WriteEncoded(writer, messages.CodeLabel);
        Write(
            writer,
            "</label><br />"
                + "<input id=\"code\" name=\""
                + CodeFieldName
                + "\" type=\"text\" inputmode=\"numeric\" autocomplete=\"one-time-code\" pattern=\"[0-9]{6}\" maxlength=\"6\" autofocus required /></p>"
                + "<button id=\"verify\" name=\""
                + ActionFieldName
                + "\" value=\"verify\" type=\"submit\">"
        );
        WriteEncoded(writer, messages.VerifyCodeButton);
        Write(
            writer,
            "</button><button id=\"resend\" name=\""
                + ActionFieldName
                + "\" value=\""
                + ResendActionValue
                + "\" type=\"submit\" formnovalidate>"
        );
        WriteEncoded(writer, messages.ResendCodeButton);
        Write(writer, "</button></form></main>");
        WriteTail(writer, frame);
    }

    /// <summary>
    /// Renders the consent page: a per-scope checkbox over the requested scopes and allow/deny submit buttons with the
    /// stable <c>consent-allow</c> / <c>consent-deny</c> ids, posting to the consent endpoint.
    /// </summary>
    /// <param name="writer">The buffer the page is written to.</param>
    /// <param name="frame">The document the page is written into.</param>
    /// <param name="form">The form the page posts.</param>
    /// <param name="appName">The name of the application that requests access.</param>
    /// <param name="scopes">The requested scopes, each presented as a checkbox.</param>
    public static void RenderConsentPage(
        IBufferWriter<char> writer,
        InteractivePageFrame frame,
        InteractivePageForm form,
        string appName,
        IReadOnlyList<ScopeView> scopes
    )
    {
        var locale = frame.Locale;
        var messages = locale.Messages;
        WriteHead(writer, frame, messages.AuthorizeAccessTitle);
        Write(writer, "<main class=\"auth-card\"><form method=\"post\" action=\"consent\"><h1>");
        WriteEncoded(writer, messages.AuthorizeAccessTitle);
        Write(writer, "</h1><p>");
        WriteConsentPrompt(writer, messages.ConsentPromptFormat, appName);
        Write(writer, "</p>");
        foreach (var scope in scopes)
        {
            var label = string.IsNullOrEmpty(scope.Description) ? scope.Value : scope.Description;
            Write(writer, "<p><label><input type=\"checkbox\" name=\"scope\" value=\"");
            WriteEncoded(writer, scope.Value);
            Write(writer, scope.Checked ? "\" checked /> " : "\" /> ");
            WriteEncoded(writer, label);
            Write(writer, "</label></p>");
        }
        WriteHiddenFields(writer, form, locale);
        Write(
            writer,
            "<button id=\"consent-deny\" name=\""
                + ActionFieldName
                + "\" value=\"deny\" type=\"submit\">"
        );
        WriteEncoded(writer, messages.DenyButton);
        Write(
            writer,
            "</button><button id=\"consent-allow\" name=\""
                + ActionFieldName
                + "\" value=\"allow\" type=\"submit\">"
        );
        WriteEncoded(writer, messages.AllowButton);
        Write(writer, "</button></form></main>");
        WriteTail(writer, frame);
    }

    /// <summary>
    /// Renders the logout confirmation page: a form with stable <c>logout-confirm</c> / <c>logout-cancel</c> submit
    /// buttons, posting to the logout confirmation endpoint.
    /// </summary>
    /// <param name="writer">The buffer the page is written to.</param>
    /// <param name="frame">The document the page is written into.</param>
    /// <param name="form">The form the page posts.</param>
    public static void RenderLogoutConfirmationPage(
        IBufferWriter<char> writer,
        InteractivePageFrame frame,
        InteractivePageForm form
    )
    {
        var locale = frame.Locale;
        var messages = locale.Messages;
        WriteHead(writer, frame, messages.SignOutTitle);
        Write(
            writer,
            "<main class=\"auth-card\"><form method=\"post\" action=\"logout/confirm\"><h1>"
        );
        WriteEncoded(writer, messages.SignOutTitle);
        Write(writer, "</h1><p>");
        WriteEncoded(writer, messages.SignOutQuestion);
        Write(writer, "</p>");
        WriteHiddenFields(writer, form, locale);
        Write(
            writer,
            "<button id=\"logout-cancel\" name=\""
                + ActionFieldName
                + "\" value=\"cancel\" type=\"submit\">"
        );
        WriteEncoded(writer, messages.StaySignedInButton);
        Write(
            writer,
            "</button><button id=\"logout-confirm\" name=\""
                + ActionFieldName
                + "\" value=\"logout\" type=\"submit\">"
        );
        WriteEncoded(writer, messages.SignOutButton);
        Write(writer, "</button></form></main>");
        WriteTail(writer, frame);
    }

    /// <summary>
    /// Renders the non-redirectable authorization error page shown to the resource owner when the authorize endpoint
    /// cannot safely redirect the error back to the client (an unregistered <c>redirect_uri</c> or an unresolvable
    /// <c>client_id</c>; OpenID Connect Core §3.1.2.4). The error code is rendered as the text of a stable
    /// <c>authorization-error</c> element (plus a <c>data-error-code</c> attribute) so automation — including the
    /// OpenID conformance suite's <c>oidcc-ensure-registered-redirect-uri</c> browser task — can assert on it.
    /// </summary>
    /// <param name="writer">The buffer the page is written to.</param>
    /// <param name="frame">The document the page is written into.</param>
    /// <param name="errorCode">The error code, such as <c>invalid_client</c>.</param>
    /// <param name="errorDescription">The human-readable error description, or <see langword="null"/> for none.</param>
    public static void RenderErrorPage(
        IBufferWriter<char> writer,
        InteractivePageFrame frame,
        string errorCode,
        string? errorDescription
    )
    {
        var locale = frame.Locale;
        var messages = locale.Messages;
        WriteHead(writer, frame, messages.AuthorizationErrorTitle);
        Write(writer, "<main class=\"auth-card\"><h1>");
        WriteEncoded(writer, messages.AuthorizationErrorTitle);
        Write(
            writer,
            "</h1><p id=\"authorization-error\" role=\"alert\" class=\"auth-error\" data-error-code=\""
        );
        WriteEncoded(writer, errorCode);
        Write(writer, "\">");
        WriteEncoded(writer, errorCode);
        Write(writer, "</p>");
        if (!string.IsNullOrEmpty(errorDescription))
        {
            Write(
                writer,
                "<p id=\"authorization-error-description\" class=\"auth-error-description\">"
            );
            WriteEncoded(writer, errorDescription);
            Write(writer, "</p>");
        }
        Write(writer, "</main>");
        WriteTail(writer, frame);
    }

    /// <summary>
    /// Renders a page the single-page bundle draws entirely (the account, applications, and sessions pages): the
    /// bundle's document with its base set to the surface beneath the tenant and its mount point left empty.
    /// </summary>
    /// <param name="writer">The buffer the page is written to.</param>
    /// <param name="frame">The document the page is written into, whose <see cref="InteractivePageFrame.Shell"/> is
    /// the bundle's split <c>index.html</c>.</param>
    /// <exception cref="ArgumentException">The frame has no bundle.</exception>
    public static void RenderBundlePage(IBufferWriter<char> writer, InteractivePageFrame frame)
    {
        if (frame.Shell is null)
        {
            throw new ArgumentException(
                "A page the bundle draws needs the bundle's document.",
                nameof(frame)
            );
        }

        WriteHead(writer, frame, title: string.Empty);
        WriteTail(writer, frame);
    }

    /// <summary>
    /// Writes the paragraph that shows the error message, or nothing when there is no message.
    /// </summary>
    /// <param name="writer">The writer of the page.</param>
    /// <param name="errorMessage">The message to show, or <see langword="null"/> when there is no error.</param>
    private static void WriteError(IBufferWriter<char> writer, string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
        {
            return;
        }

        Write(writer, "<p id=\"error\" role=\"alert\" class=\"auth-error\">");
        WriteEncoded(writer, errorMessage);
        Write(writer, "</p>");
    }

    /// <summary>
    /// Writes the paragraph that shows the status message, or nothing when there is no message.
    /// </summary>
    /// <param name="writer">The writer of the page.</param>
    /// <param name="statusMessage">The message to show, or <see langword="null"/> when there is no status.</param>
    private static void WriteStatus(IBufferWriter<char> writer, string? statusMessage)
    {
        if (string.IsNullOrEmpty(statusMessage))
        {
            return;
        }

        Write(writer, "<p id=\"status\" role=\"status\" class=\"auth-status\">");
        WriteEncoded(writer, statusMessage);
        Write(writer, "</p>");
    }

    // The sentence is written around its placeholder, every part encoded, so a format without the placeholder is
    // written as it is.
    private static void WriteConsentPrompt(
        IBufferWriter<char> writer,
        string consentPromptFormat,
        string appName
    )
    {
        var placeholderIndex = consentPromptFormat.IndexOf(
            ConsentPromptPlaceholder,
            StringComparison.Ordinal
        );
        if (placeholderIndex < 0)
        {
            WriteEncoded(writer, consentPromptFormat);
            return;
        }

        var beforePlaceholder = consentPromptFormat.AsSpan(0, placeholderIndex);
        WriteEncoded(writer, beforePlaceholder);
        WriteEncoded(writer, appName);
        var afterPlaceholder = consentPromptFormat.AsSpan(
            placeholderIndex + ConsentPromptPlaceholder.Length
        );
        WriteEncoded(writer, afterPlaceholder);
    }

    /// <summary>
    /// Writes the field that carries the connection a form signs in through: a select when several connections are
    /// offered, a hidden input when one is, and nothing when none is. The select starts with a selected blank option
    /// when the form preselects no connection.
    /// </summary>
    /// <param name="writer">The buffer the field is written to.</param>
    /// <param name="messages">The messages of the page's locale.</param>
    /// <param name="form">The form that carries the offered connections and the preselected one.</param>
    private static void WriteConnectionField(
        IBufferWriter<char> writer,
        InteractiveMessages messages,
        InteractivePageForm form
    )
    {
        var connections = form.Connections;
        if (connections.Count == 0)
        {
            return;
        }

        if (connections.Count == 1)
        {
            Write(writer, "<input type=\"hidden\" name=\"" + ConnectionFieldName + "\" value=\"");
            WriteEncoded(writer, connections[0].Name);
            Write(writer, "\" />");
            return;
        }

        Write(writer, "<p><label for=\"" + ConnectionFieldName + "\">");
        WriteEncoded(writer, messages.ConnectionLabel);
        Write(
            writer,
            "</label><br /><select id=\""
                + ConnectionFieldName
                + "\" name=\""
                + ConnectionFieldName
                + "\">"
        );
        // With none preselected, a selected blank option asks for a choice, so a page without JavaScript never posts
        // a connection the end user did not pick.
        if (string.IsNullOrEmpty(form.SelectedConnection))
        {
            Write(writer, "<option value=\"\" selected></option>");
        }

        foreach (var (name, displayName) in connections)
        {
            Write(writer, "<option value=\"");
            WriteEncoded(writer, name);
            var optionEnd = string.Equals(name, form.SelectedConnection, StringComparison.Ordinal)
                ? "\" selected>"
                : "\">";
            Write(writer, optionEnd);
            WriteEncoded(writer, displayName);
            Write(writer, "</option>");
        }

        Write(writer, "</select></p>");
    }

    /// <summary>
    /// Writes the hidden inputs a form posts: its return URL, its antiforgery token and the language tag of its page.
    /// </summary>
    /// <param name="writer">The buffer the inputs are written to.</param>
    /// <param name="form">The form that carries the return URL and the antiforgery token.</param>
    /// <param name="locale">The language of the page, which the post carries back.</param>
    private static void WriteHiddenFields(
        IBufferWriter<char> writer,
        InteractivePageForm form,
        InteractiveLocale locale
    )
    {
        Write(writer, "<input type=\"hidden\" name=\"" + ReturnUrlFieldName + "\" value=\"");
        WriteEncoded(writer, form.ReturnUrl);
        Write(writer, "\" />");
        WriteAntiforgeryField(writer, form);
        Write(writer, "<input type=\"hidden\" name=\"" + UiLocalesFieldName + "\" value=\"");
        WriteEncoded(writer, locale.LanguageTag);
        Write(writer, "\" />");
    }

    /// <summary>
    /// Writes the hidden input that carries a form's antiforgery token.
    /// </summary>
    /// <param name="writer">The buffer the input is written to.</param>
    /// <param name="form">The form whose antiforgery token set the input carries.</param>
    private static void WriteAntiforgeryField(IBufferWriter<char> writer, InteractivePageForm form)
    {
        Write(writer, "<input type=\"hidden\" name=\"");
        WriteEncoded(writer, form.Antiforgery.FormFieldName);
        Write(writer, "\" value=\"");
        WriteEncoded(writer, form.Antiforgery.RequestToken ?? string.Empty);
        Write(writer, "\" />");
    }

    // Writes the bundle's head, which ends inside its #root, so the SPA enhances the form on mount; without the bundle,
    // a minimal standalone document keeps the form working. Either way the document's base is the surface beneath the
    // request's tenant, so the bundle's relative asset URLs, its API calls, and the forms' relative actions resolve
    // there whatever path the page itself was served at (ADR-1193). The tenant's branding follows the base, so the
    // bundle finds it in the page it mounts over. The bundle's own markup carries the nonce and the language tag in
    // place of their placeholders; no rendered value does. A tenant's tab icon is the page's only icon, written before
    // the bundle's head, which is then written without the family's icon links (ADR-1253). See ADR-1256 and ADR-1402.
    private static void WriteHead(
        IBufferWriter<char> writer,
        InteractivePageFrame frame,
        string title
    )
    {
        if (frame.Shell is { } shell)
        {
            WriteShellSegment(writer, shell.Prelude.Span, frame);
            WriteBase(writer, frame.SurfaceBase);
            if (frame.Branding is { } branding)
            {
                WriteBranding(writer, branding);
            }

            if (frame.Branding is { FaviconUri: not null, FaviconUrl: { } faviconUrl })
            {
                var iconLink = HtmlIconLinks.Create(faviconUrl);
                Write(writer, iconLink);
                WriteShellSegment(writer, shell.HeadWithoutIcons.Span, frame);
            }
            else
            {
                WriteShellSegment(writer, shell.Head.Span, frame);
            }

            return;
        }

        Write(writer, StandaloneHeadStart);
        WriteEncoded(writer, frame.Locale.LanguageTag);
        Write(writer, StandaloneHeadAfterLanguage);
        WriteBase(writer, frame.SurfaceBase);
        if (frame.Branding is { FaviconUri: not null, FaviconUrl: { } standaloneFaviconUrl })
        {
            var standaloneIconLink = HtmlIconLinks.Create(standaloneFaviconUrl);
            Write(writer, standaloneIconLink);
        }

        Write(writer, StandaloneTitleStart);
        WriteEncoded(writer, title);
        Write(writer, StandaloneHeadEnd);
    }

    private static void WriteBase(IBufferWriter<char> writer, string surfaceBase)
    {
        Write(writer, "<base href=\"");
        WriteEncoded(writer, surfaceBase);
        Write(writer, "\" />");
    }

    // The bundle reads this block when it mounts (ADR-1253). The default encoder of JsonSerializerOptions.Web escapes
    // `<`, `>` and `&`, so no value can close the block.
    private static void WriteBranding(IBufferWriter<char> writer, InteractiveBranding branding)
    {
        Write(writer, BrandingStart);
        var brandingJson = JsonSerializer.Serialize(branding, JsonSerializerOptions.Web);
        Write(writer, brandingJson);
        Write(writer, BrandingEnd);
    }

    private static void WriteTail(IBufferWriter<char> writer, InteractivePageFrame frame)
    {
        if (frame.Shell is { } shell)
        {
            WriteShellSegment(writer, shell.Tail.Span, frame);
            return;
        }

        Write(writer, StandaloneTail);
    }

    // Writes a segment of the bundle's own document with the response's nonce in place of every nonce placeholder and
    // the page's language tag in place of every language placeholder, whichever comes first.
    private static void WriteShellSegment(
        IBufferWriter<char> writer,
        ReadOnlySpan<char> segment,
        InteractivePageFrame frame
    )
    {
        while (true)
        {
            var nonceIndex = segment.IndexOf(
                InteractiveBundleShell.NoncePlaceholder,
                StringComparison.Ordinal
            );
            var languageIndex = segment.IndexOf(
                InteractiveBundleShell.LanguagePlaceholder,
                StringComparison.Ordinal
            );
            if (nonceIndex < 0 && languageIndex < 0)
            {
                Write(writer, segment);
                return;
            }

            var isNonceFirst = nonceIndex >= 0 && (languageIndex < 0 || nonceIndex < languageIndex);
            var index = isNonceFirst ? nonceIndex : languageIndex;
            var placeholderLength = isNonceFirst
                ? InteractiveBundleShell.NoncePlaceholder.Length
                : InteractiveBundleShell.LanguagePlaceholder.Length;

            Write(writer, segment[..index]);
            if (isNonceFirst)
            {
                Write(writer, frame.Nonce);
            }
            else
            {
                WriteEncoded(writer, frame.Locale.LanguageTag);
            }

            segment = segment[(index + placeholderLength)..];
        }
    }

    private static void Write(IBufferWriter<char> writer, ReadOnlySpan<char> value) =>
        writer.Write(value);

    private static void WriteEncoded(IBufferWriter<char> writer, ReadOnlySpan<char> value)
    {
        var encoder = HtmlEncoder.Default;
        var sizeHint = value.Length;
        while (true)
        {
            var buffer = writer.GetSpan(sizeHint);
            var status = encoder.Encode(
                value,
                buffer,
                out var consumed,
                out var written,
                isFinalBlock: true
            );
            writer.Advance(written);
            value = value[consumed..];

            if (status == OperationStatus.Done)
            {
                return;
            }

            // An encoded character can take several characters, so ask for more room than the input still needs.
            sizeHint = Math.Max(sizeHint * 2, 16);
        }
    }
}
