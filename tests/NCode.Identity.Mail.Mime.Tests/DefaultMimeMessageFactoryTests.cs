// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using Microsoft.Extensions.Time.Testing;
using MimeKit;
using NCode.Buffers;
using Xunit;

namespace NCode.Identity.Mail.Mime.Tests;

public class DefaultMimeMessageFactoryTests : BaseTests
{
    private static MailAddress DefaultFrom { get; } = new("noreply@example.com", "Example");

    private FakeTimeProvider Clock { get; } =
        new(new DateTimeOffset(2026, 11, 22, 9, 0, 0, TimeSpan.Zero));

    private DefaultMimeMessageFactory Factory { get; }

    public DefaultMimeMessageFactoryTests()
    {
        Factory = new DefaultMimeMessageFactory(Clock);
    }

    private static MailAddress[] ToRecipients { get; } = [new("to@example.com", "To")];

    private static StreamMailContent CreateText(string text, string mediaType = "text/plain") =>
        new(new MemoryStream(SecureEncoding.UTF8.GetBytes(text)), mediaType, "utf-8");

    private static StreamMailContent CreateBinary(byte[] bytes, string mediaType) =>
        new(new MemoryStream(bytes), mediaType);

    private ValueTask<MimeMessage> CreateAsync(
        MailMessage message,
        CancellationToken cancellationToken = default
    ) => Factory.CreateAsync(message, DefaultFrom, cancellationToken);

    private byte[] Write(MimeMessage mimeMessage)
    {
        using var stream = new MemoryStream();
        var options = Factory.CreateFormatOptions();
        mimeMessage.WriteTo(options, stream);
        return stream.ToArray();
    }

