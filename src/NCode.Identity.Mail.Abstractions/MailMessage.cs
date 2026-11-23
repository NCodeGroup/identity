// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Represents one mail message that <see cref="IMailComposer"/> composes and <see cref="IMailTransport"/> delivers, and
/// that owns its content.
/// </summary>
/// <remarks>
/// The members match the ones of a MIME message, so that a transport maps them without the abstractions depending on a
/// MIME library. The message is disposed once it is delivered, which disposes each body, attachment and linked resource
/// once.
/// </remarks>
// See ADR-1532.
[PublicAPI]
public sealed class MailMessage : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the address the message is from, or <see langword="null"/> for the transport's own.
    /// </summary>
    public MailAddress? From { get; init; }

    /// <summary>
    /// Gets the display name of the sender, or <see langword="null"/> for the transport's own.
    /// </summary>
    public string? FromDisplayName { get; init; }

    /// <summary>
    /// Gets the mailbox that sent the message on behalf of the author, or <see langword="null"/> for none.
    /// </summary>
    public MailAddress? Sender { get; init; }

    /// <summary>
    /// Gets the addresses a reply is sent to.
    /// </summary>
    public List<MailAddress> ReplyTo { get; init; } = [];

    /// <summary>
    /// Gets the addresses the message is sent to.
    /// </summary>
    public List<MailAddress> To { get; init; } = [];

    /// <summary>
    /// Gets the addresses the message is copied to.
    /// </summary>
    public List<MailAddress> Cc { get; init; } = [];

    /// <summary>
    /// Gets the addresses the message is blind copied to, which the headers of the delivered message do not show.
    /// </summary>
    public List<MailAddress> Bcc { get; init; } = [];

    /// <summary>
    /// Gets the subject of the message.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Gets the date of the message, or <see langword="null"/> for the time it is delivered.
    /// </summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>
    /// Gets the identifier of the message, or <see langword="null"/> for one the transport assigns.
    /// </summary>
    public string? MessageId { get; init; }

    /// <summary>
    /// Gets the identifier of the message this one replies to, or <see langword="null"/> for none.
    /// </summary>
    public string? InReplyTo { get; init; }

    /// <summary>
    /// Gets the identifiers of the messages this one refers to.
    /// </summary>
    public List<string> References { get; init; } = [];

    /// <summary>
    /// Gets how urgently the message is delivered. Defaults to <see cref="MailPriority.Normal"/>.
    /// </summary>
    public MailPriority Priority { get; init; } = MailPriority.Normal;

    /// <summary>
    /// Gets how important the message is to its recipient. Defaults to <see cref="MailImportance.Normal"/>.
    /// </summary>
    public MailImportance Importance { get; init; } = MailImportance.Normal;

    /// <summary>
    /// Gets the custom headers of the message.
    /// </summary>
    public List<MailHeader> Headers { get; init; } = [];

    /// <summary>
    /// Gets the plain-text body, or <see langword="null"/> for none.
    /// </summary>
    public MailContent? TextBody { get; init; }

    /// <summary>
    /// Gets the HTML body, or <see langword="null"/> for none.
    /// </summary>
    public MailContent? HtmlBody { get; init; }

    /// <summary>
    /// Gets the attachments of the message.
    /// </summary>
    public List<MailAttachment> Attachments { get; init; } = [];

    /// <summary>
    /// Gets the resources the HTML body links to, such as an inline image.
    /// </summary>
    public List<MailAttachment> LinkedResources { get; init; } = [];

    private bool IsDisposed { get; set; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;

        TextBody?.Dispose();
        HtmlBody?.Dispose();

        foreach (var attachment in Attachments)
            attachment.Content.Dispose();

        foreach (var resource in LinkedResources)
            resource.Content.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;

        if (TextBody is not null)
            await TextBody.DisposeAsync();

        if (HtmlBody is not null)
            await HtmlBody.DisposeAsync();

        foreach (var attachment in Attachments)
            await attachment.Content.DisposeAsync();

        foreach (var resource in LinkedResources)
            await resource.Content.DisposeAsync();
    }
}
