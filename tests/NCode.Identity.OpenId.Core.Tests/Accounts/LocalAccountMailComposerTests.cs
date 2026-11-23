// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using NCode.Identity.OpenId.Authentication.Interactive;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

public class LocalAccountMailComposerTests
{
    private const string To = "user@example.com";
    private const string Code = "123456";

    #region Scaffolding

    internal static InteractiveLocale CreateLocale() =>
        new(
            "fr",
            new InteractiveMessages
            {
                SignInTitle = "SignInTitle",
                UsernameLabel = "UsernameLabel",
                PasswordLabel = "PasswordLabel",
                SignInButton = "SignInButton",
                AuthorizeAccessTitle = "AuthorizeAccessTitle",
                ConsentPromptFormat = "ConsentPromptFormat",
                DenyButton = "DenyButton",
                AllowButton = "AllowButton",
                SignOutTitle = "SignOutTitle",
                SignOutQuestion = "SignOutQuestion",
                StaySignedInButton = "StaySignedInButton",
                SignOutButton = "SignOutButton",
                AuthorizationErrorTitle = "AuthorizationErrorTitle",
                InvalidCredentialsError = "InvalidCredentialsError",
                MissingCredentialsError = "MissingCredentialsError",
                AccessDeniedError = "AccessDeniedError",
                SignInFailedError = "SignInFailedError",
                ForgotPasswordLink = "ForgotPasswordLink",
                PasswordResetStatus = "PasswordResetStatus",
                ForgotPasswordTitle = "ForgotPasswordTitle",
                ForgotPasswordPrompt = "ForgotPasswordPrompt",
                SendCodeButton = "SendCodeButton",
                ResetPasswordTitle = "ResetPasswordTitle",
                ResetPasswordPrompt = "ResetPasswordPrompt",
                CodeLabel = "CodeLabel",
                NewPasswordLabel = "NewPasswordLabel",
                ConfirmPasswordLabel = "ConfirmPasswordLabel",
                ResetPasswordButton = "ResetPasswordButton",
                SignInCodeTitle = "SignInCodeTitle",
                SignInCodePrompt = "SignInCodePrompt",
                VerifyCodeButton = "VerifyCodeButton",
                ResendCodeButton = "ResendCodeButton",
                CodeSentStatus = "CodeSentStatus",
                InvalidCodeError = "InvalidCodeError",
                TooManyCodesError = "TooManyCodesError",
                PasswordMismatchError = "PasswordMismatchError",
                PasswordRefusedError = "PasswordRefusedError",
                NoEmailError = "NoEmailError",
                EmailCodeFailedError = "EmailCodeFailedError",
                VerificationMailSubject = "Verify subject",
                VerificationMailBodyFormat = "Verify {0} within {1} minutes",
                PasswordResetMailSubject = "Reset subject",
                PasswordResetMailBodyFormat = "Reset {0} within {1} minutes",
                SignInMailSubject = "Sign-in subject",
                SignInMailBodyFormat = "Sign in {0} within {1} minutes",
            }
        );

    #endregion

    #region Compose Tests

    [Theory]
    [InlineData(EmailCodePurposes.VerifyEmail, "Verify subject", "Verify 123456 within 10 minutes")]
    [InlineData(EmailCodePurposes.ResetPassword, "Reset subject", "Reset 123456 within 10 minutes")]
    [InlineData(EmailCodePurposes.SignIn, "Sign-in subject", "Sign in 123456 within 10 minutes")]
    public void Compose_ForEachPurpose_UsesItsSubjectAndBody(
        string purpose,
        string expectedSubject,
        string expectedBody
    )
    {
        var message = LocalAccountMailComposer.Compose(
            purpose,
            To,
            Code,
            TimeSpan.FromMinutes(10),
            CreateLocale(),
            fromDisplayName: null
        );

        Assert.Equal(To, message.To);
        Assert.Equal(expectedSubject, message.Subject);
        Assert.Equal(expectedBody, message.TextBody);
        Assert.Null(message.FromDisplayName);
        Assert.Null(message.HtmlBody);
    }

    [Fact]
    public void Compose_WithADisplayName_SetsTheSenderName()
    {
        var message = LocalAccountMailComposer.Compose(
            EmailCodePurposes.SignIn,
            To,
            Code,
            TimeSpan.FromSeconds(90),
            CreateLocale(),
            fromDisplayName: "Acme"
        );

        Assert.Equal("Acme", message.FromDisplayName);
        Assert.Equal("Sign in 123456 within 2 minutes", message.TextBody);
    }

    [Fact]
    public void Compose_ForATagWithoutCultureData_WordsTheMailInTheInvariantCulture()
    {
        var locale = CreateLocale() with { LanguageTag = "not a culture" };

        var message = LocalAccountMailComposer.Compose(
            EmailCodePurposes.VerifyEmail,
            To,
            Code,
            TimeSpan.FromMinutes(10),
            locale,
            fromDisplayName: null
        );

        var expectedBody = string.Format(
            CultureInfo.InvariantCulture,
            locale.Messages.VerificationMailBodyFormat,
            Code,
            10
        );
        Assert.Equal(expectedBody, message.TextBody);
        Assert.Equal("Verify 123456 within 10 minutes", message.TextBody);
    }

    #endregion
}
