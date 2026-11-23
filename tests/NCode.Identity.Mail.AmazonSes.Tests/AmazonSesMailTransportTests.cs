// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Options;
using Moq;
using NCode.Buffers;
using NCode.Identity.Mail.AmazonSes.Facades;
using NCode.Identity.Mail.AmazonSes.Options;
using Xunit;

namespace NCode.Identity.Mail.AmazonSes.Tests;

public class AmazonSesMailTransportTests : BaseTests
{
    private Mock<IAmazonSesClientFactory> ClientFactoryMock { get; }

    private Mock<IAmazonSimpleEmailServiceV2> ClientMock { get; }

    private AmazonSesMailTransportOptions TransportOptions { get; } =
        new() { Region = "us-east-1", From = "noreply@example.com" };

    private AmazonSesMailTransport Transport { get; }

    private MailMessage Message { get; } =
        new()
        {
            To = [new MailAddress("user@example.org")],
            Subject = "Your code",
            TextBody = CreateContent("Your code is 123456.", "text/plain"),
        };

    private sealed record SentMail(SendEmailRequest Request, string Wire);

    public AmazonSesMailTransportTests()
    {
        ClientFactoryMock = CreateStrictMock<IAmazonSesClientFactory>();
        ClientMock = CreateStrictMock<IAmazonSimpleEmailServiceV2>();

        var optionsAccessor = new OptionsWrapper<AmazonSesMailTransportOptions>(TransportOptions);
        Transport = new AmazonSesMailTransport(ClientFactoryMock.Object, optionsAccessor);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Message.Dispose();

        base.Dispose(disposing);
    }

    private static StreamMailContent CreateContent(string text, string mediaType) =>
        new(new MemoryStream(SecureEncoding.UTF8.GetBytes(text)), mediaType, "utf-8");

    private void SetupSend(Action<SentMail> capture)
    {
        ClientFactoryMock.Setup(x => x.GetClient()).Returns(ClientMock.Object).Verifiable();
        ClientMock
            .Setup(x => x.SendEmailAsync(It.IsAny<SendEmailRequest>(), CancellationToken.None))
            .Callback<SendEmailRequest, CancellationToken>(
                (request, _) =>
                {
                    // The raw message is read in the callback because the delivery disposes it when it ends.
                    var data = request.Content.Raw.Data;
                    var bytes = data.ToArray();
                    var wire = SecureEncoding.UTF8.GetString(bytes);
                    capture(new SentMail(request, wire));
                }
            )
            .ReturnsAsync(new SendEmailResponse())
            .Verifiable();
    }

    #region DeliverAsync Tests

    [Fact]
    public async Task DeliverAsync_Always_SendsTheRawMessageWithEveryRecipientInTheEnvelope()
    {
        SentMail? sent = null;
        SetupSend(mail => sent = mail);
        using var message = new MailMessage
        {
            To = [new MailAddress("user@example.org")],
            Cc = [new MailAddress("copy@example.org")],
            Bcc = [new MailAddress("hidden@example.net")],
            Subject = "Your code",
            TextBody = CreateContent("Your code is 123456.", "text/plain"),
        };

        await Transport.DeliverAsync(message, CancellationToken.None);

        Assert.NotNull(sent);
        var destination = sent.Request.Destination;
        Assert.Equal(["user@example.org"], destination.ToAddresses);
        Assert.Equal(["copy@example.org"], destination.CcAddresses);
        Assert.Equal(["hidden@example.net"], destination.BccAddresses);
        Assert.Equal("noreply@example.com", sent.Request.FromEmailAddress);
        Assert.Contains("Subject: Your code", sent.Wire);
        Assert.Contains("user@example.org", sent.Wire);
        Assert.Contains("Your code is 123456.", sent.Wire);
        Assert.DoesNotContain("hidden@example.net", sent.Wire);
    }

