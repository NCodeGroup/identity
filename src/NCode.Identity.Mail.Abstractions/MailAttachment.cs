// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Represents an attachment or a linked resource of a mail message.
/// </summary>
// See ADR-1532.
[PublicAPI]
public sealed class MailAttachment
{
    /// <summary>
    /// Gets the content of the attachment, which the message that holds the attachment disposes.
    /// </summary>
    public required MailContent Content { get; init; }

    /// <summary>
    /// Gets the file name of the attachment, or <see langword="null"/> for none.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// Gets the content identifier that the HTML body refers to a linked resource by, or <see langword="null"/> for
    /// none.
    /// </summary>
    public string? ContentId { get; init; }

    /// <summary>
    /// Gets a description of the attachment, or <see langword="null"/> for none.
    /// </summary>
    public string? Description { get; init; }
}
