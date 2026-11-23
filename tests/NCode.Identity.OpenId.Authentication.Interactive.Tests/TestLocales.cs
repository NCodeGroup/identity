// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Authentication.Interactive.Tests;

/// <summary>
/// Locales of the tests' own, so a test of a component that renders or selects a locale proves how the component
/// treats a locale rather than the shipped wording, which <see cref="InteractiveLocalesTests"/> covers. The accented
/// text of <see cref="Xx"/> proves a renderer encodes a message.
/// </summary>
internal static class TestLocales
{
    public static InteractiveLocale Xx { get; } =
        new(
            "xx",
            new InteractiveMessages
            {
                SignInTitle = "Xx sign-in title",
                UsernameLabel = "Xx username",
                PasswordLabel = "Xx password",
                SignInButton = "Xx sign-in button",
                AuthorizeAccessTitle = "Xx authorize title",
                ConsentPromptFormat = "{0} xx consent prompt:",
                DenyButton = "Xx deny",
                AllowButton = "Xx allow",
                SignOutTitle = "Xx sign-out titlé",
                SignOutQuestion = "Xx sign-out question?",
                StaySignedInButton = "Xx stay signéd in",
                SignOutButton = "Xx sign-out button",
                AuthorizationErrorTitle = "Xx error title",
                InvalidCredentialsError = "Xx invalid credentials",
                MissingCredentialsError = "Xx missing credentials",
                AccessDeniedError = "Xx access denied",
                SignInFailedError = "Xx sign-in failed",
                ForgotPasswordLink = "Xx forgot link",
                PasswordResetStatus = "Xx password reset",
                ForgotPasswordTitle = "Xx forgot title",
                ForgotPasswordPrompt = "Xx forgot prompt",
                SendCodeButton = "Xx send code",
                ResetPasswordTitle = "Xx reset title",
                ResetPasswordPrompt = "Xx reset prompt",
                CodeLabel = "Xx code",
                NewPasswordLabel = "Xx new password",
                ConfirmPasswordLabel = "Xx confirm password",
                ResetPasswordButton = "Xx reset button",
                SignInCodeTitle = "Xx sign-in code title",
                SignInCodePrompt = "Xx sign-in code prompt",
                VerifyCodeButton = "Xx verify",
                ResendCodeButton = "Xx resend",
                CodeSentStatus = "Xx code sent",
                InvalidCodeError = "Xx invalid code",
                TooManyCodesError = "Xx too many codes",
                PasswordMismatchError = "Xx password mismatch",
                PasswordRefusedError = "Xx password refused",
                NoEmailError = "Xx no email",
                EmailCodeFailedError = "Xx email code failed",
                VerificationMailSubject = "Xx verification subject",
                VerificationMailBodyFormat = "Xx verification {0} {1}",
                PasswordResetMailSubject = "Xx reset subject",
                PasswordResetMailBodyFormat = "Xx reset {0} {1}",
                SignInMailSubject = "Xx sign-in subject",
                SignInMailBodyFormat = "Xx sign-in {0} {1}",
            }
        );

    public static InteractiveLocale Yy { get; } =
        Xx with
        {
            LanguageTag = "yy",
            Messages = Xx.Messages with { SignInTitle = "Yy sign-in title" },
        };
}
