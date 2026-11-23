// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net.Mime;
using NCode.Buffers;
using NCode.Identity.Mail;
using NCode.Identity.OpenId.Accounts.Authentication.EmailCodes;
using NCode.Identity.OpenId.Authentication.Interactive;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Accounts;

public class LocalAccountMailComposerTests
{
    private const string To = "user@example.com";
    private const int Code = 123456;

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
                MissingFieldsError = "MissingFieldsError",
                VerificationMailSubject = "Verify subject",
                VerificationMailBodyFormat = "Verify {0} within {1} minutes",
                PasswordResetMailSubject = "Reset subject",
                PasswordResetMailBodyFormat = "Reset {0} within {1} minutes",
                SignInMailSubject = "Sign-in subject",
                SignInMailBodyFormat = "Sign in {0} within {1} minutes",
            }
        );

    private static LocalAccountCodeMail CreateModel(
        int code = Code,
        string bodyFormat = "Verify {0} within {1} minutes",
        string languageTag = "fr",
        string? fromDisplayName = null
    ) => new(To, fromDisplayName, "Verify subject", bodyFormat, languageTag, code, 10);

    private static async ValueTask<string> ReadBodyAsync(
        MailMessage message,
        CancellationToken cancellationToken
    )
    {
        Assert.NotNull(message.TextBody);
        var stream = await message.TextBody.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, SecureEncoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    #endregion

    #region ComposeAsync Tests

    [Fact]
    public async Task ComposeAsync_ForACodeWithLeadingZeros_WritesSixDigits()
    {
        var composer = new LocalAccountMailComposer();

        await using var message = await composer.ComposeAsync(
            CreateModel(code: 42),
            CancellationToken.None
        );

        var body = await ReadBodyAsync(message, CancellationToken.None);
        Assert.Equal("Verify 000042 within 10 minutes", body);
        var recipient = Assert.Single(message.To);
        Assert.Equal(To, recipient.Address);
        Assert.Equal("Verify subject", message.Subject);
        Assert.Null(message.FromDisplayName);
        Assert.Null(message.HtmlBody);
        Assert.NotNull(message.TextBody);
        Assert.Equal(MediaTypeNames.Text.Plain, message.TextBody.MediaType);
        Assert.Equal("utf-8", message.TextBody.Charset);
    }

    [Fact]
    public async Task ComposeAsync_WithADisplayName_SetsTheSenderName()
    {
        var composer = new LocalAccountMailComposer();

        await using var message = await composer.ComposeAsync(
            CreateModel(fromDisplayName: "Acme"),
            CancellationToken.None
        );

        Assert.Equal("Acme", message.FromDisplayName);
        var body = await ReadBodyAsync(message, CancellationToken.None);
        Assert.Equal("Verify 123456 within 10 minutes", body);
    }

    [Fact]
    public async Task ComposeAsync_ForATagWithoutCultureData_WordsTheMailInTheInvariantCulture()
    {
        var composer = new LocalAccountMailComposer();

        await using var message = await composer.ComposeAsync(
            CreateModel(languageTag: "not a culture"),
            CancellationToken.None
        );

        var body = await ReadBodyAsync(message, CancellationToken.None);
        Assert.Equal("Verify 123456 within 10 minutes", body);
    }

    [Fact]
    public async Task ComposeAsync_WithALongFormat_GrowsItsBuffer()
    {
        var composer = new LocalAccountMailComposer();
        var padding = new string('x', 3000);

        await using var message = await composer.ComposeAsync(
            CreateModel(bodyFormat: "{0} {1} " + padding),
            CancellationToken.None
        );

        var body = await ReadBodyAsync(message, CancellationToken.None);
        Assert.Equal("123456 10 " + padding, body);
    }

    [Fact]
    public async Task ComposeAsync_WhenReadTwice_WritesTheSameBytes()
    {
        var composer = new LocalAccountMailComposer();

        await using var message = await composer.ComposeAsync(
            CreateModel(),
            CancellationToken.None
        );

        var first = await ReadBodyAsync(message, CancellationToken.None);
        var second = await ReadBodyAsync(message, CancellationToken.None);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ComposeAsync_WithAnAddressThatIsNotAnAddress_Throws()
    {
        var composer = new LocalAccountMailComposer();
        var model = CreateModel() with { To = "not an address" };

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await composer.ComposeAsync(model, CancellationToken.None)
        );
    }

    #endregion
}