    private static MimeMessage Parse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return MimeMessage.Load(stream);
    }

    #region CreateAsync Tests

    [Fact]
    public async Task CreateAsync_WithoutFrom_UsesTheDefaultAddressAndTheMessagesDisplayName()
    {
        using var message = new MailMessage
        {
            Subject = "Hello",
            To = [.. ToRecipients],
            FromDisplayName = "Support",
            TextBody = CreateText("plain body"),
        };

        using var mimeMessage = await CreateAsync(message);

        var from = Assert.IsType<MailboxAddress>(Assert.Single(mimeMessage.From));
        Assert.Equal("noreply@example.com", from.Address);
        Assert.Equal("Support", from.Name);
    }

    [Fact]
    public async Task CreateAsync_WithoutFromOrDisplayName_UsesTheDefaultDisplayName()
    {
        using var message = new MailMessage
        {
            Subject = "Hello",
            To = [.. ToRecipients],
            TextBody = CreateText("plain body"),
        };

        using var mimeMessage = await CreateAsync(message);

        var from = Assert.IsType<MailboxAddress>(Assert.Single(mimeMessage.From));
        Assert.Equal("noreply@example.com", from.Address);
        Assert.Equal("Example", from.Name);
    }

    [Fact]
    public async Task CreateAsync_WithFrom_UsesIt()
    {
        using var message = new MailMessage
        {
            Subject = "Hello",
            From = new MailAddress("sender@example.org", "Sender"),
            To = [.. ToRecipients],
            TextBody = CreateText("plain body"),
        };

        using var mimeMessage = await CreateAsync(message);

        var from = Assert.IsType<MailboxAddress>(Assert.Single(mimeMessage.From));
        Assert.Equal("sender@example.org", from.Address);
        Assert.Equal("Sender", from.Name);
    }

    [Fact]
    public async Task CreateAsync_WithEveryHeader_MapsEachOne()
    {
        var date = new DateTimeOffset(2026, 11, 22, 10, 30, 0, TimeSpan.Zero);
        using var message = new MailMessage
        {
            Subject = "Hello",
            Sender = new MailAddress("sender@example.org"),
            ReplyTo = [new MailAddress("reply@example.org")],
            To = [.. ToRecipients],
            Cc = [new MailAddress("cc@example.org")],
            Bcc = [new MailAddress("bcc@example.org")],
            Date = date,
            MessageId = "message-1@example.com",
            InReplyTo = "original@example.com",
            References = ["first@example.com", "second@example.com"],
            Priority = MailPriority.Urgent,
            Importance = MailImportance.High,
            Headers = [new MailHeader("X-Custom", "custom value")],
            TextBody = CreateText("plain body"),
        };

        using var mimeMessage = await CreateAsync(message);

        Assert.Equal("Hello", mimeMessage.Subject);
        Assert.Equal("sender@example.org", mimeMessage.Sender?.Address);
        Assert.Equal("reply@example.org", Assert.Single(mimeMessage.ReplyTo.Mailboxes).Address);
        Assert.Equal("to@example.com", Assert.Single(mimeMessage.To.Mailboxes).Address);
        Assert.Equal("cc@example.org", Assert.Single(mimeMessage.Cc.Mailboxes).Address);
        Assert.Equal("bcc@example.org", Assert.Single(mimeMessage.Bcc.Mailboxes).Address);
        Assert.Equal(date, mimeMessage.Date);
        Assert.Equal("message-1@example.com", mimeMessage.MessageId);
        Assert.Equal("original@example.com", mimeMessage.InReplyTo);
        Assert.Equal(
            new[] { "first@example.com", "second@example.com" },
            mimeMessage.References.ToArray()
        );
        Assert.Equal(MessagePriority.Urgent, mimeMessage.Priority);
        Assert.Equal(MessageImportance.High, mimeMessage.Importance);
        Assert.Equal("custom value", mimeMessage.Headers["X-Custom"]);
    }

    [Fact]
    public async Task CreateAsync_WithoutADate_DatesTheMessageByTheTimeProvider()
    {
        using var message = new MailMessage
        {
            Subject = "Hello",
            To = [.. ToRecipients],
            TextBody = CreateText("plain body"),
        };

        using var mimeMessage = await CreateAsync(message);

        Assert.Equal(Clock.GetUtcNow(), mimeMessage.Date);
    }

    [Fact]
    public async Task CreateAsync_Always_MarksTheMessageAutoSubmitted()
    {
        using var message = new MailMessage
        {
            Subject = "Hello",
            To = [.. ToRecipients],
            TextBody = CreateText("plain body"),
        };

        using var mimeMessage = await CreateAsync(message);

        Assert.Equal("auto-generated", mimeMessage.Headers["Auto-Submitted"]);
    }

    [Fact]
    public async Task CreateAsync_WithAnAutoSubmittedHeader_KeepsIt()
    {
        using var message = new MailMessage
        {
            Subject = "Hello",
            To = [.. ToRecipients],
            Headers = [new MailHeader("auto-submitted", "auto-replied")],
            TextBody = CreateText("plain body"),
        };

        using var mimeMessage = await CreateAsync(message);

        var headers = mimeMessage
            .Headers.Where(header =>
                string.Equals(header.Field, "Auto-Submitted", StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
        Assert.Equal("auto-replied", Assert.Single(headers).Value);
    }

    [Theory]
    [InlineData("From")]
    [InlineData("sender")]
    [InlineData("Reply-To")]
    [InlineData("TO")]
    [InlineData("Cc")]
    [InlineData("Bcc")]
    [InlineData("Subject")]
    [InlineData("Date")]
    [InlineData("Message-ID")]
    [InlineData("In-Reply-To")]
    [InlineData("References")]
    [InlineData("Priority")]
    [InlineData("Importance")]
    [InlineData("X-Priority")]
    [InlineData("MIME-Version")]
    [InlineData("Return-Path")]
    [InlineData("Received")]
    [InlineData("Content-Type")]
    [InlineData("content-id")]
    public async Task CreateAsync_WithAReservedHeader_Throws(string name)
    {
        using var message = new MailMessage
        {
            Subject = "Hello",
            To = [.. ToRecipients],
            Headers = [new MailHeader(name, "value")],
            TextBody = CreateText("plain body"),
        };

        await Assert.ThrowsAsync<ArgumentException>(async () => await CreateAsync(message));
    }

    [Fact]
    public async Task CreateAsync_WithoutARecipient_Throws()
    {
        using var message = new MailMessage
        {
            Subject = "Hello",
            TextBody = CreateText("plain body"),
        };

        await Assert.ThrowsAsync<ArgumentException>(async () => await CreateAsync(message));
    }

    [Fact]
    public async Task CreateAsync_WithOnlyABccRecipient_MapsIt()
    {
        using var message = new MailMessage
        {
            Subject = "Hello",
            Bcc = [new MailAddress("bcc@example.org")],
            TextBody = CreateText("plain body"),
        };

        using var mimeMessage = await CreateAsync(message);

        Assert.Equal("bcc@example.org", Assert.Single(mimeMessage.Bcc.Mailboxes).Address);
    }

    [Fact]
    public async Task CreateAsync_WithoutABody_Throws()
    {
        using var message = new MailMessage { Subject = "Hello", To = [.. ToRecipients] };

        await Assert.ThrowsAsync<ArgumentException>(async () => await CreateAsync(message));
    }

    [Fact]
    public async Task CreateAsync_WithTextAndHtml_WritesAlternatives()
    {
        using var message = new MailMessage
        {
            Subject = "Hello",
            To = [.. ToRecipients],
            TextBody = CreateText("plain body"),
            HtmlBody = CreateText("<p>html body</p>", "text/html"),
        };

        using var mimeMessage = await CreateAsync(message);

        var alternative = Assert.IsType<MultipartAlternative>(mimeMessage.Body);
        Assert.Equal(2, alternative.Count);

        var textPart = Assert.IsType<TextPart>(alternative[0]);
        Assert.Equal("plain body", textPart.Text);
        Assert.Equal(ContentEncoding.QuotedPrintable, textPart.ContentTransferEncoding);

        var htmlPart = Assert.IsType<TextPart>(alternative[1]);
        Assert.Equal("<p>html body</p>", htmlPart.Text);
        Assert.Equal("text/html", htmlPart.ContentType.MimeType);
    }

    [Fact]
    public async Task CreateAsync_WithAnExplicitTransferEncoding_UsesIt()
    {
        using var message = new MailMessage
        {
            Subject = "Hello",
            To = [.. ToRecipients],
            TextBody = new StreamMailContent(
                new MemoryStream(SecureEncoding.UTF8.GetBytes("plain body")),
                "text/plain",
                "utf-8"
            )
            {
                TransferEncoding = MailTransferEncodings.Base64,
            },
        };

        using var mimeMessage = await CreateAsync(message);

        var part = Assert.IsType<TextPart>(mimeMessage.Body);
        Assert.Equal(ContentEncoding.Base64, part.ContentTransferEncoding);
        Assert.Equal("utf-8", part.ContentType.Charset);
    }

    [Fact]
    public async Task CreateAsync_WithAnUnknownTransferEncoding_Throws()
    {
        using var message = new MailMessage
        {
            Subject = "Hello",
            To = [.. ToRecipients],
            TextBody = new StreamMailContent(
                new MemoryStream(SecureEncoding.UTF8.GetBytes("plain body")),
                "text/plain",
                "utf-8"
            )
            {
                TransferEncoding = "uuencode",
            },
        };

        await Assert.ThrowsAsync<ArgumentException>(async () => await CreateAsync(message));
    }

    [Fact]
    public async Task CreateAsync_WithLinkedResources_WritesARelatedHtmlPart()
    {
        byte[] image = [1, 2, 3, 4];
        using var message = new MailMessage
        {
            Subject = "Hello",
            To = [.. ToRecipients],
            TextBody = CreateText("plain body"),
            HtmlBody = CreateText("<img src=\"cid:logo\">", "text/html"),
            LinkedResources =
            [
                new MailAttachment
                {
                    Content = CreateBinary(image, "image/png"),
                    ContentId = "logo",
                    FileName = "logo.png",
                },
            ],
        };

        using var mimeMessage = await CreateAsync(message);

        var alternative = Assert.IsType<MultipartAlternative>(mimeMessage.Body);
        var related = Assert.IsType<MultipartRelated>(alternative[1]);
        Assert.Equal(2, related.Count);
        Assert.IsType<TextPart>(related[0]);

        var resource = Assert.IsType<MimePart>(related[1]);
        Assert.Equal("logo", resource.ContentId);
        Assert.Equal("logo.png", resource.FileName);
        Assert.Equal(ContentDisposition.Inline, resource.ContentDisposition?.Disposition);
        Assert.Equal("image/png", resource.ContentType.MimeType);
        Assert.Equal(ContentEncoding.Base64, resource.ContentTransferEncoding);
    }

    [Fact]
    public async Task CreateAsync_WithALinkedResourceWithoutAContentId_Throws()
    {
        using var message = new MailMessage
        {
            Subject = "Hello",
            To = [.. ToRecipients],
            HtmlBody = CreateText("<p>html body</p>", "text/html"),
            LinkedResources = [new MailAttachment { Content = CreateBinary([1, 2], "image/png") }],
        };

        await Assert.ThrowsAsync<ArgumentException>(async () => await CreateAsync(message));
    }

    [Fact]
    public async Task CreateAsync_WithLinkedResourcesAndNoHtmlBody_Throws()
    {
        using var message = new MailMessage
        {
            Subject = "Hello",
            To = [.. ToRecipients],
            TextBody = CreateText("plain body"),
            LinkedResources =
            [
                new MailAttachment
                {
                    Content = CreateBinary([1, 2], "image/png"),
                    ContentId = "logo",
                },
            ],
        };

        await Assert.ThrowsAsync<ArgumentException>(async () => await CreateAsync(message));
    }

    [Fact]
    public async Task CreateAsync_WithAttachments_WritesAMixedMessage()
    {
        byte[] file = [10, 20, 30, 40, 50];
        using var message = new MailMessage
        {
            Subject = "Hello",
            To = [.. ToRecipients],
            TextBody = CreateText("plain body"),
            Attachments =
            [
                new MailAttachment
                {
                    Content = CreateBinary(file, "application/pdf"),
                    FileName = "report.pdf",
                    Description = "The report",
                },
            ],
        };

        using var mimeMessage = await CreateAsync(message);

        var mixed = Assert.IsType<Multipart>(mimeMessage.Body);
        Assert.Equal("multipart/mixed", mixed.ContentType.MimeType);
        Assert.Equal(2, mixed.Count);
        Assert.IsType<TextPart>(mixed[0]);

        var attachment = Assert.IsType<MimePart>(mixed[1]);
        Assert.Equal(ContentDisposition.Attachment, attachment.ContentDisposition?.Disposition);
        Assert.Equal("report.pdf", attachment.FileName);
        Assert.Equal("The report", attachment.ContentDescription);

        var bytes = Write(mimeMessage);
        using var parsed = Parse(bytes);
        var parsedAttachment = Assert.IsType<MimePart>(Assert.Single(parsed.Attachments));
        Assert.NotNull(parsedAttachment.Content);

        using var decoded = new MemoryStream();
        parsedAttachment.Content.DecodeTo(decoded);
        Assert.Equal(file, decoded.ToArray());
    }

    [Fact]
    public async Task CreateAsync_WithANonSeekableContent_WritesTheSameBytesTwice()
    {
        var compressed = new MemoryStream();
        using (
            var deflate = new DeflateStream(compressed, CompressionMode.Compress, leaveOpen: true)
        )
        {
            var original = SecureEncoding.UTF8.GetBytes("a body from a stream that does not seek");
            deflate.Write(original);
        }

        compressed.Position = 0;
        var source = new DeflateStream(compressed, CompressionMode.Decompress);
        using var message = new MailMessage
        {
            Subject = "Hello",
            To = [.. ToRecipients],
            TextBody = new StreamMailContent(source, "text/plain", "utf-8"),
        };

        using var mimeMessage = await CreateAsync(message);

        var first = Write(mimeMessage);
        var second = Write(mimeMessage);

        Assert.Equal(first, second);

        using var parsed = Parse(first);
        var parsedBody = Assert.IsType<TextPart>(parsed.Body);
        Assert.Equal("a body from a stream that does not seek", parsedBody.Text);
    }

    [Fact]
    public async Task CreateAsync_WithBcc_HidesItWhenWritten()
    {
        using var message = new MailMessage
        {
            Subject = "Hello",
            To = [.. ToRecipients],
            Bcc = [new MailAddress("bcc@example.org")],
            TextBody = CreateText("plain body"),
        };

        using var mimeMessage = await CreateAsync(message);

        var bytes = Write(mimeMessage);
        var text = SecureEncoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("bcc@example.org", text);
        Assert.Contains("to@example.com", text);
    }

    #endregion

    #region CreateFormatOptions Tests

    [Fact]
    public void CreateFormatOptions_Always_HidesBcc()
    {
        var options = Factory.CreateFormatOptions();

        Assert.Contains(HeaderId.Bcc, options.HiddenHeaders);
        Assert.DoesNotContain(HeaderId.Bcc, FormatOptions.Default.HiddenHeaders);
    }

    #endregion
}