    [Fact]
    public async Task DeliverAsync_WithOptions_SendsTheConfigurationSetIdentityArnAndTags()
    {
        const string identityArn = "arn:aws:ses:us-east-1:123456789012:identity/example.com";
        TransportOptions.ConfigurationSetName = "ncode-identity";
        TransportOptions.FromIdentityArn = identityArn;
        TransportOptions.Tags["cell"] = "ci";
        TransportOptions.Tags["team"] = "platform";
        SentMail? sent = null;
        SetupSend(mail => sent = mail);

        await Transport.DeliverAsync(Message, CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Equal("ncode-identity", sent.Request.ConfigurationSetName);
        Assert.Equal(identityArn, sent.Request.FromEmailAddressIdentityArn);
        Assert.Equal(2, sent.Request.EmailTags.Count);
        Assert.Contains(sent.Request.EmailTags, tag => tag.Name == "cell" && tag.Value == "ci");
        Assert.Contains(
            sent.Request.EmailTags,
            tag => tag.Name == "team" && tag.Value == "platform"
        );
    }

    [Fact]
    public async Task DeliverAsync_WithoutOptions_SendsNoConfigurationSetIdentityArnOrTags()
    {
        SentMail? sent = null;
        SetupSend(mail => sent = mail);

        await Transport.DeliverAsync(Message, CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Null(sent.Request.ConfigurationSetName);
        Assert.Null(sent.Request.FromEmailAddressIdentityArn);
        Assert.Empty(sent.Request.EmailTags ?? []);
    }

    [Fact]
    public async Task DeliverAsync_WithAMessageFrom_SendsFromIt()
    {
        SentMail? sent = null;
        SetupSend(mail => sent = mail);
        using var message = new MailMessage
        {
            From = new MailAddress("accounts@example.org"),
            To = [new MailAddress("user@example.org")],
            Subject = "Your code",
            TextBody = CreateContent("Your code is 123456.", "text/plain"),
        };

        await Transport.DeliverAsync(message, CancellationToken.None);

        Assert.NotNull(sent);
        Assert.Equal("accounts@example.org", sent.Request.FromEmailAddress);
        Assert.Contains("accounts@example.org", sent.Wire);
    }

    [Fact]
    public async Task DeliverAsync_WhenSesRefuses_ThrowsTheErrorCodeAndStatusWithoutTheMessageText()
    {
        ClientFactoryMock.Setup(x => x.GetClient()).Returns(ClientMock.Object).Verifiable();
        var refusal = new AmazonSimpleEmailServiceV2Exception(
            "Email address user@example.org is not verified.",
            Amazon.Runtime.ErrorType.Sender,
            "MessageRejected",
            "request-1",
            HttpStatusCode.BadRequest
        );
        ClientMock
            .Setup(x => x.SendEmailAsync(It.IsAny<SendEmailRequest>(), CancellationToken.None))
            .ThrowsAsync(refusal)
            .Verifiable();

        var actual = await Assert.ThrowsAsync<AmazonSesDeliveryException>(async () =>
            await Transport.DeliverAsync(Message, CancellationToken.None)
        );

        Assert.Equal("MessageRejected", actual.ErrorCode);
        Assert.Equal(400, actual.StatusCode);
        Assert.Equal(
            "Amazon SES refused the message with error code 'MessageRejected' and status 400.",
            actual.Message
        );
        Assert.Null(actual.InnerException);
        var text = actual.ToString();
        Assert.DoesNotContain("user@example.org", actual.Message);
        Assert.DoesNotContain("user@example.org", text);
        Assert.DoesNotContain("is not verified", actual.Message);
        Assert.DoesNotContain("is not verified", text);
    }

    [Fact]
    public async Task DeliverAsync_WhenTheCallFails_PropagatesTheException()
    {
        ClientFactoryMock.Setup(x => x.GetClient()).Returns(ClientMock.Object).Verifiable();
        var failure = new HttpRequestException("The connection was reset.");
        ClientMock
            .Setup(x => x.SendEmailAsync(It.IsAny<SendEmailRequest>(), CancellationToken.None))
            .ThrowsAsync(failure)
            .Verifiable();

        var actual = await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await Transport.DeliverAsync(Message, CancellationToken.None)
        );

        Assert.Same(failure, actual);
    }

    #endregion
}
