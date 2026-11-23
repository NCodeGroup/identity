// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.Authentication.Interactive;

/// <summary>
/// The text of the pages the interactive surface renders on the server, and of the mail it sends, in one locale: the
/// no-JavaScript login, consent, logout confirmation and authorization error pages, and the messages the login page
/// shows for a sign-in that fails. Every page value is written HTML-encoded, so a message holds plain text; the mail
/// subjects and body formats are plain text the mail carries as they are.
/// </summary>
// See ADR-1402.
[PublicAPI]
public sealed record InteractiveMessages
{
    /// <summary>
    /// Gets the heading and the document title of the login page.
    /// </summary>
    public required string SignInTitle { get; init; }

    /// <summary>
    /// Gets the label of the username field.
    /// </summary>
    public required string UsernameLabel { get; init; }

    /// <summary>
    /// Gets the label of the password field.
    /// </summary>
    public required string PasswordLabel { get; init; }

    /// <summary>
    /// Gets the text of the button that submits the login form.
    /// </summary>
    public required string SignInButton { get; init; }

    /// <summary>
    /// Gets the heading and the document title of the consent page.
    /// </summary>
    public required string AuthorizeAccessTitle { get; init; }

    /// <summary>
    /// Gets the sentence that introduces the requested scopes on the consent page, in which the <c>{0}</c> placeholder
    /// stands for the name of the application that requests access.
    /// </summary>
    public required string ConsentPromptFormat { get; init; }

    /// <summary>
    /// Gets the text of the button that denies the consent.
    /// </summary>
    public required string DenyButton { get; init; }

    /// <summary>
    /// Gets the text of the button that grants the consent.
    /// </summary>
    public required string AllowButton { get; init; }

    /// <summary>
    /// Gets the heading and the document title of the logout confirmation page.
    /// </summary>
    public required string SignOutTitle { get; init; }

    /// <summary>
    /// Gets the question the logout confirmation page asks.
    /// </summary>
    public required string SignOutQuestion { get; init; }

    /// <summary>
    /// Gets the text of the button that declines the logout.
    /// </summary>
    public required string StaySignedInButton { get; init; }

    /// <summary>
    /// Gets the text of the button that confirms the logout.
    /// </summary>
    public required string SignOutButton { get; init; }

    /// <summary>
    /// Gets the heading and the document title of the authorization error page.
    /// </summary>
    public required string AuthorizationErrorTitle { get; init; }

    /// <summary>
    /// Gets the message the login page shows when the username or the password is incorrect.
    /// </summary>
    public required string InvalidCredentialsError { get; init; }

    /// <summary>
    /// Gets the message the login page shows when the username or the password is missing.
    /// </summary>
    public required string MissingCredentialsError { get; init; }

    /// <summary>
    /// Gets the message the login page shows when the account is refused a sign-in.
    /// </summary>
    public required string AccessDeniedError { get; init; }

    /// <summary>
    /// Gets the message the login page shows for any other sign-in failure.
    /// </summary>
    public required string SignInFailedError { get; init; }

    /// <summary>
    /// Gets the text of the link from the login page to the forgot-password page.
    /// </summary>
    public required string ForgotPasswordLink { get; init; }

    /// <summary>
    /// Gets the message the login page shows after a password reset.
    /// </summary>
    public required string PasswordResetStatus { get; init; }

    /// <summary>
    /// Gets the heading and the document title of the forgot-password page.
    /// </summary>
    public required string ForgotPasswordTitle { get; init; }

    /// <summary>
    /// Gets the sentence that asks for the user name on the forgot-password page.
    /// </summary>
    public required string ForgotPasswordPrompt { get; init; }

    /// <summary>
    /// Gets the text of the button that asks for a code to be sent.
    /// </summary>
    public required string SendCodeButton { get; init; }

    /// <summary>
    /// Gets the heading and the document title of the reset-password page.
    /// </summary>
    public required string ResetPasswordTitle { get; init; }

