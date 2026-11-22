// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace NCode.Identity.Mail;

/// <summary>
/// Represents one mail message with one recipient, which <see cref="IMailSender"/> accepts and
/// <see cref="IMailTransport"/> delivers.
/// </summary>
// See ADR-1532.
// A property bag with no behavior to test.
[PublicAPI]
[ExcludeFromCodeCoverage]
public sealed class MailMessage
{
    /// <summary>
    /// Gets the address the message is sent to.
    /// </summary>
    public required string To { get; init; }

    /// <summary>
    /// Gets the display name of the sender, or <see langword="null"/> for the transport's own.
    /// </summary>
    public string? FromDisplayName { get; init; }

    /// <summary>
    /// Gets the subject of the message.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Gets the plain-text body, which every message has.
    /// </summary>
    public required string TextBody { get; init; }

    /// <summary>
    /// Gets an HTML alternative of the body, or <see langword="null"/> for none.
    /// </summary>
    public string? HtmlBody { get; init; }
}
