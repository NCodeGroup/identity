// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Moq;
using NCode.Identity.Mail.Smtp.Facades;
using NCode.Identity.Mail.Smtp.Options;
using Xunit;

namespace NCode.Identity.Mail.Smtp.Tests;

public class SmtpMailTransportTests : BaseTests
{
    private Mock<ISmtpClientFactory> ClientFactoryMock { get; }

    private Mock<ISmtpClient> ClientMock { get; }

    private SmtpMailTransportOptions TransportOptions { get; } =
        new() { Host = "smtp.example.com", From = "noreply@example.com" };

    private SmtpMailTransport Transport { get; }

    private MailMessage Message { get; } =
        new()
        {
            To = "user@example.org",
            Subject = "Your code",
            TextBody = "Your code is 123456.",
        };

    public SmtpMailTransportTests()
    {
        ClientFactoryMock = CreateStrictMock<ISmtpClientFactory>();
        ClientMock = CreateStrictMock<ISmtpClient>();

        var optionsAccessor = new OptionsWrapper<SmtpMailTransportOptions>(TransportOptions);
        Transport = new SmtpMailTransport(ClientFactoryMock.Object, optionsAccessor);
    }

    private void SetupConnection(SecureSocketOptions expectedSocketOptions, int port = 587)
    {
        ClientFactoryMock.Setup(x => x.Create()).Returns(ClientMock.Object).Verifiable();
        ClientMock.SetupSet(x => x.Timeout = 30_000).Verifiable();
        ClientMock
            .Setup(x =>
                x.ConnectAsync(
                    "smtp.example.com",
                    port,
                    expectedSocketOptions,
                    CancellationToken.None
                )
            )
            .Returns(Task.CompletedTask)
            .Verifiable();
        ClientMock.Setup(x => x.Dispose()).Verifiable();
    }

    private void SetupSend(Action<MimeMessage> capture)
    {
        ClientMock
            .Setup(x => x.SendAsync(It.IsAny<ISendRequest>(), CancellationToken.None))
            .Callback<ISendRequest, CancellationToken>((request, _) => capture(request.Message))
            .ReturnsAsync(new SendResult("250 queued", [], []))
            .Verifiable();
        ClientMock
            .Setup(x => x.DisconnectAsync(true, CancellationToken.None))
            .Returns(Task.CompletedTask)
            .Verifiable();
    }

    #region DeliverAsync Tests

    [Fact]
    public async Task DeliverAsync_WithStartTls_ConnectsRequiringStartTls()
    {
        TransportOptions.Tls = SmtpTls.StartTls;
        SetupConnection(SecureSocketOptions.StartTls);
        SetupSend(_ => { });

        await Transport.DeliverAsync(Message, CancellationToken.None);
    }

    [Fact]
    public async Task DeliverAsync_WithImplicitTls_ConnectsOverTls()
    {
        TransportOptions.Tls = SmtpTls.ImplicitTls;
        TransportOptions.Port = 465;
        SetupConnection(SecureSocketOptions.SslOnConnect, 465);
        SetupSend(_ => { });

        await Transport.DeliverAsync(Message, CancellationToken.None);
    }

    [Fact]
    public async Task DeliverAsync_WithoutEncryption_ConnectsWithoutEncryption()
    {
        TransportOptions.Tls = SmtpTls.None;
        TransportOptions.Port = 1025;
        SetupConnection(SecureSocketOptions.None, 1025);
        SetupSend(_ => { });

        await Transport.DeliverAsync(Message, CancellationToken.None);
    }

    [Fact]
    public async Task DeliverAsync_WithCredentials_Authenticates()
    {
        TransportOptions.UserName = "smtp-user";
        TransportOptions.Password = "smtp-password";
        SetupConnection(SecureSocketOptions.StartTls);
        ClientMock
            .Setup(x => x.AuthenticateAsync("smtp-user", "smtp-password", CancellationToken.None))
            .Returns(Task.CompletedTask)
            .Verifiable();
        SetupSend(_ => { });

        await Transport.DeliverAsync(Message, CancellationToken.None);
    }