    /// <summary>
    /// Gets the sentence that asks for the code and a new password on the reset-password page.
    /// </summary>
    public required string ResetPasswordPrompt { get; init; }

    /// <summary>
    /// Gets the label of the field that takes an email code.
    /// </summary>
    public required string CodeLabel { get; init; }

    /// <summary>
    /// Gets the label of the field that takes the new password.
    /// </summary>
    public required string NewPasswordLabel { get; init; }

    /// <summary>
    /// Gets the label of the field that confirms the new password.
    /// </summary>
    public required string ConfirmPasswordLabel { get; init; }

    /// <summary>
    /// Gets the text of the button that resets the password.
    /// </summary>
    public required string ResetPasswordButton { get; init; }

    /// <summary>
    /// Gets the heading and the document title of the sign-in code page.
    /// </summary>
    public required string SignInCodeTitle { get; init; }

    /// <summary>
    /// Gets the sentence that asks for the code on the sign-in code page.
    /// </summary>
    public required string SignInCodePrompt { get; init; }

    /// <summary>
    /// Gets the text of the button that submits an email code.
    /// </summary>
    public required string VerifyCodeButton { get; init; }

    /// <summary>
    /// Gets the text of the button that asks for another code.
    /// </summary>
    public required string ResendCodeButton { get; init; }

    /// <summary>
    /// Gets the message the sign-in code page shows after it sends a code.
    /// </summary>
    public required string CodeSentStatus { get; init; }

    /// <summary>
    /// Gets the message shown when an email code is incorrect or has expired.
    /// </summary>
    public required string InvalidCodeError { get; init; }

    /// <summary>
    /// Gets the message shown when too many codes were requested.
    /// </summary>
    public required string TooManyCodesError { get; init; }

    /// <summary>
    /// Gets the message shown when the new password and its confirmation differ.
    /// </summary>
    public required string PasswordMismatchError { get; init; }

    /// <summary>
    /// Gets the message shown when the password policy refuses the new password.
    /// </summary>
    public required string PasswordRefusedError { get; init; }

    /// <summary>
    /// Gets the message the sign-in code page shows when the account has no email address.
    /// </summary>
    public required string NoEmailError { get; init; }

    /// <summary>
    /// Gets the message the sign-in code page shows for any other failure.
    /// </summary>
    public required string EmailCodeFailedError { get; init; }

    /// <summary>
    /// Gets the message the forgot-password, reset-password and sign-in code pages show when a field is empty.
    /// </summary>
    public required string MissingFieldsError { get; init; }

    /// <summary>
    /// Gets the subject of the mail that carries an email verification code.
    /// </summary>
    public required string VerificationMailSubject { get; init; }

    /// <summary>
    /// Gets the body of the mail that carries an email verification code, in which the <c>{0}</c> placeholder stands
    /// for the code and the <c>{1}</c> placeholder for the code's lifetime in whole minutes.
    /// </summary>
    public required string VerificationMailBodyFormat { get; init; }

    /// <summary>
    /// Gets the subject of the mail that carries a password reset code.
    /// </summary>
    public required string PasswordResetMailSubject { get; init; }

    /// <summary>
    /// Gets the body of the mail that carries a password reset code, in which the <c>{0}</c> placeholder stands for the
    /// code and the <c>{1}</c> placeholder for the code's lifetime in whole minutes.
    /// </summary>
    public required string PasswordResetMailBodyFormat { get; init; }

    /// <summary>
    /// Gets the subject of the mail that carries a sign-in code.
    /// </summary>
    public required string SignInMailSubject { get; init; }

    /// <summary>
    /// Gets the body of the mail that carries a sign-in code, in which the <c>{0}</c> placeholder stands for the code
    /// and the <c>{1}</c> placeholder for the code's lifetime in whole minutes.
    /// </summary>
    public required string SignInMailBodyFormat { get; init; }
}
