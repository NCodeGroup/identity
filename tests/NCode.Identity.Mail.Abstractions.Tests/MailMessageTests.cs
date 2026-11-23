// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Moq;
using Xunit;

namespace NCode.Identity.Mail.Abstractions.Tests;

public class MailMessageTests : BaseTests
{
    private Mock<Stream> CreateStreamMock() => CreateStrictMock<Stream>();

    private static MailContent CreateContent(Mock<Stream> stream) =>
        new StreamMailContent(stream.Object, "text/plain", "utf-8");

    private static MailAttachment CreateAttachment(Mock<Stream> stream) =>
        new() { Content = CreateContent(stream) };

    #region Property Tests

    [Fact]
    public void Constructor_WithASubject_DefaultsTheOtherMembers()
    {
        var message = new MailMessage { Subject = "Your code" };

        Assert.Equal("Your code", message.Subject);
        Assert.Null(message.From);
        Assert.Null(message.FromDisplayName);
        Assert.Null(message.Sender);
        Assert.Empty(message.ReplyTo);
        Assert.Empty(message.To);
        Assert.Empty(message.Cc);
        Assert.Empty(message.Bcc);
        Assert.Null(message.Date);
        Assert.Null(message.MessageId);
        Assert.Null(message.InReplyTo);
        Assert.Empty(message.References);
        Assert.Equal(MailPriority.Normal, message.Priority);
        Assert.Equal(MailImportance.Normal, message.Importance);
        Assert.Empty(message.Headers);
        Assert.Null(message.TextBody);
        Assert.Null(message.HtmlBody);
        Assert.Empty(message.Attachments);
        Assert.Empty(message.LinkedResources);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_Always_DisposesEachContentOnce()
    {
        var text = CreateStreamMock();
        var html = CreateStreamMock();
        var attachment = CreateStreamMock();
        var resource = CreateStreamMock();
        var streams = new[] { text, html, attachment, resource };
        foreach (var stream in streams)
            stream.Setup(x => x.Close()).Verifiable();

        var message = new MailMessage
        {
            Subject = "Your code",
            TextBody = CreateContent(text),
            HtmlBody = CreateContent(html),
            Attachments = [CreateAttachment(attachment)],
            LinkedResources = [CreateAttachment(resource)],
        };

        message.Dispose();
        message.Dispose();

        foreach (var stream in streams)
            stream.Verify(x => x.Close(), Times.Once());
    }

    [Fact]
    public async Task DisposeAsync_Always_DisposesEachContentOnce()
    {
        var text = CreateStreamMock();
        var html = CreateStreamMock();
        var attachment = CreateStreamMock();
        var resource = CreateStreamMock();
        var streams = new[] { text, html, attachment, resource };
        foreach (var stream in streams)
            stream.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask).Verifiable();

        var message = new MailMessage
        {
            Subject = "Your code",
            TextBody = CreateContent(text),
            HtmlBody = CreateContent(html),
            Attachments = [CreateAttachment(attachment)],
            LinkedResources = [CreateAttachment(resource)],
        };

        await message.DisposeAsync();
        await message.DisposeAsync();

        foreach (var stream in streams)
            stream.Verify(x => x.DisposeAsync(), Times.Once());
    }

    [Fact]
    public void Dispose_WithoutContent_Succeeds()
    {
        var message = new MailMessage { Subject = "Your code" };

        message.Dispose();
    }

    #endregion
}