    [Fact]
    public async Task DeliverAsync_WithoutCredentials_SendsWithoutAuthenticating()
    {
        SetupConnection(SecureSocketOptions.StartTls);
        SetupSend(_ => { });

        await Transport.DeliverAsync(Message, CancellationToken.None);

        ClientMock.Verify(
            x =>
                x.AuthenticateAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task DeliverAsync_Always_SendsFromTheConfiguredAddressMarkedAutoSubmitted()
    {
        TransportOptions.FromName = "NCode Identity";
        SetupConnection(SecureSocketOptions.StartTls);
        MimeMessage? sent = null;
        SetupSend(mimeMessage => sent = mimeMessage);

        await Transport.DeliverAsync(Message, CancellationToken.None);

        Assert.NotNull(sent);
        var from = Assert.IsType<MailboxAddress>(Assert.Single(sent.From));
        Assert.Equal("noreply@example.com", from.Address);
        Assert.Equal("NCode Identity", from.Name);
        var to = Assert.IsType<MailboxAddress>(Assert.Single(sent.To));
        Assert.Equal("user@example.org", to.Address);
        Assert.Equal("Your code", sent.Subject);
        var body = Assert.IsType<TextPart>(sent.Body);
        Assert.True(body.IsPlain);
        Assert.Equal("Your code is 123456.", body.Text);
        Assert.Equal("auto-generated", sent.Headers["Auto-Submitted"]);
    }

    [Fact]
    public async Task DeliverAsync_WithAMessageDisplayName_SendsItInPlaceOfTheConfiguredName()
    {
        TransportOptions.FromName = "NCode Identity";
        SetupConnection(SecureSocketOptions.StartTls);
        MimeMessage? sent = null;
        SetupSend(mimeMessage => sent = mimeMessage);
        var message = new MailMessage
        {
            To = "user@example.org",
            FromDisplayName = "Acme Accounts",
            Subject = "Your code",
            TextBody = "Your code is 123456.",
        };

        await Transport.DeliverAsync(message, CancellationToken.None);

        Assert.NotNull(sent);
        var from = Assert.IsType<MailboxAddress>(Assert.Single(sent.From));
        Assert.Equal("Acme Accounts", from.Name);
        Assert.Equal("noreply@example.com", from.Address);
    }

    [Fact]
    public async Task DeliverAsync_WithAnHtmlBody_SendsBothAlternatives()
    {
        SetupConnection(SecureSocketOptions.StartTls);
        MimeMessage? sent = null;
        SetupSend(mimeMessage => sent = mimeMessage);
        var message = new MailMessage
        {
            To = "user@example.org",
            Subject = "Your code",
            TextBody = "Your code is 123456.",
            HtmlBody = "<p>Your code is <b>123456</b>.</p>",
        };

        await Transport.DeliverAsync(message, CancellationToken.None);

        Assert.NotNull(sent);
        var alternative = Assert.IsType<MultipartAlternative>(sent.Body);
        Assert.Equal(2, alternative.Count);
        var plain = Assert.IsType<TextPart>(alternative[0]);
        Assert.True(plain.IsPlain);
        Assert.Equal("Your code is 123456.", plain.Text);
        var html = Assert.IsType<TextPart>(alternative[1]);
        Assert.True(html.IsHtml);
        Assert.Equal("<p>Your code is <b>123456</b>.</p>", html.Text);
    }

    [Fact]
    public async Task DeliverAsync_WhenTheServerRefuses_Throws()
    {
        SetupConnection(SecureSocketOptions.StartTls);
        var refusal = new SmtpCommandException(
            SmtpErrorCode.MessageNotAccepted,
            SmtpStatusCode.MailboxUnavailable,
            "The mailbox is unavailable."
        );
        ClientMock
            .Setup(x => x.SendAsync(It.IsAny<ISendRequest>(), CancellationToken.None))
            .ThrowsAsync(refusal)
            .Verifiable();

        var actual = await Assert.ThrowsAsync<SmtpCommandException>(async () =>
            await Transport.DeliverAsync(Message, CancellationToken.None)
        );

        Assert.Same(refusal, actual);
    }

    #endregion
}
